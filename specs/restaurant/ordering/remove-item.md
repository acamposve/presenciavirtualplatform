# Specification: Ordering / RemoveItem

**Status:** Draft
**Bounded Context:** Restaurant
**Business Capability:** Ordering
**Related ADRs:** [ADR 0002 — Tenant Isolation Strategy](../../../docs/adr/0002-tenant-isolation-strategy.md)
**Last Updated:** 2026-10-20

This specification follows `constitution.md` Article I. It requires human review and approval before implementation begins, per Article 1.4.

---

## Business Objective

Allow restaurant staff to take a line back off an order that is still open — the guest changed their mind, the wrong item was entered, or the kitchen has confirmed nothing was made — so the order's items and running total stop reflecting a line that will never be served or paid for.

`AddItem` (`specs/restaurant/ordering/add-item.md`) is currently the only capability that changes an order's contents, and it only ever adds or increases. That leaves a gap with real operational cost: the only way to correct a mistaken line today is to void the entire order with `CancelOrder` and start over, discarding the correct lines with it.

## Actors

- **Waiter / Front-of-house staff** — removes a line from an order for a table they are serving, typically moments after entering it.
- **Restaurant Manager** — not a direct actor. Once reporting exists, a removed line simply never appears in the order's final contents; no separate manager approval step is modeled (see Out of Scope).

## Business Context

```text
CreateOrder ─→ AddItem ─→ (Kitchen) ─→ CloseOrder ─→ Payment
                  │
                  └─→ RemoveItem   (drops an entire line while the order is still Open)
```

This specification covers only removing an already-added line from an order that is still `Open`. It sits alongside `AddItem` as the second way an open order's contents change, and is deliberately narrower than both neighbouring capabilities:

- `CancelOrder` (`specs/restaurant/ordering/cancel-order.md`) voids the **whole order**, including its contents and history, and moves the order to a terminal state. `RemoveItem` leaves the order `Open` and touches only the one line.
- `CloseOrder` (`specs/restaurant/ordering/close-order.md`) is unaffected: a removed line simply is not in the order at the moment it closes, so every existing guarantee about totals and final state continues to hold.

Reducing a line's quantity without removing it (e.g. "4 × Coke, make it 2") is a **different** capability, deliberately excluded here — see Out of Scope and Open Questions.

## Functional Requirements

1. An authorized user MUST be able to remove an existing line item from an existing, `Open` order belonging to their own tenant, identified by `OrderId` and `MenuItemId`.
2. The system MUST reject the request if the order does not exist within the caller's tenant.
3. The system MUST reject the request if the order is not currently in the `Open` status (already `Closed` or already `Cancelled`).
4. The system MUST reject the request if the order does not currently have a line for the given `MenuItemId`, including the case where the line was already removed by an earlier request.
5. Removing a line MUST delete that line entirely — it is not a quantity reduction, and it does not zero or re-price the line. A later `AddItem` for the same menu item starts a new line, snapshotted at the menu item's price at that later time (per `add-item.md` BR3/BR4).
6. The system MUST leave every other line, its quantity, and its `UnitPriceSnapshot` untouched.
7. The system MUST return the order's current items and total after the line is removed.
8. The system MUST support an optional `Idempotency-Key` request header, with the same semantics as `CreateOrder`'s, `AddItem`'s, `CloseOrder`'s and `CancelOrder`'s: a repeated request with the same key that matches the original request does not attempt to remove the line a second time (which would otherwise fail as "line not found"); it returns the order's current state instead.

## Non-Functional Requirements

- **Tenant isolation:** enforced per ADR 0002 on every read and write involved (order lookup, line deletion, and the response's items read).
- **Consistency:** the removal and the resulting total must be immediately visible to a subsequent read by the same tenant — in particular to `GetOrder` and to `CloseOrder`, so a closed order can never include a line whose removal had already been acknowledged.
- **Observability:** the request must be traceable via structured logging with a correlation ID, per `constitution.md` Article XI. As with every other Ordering specification, no such mechanism exists yet anywhere in the codebase; this specification does not introduce one on its own.
- **Idempotency:** guaranteed for repeated requests carrying the same `Idempotency-Key`; a request without one has no idempotency guarantee — same as `CreateOrder`/`AddItem`/`CloseOrder`/`CancelOrder`.

## Business Rules

- **BR1:** A line can only be removed from an Order whose status is `Open`. A `Closed` or `Cancelled` order's contents are final: removing a line from it MUST be rejected as a conflict, not applied silently. (This is the same status rule `add-item.md` BR1 already applies, expressed for removal.)
- **BR2:** A removal is identified by `MenuItemId`, not by a separate line identifier. Because `add-item.md` BR4 guarantees at most one line per (`OrderId`, `MenuItemId`), a `MenuItemId` identifies at most one line on the order, so no ambiguity is possible. There is no bulk or "remove all" operation in this specification.
- **BR3:** Removal is a **delete**, not a mutation of the line's quantity or price. No row is left behind with quantity zero, and no soft-delete flag is introduced: an order's items are exactly the lines currently on it, which is what `add-item.md` BR5's derived `Total` and `get-order.md` FR3 already assume. Consequently, removing a line can only ever decrease the order's `Total`, and only by that line's `LineTotal`.
- **BR4:** The status check (BR1) and the deletion MUST be serialized against every other mutation of the same order — `AddItem`'s merge, `CloseOrder`'s transition, and `CancelOrder`'s transition — by acquiring the same shared, order-scoped `OrderLock` (`OrderLock.Key(tenantId, orderId)`, `close-order.md` BR7) **before** checking status and deleting, exactly as those capabilities already do. Two consequences are required and tested:
  - A removal racing a `CloseOrder`/`CancelOrder` resolves to exactly one winner: if the transition commits first, the removal MUST be rejected as a conflict (BR1); if the removal commits first, the transition still succeeds and simply no longer sees that line.
  - A removal racing an `AddItem` for the **same** menu item is serialized in one order or the other, never interleaved: if the addition commits first, the removal deletes the merged line entirely; if the removal commits first, the addition creates a fresh line (with a fresh price snapshot). Neither ordering may leave a partially-applied line or a total that disagrees with the lines actually present.
  - No additional per-line lock is required for correctness, because every mutation of this order already holds the order-scoped lock first; the implementation MUST NOT acquire locks in an order that could deadlock against `AddItem`'s existing order-lock → idempotency-key-lock → per-line-lock sequence.
- **BR5:** The delete MUST be guarded by the affected-row count across `(tenant_id, order_id, menu_item_id)`, so that a non-existent line is reported as not found (FR4, AC4) rather than being treated as a successful removal of nothing, and so that a concurrent second removal of the same line cannot also "succeed".
- **BR6:** An `Idempotency-Key`, when supplied, uniquely identifies a single logical remove-item attempt within a tenant — scoped by `(TenantId, IdempotencyKey)` alone, not also by `OrderId`/`MenuItemId`, for the same reason `close-order.md` BR3 and `cancel-order.md` BR3 give: a key scoped by the request's identifiers could not detect reuse against a different order or item as a conflict. The stored record retains the original `OrderId` and `MenuItemId`, so a replay (both match) can be told apart from a conflicting reuse (either differs). A replay MUST NOT attempt the delete again; the response reflects the order's **current** state at the time of the replay, not a frozen snapshot.
  - The deletion and claiming the idempotency key MUST commit as a single atomic operation, exactly as `close-order.md` BR3 and `cancel-order.md` BR3 require.
  - When an `Idempotency-Key` is supplied, the existing claim for it (if any) MUST be checked **before** resolving the requested `OrderId` and `MenuItemId` in any way, mirroring `CloseOrderHandler`'s and `CancelOrderHandler`'s established precedence: a key reused against a different order or item must deterministically return 409 Conflict (AC8), and the claim check alone keeps AC2/AC4's tenant-isolation guarantee intact without ever needing to resolve the other identifiers' existence or ownership.
- **BR7:** `Total` remains, as always (`add-item.md` BR5), derived from the order's persisted lines rather than stored — removing a line requires no total arithmetic of its own, and must not introduce an independently maintained total column.

## Acceptance Criteria

- **AC1 — Happy path:** Given an authenticated user with the `restaurant.orders.removeitem` permission and an `Open` order with two lines, when they remove one of them, then the response contains the remaining line only, and `Total` equals the sum of the remaining line totals.
- **AC2 — Order not found:** Given an `OrderId` that does not exist, belongs to a different tenant, or is malformed (fails the route's `:guid` constraint), when the user attempts the removal, then the request is rejected as Not Found. `OrderId` MUST be bound exclusively from the request path, via `DELETE /api/v1/restaurants/orders/{orderId:guid}/items/{menuItemId:guid}`, never from the body or query string.
- **AC3 — Order not Open:** Given an order that is `Closed` or `Cancelled`, when a user attempts to remove a line from it, then the request is rejected as Conflict, and the line is still present afterwards.
- **AC4 — Line not found:** Given an `Open` order that has no line for the requested `MenuItemId` — including one already removed by an earlier request — when the user attempts the removal, then the request is rejected as Not Found.
- **AC5 — Malformed identifiers:** Given a malformed `MenuItemId` (or `OrderId`), when the removal is attempted, then the request is rejected as Not Found by the route's `:guid` constraint, before the handler runs.
- **AC6 — Missing permission:** Given an authenticated user without the `restaurant.orders.removeitem` permission, when they attempt the removal, then the request is rejected as Forbidden and the line is still present.
- **AC7 — Unauthenticated request:** Given no valid authentication, when remove-item is called, then the request is rejected as Unauthorized.
- **AC8 — Idempotency key reuse conflict:** Given a prior successful remove-item request with `Idempotency-Key: K`, when a new request reuses key `K` with a different `OrderId` or a different `MenuItemId`, then the request is rejected as Conflict — including when the other `OrderId` is nonexistent or belongs to another tenant (409 takes precedence over 404, per BR6).
- **AC9 — Idempotent replay:** Given a prior successful remove-item request with `Idempotency-Key: K`, when the same request (same `OrderId` and `MenuItemId`) is repeated with the same key, then the line is not looked up or deleted again, and the response reflects the order's current state instead of a spurious Not Found.
- **AC10 — Removing the last line:** Given an `Open` order with exactly one line, when it is removed, then the response has no items and a `Total` of zero, and the order itself remains `Open` (removal never changes the order's status).
- **AC11 — Other lines and their prices are untouched:** Given an `Open` order with lines for menu items A and B, each snapshotted at a different price, when line B is removed, then line A's quantity, `UnitPriceSnapshot`, and line total are unchanged, and the order's `Total` equals line A's line total alone.
- **AC12 — Concurrent removals of the same line:** Given an `Open` order with one line, when two remove requests (without an `Idempotency-Key`, or with two different keys) race against it, then exactly one succeeds and the other is rejected as Not Found — the line is deleted exactly once, never twice.
- **AC13 — Race between a removal and a CloseOrder/CancelOrder:** Given an `Open` order, when a remove request and a `CloseOrder` (or `CancelOrder`) request race against the same order, then either the transition wins and the removal is rejected as Conflict (AC3), or the removal wins and the transition succeeds without that line. The order must never end up closed or cancelled while still reporting a line whose removal succeeded.
- **AC14 — Race between AddItem and RemoveItem for the same menu item:** Given an `Open` order, when an `AddItem` and a `RemoveItem` request for the **same** menu item race, then neither is left partially applied: if the addition commits first, the line is absent afterwards; if the removal commits first, the addition creates a fresh line (with the menu item's price snapshotted at that moment). The persisted state and the returned items must always agree with one of these two orderings, and the order's `Total` must always equal the sum of the lines actually present.
- **AC15 — Cross-tenant isolation:** Given an order or line belonging to another tenant, when a user attempts the removal, then the request is rejected as Not Found (never Forbidden), consistent with `add-item.md` AC2/AC4, and the other tenant's line is untouched.

## Domain Concepts

- **OrderItem** (existing, unchanged) — a line on an `Order`; removing one deletes the row. No new field, flag, tombstone, or `RemovedAt` timestamp is introduced (see Out of Scope).
- **Order** (existing, unchanged) — its `Items` collection simply no longer contains the removed line; `Total` remains derived from whatever lines remain (`add-item.md` BR5). No new behavior is required on the aggregate beyond what `AddItem` already models.
- **OrderStatus** (existing, unchanged) — removed from the lifecycle: `Open`, `Closed`, `Cancelled` are already all reachable and this specification adds no value.

No new term needs to be added to `glossary.md`; if approved, its `OrderItem` entry may be extended with one sentence noting that a line can be removed while the order remains `Open`.

## Security Requirements

- The endpoint requires authentication (Core Identity).
- The endpoint requires the `restaurant.orders.removeitem` permission, scoped to the caller's tenant (Core Authorization/RBAC), per the permission naming convention in `architecture.md` §20. This is a new permission string; implementing this specification requires registering it alongside the existing ones (`Program.cs`'s authorization-policy list), the same way `restaurant.orders.close` and `restaurant.orders.cancel` were registered for their capabilities.
- The Tenant is resolved exclusively from the authenticated context, never from client input, per ADR 0002 rules 2–3.
- An order belonging to a different tenant MUST be treated as Not Found, not Forbidden, consistent with `add-item.md` AC2/AC4 and `create-order.md` AC3.

## Error Scenarios

| Scenario | Response |
|---|---|
| No authentication | 401 Unauthorized |
| Authenticated but missing `restaurant.orders.removeitem` | 403 Forbidden |
| `OrderId` does not exist, belongs to a different tenant, or is malformed | 404 Not Found |
| Order exists but has no line for that `MenuItemId`, or the line was already removed | 404 Not Found |
| Order exists but is not `Open` | 409 Conflict |
| `Idempotency-Key` reused with a different `OrderId` or `MenuItemId` | 409 Conflict |

A malformed `OrderId` or `MenuItemId` fails the route's `:guid` constraint before reaching the handler at all, so it is always 404 regardless of any `Idempotency-Key` — the same reasoning `close-order.md` and `cancel-order.md` give in full. When an `Idempotency-Key` is supplied, BR6's claim check runs before order and line resolution, so a key already used for a different order or item always returns 409, never 404.

Internal implementation details MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Reads:** the referenced Order (to check BR1's status), scoped to the caller's tenant; and, to build the response per FR7, its remaining `OrderItem` rows. Both reads MUST happen within the same transaction as the delete (the items read *after* the delete), so the response never includes a line that was concurrently removed or omits one that was concurrently added while the order lock was held.
- **Writes:** exactly one `order_items` row is deleted, identified by `(tenant_id, order_id, menu_item_id)`. No other column and no other table changes: the order's own row is untouched, and no `Total` column exists to update (BR7).
- The delete MUST be guarded at the database level by the affected-row count (BR5), so BR2/BR5's "exactly one line, exactly once" guarantee does not depend solely on an application-level pre-check.
- **A grants migration is required:** `0010_add_item_grants.sql` granted the least-privilege application role `SELECT, INSERT, UPDATE` on `restaurant.order_items` — deliberately **not** `DELETE`, because AddItem never deletes. RemoveItem is the first capability in the module that deletes from this table, so a new grants migration MUST add `DELETE` on `restaurant.order_items` to `presenciavirtual_app`; without it the delete fails with `InsufficientPrivilege` rather than working at all.
- No schema change to `restaurant.orders` or `restaurant.order_items` is required.
- The idempotency mechanism reuses the same approach as `create-order.md`/`add-item.md`/`close-order.md`/`cancel-order.md` (a persisted key record keyed by `(TenantId, IdempotencyKey)`, retaining the `OrderId` and `MenuItemId` per BR6), in its own dedicated table (`restaurant.remove_item_idempotency_keys`) — following the established precedent (`order_idempotency_keys`, `add_item_idempotency_keys`, `close_order_idempotency_keys`, `cancel_order_idempotency_keys`) that each vertical slice gets its own idempotency table, never one shared across capabilities.
- That new table is tenant-owned data like every other table in this module: its migration MUST enable and force Row-Level Security with a tenant-scoped policy (covering both the read and the `WITH CHECK` insert side), following the pattern `0012_restaurant_close_order_idempotency_keys.sql` already established, and grant the application role `SELECT`/`INSERT` on it in the same or a corresponding grants migration.
- Its `OrderId` column MUST be a tenant-consistent composite foreign key — `(tenant_id, order_id) REFERENCES restaurant.orders (tenant_id, id)` — using the composite `UNIQUE (tenant_id, id)` `restaurant.orders` already has, and its `MenuItemId` column MUST likewise be a tenant-consistent composite foreign key to `restaurant.menu_items (tenant_id, id)`, not a plain single-column reference, for the same reason `order_items` itself uses composite keys (`0007_restaurant_order_items.sql`).
- Tenant isolation on both the read and the write MUST follow ADR 0002 (application-level scoping plus PostgreSQL RLS).

## Integration Requirements

- **Depends on:** Core Identity & Authentication, Core Authorization/RBAC, tenant isolation infrastructure (ADR 0002) — the same minimal Core slice `CreateOrder`, `AddItem`, `GetOrder`, `CloseOrder` and `CancelOrder` depend on.
- **Does not depend on:** Core Organization/Location, Core Platform Billing, Notifications, Kitchen, Payments, Inventory, full Menu Management.
- **Publishes:** an `OrderItemRemoved` domain event is defined for architectural consistency with `architecture.md` §15 and the other Ordering specifications' treatment of their domain events — this specification does not require any subscriber to exist yet; consumption (e.g. by a future Kitchen void signal) is deferred until one exists.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** `RemoveItemValidator` (well-formed command accepted; over-long `Idempotency-Key` rejected) and `RemoveItemHandler`'s delegation and result mapping against a fake `IOrderRepository` — the same structure as `AddItemHandlerTests`, since no new aggregate behavior is introduced.
- **Integration tests:** AC1–AC15 above, executed against the real API and database, including:
  - Cross-tenant isolation for the order lookup and line deletion (per ADR 0002 rule 8), following the pattern established in `close-order.md`'s and `cancel-order.md`'s own reviews.
  - **Cross-tenant isolation for the new idempotency table:** a direct RLS read/write-isolation test for `remove_item_idempotency_keys` (both the `SELECT` and the `WITH CHECK` insert side), following `RowLevelSecurityTests.cs`'s established pattern — including a successful same-tenant insert first, so a failure can only mean RLS blocked it rather than the grants migration never having been applied.
  - **Delete grant:** coverage that proves the app role can actually delete from `restaurant.order_items` — i.e. AC1 failing with `InsufficientPrivilege` rather than 200 if the grants migration were missing.
  - Idempotency replay and conflict (AC8/AC9), following the same pattern as `close-order.md`.
  - **Validation (AC2/AC5's malformed-identifier cases):** malformed `OrderId` and malformed `MenuItemId` are each rejected as 404, following `close-order.md`'s own precedent test.
  - **Concurrent removals of the same line (BR5, AC12):** two simultaneous remove requests must result in exactly one success and one Not Found — proving the affected-row-count guard, not a read-then-write race.
  - **Race between RemoveItem and CloseOrder/CancelOrder (BR4, AC13):** deterministically forced in both directions (removal-wins and transition-wins), with the loser rejected as Conflict — mirroring `close-order.md`'s AC13 and `cancel-order.md`'s AC14 tests.
  - **Race between AddItem and RemoveItem (BR4, AC14):** deterministically forced in both directions, asserting the persisted rows and the derived `Total` agree with the ordering that actually occurred.
  - **Cross-capability regression (AC10/AC11):** a removed line does not reappear in `GetOrder`, and the surviving line's price snapshot is unchanged.

## Out of Scope

- `UpdateItemQuantity` — reducing (or otherwise adjusting) a line's quantity while keeping the line. `AddItem` only ever increases quantity and `RemoveItem` only ever removes the whole line; a partial reduction is a distinct capability with its own rules (e.g. whether reducing to zero is a removal, whether the price snapshot survives a reduction).
- `RemoveAllItems` / `ClearOrder` — a bulk operation. `CancelOrder` already exists as the "void the whole order" capability, and a bulk clear that leaves the order `Open` is a different, not-yet-requested shape.
- Any soft delete, tombstone, `RemovedAt`, `RemovedBy`, or cancellation-reason field on `OrderItem`. The item is simply deleted; the idempotency record is the only trace, and it exists for replay semantics, not as an audit trail. A real audit trail is Core Audit Logging, a separate not-yet-written capability.
- Kitchen void/recall signals — a removed line is not routed anywhere; kitchen integration does not exist yet.
- Re-adding a removed line at the original price — a later `AddItem` is a new line snapshotted at the current price (`add-item.md` BR3), deliberately.
- Refunds, discounts, or adjustments for a line removed after a payment exists — no payment capability exists yet.
- Reporting/metrics on removed lines (waste tracking, "voids per shift") — a future reporting capability.
- Removing a line from a `Closed` or `Cancelled` order — explicitly a conflict (BR1). Undoing a completed or voided order is not this capability.

## Open Questions

- **Quantity reduction vs. whole-line removal:** this specification removes the entire line. Is that sufficient operationally, or does a waiter need to correct "4 × Coke" to "2 × Coke" without losing the line (and its price snapshot)? Resolved here as *whole-line removal only*, matching the "minimal first increment" approach `create-table.md`, `create-menu-item.md` and `get-order.md` each took; `UpdateItemQuantity` is the natural follow-up specification if it turns out to be needed.
- **Should removal after the kitchen has started be blocked?** No kitchen routing exists yet, so there is nothing to consult. When Kitchen exists, it may need to reject or warn on removal of a line already in preparation — that decision belongs to the Kitchen specification, not this one.
- **Should a removed line publish an event now?** `OrderItemRemoved` is defined (Integration Requirements) for consistency, with no subscriber. If Kitchen is specified before any subscriber appears, it may choose to consume it then.
