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

This use case assumes a **menu item** already exists for the tenant, identified by `MenuItemId` with a name, a price, and whether it is alcoholic. Full Menu Management (creating/editing menu items, categories, availability) is out of scope — the same relationship `create-order.md` had with Table Management before [`create-table.md`](../tables/create-table.md) existed. Unlike that precedent, this specification does not defer the minimal schema: since no menu capability exists at all yet, this specification introduces the smallest possible `restaurant.menu_items` reference schema needed to make `AddItem` real, the same way `create-order.md` introduced a minimal `restaurant.tables` schema for `CreateOrder`.

## Functional Requirements

1. An authorized user MUST be able to add an item to an existing, Open order belonging to their own tenant, given a `MenuItemId` and a `Quantity`.
2. The system MUST reject the request if the order does not exist within the caller's tenant, or is not in the `Open` status.
3. The system MUST reject the request if the menu item does not exist within the caller's tenant.
4. The system MUST reject the request if `Quantity` is not a positive integer.
5. The system MUST record the menu item's price **at the time its line is first created** on the order (a price snapshot) rather than a live reference to the menu item's current price. A later `AddItem` call that merges into an existing line (BR4) does not re-snapshot the price, even if the menu item's price has since changed — the snapshot is a property of the line, captured once, not of each individual `AddItem` call.
6. The system MUST recompute the order's total to include the newly added item.
7. The system MUST return the order's current items and total after the item is added.
8. The system MUST support an optional `Idempotency-Key` request header, with the same semantics as `CreateOrder`'s (FR7/BR6 in `create-order.md`): a repeated request with the same key returns the original result instead of adding the item again.
9. If the menu item is marked alcoholic, the system MUST reject the request if adding it would bring that line's quantity above the tenant's configured limit, when one is configured.

## Non-Functional Requirements

- **Tenant isolation:** enforced per ADR 0002 on every read and write involved (order lookup, menu item lookup, item insert).
- **Consistency:** the added item and recomputed total must be immediately visible to a subsequent read by the same tenant.
- **Observability:** the request must be traceable via structured logging with a correlation ID, per `constitution.md` Article XI.
- **Idempotency:** guaranteed for repeated requests carrying the same `Idempotency-Key`; a request without one has no idempotency guarantee — same as `CreateOrder`.

## Business Rules

- **BR1:** An item can only be added to an Order whose status is `Open`. (Only `Open` exists today; this rule anticipates `CloseOrder`/`CancelOrder` introducing other statuses.)
- **BR2:** `Quantity` MUST be a positive integer (at least 1).
- **BR3:** A line's unit price is a property of the *line*, not of each `AddItem` call: it is fixed at the price the referenced menu item had when the line was first created ("price snapshot"), per FR5. A later change to the menu item's price MUST NOT retroactively change an already-created line, and merging more quantity into that line (BR4) MUST NOT re-price it either — this is a deliberate, explicit line-level semantic (avoiding a weighted-average price across multiple snapshots within one line), not an oversight.
- **BR4:** Adding a menu item that already has a line on the order increases that line's `Quantity` by the newly requested amount rather than creating a second line for the same `MenuItemId`. Adding a *different* menu item always creates its own line. (Resolved from Open Questions: e.g. adding a Coke twice results in one line with quantity 2; adding a Coke and then a dessert results in two lines.) This merge MUST be performed as a single atomic operation (e.g. an `INSERT ... ON CONFLICT (tenant_id, order_id, menu_item_id) DO UPDATE SET quantity = order_items.quantity + excluded.quantity`), not a separate read-then-write — two concurrent `AddItem` calls for the same line must both be reflected in the final quantity, not have one silently lost to the other (see Testing Requirements).
- **BR5:** The order's `Total` is always the sum of all its items' line totals (`Quantity × UnitPriceSnapshot`).
- **BR6:** An `Idempotency-Key`, when supplied, uniquely identifies a single logical add-item attempt **within a tenant** — scoped by `(TenantId, IdempotencyKey)` alone, *not* also by `OrderId`. (A key scoped by `(TenantId, OrderId, IdempotencyKey)` would fail AC9: reusing the same key against a different `OrderId` would look like a key never seen before for that order, and would be processed as a new request instead of rejected as a conflict.) The stored record retains the original `OrderId`, `MenuItemId`, and `Quantity` so a replay (all three match) can be distinguished from a conflicting reuse (any of the three differs) — the same pattern `create-order.md`'s `IdempotencyRecord` already uses for `TableId`. Replaying MUST NOT add the item twice (nor increase the merged line's quantity twice). Reusing a key with a different request (different `OrderId`, `MenuItemId`, or `Quantity`) MUST be rejected rather than silently accepted.
- **BR7:** A menu item MAY be marked `IsAlcoholic`. A tenant MAY configure a maximum quantity per line for alcoholic items (`MaxAlcoholicItemQuantityPerLine`). When configured, the *resulting* quantity of an alcoholic item's line (after merging, per BR4) MUST NOT exceed it. When not configured for a tenant, no limit is enforced. The specific limit is a business/legal decision for each tenant to set, not a value this specification fixes.

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
- **AC11 — Merging the same menu item:** Given an order that already has a line for a menu item with quantity 1, when the same menu item is added again with quantity 1, then the order ends up with a single line for that menu item at quantity 2, not two separate lines.
- **AC12 — Distinct menu items stay separate:** Given an order with a line for menu item A, when menu item B is added, then the order has two lines, one per menu item.
- **AC13 — Alcoholic item within the configured limit:** Given a tenant with `MaxAlcoholicItemQuantityPerLine` configured to N, and an order with an alcoholic item's line at a quantity below N, when enough of that item is added to reach exactly N, then the request succeeds.
- **AC14 — Alcoholic item over the configured limit:** Given the same setup as AC13, when adding more would bring the line's quantity above N, then the request is rejected as Conflict and the line's quantity is unchanged.
- **AC15 — No configured limit:** Given a tenant with no `MaxAlcoholicItemQuantityPerLine` configured, when an alcoholic item is added in any quantity, then the request succeeds (no cap is enforced).

## Domain Concepts

- **OrderItem** (new) — a line item on an `Order`: `OrderItemId`, `OrderId`, `MenuItemId`, `Quantity`, `UnitPriceSnapshot`, `LineTotal` (`Quantity × UnitPriceSnapshot`). At most one `OrderItem` per (`OrderId`, `MenuItemId`) pair, per BR4.
- **MenuItem** (new, minimal reference concept — not a full Menu Management aggregate) — `MenuItemId`, `TenantId`, `Name`, `Price`, `IsAlcoholic`.
- **RestaurantSettings** (new, minimal reference concept — not a full Restaurant configuration aggregate) — one row per tenant: `TenantId`, `MaxAlcoholicItemQuantityPerLine` (nullable; absent/null means no limit). Configuring this is out of scope (see Out of Scope) — for now a tenant without a row is treated as having no limit.
- **Order** (existing, extended) — gains a collection of `OrderItem`s; `Total` becomes the sum of its items' `LineTotal`s instead of a fixed zero (`create-order.md`'s own note: "Items and Total exist structurally but remain empty/zero until AddItem is specified").

Add `OrderItem`, `MenuItem`, and `RestaurantSettings` to `glossary.md` under Restaurant once this specification is approved, and update the existing `Order` entry to reflect that it now owns items.

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
| Adding this quantity would exceed the tenant's configured alcoholic-item limit for this line (BR7) | 409 Conflict |
| `Idempotency-Key` reused with a different request | 409 Conflict |

Internal implementation details MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Reads:** the referenced Order and MenuItem, both scoped to the caller's tenant; and the caller's tenant's `RestaurantSettings` row, to obtain `MaxAlcoholicItemQuantityPerLine` for BR7 — a missing row is read as "no limit configured," not skipped. An implementation that never performs this read cannot enforce BR7 and does not satisfy this specification.
- **Writes:** an `OrderItem` row is created or its `Quantity` increased (BR4); the Order's `Total` is derived (computed from items at read time or maintained incrementally — an implementation choice, not a business rule) rather than independently settable.
- The BR4 merge MUST be a single atomic database operation (e.g. `INSERT ... ON CONFLICT (tenant_id, order_id, menu_item_id) DO UPDATE SET quantity = order_items.quantity + excluded.quantity`), not an application-level read-then-write — see BR4 and Testing Requirements for why.
- This specification introduces a new, minimal `restaurant.menu_items` table (id, tenant_id, name, price, is_alcoholic, created_at) — analogous to `restaurant.tables`' role for `CreateOrder`. Full Menu Management (its own specification) will own creating/editing these rows; for now they may need to be seeded directly, the same way tables were before `create-table.md`.
- A new `restaurant.order_items` table is required: id, tenant_id, order_id, menu_item_id, quantity, unit_price_snapshot, created_at. A uniqueness constraint on (tenant_id, order_id, menu_item_id) enforces BR4 (at most one line per menu item per order) and is also the conflict target for the atomic merge above. `order_id` and `menu_item_id` MUST be tenant-consistent composite foreign keys — `(tenant_id, order_id) REFERENCES restaurant.orders (tenant_id, id)` and `(tenant_id, menu_item_id) REFERENCES restaurant.menu_items (tenant_id, id)` — not plain single-column foreign keys, so that a row can never reference an order or menu item belonging to a different tenant even if application code has a bug in how it resolves those IDs; this requires `restaurant.orders` and `restaurant.menu_items` to each have a `UNIQUE (tenant_id, id)` constraint for the composite foreign keys to target.
- A new, minimal `restaurant.settings` table is required: tenant_id (PK), max_alcoholic_item_quantity_per_line (nullable). A tenant with no row is treated as having no limit (BR7). Populating this — and any API to manage it — is out of scope; for now it may need to be seeded directly, the same way tables and menu items are before their own management specifications exist.
- Tenant isolation on all reads/writes MUST follow ADR 0002 (application-level scoping plus PostgreSQL RLS, including the `WITH CHECK` side for this insert, per the write-isolation testing gap found while reviewing `create-table.md`).
- The idempotency mechanism reuses the same approach as `create-order.md` (a persisted key record, keyed by `(TenantId, IdempotencyKey)` per BR6); whether it is a shared table across Ordering use cases or a per-use-case table is an implementation decision, not a business rule.

## Integration Requirements

- **Depends on:** Core Identity & Authentication, Core Authorization/RBAC, tenant isolation infrastructure (ADR 0002) — the same minimal Core slice `CreateOrder` and `CreateTable` depend on.
- **Does not depend on:** Core Organization/Location, Core Platform Billing, Notifications, Kitchen, Payments, Inventory, full Menu Management.
- **Publishes:** no event is defined for this version — Kitchen routing (a plausible future consumer of "item added") is out of scope until its own specification exists.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** `Order.AddItem` invariants (BR1 status check, BR2 quantity validation, BR3 price snapshot, BR4 merge-by-menu-item behavior including the "snapshot doesn't change on merge" rule, BR5 total recomputation, BR7 alcoholic-item limit enforcement and the no-limit-configured case).
- **Integration tests:** AC1–AC15 above, executed against the real API and database, including:
  - Cross-tenant isolation for both the order lookup and the menu item lookup (read side, per ADR 0002 rule 8).
  - Write isolation for the new `restaurant.order_items` table (the `WITH CHECK` side of RLS), following the pattern established in `create-table.md`'s review (`AppRole_CannotInsertARowForAnotherTenant`).
  - Idempotency replay and conflict (AC8/AC9), following the same pattern as `create-order.md`.
  - **Concurrent merge (BR4):** two simultaneous `AddItem` requests for the same order and menu item must both be reflected in the final quantity (e.g. two concurrent requests for quantity 1 each must result in a line at quantity 2, not 1) — proving the atomic upsert, not a read-then-write, is what's implemented.

## Out of Scope

- `RemoveItem`, `CancelOrder`, `CloseOrder` (future Ordering specifications).
- Full Menu Management (creating/editing menu items, categories, availability, images) — only the minimal reference schema is introduced here.
- Kitchen routing/ticket generation in response to items being added.
- Modifying or removing a line's quantity directly (that would be a distinct `UpdateItemQuantity`/`RemoveItem` capability; the only way to change a line's quantity in this specification is by adding more of the same menu item, which only ever increases it).
- Discounts, promotions, or per-item modifiers/notes (e.g. "no onions").
- Any API or UI to configure `RestaurantSettings` (including `MaxAlcoholicItemQuantityPerLine`) — a future "Restaurant Settings" specification. For now, a tenant's limit (if any) must be seeded directly.
- A read/query capability to fetch an order by table (`GetOrder`/`GetOpenOrderForTable`) — resolved as needed (see Open Questions) but specified separately, since it is a distinct read capability keyed by `TableId` rather than something `AddItem` itself performs.

## Open Questions

None remaining that block this specification. Resolved during review:

- **Merge behavior (BR4):** adding the same menu item merges into its existing line (quantity increases); a different menu item always gets its own line.
- **Alcoholic item quantity limit (BR7):** configurable per tenant rather than a fixed number this specification chooses. No configuration capability exists yet — a tenant without a configured value has no limit enforced.
- **`GetOrder` is needed:** confirmed necessary because staff work multiple tables simultaneously and need to look up an order without already holding its `OrderId`. It must support lookup **by `TableId`** (the table's current open order), not only by `OrderId`. This is a separate capability from `AddItem` and will be written as its own specification next, following the same pattern `create-table.md` followed after `create-order.md`.
