# Specification: Ordering / GetOrder

**Status:** Draft
**Bounded Context:** Restaurant
**Business Capability:** Ordering
**Related ADRs:** [ADR 0002 — Tenant Isolation Strategy](../../../docs/adr/0002-tenant-isolation-strategy.md)
**Last Updated:** 2026-09-12

This specification follows `constitution.md` Article I. It requires human review and approval before implementation begins, per Article 1.4.

---

## Business Objective

Allow restaurant staff to look up a table's currently open order — its items and running total — without already holding the order's identifier, so a waiter working several tables at once can check or resume any of them. `create-order.md` creates an order and `add-item.md` mutates one, but neither exposes a way to find one again; this closes that gap, as already flagged in `add-item.md`'s Open Questions.

## Actors

- **Waiter / Front-of-house staff** — looks up the order currently open for a table they are serving.

## Business Context

```text
CreateOrder → AddItem → GetOrder → (Kitchen) → CloseOrder → Payment
```

`GetOrder` is a read-only capability that can be exercised at any point after `CreateOrder` and interleaved freely with `AddItem` calls — it does not advance the order lifecycle. This specification covers only looking up a table's open order; `RemoveItem`, `CancelOrder`, `CloseOrder`, kitchen routing, and payment remain separate, not-yet-written specifications.

## Functional Requirements

1. An authorized user MUST be able to retrieve the currently `Open` order for a specific table belonging to their own tenant, given a `TableId`.
2. The system MUST reject the request if the table does not exist within the caller's tenant.
3. The system MUST reject the request if the table exists but has no order in `Open` status.
4. The system MUST return the order's identifier (`OrderId`), `TableId`, `Status`, current items (with each line's `MenuItemId`, `Quantity`, `UnitPriceSnapshot`, and `LineTotal`), and `Total` — the same field set `CreateOrder`'s and `AddItem`'s responses already use, so a single client-side order representation covers all three.
5. The response MUST reflect the order's current state at the time of the request (no caching or stale snapshot), assembled from a single consistent read of the order and its items — not from separate reads that could straddle a concurrent `AddItem` commit and mix an old order state with new items or vice versa.

## Non-Functional Requirements

- **Tenant isolation:** enforced per ADR 0002 on the table lookup and the order/items read.
- **Consistency:** the response MUST reflect any `AddItem` calls already committed at the time of the request (read-your-writes within the same tenant).
- **Observability:** the request must be traceable via structured logging with a correlation ID, per `constitution.md` Article XI.

## Business Rules

- **BR1:** A lookup is always scoped to a `TableId`, not an `OrderId` — this is the capability `add-item.md` identified as missing: finding an order without already holding its identifier.
- **BR2:** At most one `Open` order can exist per table (per `create-order.md` BR2), so a lookup by `TableId` unambiguously identifies at most one order.
- **BR3:** This is a read-only operation. It MUST NOT create, modify, or close any order, table, or item.
- **BR4:** The order and its items MUST be read as of a single consistent point in time (e.g. within one database transaction/snapshot). Reading the order and its items through separate, uncoordinated queries MUST NOT be considered compliant, since a concurrent `AddItem` commit between those two reads could otherwise produce a response whose `Total` does not match its own `Items` list.

## Acceptance Criteria

- **AC1 — Happy path:** Given an authenticated user with the `restaurant.orders.read` permission and a table with an `Open` order that has items, when they request that table's order, then the response contains `OrderId`, `TableId`, `Status`, the current `Items` (each with `MenuItemId`, `Quantity`, `UnitPriceSnapshot`, `LineTotal`), and `Total`.
- **AC2 — Table with no open order:** Given a table with no `Open` order (never opened, or already closed once `CloseOrder` exists), when a user requests it, then the request is rejected as Not Found.
- **AC3 — Table does not exist:** Given a `TableId` that does not exist at all, when a user requests it, then the request is rejected as Not Found.
- **AC4 — Cross-tenant table reference:** Given a `TableId` that belongs to a different tenant than the authenticated user, when they request it, then the request is rejected as Not Found (not Forbidden — the existence of another tenant's table must not be revealed), consistent with `create-order.md` AC3.
- **AC5 — Missing permission:** Given an authenticated user without the `restaurant.orders.read` permission, when they attempt the lookup, then the request is rejected as Forbidden.
- **AC6 — Unauthenticated request:** Given no valid authentication, when get-order is called, then the request is rejected as Unauthorized.
- **AC7 — Reflects prior AddItem calls:** Given an `Open` order that has had one or more `AddItem` calls applied to it, when the order is requested, then the response's items and total match the order's current state, not the state at the time the order was created.
- **AC8 — Empty order:** Given an `Open` order with no items yet (no `AddItem` call made), when it is requested, then the response contains an empty item list and a total of zero.
- **AC9 — Consistent read under a concurrent write:** Given a `GetOrder` request running concurrently with an `AddItem` request against the same order, when both complete, then the `GetOrder` response's `Total` always matches the sum of its own `Items` list (BR4) — it never reflects the `AddItem` call's new line without also reflecting its line total, or vice versa.

## Domain Concepts

No new domain concepts. Reuses `Order`, `OrderItem`, `OrderStatus`, and `Table`, all already defined in `glossary.md`.

## Security Requirements

- The endpoint requires authentication (Core Identity).
- The endpoint requires the `restaurant.orders.read` permission, scoped to the caller's tenant (Core Authorization/RBAC), per the permission naming convention in `architecture.md` §20.
- The Tenant is resolved exclusively from the authenticated context, never from client input, per ADR 0002 rules 2–3.
- A table belonging to a different tenant MUST be treated as Not Found, not Forbidden, consistent with `create-order.md`'s AC3 and `add-item.md`'s Security Requirements.

## Error Scenarios

| Scenario | Response |
|---|---|
| No authentication | 401 Unauthorized |
| Authenticated but missing `restaurant.orders.read` | 403 Forbidden |
| `TableId` missing or malformed in request | 400 Bad Request |
| `TableId` does not exist, or belongs to a different tenant | 404 Not Found |
| Table exists but has no `Open` order | 404 Not Found |

Internal implementation details MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Reads:** the referenced Table (to confirm it exists within the caller's tenant), the `Open` order for that table (if any), and its `OrderItem` rows — all scoped to the caller's tenant. The order and its items MUST be read within a single consistent transaction/snapshot (BR4), not as independent queries.
- **Writes:** none. This is a pure read capability.
- Tenant isolation on the table and order/items reads MUST follow ADR 0002 (application-level scoping plus PostgreSQL RLS).
- No new tables or columns are required — this specification reads existing `restaurant.tables`, `restaurant.orders`, and `restaurant.order_items` rows introduced by `create-order.md`, `create-table.md`, and `add-item.md`.

## Integration Requirements

- **Depends on:** Core Identity & Authentication, Core Authorization/RBAC, tenant isolation infrastructure (ADR 0002) — the same minimal Core slice `CreateOrder`, `CreateTable`, and `AddItem` depend on.
- **Does not depend on:** Core Organization/Location, Core Platform Billing, Notifications, Kitchen, Payments, Inventory, full Menu Management, full Restaurant Settings management.
- **Publishes:** none — this is a read-only capability.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** none beyond what `OrderTests.cs` already covers (`Order.Reconstruct` deriving `Total` from items) — this capability introduces no new domain behavior, only a new read path.
- **Integration tests:** AC1–AC9 above, executed against the real API and database, including:
  - Cross-tenant isolation (read side, per ADR 0002 rule 8) for the table lookup **and** for the order/item reads: a request authenticated as Tenant A must never be able to retrieve Tenant B's table, order, or order items, exercised as direct least-privilege-role checks (following `RowLevelSecurityTests.cs`'s pattern), not only through application-level behavior.
  - A table with an `Open` order that has multiple items across multiple `AddItem` calls (including merged lines, per `add-item.md` BR4), confirming the response's `Items` and `Total` match the order's persisted items — with `Total` always the sum of those items' line totals, never an independently stored value.
  - **Consistent read under concurrency (BR4, AC9):** a `GetOrder` request running concurrently with an `AddItem` request against the same order must not return a response whose `Total` disagrees with its own `Items` list.

## Out of Scope

- Lookup by `OrderId` directly — deferred until a concrete need arises; today, staff only need to resolve a table's current order, per `add-item.md`'s Open Questions.
- Listing all orders for a tenant, or all open orders across tables (a future reporting/dashboard capability).
- Order history — orders that are no longer `Open` (relevant once `CloseOrder`/`CancelOrder` exist).
- `RemoveItem`, `CancelOrder`, `CloseOrder` (future Ordering specifications).
- Real-time push/streaming updates (e.g. websockets) — this is a simple request/response read.

## Open Questions

None.
