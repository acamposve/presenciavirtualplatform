# Specification: Ordering / AddItem

**Status:** Draft — Pending Review
**Bounded Context:** Restaurant
**Business Capability:** Ordering
**Related ADRs:** [ADR 0002 — Tenant Isolation Strategy](../../../docs/adr/0002-tenant-isolation-strategy.md)
**Last Updated:** 2026-09-12

This specification follows `constitution.md` Article I. It requires human review and approval before implementation begins, per Article 1.4.

---

## Business Objective

Allow restaurant staff to add menu items to an order that is already open, so the order accumulates the items and running total that will eventually be sent to the kitchen, closed, and paid. This is the second step of the order lifecycle defined in [`create-order.md`](create-order.md).

## Actors

- **Waiter / Front-of-house staff** — adds items to an order for the table they are serving.

## Business Context

```text
CreateOrder → AddItem → (Kitchen) → CloseOrder → Payment
```

This specification covers only adding an item to an already-open order. `RemoveItem`, `CancelOrder`, `CloseOrder`, kitchen routing, and payment remain separate, not-yet-written specifications, exactly as `create-order.md` already stated.

This use case assumes a **menu item** already exists for the tenant, identified by `MenuItemId` with a name and a price. Full Menu Management (creating/editing menu items, categories, availability) is out of scope — the same relationship `create-order.md` had with Table Management before [`create-table.md`](../tables/create-table.md) existed. Unlike that precedent, this specification does not defer the minimal schema: since no menu capability exists at all yet, this specification introduces the smallest possible `restaurant.menu_items` reference schema (id, tenant, name, price) needed to make `AddItem` real, the same way `create-order.md` introduced a minimal `restaurant.tables` schema for `CreateOrder`.

## Functional Requirements

1. An authorized user MUST be able to add an item to an existing, Open order belonging to their own tenant, given a `MenuItemId` and a `Quantity`.
2. The system MUST reject the request if the order does not exist within the caller's tenant, or is not in the `Open` status.
3. The system MUST reject the request if the menu item does not exist within the caller's tenant.
4. The system MUST reject the request if `Quantity` is not a positive integer.
5. The system MUST record the menu item's price **at the time it is added** (a price snapshot) rather than a live reference to the menu item's current price.
6. The system MUST recompute the order's total to include the newly added item.
7. The system MUST return the order's current items and total after the item is added.
8. The system MUST support an optional `Idempotency-Key` request header, with the same semantics as `CreateOrder`'s (FR7/BR6 in `create-order.md`): a repeated request with the same key returns the original result instead of adding the item again.

## Non-Functional Requirements

- **Tenant isolation:** enforced per ADR 0002 on every read and write involved (order lookup, menu item lookup, item insert).
- **Consistency:** the added item and recomputed total must be immediately visible to a subsequent read by the same tenant.
- **Observability:** the request must be traceable via structured logging with a correlation ID, per `constitution.md` Article XI.
- **Idempotency:** guaranteed for repeated requests carrying the same `Idempotency-Key`; a request without one has no idempotency guarantee — same as `CreateOrder`.

## Business Rules

- **BR1:** An item can only be added to an Order whose status is `Open`. (Only `Open` exists today; this rule anticipates `CloseOrder`/`CancelOrder` introducing other statuses.)
- **BR2:** `Quantity` MUST be a positive integer (at least 1).
- **BR3:** An added item's unit price is fixed at the price the referenced menu item had at the moment it was added ("price snapshot"). A later change to the menu item's price MUST NOT retroactively change already-added items.
- **BR4:** Each `AddItem` call creates its own line item; it does **not** merge into an existing line for the same menu item on the same order — see Open Questions.
- **BR5:** The order's `Total` is always the sum of all its items' line totals (`Quantity × UnitPriceSnapshot`).
- **BR6:** An `Idempotency-Key`, when supplied, uniquely identifies a single logical add-item attempt within a tenant and order. Replaying it MUST NOT add the item twice. Reusing a key with a different request (different `OrderId`, `MenuItemId`, or `Quantity`) MUST be rejected rather than silently accepted.

## Acceptance Criteria

- **AC1 — Happy path:** Given an authenticated user with the `restaurant.orders.additem` permission, an Open order, and an existing menu item, when they add the item with a valid quantity, then the order gains a new line item with the menu item's current price snapshotted, and the response reflects the updated items and total.
- **AC2 — Order not found:** Given an `OrderId` that does not exist, or belongs to a different tenant, when the user attempts to add an item, then the request is rejected as Not Found.
- **AC3 — Order not Open:** Given an order that is not in the `Open` status, when the user attempts to add an item, then the request is rejected as Conflict. (Not reachable until a future specification introduces a non-`Open` status — the rule and its test still apply to whatever mechanism a test uses to reach that state.)
- **AC4 — Menu item not found:** Given a `MenuItemId` that does not exist, or belongs to a different tenant, when the user attempts to add it, then the request is rejected as Not Found.
- **AC5 — Invalid quantity:** Given a `Quantity` of zero or negative, when the user attempts to add the item, then the request is rejected as a validation error.
- **AC6 — Missing permission:** Given an authenticated user without the `restaurant.orders.additem` permission, when they attempt to add an item, then the request is rejected as Forbidden.
- **AC7 — Unauthenticated request:** Given no valid authentication, when add-item is called, then the request is rejected as Unauthorized.
- **AC8 — Idempotent replay:** Given a prior successful add-item request with `Idempotency-Key: K`, when the same request (same `OrderId`, `MenuItemId`, `Quantity`) is repeated with the same key, then the system returns the same result without adding the item again.
- **AC9 — Idempotency key reuse conflict:** Given a prior successful add-item request with `Idempotency-Key: K`, when a new request reuses key `K` with a different `OrderId`, `MenuItemId`, or `Quantity`, then the request is rejected as a Conflict.
- **AC10 — Price snapshot:** Given a menu item priced at $10 when added to an order, when the menu item's price later changes to $12 (however that happens — out of scope here), then the order's existing line item and total still reflect $10.

## Domain Concepts

- **OrderItem** (new) — a line item on an `Order`: `OrderItemId`, `OrderId`, `MenuItemId`, `Quantity`, `UnitPriceSnapshot`, `LineTotal` (`Quantity × UnitPriceSnapshot`).
- **MenuItem** (new, minimal reference concept — not a full Menu Management aggregate) — `MenuItemId`, `TenantId`, `Name`, `Price`.
- **Order** (existing, extended) — gains a collection of `OrderItem`s; `Total` becomes the sum of its items' `LineTotal`s instead of a fixed zero (`create-order.md`'s own note: "Items and Total exist structurally but remain empty/zero until AddItem is specified").

Add `OrderItem` and `MenuItem` to `glossary.md` under Restaurant once this specification is approved, and update the existing `Order` entry to reflect that it now owns items.

## Security Requirements

- The endpoint requires authentication (Core Identity).
- The endpoint requires the `restaurant.orders.additem` permission, scoped to the caller's tenant (Core Authorization/RBAC), per the permission naming convention in `architecture.md` §20.
- The Tenant is resolved exclusively from the authenticated context, never from client input, per ADR 0002 rules 2–3.
- An order or menu item belonging to a different tenant MUST be treated as Not Found, not Forbidden, consistent with `create-order.md`'s AC3.

## Error Scenarios

| Scenario | Response |
|---|---|
| No authentication | 401 Unauthorized |
| Authenticated but missing `restaurant.orders.additem` | 403 Forbidden |
| `MenuItemId` missing/malformed, or `Quantity` not a positive integer | 400 Bad Request |
| `OrderId` does not exist, or belongs to a different tenant | 404 Not Found |
| `MenuItemId` does not exist, or belongs to a different tenant | 404 Not Found |
| Order exists but is not `Open` | 409 Conflict |
| `Idempotency-Key` reused with a different request | 409 Conflict |

Internal implementation details MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Reads:** the referenced Order and MenuItem, both scoped to the caller's tenant.
- **Writes:** a new `OrderItem` row; the Order's `Total` is derived (computed from items at read time or maintained incrementally — an implementation choice, not a business rule) rather than independently settable.
- This specification introduces a new, minimal `restaurant.menu_items` table (id, tenant_id, name, price, created_at) — analogous to `restaurant.tables`' role for `CreateOrder`. Full Menu Management (its own specification) will own creating/editing these rows; for now they may need to be seeded directly, the same way tables were before `create-table.md`.
- A new `restaurant.order_items` table is required: id, tenant_id, order_id (FK), menu_item_id (FK), quantity, unit_price_snapshot, created_at.
- Tenant isolation on all reads/writes MUST follow ADR 0002 (application-level scoping plus PostgreSQL RLS, including the `WITH CHECK` side for this insert, per the write-isolation testing gap found while reviewing `create-table.md`).
- The idempotency mechanism reuses the same approach as `create-order.md` (a persisted key record); whether it is a shared table across Ordering use cases or a per-use-case table is an implementation decision, not a business rule.

## Integration Requirements

- **Depends on:** Core Identity & Authentication, Core Authorization/RBAC, tenant isolation infrastructure (ADR 0002) — the same minimal Core slice `CreateOrder` and `CreateTable` depend on.
- **Does not depend on:** Core Organization/Location, Core Platform Billing, Notifications, Kitchen, Payments, Inventory, full Menu Management.
- **Publishes:** no event is defined for this version — Kitchen routing (a plausible future consumer of "item added") is out of scope until its own specification exists.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** `Order.AddItem` invariants (BR1 status check, BR2 quantity validation, BR3 price snapshot, BR5 total recomputation).
- **Integration tests:** AC1–AC10 above, executed against the real API and database, including:
  - Cross-tenant isolation for both the order lookup and the menu item lookup (read side, per ADR 0002 rule 8).
  - Write isolation for the new `restaurant.order_items` table (the `WITH CHECK` side of RLS), following the pattern established in `create-table.md`'s review (`AppRole_CannotInsertARowForAnotherTenant`).
  - Idempotency replay and conflict (AC8/AC9), following the same pattern as `create-order.md`.

## Out of Scope

- `RemoveItem`, `CancelOrder`, `CloseOrder` (future Ordering specifications).
- Full Menu Management (creating/editing menu items, categories, availability, images) — only the minimal reference schema is introduced here.
- Kitchen routing/ticket generation in response to items being added.
- Merging multiple `AddItem` calls for the same menu item into a single line (BR4 — see Open Questions).
- Modifying an item's quantity after it has been added (that would be a distinct `UpdateItemQuantity` capability, not covered here).
- Discounts, promotions, or per-item modifiers/notes (e.g. "no onions").

## Open Questions

- Should adding the same menu item twice to the same order merge into one line (incrementing quantity) or always create a separate line (BR4's current default)? Separate lines is simpler to implement and matches "each add is its own action," but some POS systems consolidate. Revisit if real usage shows a preference.
- Should there be a maximum quantity per line item? No business requirement identified yet.
- Is a `GetOrder` read capability needed now that `AddItem`'s response returns the full order state, or does returning it inline from `AddItem` (and `CreateOrder`) cover the need until a dedicated query capability is justified?
