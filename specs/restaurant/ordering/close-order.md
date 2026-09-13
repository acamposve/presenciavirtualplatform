# Specification: Ordering / CloseOrder

**Status:** Approved
**Bounded Context:** Restaurant
**Business Capability:** Ordering
**Related ADRs:** [ADR 0002 — Tenant Isolation Strategy](../../../docs/adr/0002-tenant-isolation-strategy.md)
**Last Updated:** 2026-09-13

This specification follows `constitution.md` Article I. It requires human review and approval before implementation begins, per Article 1.4. Approved after seven rounds of review (concurrency correctness with `AddItem`, two pre-existing implementation defects it surfaced, idempotency-table isolation, and acceptance-criteria precision), in [PR #14](https://github.com/acamposve/presenciavirtualplatform/pull/14).

---

## Business Objective

Allow restaurant staff to close an order once service for that table is finished, so the order stops accepting further items and the table becomes available for a new order. This is the next state transition in the order lifecycle already sketched in `create-order.md` and `add-item.md`:

```text
CreateOrder → AddItem → (Kitchen) → CloseOrder → Payment
```

`GetOrder` deliberately does not appear in this diagram: per its own specification, it is a read-only query that "can be exercised at any point after `CreateOrder` and interleaved freely with `AddItem` calls — it does not advance the order lifecycle" (`get-order.md`). It remains callable after an order is closed; while the table has no other `Open` order, it will reject that request as Not Found, the same as for any table with no `Open` order (`get-order.md` FR3/AC2) — it does not somehow keep returning the closed order under a different status. Once a new order is opened for that table (AC9), `GetOrder` correctly starts returning *that* order instead.

## Actors

- **Waiter / Front-of-house staff** — closes the order for a table once service is complete.

## Business Context

`CloseOrder` is the last state transition this specification's Ordering capability defines. `Payment` (shown after it in the lifecycle diagram above) is a separate, not-yet-written capability — closing an order here does not perform or require any payment step; it only marks the order as no longer `Open`. `RemoveItem` and `CancelOrder` remain separate, not-yet-written specifications.

## Functional Requirements

1. An authorized user MUST be able to close an existing, `Open` order belonging to their own tenant, given an `OrderId`.
2. The system MUST reject the request if the order does not exist within the caller's tenant.
3. The system MUST reject the request if the order is not currently in the `Open` status (e.g. it was already closed).
4. Closing an order MUST succeed regardless of how many items it has — an order with zero items (e.g. a table that sat down and left without ordering) MUST be closeable, not rejected as invalid. **Decided** (see Open Questions): requiring at least one item would add a business rule nobody asked for, and would leave a table that never ordered with no valid way to free itself up for the next party.
5. Once closed, the order MUST NOT accept further `AddItem` calls. `add-item.md` BR1/AC3 already reject any `AddItem` against a non-`Open` order **in principle**, but two existing implementation gaps currently prevent that guarantee from actually holding once a second status value exists (see BR6 and BR7). Implementing this specification therefore requires changes to `AddItem`'s own existing implementation (its merge's locking, per BR7) and to `Order`'s rehydration (per BR6) — **not** merely adding the enum value and leaving `AddItem`/`CreateOrder` untouched, contrary to what a first pass at this specification assumed.
6. Once closed, the order's table MUST become eligible for a new `CreateOrder` (already guaranteed by `create-order.md` BR2, which only blocks a new order while the table has an order in `Open` status).
7. The system MUST return the order's current identifier, table, status, creation timestamp, items, and total after closing — the same full representation `get-order.md` returns, since this is effectively the order's final state at the moment of closing.
8. The system MUST support an optional `Idempotency-Key` request header, with the same semantics as `CreateOrder`'s and `AddItem`'s (FR7/BR6 in `create-order.md`, FR8/BR6 in `add-item.md`): a repeated request with the same key does not attempt to re-close an already-closed-by-this-key order as a conflict; it returns that order's current state instead.

## Non-Functional Requirements

- **Tenant isolation:** enforced per ADR 0002 on every read and write involved (order lookup, status update).
- **Consistency:** the closed status must be immediately visible to a subsequent read (`GetOrder` will no longer return the order, per `get-order.md` FR3; a direct read by `OrderId`, if any future capability adds one, must reflect `Closed`). See BR8 for why `get-order.md`'s own deferred read-consistency question does not require any change to `GetOrder` as a result of this specification.
- **Observability:** the request must be traceable via structured logging with a correlation ID, per `constitution.md` Article XI. As with `create-order.md`, `create-table.md`, and `add-item.md`, no such mechanism exists yet anywhere in the codebase; this specification does not introduce one on its own; see those specs' own NFR sections for the same note.
- **Idempotency:** guaranteed for repeated requests carrying the same `Idempotency-Key`; a request without one has no idempotency guarantee — same as `CreateOrder`/`AddItem`.

## Business Rules

- **BR1:** An order can only transition from `Open` to `Closed`. Attempting to close an order that is not currently `Open` (e.g. already `Closed`) MUST be rejected, except when the request replays a prior successful close via a matching `Idempotency-Key` (BR3).
- **BR2:** Closing an order does not require it to have any items. An order with zero items and a total of zero MUST be closeable — rejecting it for having no items is not a valid implementation of this rule (FR4/AC8 already require this as mandatory behavior; "MAY" here would wrongly suggest it is optional).
- **BR3:** An `Idempotency-Key`, when supplied, uniquely identifies a single logical close-order attempt within a tenant — scoped by `(TenantId, IdempotencyKey)` alone, not also by `OrderId`, for the same reason `add-item.md` BR6 gives (a key scoped by `(TenantId, OrderId, IdempotencyKey)` could not detect reuse against a different `OrderId` as a conflict). The stored record retains the original `OrderId`, so a replay (same `OrderId`) can be told apart from a conflicting reuse (a different `OrderId`). A replay MUST NOT attempt to transition the order's status again; the response reflects the order's **current** state at the time of the replay, not a frozen snapshot of the original close.
  - The `Open` → `Closed` transition and claiming the idempotency key MUST commit as a single atomic operation, for the same reason `create-order.md` BR6 and `add-item.md` BR6 require it: if two concurrent requests supply the same key, at most one may transition the order and claim the key; the other MUST roll back and re-read the order's current state instead of double-transitioning or racing.
  - When an `Idempotency-Key` is supplied, the existing claim for it (if any) MUST be checked **before** resolving the requested `OrderId` in any way — whether it does not exist, or belongs to a different tenant — mirroring `AddItemHandler`'s established precedence for the identical reason: a key reused against a different `OrderId` must deterministically return 409 Conflict (AC7), regardless of what that `OrderId` turns out to be. This is also what keeps AC2's tenant-isolation guarantee intact: since the claim check alone (a key lookup scoped to the caller's own tenant) decides the 409 without ever needing to resolve the other `OrderId`'s existence or ownership, the response reveals nothing about a cross-tenant order that a same-tenant, nonexistent one wouldn't also produce.
- **BR4:** The `Open` → `Closed` transition itself MUST be atomic against concurrent close attempts that do **not** share an `Idempotency-Key` — e.g. a conditional update (`UPDATE ... WHERE status = 'Open'`) that only one concurrent request can apply. The request that loses this race MUST be rejected as a conflict (BR1), not silently succeed a second time or corrupt the order's status.
- **BR5:** Closing an order is a pure status transition. It MUST NOT modify the order's items or recompute/store its total — `Total` remains, as always (`add-item.md` BR5), the sum of the order's persisted items' line totals.
- **BR6 (existing defect, must be fixed as part of this specification):** every code path that reconstructs an `Order` from storage MUST carry through its actual persisted `Status`. Today, `Order.Reconstruct` does not even accept a `Status` parameter, and `Order`'s private constructor unconditionally initializes `Status` to `Open` — so every rehydrated `Order` reports `Open` regardless of what actually happened to it. This has been invisible so far because `OrderStatus` has only ever had one value. It becomes an active correctness bug the moment `Closed` exists: `AddItemHandler`'s own BR1 guard reads the order via `OrderRepository.GetAsync`, which (unlike `GetOpenByTableAsync`) does **not** filter by status, so it would rehydrate a closed order as `Open` and incorrectly let `AddItem` succeed against it. (`GetOrder` itself is not affected the same way — `GetOpenByTableAsync` already filters to `status = 'Open'` before calling `Reconstruct` at all, so a closed order simply is not returned there, consistent with `get-order.md` FR3/AC2 — but this specification's own read of the order, and any other future consumer of `GetAsync`, still needs the real status.) `Order.Reconstruct` MUST gain a `Status` parameter and preserve it, and every repository read path (`GetAsync`, `GetOpenByTableAsync`) MUST pass the row's actual persisted status through to it.
- **BR7 (existing gap, must be fixed as part of this specification):** `AddItem`'s existing merge (`add-item.md` BR4) checks the order's `Status` once, at the start of `AddItemHandler`, before performing its atomic upsert — it does not re-validate `Status` inside the same atomic operation as the merge, and that upsert is synchronized only by a **per-line** advisory lock (keyed by order + menu item), which does not exclude a `CloseOrder` request touching the order as a whole. A `CloseOrder` request that commits in between `AddItem`'s status check and its upsert would let the `AddItem` request go on to successfully add or merge a line onto an order that is, by the time the write actually happens, already `Closed`. Fixing this requires both operations to serialize against each other through the **same, order-scoped** lock — e.g. a single advisory lock keyed by `OrderId` alone (distinct from, and acquired in addition to, `AddItem`'s existing per-line lock) that both `AddItem`'s merge and `CloseOrder`'s transition acquire before checking `Status` and writing. A per-line lock on one side and an order-level conditional update on the other are not mutually exclusive and MUST NOT be treated as sufficient — re-checking `Status` under a lock the other operation never acquires does not prevent the race.
- **BR8 (answers a question `get-order.md` deliberately deferred — the answer is no additional requirement is needed):** `get-order.md` BR4 left its order-row read and items read as two independent queries, and said that a future specification introducing a way to mutate the order's own row after creation "MUST then decide whether `GetOrder` needs a single consistent read." This is that specification, and — having considered it — **no such requirement is needed**: BR5 guarantees closing never touches an order's items, and BR7 guarantees no `AddItem` can commit after a close has committed. Together, these mean the items `GetOrder` observes after reading a still-`Open` order row are, at worst, the same items that existed at the moment the close committed (never more, since BR7 forbids a later addition, and never fewer, since items only accumulate) — a state that genuinely existed while the order was still `Open`, a moment before the close. There is no combination of `Status: Open` with an "impossible" item set for `GetOrder` to produce, so no snapshot/transaction change to `GetOrder`'s existing two-query read is required. (This would change if a future capability — e.g. `CancelOrder` — could mutate items and the order's own row together; that capability would need to revisit this question again.)

## Acceptance Criteria

- **AC1 — Happy path:** Given an authenticated user with the `restaurant.orders.close` permission and an `Open` order with items, when they close it, then the order's status becomes `Closed` and the response contains `OrderId`, `TableId`, `Status`, `CreatedAt`, `Items`, and `Total` reflecting the order's final state.
- **AC2 — Order not found:** Given an `OrderId` that does not exist, belongs to a different tenant, or is malformed (fails the route's `:guid` constraint), when the user attempts to close it, then the request is rejected as Not Found.
- **AC3 — Order already closed:** Given an order that is already `Closed`, when a user attempts to close it again without a matching `Idempotency-Key`, then the request is rejected as Conflict.
- **AC4 — Missing permission:** Given an authenticated user without the `restaurant.orders.close` permission, when they attempt to close an order, then the request is rejected as Forbidden.
- **AC5 — Unauthenticated request:** Given no valid authentication, when close-order is called, then the request is rejected as Unauthorized.
- **AC6 — Idempotent replay:** Given a prior successful close-order request with `Idempotency-Key: K`, when the same request (same `OrderId`) is repeated with the same key, then the order is not re-processed, and the response reflects the order's current state (still `Closed`) rather than an error.
- **AC7 — Idempotency key reuse conflict:** Given a prior successful close-order request with `Idempotency-Key: K` for `OrderId: A`, when a new request reuses key `K` for a different `OrderId: B`, then the request is rejected as Conflict.
- **AC8 — Closing an order with no items:** Given an `Open` order with no items, when it is closed, then the request succeeds and the response shows an empty item list and a total of zero.
- **AC9 — Closing frees the table:** Given an `Open` order for a table, when it is closed, then a subsequent `CreateOrder` request for the same table — **without** an `Idempotency-Key`, or with one not previously used — succeeds in creating a new order (no longer blocked by `create-order.md` BR2). This does **not** apply to a `CreateOrder` request that reuses the *original* order's own `Idempotency-Key`: per `create-order.md` BR6, that replays and returns the original (now `Closed`) order rather than creating a new one — reusing that key is not a way to open a replacement order for the table.
- **AC10 — A closed order rejects further AddItem calls:** Given a `Closed` order, when a user attempts to add a **new** item to it (or reuses an `Idempotency-Key` that does not match a claim already recorded against that order), then the request is rejected as Conflict, per `add-item.md` AC3. This does **not** apply to a replay of an `Idempotency-Key` that matches a claim recorded while the order was still `Open` (e.g. the `AddItem` call that committed before a racing `CloseOrder`, per AC13) — `add-item.md` BR6 already requires that replay to succeed as a no-op regardless of the order's current status, and this specification does not change that.
- **AC11 — Concurrent close attempts:** Given an `Open` order, when two close requests (without an `Idempotency-Key`, or with two different keys) race against it, then exactly one succeeds and the other is rejected as Conflict — the order is never left in an inconsistent state, and it is not possible for both to "succeed" independently.
- **AC12 — Concurrent identical idempotency key, same order:** Given an `Open` order, when two simultaneous close requests both target that **same** `OrderId` and carry the *same* `Idempotency-Key`, then exactly one transitions the order to `Closed` and claims the key, and the other returns that same result as a replay — neither is rejected as a conflict against the other, and the order is closed exactly once. (Mirrors `add-item.md`'s equivalent concurrency requirement for its own `Idempotency-Key`.) This is distinct from AC7: two requests reusing the same key for **different** `OrderId`s are a conflict, not a replay, whether concurrent or sequential.
- **AC13 — Race between a concurrent AddItem and CloseOrder:** Given an `Open` order, when an `AddItem` request and a `CloseOrder` request race against the same order, then no item is ever added *after* the close has committed. If `AddItem`'s mutation commits first (the order was genuinely still `Open` at that moment), both requests MAY succeed — the order closes with that item included, exactly as if the two calls had been made sequentially in that order; this is correct behavior, not a race to reject. If `CloseOrder`'s transition commits first instead, the `AddItem` request MUST be rejected as Conflict (per `add-item.md` AC3) once it reaches its guarded check. What the criterion forbids is an `AddItem` write landing on an order that, by the time that write happens, is already `Closed`.

## Domain Concepts

- **OrderStatus** (extended) — gains the `Closed` value. `Open` and `Closed` are now both reachable; `Cancelled` (or similar) remains introduced by a future `CancelOrder` specification.
- **Order** (existing, extended) — gains a `Close()` behavior (mirroring the existing `Open()`/`Reconstruct()` factories): an instance-level transition that enforces BR1 as a domain invariant (throwing if the order is not currently `Open`), independent of — and in addition to — the database-level guard in Data Requirements. `Reconstruct` must now also carry through the order's actual persisted `Status` (BR6), rather than assuming `Open`. No new fields are introduced (see Out of Scope re: a `ClosedAt` timestamp).

Update `glossary.md`'s `OrderStatus` entry once this specification is approved to reflect that `Closed` now exists.

## Security Requirements

- The endpoint requires authentication (Core Identity).
- The endpoint requires the `restaurant.orders.close` permission, scoped to the caller's tenant (Core Authorization/RBAC), per the permission naming convention in `architecture.md` §20. This is a new permission string; implementing this specification requires registering it alongside the existing ones (`Program.cs`'s authorization-policy list), the same way `restaurant.orders.read` was registered for `get-order.md`.
- The Tenant is resolved exclusively from the authenticated context, never from client input, per ADR 0002 rules 2–3.
- An order belonging to a different tenant MUST be treated as Not Found, not Forbidden, consistent with `create-order.md`'s AC3.

## Error Scenarios

| Scenario | Response |
|---|---|
| No authentication | 401 Unauthorized |
| Authenticated but missing `restaurant.orders.close` | 403 Forbidden |
| `OrderId` does not exist, belongs to a different tenant, or is malformed | 404 Not Found |
| Order is not currently `Open` (and no matching `Idempotency-Key` replay applies) | 409 Conflict |
| `Idempotency-Key` reused with a different `OrderId` | 409 Conflict |

The "order not found" and "`Idempotency-Key` reused with a different `OrderId`" rows can overlap for a request carrying a key already used for a *different* `OrderId` than the (possibly nonexistent) one in this request: per BR3, the claim check runs before order resolution, so that case always returns 409 Conflict, never 404, regardless of whether the requested `OrderId` happens to exist. This precedence only matters for a well-formed `OrderId` that simply doesn't exist — a malformed one never reaches the handler at all (it fails the route's `:guid` constraint first, per the note below), so it is always 404 regardless of any `Idempotency-Key`.

`OrderId` is a resource identifier in the request path (`POST /api/v1/restaurants/orders/{orderId:guid}/close`). The `:guid` route constraint is not itself something `add-item.md` specifies — it is an implementation detail of its existing endpoint (`OrderEndpoints.cs`'s `/{orderId:guid}/items` route) — but this specification requires the same constraint on its own route, for the same reason: without it, ASP.NET Core's minimal-API model binding would turn a malformed value into a 400 instead of failing route matching, which would contradict the 404 this table requires. A malformed value therefore fails route matching and surfaces as 404, the same as a well-formed but nonexistent `OrderId`.

Internal implementation details MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Reads:** the referenced Order (and, to build the full response per FR7, its `OrderItem` rows), scoped to the caller's tenant.
- **Writes:** the Order's `status` column transitions from `Open` to `Closed`. No other column changes, and no `OrderItem` rows are read-write beyond the existing read needed for the response.
- The `Open` → `Closed` transition MUST be guarded at the database level (e.g. `UPDATE restaurant.orders SET status = 'Closed' WHERE tenant_id = @tenantId AND id = @orderId AND status = 'Open'`, checking the affected row count) so that BR4's concurrency guarantee does not depend solely on an application-level check-then-write.
- The `OrderItem` rows read to build the response (FR7) MUST be read within the same transaction as the status transition (guarded per BR4, synchronized against a concurrent `AddItem` per BR7), *after* the `UPDATE` statement but *before that transaction commits* — not from an earlier, separate read taken before the order-scoped lock (e.g. `pg_advisory_xact_lock`, which releases at commit and so cannot be "held" afterward) was acquired, and not from a second transaction after the first has already committed and released the lock. Reading within the same still-open transaction sees that transaction's own uncommitted `UPDATE` (ordinary read-your-own-writes) and anything committed before it, without needing the lock to survive past commit. Otherwise a concurrent `AddItem` that legitimately commits before the close (AC13's "both succeed" case) could be silently missing from the very response that is supposed to reflect the order's final state.
- A migration is required: `restaurant.orders.status` currently has `CHECK (status = 'Open')` (`0002_restaurant_orders.sql`), which would reject any attempt to write `Closed` outright. This constraint MUST be widened to allow both `Open` and `Closed` (e.g. `CHECK (status IN ('Open', 'Closed'))`), while the existing partial unique index that enforces `create-order.md` BR2 (at most one `Open` order per table) MUST remain scoped to `status = 'Open'` only — it must not start also considering `Closed` orders.
- Tenant isolation on both the read and the write MUST follow ADR 0002 (application-level scoping plus PostgreSQL RLS).
- The idempotency mechanism reuses the same approach as `create-order.md`/`add-item.md` (a persisted key record keyed by `(TenantId, IdempotencyKey)`, retaining the `OrderId` per BR3), in its own dedicated table (e.g. `restaurant.close_order_idempotency_keys`) — following the precedent both existing capabilities already established (`order_idempotency_keys` for `CreateOrder`, `add_item_idempotency_keys` for `AddItem`; see their respective migrations). A single table shared across capabilities, keyed only by `(TenantId, IdempotencyKey)`, MUST NOT be used: a key a client happened to reuse across `CreateOrder`, `AddItem`, and this capability would otherwise collide, since nothing would distinguish which operation it belongs to.
- This new table is tenant-owned data like every other table in this module: its migration MUST enable and force Row-Level Security with a tenant-scoped policy (covering both the read and the `WITH CHECK` insert side) — following the pattern `0003_restaurant_order_idempotency_keys.sql`/`0009_restaurant_add_item_idempotency_keys.sql` already established — and grant the least-privilege application role `SELECT`/`INSERT` on it, in a corresponding grants migration (following `0004_grants.sql`/`0010_add_item_grants.sql`). Skipping either would either make key lookups inaccessible to the app role or, worse, leave them unenforced across tenants.
- Its `OrderId` column MUST be a tenant-consistent composite foreign key — `(tenant_id, order_id) REFERENCES restaurant.orders (tenant_id, id)`, using the composite `UNIQUE (tenant_id, id)` `restaurant.orders` already has (per `add-item.md`'s own precedent for `order_items`) — not a plain `REFERENCES orders(id)`. Since `orders.id` is globally unique, a plain foreign key would let a row satisfy referential integrity while still storing another tenant's `OrderId` under this tenant's key, a defect RLS alone would not catch (RLS governs which rows a query can see or write, not which foreign values a permitted row may reference).

## Integration Requirements

- **Depends on:** Core Identity & Authentication, Core Authorization/RBAC, tenant isolation infrastructure (ADR 0002) — the same minimal Core slice `CreateOrder`, `CreateTable`, `AddItem`, and `GetOrder` depend on.
- **Does not depend on:** Core Organization/Location, Core Platform Billing, Notifications, Kitchen, Payments, Inventory, full Menu Management, full Restaurant Settings management.
- **Publishes:** an `OrderClosed` domain event is defined for architectural consistency with `architecture.md` §15 and `create-order.md`'s own treatment of `OrderCreated` — this specification does not require any subscriber to exist yet; consumption (e.g. by a future Payment or reporting capability) is deferred until one exists.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** `Order.Close()` invariants — transitions an `Open` order to `Closed`; rejects closing an order that is not `Open` (BR1); leaves `Items`/`Total` unchanged (BR5). Also, `Order.Reconstruct` must be covered for preserving whatever `Status` it is given, not just `Open` (BR6).
- **Integration tests:** AC1–AC13 above, executed against the real API and database, including:
  - Cross-tenant isolation for the order lookup and the status update (per ADR 0002 rule 8), following the pattern established in prior specs' review rounds.
  - **Cross-tenant isolation for the new idempotency table:** a direct RLS read/write-isolation test for `close_order_idempotency_keys` (both the `SELECT` and the `WITH CHECK` insert side), following `RowLevelSecurityTests.cs`'s established pattern — the same gap closed for `order_items` in `add-item.md`'s own review.
  - Idempotency replay and conflict (AC6/AC7), following the same pattern as `create-order.md`/`add-item.md`.
  - **Validation (AC2's malformed-`OrderId` case):** a malformed `OrderId` is rejected as 404. No existing test in this codebase exercises a malformed `{orderId:guid}` route value (`AddItemEndpointTests.cs`'s own `AC2`/`AC4` cases use a valid but nonexistent `Guid`, not a malformed string) — this is the first test of that specific behavior, not a precedent to follow.
  - **Concurrent close attempts (BR4, AC11):** two simultaneous close requests against the same `Open` order (with no `Idempotency-Key`, or with two different keys) must result in exactly one success and one Conflict — proving the conditional-update guard, not a read-then-write race.
  - **Concurrent identical idempotency key, same order (BR3, AC12):** two simultaneous close requests, both against the same `OrderId` and carrying the same `Idempotency-Key`, must result in the order being closed exactly once, with one request transitioning it and the other replaying — following the same pattern as `add-item.md`'s `Concurrency_SameIdempotencyKeyAtTheSameTime_...` test.
  - **Race between AddItem and CloseOrder (BR7, AC13):** a concurrent `AddItem` and `CloseOrder` against the same order must never result in an item being added *after* the order is `Closed`. Assert this in both directions: when `AddItem`'s mutation is forced to commit first, both requests succeed and the resulting order is `Closed` with that item included; when `CloseOrder`'s transition is forced to commit first, the `AddItem` request is rejected as Conflict.
  - **Cross-capability regression (AC9, AC10):** closing an order actually unblocks `CreateOrder` for the same table, and actually blocks a subsequent `AddItem` against the now-closed order — and, critically, that `AddItem`'s rejection is based on the order's *actual* persisted status (BR6), not a rehydration bug that happens to also reject it for the wrong reason.

## Out of Scope

- `RemoveItem`, `CancelOrder` (future Ordering specifications).
- Payment, and any linkage between closing an order and a payment having been made — closing here is purely a status transition, not a checkout/settlement step.
- A `ClosedAt` timestamp or any other new field on `Order` — not required by any functional requirement above; add it if and when a future capability (e.g. reporting, or Payment) actually needs it.
- Kitchen routing/ticket completion signals.
- Reopening a closed order.
- Any bulk/batch close operation (e.g. "close all tables at end of day").

## Open Questions

None remaining that block this specification. Resolved before drafting:

- **Closing an order with no items:** confirmed allowed (AC8) — requiring at least one item would be an invented business rule with no operational justification, and would leave "a table that never ordered" with no valid way to free the table.
