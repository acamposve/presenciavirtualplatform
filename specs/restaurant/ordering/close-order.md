# Specification: Ordering / CloseOrder

**Status:** Draft
**Bounded Context:** Restaurant
**Business Capability:** Ordering
**Related ADRs:** [ADR 0002 — Tenant Isolation Strategy](../../../docs/adr/0002-tenant-isolation-strategy.md)
**Last Updated:** 2026-09-13

This specification follows `constitution.md` Article I. It requires human review and approval before implementation begins, per Article 1.4.

---

## Business Objective

Allow restaurant staff to close an order once service for that table is finished, so the order stops accepting further items and the table becomes available for a new order. This is the fourth step of the order lifecycle already sketched in `create-order.md`, `add-item.md`, and `get-order.md`:

```text
CreateOrder → AddItem → GetOrder → (Kitchen) → CloseOrder → Payment
```

## Actors

- **Waiter / Front-of-house staff** — closes the order for a table once service is complete.

## Business Context

`CloseOrder` is the last state transition this specification's Ordering capability defines. `Payment` (shown after it in the lifecycle diagram above) is a separate, not-yet-written capability — closing an order here does not perform or require any payment step; it only marks the order as no longer `Open`. `RemoveItem` and `CancelOrder` remain separate, not-yet-written specifications.

## Functional Requirements

1. An authorized user MUST be able to close an existing, `Open` order belonging to their own tenant, given an `OrderId`.
2. The system MUST reject the request if the order does not exist within the caller's tenant.
3. The system MUST reject the request if the order is not currently in the `Open` status (e.g. it was already closed).
4. Closing an order MUST succeed regardless of how many items it has — an order with zero items (e.g. a table that sat down and left without ordering) MUST be closeable, not rejected as invalid. **Decided** (see Open Questions): requiring at least one item would add a business rule nobody asked for and would leave "table freed up a table with no consumption" with no valid path.
5. Once closed, the order MUST NOT accept further `AddItem` calls (already guaranteed by `add-item.md` BR1/AC3, which rejects any `AddItem` against a non-`Open` order — introducing the `Closed` status value is what makes that existing guarantee reachable for the first time).
6. Once closed, the order's table MUST become eligible for a new `CreateOrder` (already guaranteed by `create-order.md` BR2, which only blocks a new order while the table has an order in `Open` status).
7. The system MUST return the order's current identifier, table, status, creation timestamp, items, and total after closing — the same full representation `get-order.md` returns, since this is effectively the order's final state at the moment of closing.
8. The system MUST support an optional `Idempotency-Key` request header, with the same semantics as `CreateOrder`'s and `AddItem`'s (FR7/BR6 in `create-order.md`, FR8/BR6 in `add-item.md`): a repeated request with the same key does not attempt to re-close an already-closed-by-this-key order as a conflict; it returns that order's current state instead.

## Non-Functional Requirements

- **Tenant isolation:** enforced per ADR 0002 on every read and write involved (order lookup, status update).
- **Consistency:** the closed status must be immediately visible to a subsequent read (`GetOrder` will no longer return the order, per `get-order.md` FR3; a direct read by `OrderId`, if any future capability adds one, must reflect `Closed`).
- **Observability:** the request must be traceable via structured logging with a correlation ID, per `constitution.md` Article XI. As with `create-order.md`, `create-table.md`, and `add-item.md`, no such mechanism exists yet anywhere in the codebase; this specification does not introduce one on its own; see those specs' own NFR sections for the same note.
- **Idempotency:** guaranteed for repeated requests carrying the same `Idempotency-Key`; a request without one has no idempotency guarantee — same as `CreateOrder`/`AddItem`.

## Business Rules

- **BR1:** An order can only transition from `Open` to `Closed`. Attempting to close an order that is not currently `Open` (e.g. already `Closed`) MUST be rejected, except when the request replays a prior successful close via a matching `Idempotency-Key` (BR3).
- **BR2:** Closing an order does not require it to have any items; an order with zero items and a total of zero MAY be closed.
- **BR3:** An `Idempotency-Key`, when supplied, uniquely identifies a single logical close-order attempt within a tenant — scoped by `(TenantId, IdempotencyKey)` alone, not also by `OrderId`, for the same reason `add-item.md` BR6 gives (a key scoped by `(TenantId, OrderId, IdempotencyKey)` could not detect reuse against a different `OrderId` as a conflict). The stored record retains the original `OrderId`, so a replay (same `OrderId`) can be told apart from a conflicting reuse (a different `OrderId`). A replay MUST NOT attempt to transition the order's status again; the response reflects the order's **current** state at the time of the replay, not a frozen snapshot of the original close.
  - The `Open` → `Closed` transition and claiming the idempotency key MUST commit as a single atomic operation, for the same reason `create-order.md` BR6 and `add-item.md` BR6 require it: if two concurrent requests supply the same key, at most one may transition the order and claim the key; the other MUST roll back and re-read the order's current state instead of double-transitioning or racing.
- **BR4:** The `Open` → `Closed` transition itself MUST be atomic against concurrent close attempts that do **not** share an `Idempotency-Key` — e.g. a conditional update (`UPDATE ... WHERE status = 'Open'`) that only one concurrent request can apply. The request that loses this race MUST be rejected as a conflict (BR1), not silently succeed a second time or corrupt the order's status.
- **BR5:** Closing an order is a pure status transition. It MUST NOT modify the order's items or recompute/store its total — `Total` remains, as always (`add-item.md` BR5), the sum of the order's persisted items' line totals.

## Acceptance Criteria

- **AC1 — Happy path:** Given an authenticated user with the `restaurant.orders.close` permission and an `Open` order with items, when they close it, then the order's status becomes `Closed` and the response contains `OrderId`, `TableId`, `Status`, `CreatedAt`, `Items`, and `Total` reflecting the order's final state.
- **AC2 — Order not found:** Given an `OrderId` that does not exist, or belongs to a different tenant, when the user attempts to close it, then the request is rejected as Not Found.
- **AC3 — Order already closed:** Given an order that is already `Closed`, when a user attempts to close it again without a matching `Idempotency-Key`, then the request is rejected as Conflict.
- **AC4 — Missing permission:** Given an authenticated user without the `restaurant.orders.close` permission, when they attempt to close an order, then the request is rejected as Forbidden.
- **AC5 — Unauthenticated request:** Given no valid authentication, when close-order is called, then the request is rejected as Unauthorized.
- **AC6 — Idempotent replay:** Given a prior successful close-order request with `Idempotency-Key: K`, when the same request (same `OrderId`) is repeated with the same key, then the order is not re-processed, and the response reflects the order's current state (still `Closed`) rather than an error.
- **AC7 — Idempotency key reuse conflict:** Given a prior successful close-order request with `Idempotency-Key: K` for `OrderId: A`, when a new request reuses key `K` for a different `OrderId: B`, then the request is rejected as Conflict.
- **AC8 — Closing an order with no items:** Given an `Open` order with no items, when it is closed, then the request succeeds and the response shows an empty item list and a total of zero.
- **AC9 — Closing frees the table:** Given an `Open` order for a table, when it is closed, then a subsequent `CreateOrder` request for the same table succeeds (no longer blocked by `create-order.md` BR2).
- **AC10 — A closed order rejects further AddItem calls:** Given a `Closed` order, when a user attempts to add an item to it, then the request is rejected as Conflict, per `add-item.md` AC3.
- **AC11 — Concurrent close attempts:** Given an `Open` order, when two close requests (without an `Idempotency-Key`, or with two different keys) race against it, then exactly one succeeds and the other is rejected as Conflict — the order is never left in an inconsistent state, and it is not possible for both to "succeed" independently.

## Domain Concepts

- **OrderStatus** (extended) — gains the `Closed` value. `Open` and `Closed` are now both reachable; `Cancelled` (or similar) remains introduced by a future `CancelOrder` specification.
- **Order** (existing, extended) — its `Status` can now change after creation, via this capability's transition. No new fields are introduced (see Out of Scope re: a `ClosedAt` timestamp).

Update `glossary.md`'s `OrderStatus` entry once this specification is approved to reflect that `Closed` now exists.

## Security Requirements

- The endpoint requires authentication (Core Identity).
- The endpoint requires the `restaurant.orders.close` permission, scoped to the caller's tenant (Core Authorization/RBAC), per the permission naming convention in `architecture.md` §20.
- The Tenant is resolved exclusively from the authenticated context, never from client input, per ADR 0002 rules 2–3.
- An order belonging to a different tenant MUST be treated as Not Found, not Forbidden, consistent with `create-order.md`'s AC3.

## Error Scenarios

| Scenario | Response |
|---|---|
| No authentication | 401 Unauthorized |
| Authenticated but missing `restaurant.orders.close` | 403 Forbidden |
| `OrderId` missing or malformed in request | 400 Bad Request |
| `OrderId` does not exist, or belongs to a different tenant | 404 Not Found |
| Order is not currently `Open` (and no matching `Idempotency-Key` replay applies) | 409 Conflict |
| `Idempotency-Key` reused with a different `OrderId` | 409 Conflict |

Internal implementation details MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Reads:** the referenced Order (and, to build the full response per FR7, its `OrderItem` rows), scoped to the caller's tenant.
- **Writes:** the Order's `status` column transitions from `Open` to `Closed`. No other column changes, and no `OrderItem` rows are read-write beyond the existing read needed for the response.
- The `Open` → `Closed` transition MUST be guarded at the database level (e.g. `UPDATE restaurant.orders SET status = 'Closed' WHERE tenant_id = @tenantId AND id = @orderId AND status = 'Open'`, checking the affected row count) so that BR4's concurrency guarantee does not depend solely on an application-level check-then-write.
- Tenant isolation on both the read and the write MUST follow ADR 0002 (application-level scoping plus PostgreSQL RLS).
- The idempotency mechanism reuses the same approach as `create-order.md`/`add-item.md` (a persisted key record keyed by `(TenantId, IdempotencyKey)`, retaining the `OrderId` per BR3); whether it is a shared table across Ordering use cases or a per-use-case table remains an implementation decision, not a business rule.

## Integration Requirements

- **Depends on:** Core Identity & Authentication, Core Authorization/RBAC, tenant isolation infrastructure (ADR 0002) — the same minimal Core slice `CreateOrder`, `CreateTable`, `AddItem`, and `GetOrder` depend on.
- **Does not depend on:** Core Organization/Location, Core Platform Billing, Notifications, Kitchen, Payments, Inventory, full Menu Management, full Restaurant Settings management.
- **Publishes:** no event is defined for this version — a future Payment or reporting capability that needs to react to an order closing is deferred until it exists, consistent with `create-order.md`'s treatment of `OrderCreated`.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** none beyond what already exists for `Order`/`OrderStatus` — this capability introduces one new enum value and a status transition, not new derived behavior (`Total` continues to be derived exactly as `add-item.md` BR5 already specifies).
- **Integration tests:** AC1–AC11 above, executed against the real API and database, including:
  - Cross-tenant isolation for the order lookup and the status update (per ADR 0002 rule 8), following the pattern established in prior specs' review rounds.
  - Idempotency replay and conflict (AC6/AC7), following the same pattern as `create-order.md`/`add-item.md`.
  - **Concurrent close attempts (BR4, AC11):** two simultaneous close requests against the same `Open` order (with no `Idempotency-Key`, or with two different keys) must result in exactly one success and one Conflict — proving the conditional-update guard, not a read-then-write race.
  - **Cross-capability regression (AC9, AC10):** closing an order actually unblocks `CreateOrder` for the same table, and actually blocks a subsequent `AddItem` against the now-closed order.

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
