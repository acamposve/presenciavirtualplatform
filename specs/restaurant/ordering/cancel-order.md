# Specification: Ordering / CancelOrder

**Status:** Draft
**Bounded Context:** Restaurant
**Business Capability:** Ordering
**Related ADRs:** [ADR 0002 — Tenant Isolation Strategy](../../../docs/adr/0002-tenant-isolation-strategy.md)
**Last Updated:** 2026-09-13

This specification follows `constitution.md` Article I. It requires human review and approval before implementation begins, per Article 1.4.

---

## Business Objective

Allow restaurant staff to void an order that should never be paid — opened by mistake, a duplicate of another order, or a table that left before any service was actually rendered — so it stops accepting items and its table becomes available again, without that order being confused with a normally completed (`Closed`) one. `close-order.md` already covers the "service finished normally, proceed to payment" transition; this specification covers the other terminal outcome the lifecycle diagram has always reserved a value for:

```text
CreateOrder ─┬─→ AddItem → (Kitchen) → CloseOrder → Payment
             └─→ CancelOrder (void, no payment — reachable from any Open order,
                              with or without items, per BR1/FR4/AC1)
```

## Actors

- **Waiter / Front-of-house staff** — cancels an order for a table they are serving, typically right after realizing it was opened in error or a duplicate.
- **Restaurant Manager** — indirectly affected (an order voided rather than paid affects revenue reporting once that exists), not a direct actor of this use case.

## Business Context

`close-order.md` §Business Context already named `CancelOrder` as a distinct, not-yet-written specification, and its own BR8 explicitly flagged that "a future capability — e.g. `CancelOrder` — could mutate items and the order's own row together" and would need to revisit `GetOrder`'s single-read question if it did. This specification deliberately does **not** do that: like `CloseOrder`, `CancelOrder` is a pure status transition that never touches `OrderItem` rows, so `close-order.md` BR8's conclusion (no consistent-read requirement needed for `GetOrder`) continues to hold unchanged — see BR5.

`RemoveItem` remains a separate, not-yet-written specification: `CancelOrder` voids an entire order, it does not remove individual lines from one that continues.

## Functional Requirements

1. An authorized user MUST be able to cancel an existing, `Open` order belonging to their own tenant, given an `OrderId`.
2. The system MUST reject the request if the order does not exist within the caller's tenant.
3. The system MUST reject the request if the order is not currently in the `Open` status (e.g. it was already closed, or already cancelled).
4. Cancelling an order MUST succeed regardless of how many items it has — an order with items already added (e.g. a duplicate order created by mistake, or a table that left before the kitchen started) MUST be cancellable, not rejected for having items. There is no requirement to first remove an order's items before it can be cancelled.
5. Once cancelled, the order MUST NOT accept further `AddItem` calls, exactly as `close-order.md` FR5/BR7 already require for a `Closed` order — this specification reuses that same guarantee rather than introducing a second one (see BR7).
6. Once cancelled, the order's table MUST become eligible for a new `CreateOrder` (already guaranteed by `create-order.md` BR2, which only blocks a new order while the table has an order in `Open` status).
7. The system MUST return the order's current identifier, table, status, creation timestamp, items, and total after cancelling — the same full representation `get-order.md` and `close-order.md` return, since this is effectively the order's final state at the moment of cancelling.
8. The system MUST support an optional `Idempotency-Key` request header, with the same semantics as `CreateOrder`'s, `AddItem`'s, and `CloseOrder`'s (FR7/BR6 in `create-order.md`, FR8/BR6 in `add-item.md`, FR8/BR3 in `close-order.md`): a repeated request with the same key does not attempt to re-cancel an already-cancelled-by-this-key order as a conflict; it returns that order's current state instead.

## Non-Functional Requirements

- **Tenant isolation:** enforced per ADR 0002 on every read and write involved (order lookup, status update).
- **Consistency:** the cancelled status must be immediately visible to a subsequent read (`GetOrder` will no longer return the order, per `get-order.md` FR3, the same as for `Closed`).
- **Observability:** the request must be traceable via structured logging with a correlation ID, per `constitution.md` Article XI. As with every other Ordering specification, no such mechanism exists yet anywhere in the codebase; this specification does not introduce one on its own.
- **Idempotency:** guaranteed for repeated requests carrying the same `Idempotency-Key`; a request without one has no idempotency guarantee — same as `CreateOrder`/`AddItem`/`CloseOrder`.

## Business Rules

- **BR1:** An order can only transition from `Open` to `Cancelled`. Attempting to cancel an order that is not currently `Open` (e.g. already `Closed`, or already `Cancelled`) MUST be rejected, except when the request replays a prior successful cancellation via a matching `Idempotency-Key` (BR3). In particular, a `Closed` order MUST NOT be cancelled — undoing a completed service is a distinct, not-yet-written concern (e.g. a refund/void-after-close process), not this capability (see Out of Scope).
- **BR2:** Cancelling an order does not require it to have no items, and does not remove or alter any item it already has. An order with existing lines MUST remain cancellable, and its persisted `OrderItem` rows MUST remain untouched by this operation — cancelling is not a deletion of the order or its history. Since `Total` is never itself stored (BR5), "unchanged" here means the response's derived `Total` after cancelling equals what it was immediately before, because the items it is derived from did not change.
- **BR3:** An `Idempotency-Key`, when supplied, uniquely identifies a single logical cancel-order attempt within a tenant — scoped by `(TenantId, IdempotencyKey)` alone, not also by `OrderId`, for the same reason `close-order.md` BR3 gives (a key scoped by `(TenantId, OrderId, IdempotencyKey)` could not detect reuse against a different `OrderId` as a conflict). The stored record retains the original `OrderId`, so a replay (same `OrderId`) can be told apart from a conflicting reuse (a different `OrderId`). A replay MUST NOT attempt to transition the order's status again; the response reflects the order's **current** state at the time of the replay, not a frozen snapshot of the original cancellation.
  - The `Open` → `Cancelled` transition and claiming the idempotency key MUST commit as a single atomic operation, for the same reason `close-order.md` BR3 requires it: if two concurrent requests supply the same key, at most one may transition the order and claim the key; the other MUST roll back and re-read the order's current state instead of double-transitioning or racing.
  - When an `Idempotency-Key` is supplied, the existing claim for it (if any) MUST be checked **before** resolving the requested `OrderId` in any way, mirroring `CloseOrderHandler`'s established precedence for the identical reason: a key reused against a different `OrderId` must deterministically return 409 Conflict (AC7), and the claim check alone keeps AC2's tenant-isolation guarantee intact without ever needing to resolve the other `OrderId`'s existence or ownership.
- **BR4:** The `Open` → `Cancelled` transition itself MUST be atomic against concurrent cancel attempts that do **not** share an `Idempotency-Key` — e.g. a conditional update (`UPDATE ... WHERE status = 'Open'`) that only one concurrent request can apply. The request that loses this race MUST be rejected as a conflict (BR1), not silently succeed a second time or corrupt the order's status. This same conditional update, being scoped to `WHERE status = 'Open'`, is also what makes a race between a concurrent `CancelOrder` and `CloseOrder` against the same order resolve to exactly one winner (AC14): whichever transition's `UPDATE` commits first flips `status` away from `Open`, so the other's `UPDATE` (still conditioned on `status = 'Open'`) affects zero rows and is rejected as a conflict.
- **BR5:** Cancelling an order is a pure status transition. It MUST NOT modify the order's items or recompute/store its total — `Total` remains, as always (`add-item.md` BR5), the sum of the order's persisted items' line totals. This is the same guarantee `close-order.md` BR5 makes for `CloseOrder`, and is why `close-order.md` BR8's conclusion (no single consistent-read requirement for `GetOrder`) continues to hold: this specification does not mutate items alongside the order's own row either.
- **BR6:** `OrderStatus` gains a `Cancelled` value, structurally parallel to `Closed`: both are terminal states reachable only from `Open`, and neither is reachable from the other. There is no transition between `Closed` and `Cancelled` in either direction.
- **BR7 (reuses an existing guarantee — no new implementation gap):** `close-order.md` BR6 and BR7 already fixed `Order.Reconstruct` to carry through the row's real persisted `Status`, and already made `AddItem`'s merge acquire the shared, order-scoped `OrderLock` (keyed by `(TenantId, OrderId)`) before its authoritative status check — a check written generically as "is the order's status `Open`," not "is it specifically not `Closed`." Because of that, a `Cancelled` order is *already* correctly rejected by an `AddItem` call today, and a race between a concurrent `AddItem` and a `CancelOrder` is *already* correctly serialized by that same lock, the moment `CancelOrder`'s own transition acquires `OrderLock.Key(tenantId, orderId)` before its conditional `UPDATE` — exactly as `CloseOrder`'s implementation already does. This specification requires `CancelOrder` to acquire that same lock the same way; it does **not** require any further change to `AddItem`, `Order.Reconstruct`, or the lock itself.

## Acceptance Criteria

- **AC1 — Happy path, no items:** Given an authenticated user with the `restaurant.orders.cancel` permission and an `Open` order with no items, when they cancel it, then the order's status becomes `Cancelled` and the response contains `OrderId`, `TableId`, `Status`, `CreatedAt`, `Items` (empty), and `Total` (zero).
- **AC2 — Order not found:** Given an `OrderId` that does not exist, belongs to a different tenant, or is malformed (fails the route's `:guid` constraint), when the user attempts to cancel it, then the request is rejected as Not Found. `OrderId` MUST be bound exclusively from the request path, via `POST /api/v1/restaurants/orders/{orderId:guid}/cancel` — the same routing shape and `:guid` constraint `close-order.md`'s own `POST .../{orderId:guid}/close` route uses — never from the request body or query string, so this criterion cannot be satisfied by a route that would let a malformed value bind as something other than a 404.
- **AC3 — Order already cancelled:** Given an order that is already `Cancelled`, when a user attempts to cancel it again without a matching `Idempotency-Key`, then the request is rejected as Conflict.
- **AC4 — Order already closed:** Given an order that is already `Closed`, when a user attempts to cancel it, then the request is rejected as Conflict — a completed order cannot be voided through this capability.
- **AC5 — Missing permission:** Given an authenticated user without the `restaurant.orders.cancel` permission, when they attempt to cancel an order, then the request is rejected as Forbidden.
- **AC6 — Unauthenticated request:** Given no valid authentication, when cancel-order is called, then the request is rejected as Unauthorized.
- **AC7 — Idempotency key reuse conflict:** Given a prior successful cancel-order request with `Idempotency-Key: K` for `OrderId: A`, when a new request reuses key `K` for a different `OrderId: B`, then the request is rejected as Conflict.
- **AC8 — Idempotent replay:** Given a prior successful cancel-order request with `Idempotency-Key: K`, when the same request (same `OrderId`) is repeated with the same key, then the order is not re-processed, and the response reflects the order's current state (still `Cancelled`) rather than an error.
- **AC9 — Cancelling an order with existing items:** Given an `Open` order with one or more items already added, when it is cancelled, then the request succeeds and the response shows those same items and their total, unchanged.
- **AC10 — Cancelling frees the table:** Given an `Open` order for a table, when it is cancelled, then a subsequent `CreateOrder` request for the same table — without an `Idempotency-Key`, or with one not previously used — succeeds in creating a new order (no longer blocked by `create-order.md` BR2), the same as `close-order.md` AC9.
- **AC11 — A cancelled order rejects further AddItem calls:** Given a `Cancelled` order, when a user attempts to add a new item to it (or reuses an `Idempotency-Key` that does not match a claim already recorded against that order), then the request is rejected as Conflict, per `add-item.md` AC3. This does not apply to a replay of an `Idempotency-Key` that matches a claim recorded while the order was still `Open` (e.g. an `AddItem` call that committed before a racing `CancelOrder`, per AC13) — `add-item.md` BR6 already requires that replay to succeed as a no-op regardless of the order's current status.
- **AC12 — Concurrent cancel attempts:** Given an `Open` order, when two cancel requests (without an `Idempotency-Key`, or with two different keys) race against it, then exactly one succeeds and the other is rejected as Conflict.
- **AC13 — Concurrent identical idempotency key, same order:** Given an `Open` order, when two simultaneous cancel requests both target that **same** `OrderId` and carry the *same* `Idempotency-Key`, then exactly one transitions the order to `Cancelled` and claims the key, and the other returns that same result as a replay — neither is rejected as a conflict against the other, and the order is cancelled exactly once. (Mirrors `close-order.md` AC12.) This is distinct from AC7: two requests reusing the same key for **different** `OrderId`s are a conflict, not a replay, whether concurrent or sequential.
- **AC14 — Race between a concurrent CancelOrder and CloseOrder:** Given an `Open` order, when a `CancelOrder` request and a `CloseOrder` request race against the same order, then exactly one of them commits its transition and the other is rejected as Conflict — the order never ends up ambiguously `Closed` and `Cancelled`, and it is not possible for both to "succeed."
- **AC15 — Race between a concurrent AddItem and CancelOrder:** Given an `Open` order, when a **new** `AddItem` request (not a replay of an `Idempotency-Key` already claimed while the order was `Open`, per AC11) and a `CancelOrder` request race against the same order, then no item is ever added *after* the cancellation has committed — mirroring `close-order.md` AC13 exactly, with `CancelOrder` in place of `CloseOrder`. If `AddItem`'s mutation commits first, both requests MAY succeed (the order is cancelled with that item still present in its history, per BR2); if `CancelOrder`'s transition commits first, the `AddItem` request MUST be rejected as Conflict once it reaches its guarded check. This is distinct from AC11's replay exception, which is unaffected by which request commits first.

## Domain Concepts

- **OrderStatus** (extended) — gains the `Cancelled` value (BR6). `Open`, `Closed`, and `Cancelled` are now all reachable; `Open` is the only non-terminal one.
- **Order** (existing, extended) — gains a `Cancel()` behavior, structurally parallel to the existing `Close()` (`close-order.md`): an instance-level transition that enforces BR1 as a domain invariant (throwing if the order is not currently `Open`), independent of — and in addition to — the database-level guard in Data Requirements. No new fields are introduced (see Out of Scope re: a `CancellationReason`).

Update `glossary.md`'s `OrderStatus` entry once this specification is approved to reflect that `Cancelled` now exists.

## Security Requirements

- The endpoint requires authentication (Core Identity).
- The endpoint requires the `restaurant.orders.cancel` permission, scoped to the caller's tenant (Core Authorization/RBAC), per the permission naming convention in `architecture.md` §20. This is a new permission string; implementing this specification requires registering it alongside the existing ones (`Program.cs`'s authorization-policy list), the same way `restaurant.orders.close` was registered for `close-order.md`.
- The Tenant is resolved exclusively from the authenticated context, never from client input, per ADR 0002 rules 2–3.
- An order belonging to a different tenant MUST be treated as Not Found, not Forbidden, consistent with `create-order.md`'s AC3.

## Error Scenarios

| Scenario | Response |
|---|---|
| No authentication | 401 Unauthorized |
| Authenticated but missing `restaurant.orders.cancel` | 403 Forbidden |
| `OrderId` does not exist, belongs to a different tenant, or is malformed | 404 Not Found |
| Order is not currently `Open` (already `Cancelled` or already `Closed`, and no matching `Idempotency-Key` replay applies) | 409 Conflict |
| `Idempotency-Key` reused with a different `OrderId` | 409 Conflict |

The "order not found" and "`Idempotency-Key` reused with a different `OrderId`" rows can overlap exactly as documented in `close-order.md`'s own Error Scenarios note: since BR3's claim check runs before order resolution, a key already used for a different `OrderId` always returns 409, never 404, regardless of whether the requested `OrderId` happens to exist. A malformed `OrderId` fails the route's `:guid` constraint before reaching the handler at all, so it is always 404 regardless of any `Idempotency-Key` — the same reasoning `close-order.md` gives in full.

Internal implementation details MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Reads:** the referenced Order (and, to build the full response per FR7, its `OrderItem` rows), scoped to the caller's tenant.
- **Writes:** the Order's `status` column transitions from `Open` to `Cancelled`. No other column changes, and no `OrderItem` rows are written.
- The `Open` → `Cancelled` transition MUST be guarded at the database level (e.g. `UPDATE restaurant.orders SET status = 'Cancelled' WHERE tenant_id = @tenantId AND id = @orderId AND status = 'Open'`, checking the affected row count) so that BR4's concurrency guarantee does not depend solely on an application-level check-then-write — the same pattern `close-order.md` establishes for its own `UPDATE`.
- The transition MUST acquire the same shared, order-scoped advisory lock `close-order.md` BR7 introduced (`OrderLock.Key(tenantId, orderId)`) before its conditional `UPDATE`, for the same reason: to serialize against a concurrent `AddItem`'s merge (BR7/AC15) and against a concurrent `CloseOrder` (BR4/AC14). This is the existing lock, acquired the same way `CloseOrderHandler`'s own transition already acquires it — not a new lock.
- The `OrderItem` rows read to build the response (FR7) MUST be read within the same transaction as the status transition, *after* the `UPDATE` statement but *before that transaction commits* — the identical requirement `close-order.md`'s own Data Requirements impose on `CloseOrder`, for the identical reason (a concurrent `AddItem` that legitimately commits before the cancellation, per AC15's "both succeed" case, must not be silently missing from the response).
- A migration is required: `restaurant.orders.status` currently has `CHECK (status IN ('Open', 'Closed'))` (`0011_restaurant_orders_status_closed.sql`), which would reject any attempt to write `Cancelled` outright. This constraint MUST be widened to allow `Open`, `Closed`, and `Cancelled` (e.g. `CHECK (status IN ('Open', 'Closed', 'Cancelled'))`), while the existing partial unique index that enforces `create-order.md` BR2 (at most one `Open` order per table) MUST remain scoped to `status = 'Open'` only.
- Tenant isolation on both the read and the write MUST follow ADR 0002 (application-level scoping plus PostgreSQL RLS).
- The idempotency mechanism reuses the same approach as `create-order.md`/`add-item.md`/`close-order.md` (a persisted key record keyed by `(TenantId, IdempotencyKey)`, retaining the `OrderId` per BR3), in its own dedicated table (`restaurant.cancel_order_idempotency_keys`) — following the established precedent (`order_idempotency_keys`, `add_item_idempotency_keys`, `close_order_idempotency_keys`) that each vertical slice gets its own idempotency table, never one shared across capabilities.
- This new table is tenant-owned data like every other table in this module: its migration MUST enable and force Row-Level Security with a tenant-scoped policy (covering both the read and the `WITH CHECK` insert side), following the pattern `0012_restaurant_close_order_idempotency_keys.sql` already established, and grant the least-privilege application role `SELECT`/`INSERT` on it in a corresponding grants migration (following `0013_close_order_grants.sql`).
- Its `OrderId` column MUST be a tenant-consistent composite foreign key — `(tenant_id, order_id) REFERENCES restaurant.orders (tenant_id, id)`, using the composite `UNIQUE (tenant_id, id)` `restaurant.orders` already has — not a plain `REFERENCES orders(id)`, for the same reason `close-order.md`'s own idempotency table requires it.

## Integration Requirements

- **Depends on:** Core Identity & Authentication, Core Authorization/RBAC, tenant isolation infrastructure (ADR 0002) — the same minimal Core slice `CreateOrder`, `CreateTable`, `AddItem`, `GetOrder`, and `CloseOrder` depend on.
- **Does not depend on:** Core Organization/Location, Core Platform Billing, Notifications, Kitchen, Payments, Inventory, full Menu Management, full Restaurant Settings management.
- **Publishes:** an `OrderCancelled` domain event is defined for architectural consistency with `architecture.md` §15 and `create-order.md`'s and `close-order.md`'s own treatment of their domain events — this specification does not require any subscriber to exist yet; consumption (e.g. by a future reporting capability distinguishing paid revenue from voided orders) is deferred until one exists.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** `Order.Cancel()` invariants — transitions an `Open` order to `Cancelled`; rejects cancelling an order that is not `Open` (BR1, including specifically an already-`Closed` order per AC4); leaves `Items`/`Total` unchanged (BR2/BR5).
- **Integration tests:** AC1–AC15 above, executed against the real API and database, including:
  - Cross-tenant isolation for the order lookup and the status update (per ADR 0002 rule 8), following the pattern established in `close-order.md`'s own review.
  - **Cross-tenant isolation for the new idempotency table:** a direct RLS read/write-isolation test for `cancel_order_idempotency_keys` (both the `SELECT` and the `WITH CHECK` insert side), following `RowLevelSecurityTests.cs`'s established pattern.
  - Idempotency replay and conflict (AC7/AC8), following the same pattern as `close-order.md`.
  - **Validation (AC2's malformed-`OrderId` case):** a malformed `OrderId` is rejected as 404, following `close-order.md`'s own precedent test.
  - **Concurrent cancel attempts (BR4, AC12):** two simultaneous cancel requests against the same `Open` order must result in exactly one success and one Conflict — proving the conditional-update guard, not a read-then-write race.
  - **Concurrent identical idempotency key, same order (BR3, AC13):** two simultaneous cancel requests, both against the same `OrderId` and carrying the same `Idempotency-Key`, must result in the order being cancelled exactly once, with one request transitioning it and the other replaying — following the same pattern as `close-order.md`'s equivalent AC12 test, proving the idempotency claim and the status transition commit together rather than as two independent writes.
  - **Race between CancelOrder and CloseOrder (BR4, AC14):** a concurrent `CancelOrder` and `CloseOrder` against the same order must result in exactly one of the two transitions committing, deterministically forced in both directions (cancel-wins and close-wins), with the loser rejected as Conflict in each case.
  - **Race between AddItem and CancelOrder (BR7, AC15):** a concurrent `AddItem` and `CancelOrder` against the same order must never result in an item being added *after* the order is `Cancelled` — the same two-direction deterministic test `close-order.md` requires for its own AC13, with `CancelOrder` substituted for `CloseOrder`.
  - **Cross-capability regression (AC10, AC11):** cancelling an order actually unblocks `CreateOrder` for the same table, and actually blocks a subsequent `AddItem` against the now-cancelled order.

## Out of Scope

- `RemoveItem` (future Ordering specification) — removing individual lines from an order that continues, as distinct from voiding the whole order.
- Any refund, void-after-close, or "undo a completed order" process — cancelling only ever applies to an `Open` order (BR1); a `Closed` order is explicitly not reachable through this capability (AC4).
- A `CancellationReason` field or any other new field on `Order` — not required by any functional requirement above, mirroring `close-order.md`'s identical decision about a `ClosedAt` timestamp; add it if and when a future capability (e.g. reporting) actually needs it.
- Kitchen routing/ticket voiding signals.
- Reopening a cancelled order.
- Any bulk/batch cancel operation.
- Reporting that distinguishes cancelled orders' revenue impact from closed ones — a future capability once any revenue reporting exists at all.

## Open Questions

None remaining that block this specification.
