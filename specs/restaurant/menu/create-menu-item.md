# Specification: Menu / CreateMenuItem

**Status:** Draft
**Bounded Context:** Restaurant
**Business Capability:** Menu
**Related ADRs:** [ADR 0002 — Tenant Isolation Strategy](../../../docs/adr/0002-tenant-isolation-strategy.md)
**Last Updated:** 2026-09-13

This specification follows `constitution.md` Article I. It requires human review and approval before implementation begins, per Article 1.4.

---

## Business Objective

Allow a restaurant to register the items it sells, so that Ordering ([`specs/restaurant/ordering/add-item.md`](../ordering/add-item.md)) has a real menu item to reference instead of one seeded directly in the database. `add-item.md` explicitly assumed a menu item already exists, with "full Menu Management... out of scope" of that specification — this is that capability's first increment, the same way `create-table.md` was Table Management's first increment for `create-order.md`.

## Actors

- **Restaurant Manager** — registers and maintains the restaurant's menu as part of setting it up. This is treated as a setup/configuration action, distinct from day-to-day order-taking.

## Business Context

```text
CreateMenuItem → (menu item exists) → AddItem
```

This specification covers only creating a menu item. Editing a menu item's name, price, or alcoholic flag, deactivating/removing one, listing/querying the menu, categories, availability ("86'd" items), images, and per-item modifiers are all separate, not-yet-written specifications (see Out of Scope).

## Functional Requirements

1. An authorized user MUST be able to register a new menu item within their own tenant, given a name and a price.
2. The system MUST reject the request if the name is empty or exceeds 200 characters.
3. The system MUST reject the request if the price is not a positive value.
4. The system MUST accept an optional `IsAlcoholic` flag; when omitted, it defaults to `false`.
5. The system MUST generate a unique identifier for the new menu item.
6. The system MUST record the tenant that owns the menu item and when it was created.
7. The system MUST return the created menu item's identifier, name, price, and alcoholic flag to the caller.

## Non-Functional Requirements

- **Tenant isolation:** enforced per ADR 0002 on the write (and on any future read of this data).
- **Consistency:** the created menu item must be immediately visible to a subsequent read by the same tenant — in particular, immediately usable by `AddItem`.
- **Observability:** the request must be traceable via structured logging with a correlation ID, per `constitution.md` Article XI. As with every other capability in this module so far, no such mechanism exists yet anywhere in the codebase; this specification does not introduce one on its own.

## Business Rules

- **BR1:** A MenuItem always belongs to exactly one Tenant.
- **BR2:** A MenuItem's name MUST NOT be empty and MUST NOT exceed 200 characters.
- **BR3:** A MenuItem's price MUST be a positive value (greater than zero). A free or negatively-priced item is not a valid menu item in this version.
- **BR4:** `IsAlcoholic` is a plain boolean, defaulting to `false` when not supplied. It has no effect on its own here — it only matters once `add-item.md` BR7's configured per-tenant limit applies to a line referencing this item.

Menu item names are **not** required to be unique within a tenant in this version — see Open Questions.

## Acceptance Criteria

- **AC1 — Happy path:** Given an authenticated user with the `restaurant.menuitems.create` permission, when they create a menu item with a valid name and price, then a new MenuItem is created, scoped to the caller's tenant, and the response contains a generated `MenuItemId`, the given `Name`, `Price`, and `IsAlcoholic` (`false` unless supplied).
- **AC2 — Alcoholic item:** Given a request with `IsAlcoholic: true`, when the item is created, then the response reflects `IsAlcoholic: true`, and the item is subsequently subject to `add-item.md` BR7's configured limit like any other alcoholic item.
- **AC3 — Empty name:** Given a request with an empty or whitespace-only name, when the user attempts to create the menu item, then the request is rejected as a validation error and no MenuItem is created.
- **AC4 — Name too long:** Given a name longer than 200 characters, when the user attempts to create the menu item, then the request is rejected as a validation error and no MenuItem is created.
- **AC5 — Invalid price:** Given a price of zero or negative, when the user attempts to create the menu item, then the request is rejected as a validation error and no MenuItem is created.
- **AC6 — Missing permission:** Given an authenticated user without the `restaurant.menuitems.create` permission, when they attempt to create a menu item, then the request is rejected as Forbidden and no MenuItem is created.
- **AC7 — Unauthenticated request:** Given no valid authentication, when create-menu-item is called, then the request is rejected as Unauthorized.

## Domain Concepts

- **MenuItem** (already referenced by `add-item.md`, formalized here as an aggregate) — fields relevant to this specification: `MenuItemId`, `TenantId`, `Name`, `Price`, `IsAlcoholic`, `CreatedAt`.

`MenuItem` is already listed in `glossary.md` under Restaurant (added when `add-item.md` was approved), described there as "referenced by `AddItem`; no capability to create or edit one exists yet." This specification is that capability's first increment, so the glossary entry is updated accordingly once this specification is approved.

## Security Requirements

- The endpoint requires authentication (Core Identity).
- The endpoint requires the `restaurant.menuitems.create` permission, scoped to the caller's tenant (Core Authorization/RBAC), per the permission naming convention in `architecture.md` §20. This is a new permission string; implementing this specification requires registering it alongside the existing ones.
- The Tenant is resolved exclusively from the authenticated context, never from client input, per ADR 0002 rules 2–3.

## Error Scenarios

| Scenario | Response |
|---|---|
| No authentication | 401 Unauthorized |
| Authenticated but missing `restaurant.menuitems.create` | 403 Forbidden |
| Name missing, empty, or longer than 200 characters | 400 Bad Request |
| Price missing, zero, or negative | 400 Bad Request |

Internal implementation details MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Writes:** a new MenuItem record scoped to the caller's tenant.
- This reuses the `restaurant.menu_items` table already created by `0005_restaurant_menu_items.sql` for `AddItem` — no new migration is expected, only a write path where today only a manual/test seed exists. That table already has the `NOT NULL` price/name columns and the `UNIQUE (tenant_id, id)` constraint `add-item.md`'s composite foreign keys depend on; this specification does not change its schema.
- Tenant isolation on the write MUST follow ADR 0002 (application-level scoping plus PostgreSQL RLS), consistent with the existing table.

## Integration Requirements

- **Depends on:** Core Identity & Authentication, Core Authorization/RBAC, tenant isolation infrastructure (ADR 0002) — the same minimal Core slice every other Restaurant capability depends on.
- **Does not depend on:** Core Organization/Location, Core Platform Billing, Notifications, Restaurant Ordering, Kitchen, Payments, Inventory.
- **Publishes:** no event is defined for this version — no capability yet needs to react to a menu item being created.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** MenuItem aggregate creation invariants (BR2 — name required, length bound; BR3 — price must be positive; BR4 — `IsAlcoholic` defaults to `false`).
- **Integration tests:** AC1–AC7 above, executed against the real API and database.
- **Tenant isolation (ADR 0002 rule 8 — both read and write):** `CreateMenuItem` takes no menu item or tenant identifier as input and listing/querying menu items is out of scope, so isolation cannot be exercised through the public API alone. Following the same pattern used for `CreateTable` (`RowLevelSecurityTests`), verify directly through the application's least-privileged database role:
  - **Read:** a menu item created under Tenant A is not returned by a query scoped to Tenant B's tenant context.
  - **Write:** with the database tenant context set to Tenant B, an attempt to insert a menu item row carrying Tenant A's `tenant_id` is rejected by Row-Level Security (the `WITH CHECK` side of the policy) — `restaurant.menu_items` has RLS enabled since `add-item.md`, but only ever been exercised as a read (by `AddItem`'s own lookup); this is its first write-isolation test.

## Out of Scope

- Editing a menu item's name, price, or alcoholic flag.
- Deactivating, archiving, or deleting a menu item.
- Listing or querying menu items (`GetMenuItem`, `ListMenuItems`) — needed for a real UI to pick a menu item, but not required to unblock `AddItem`, which only needs a `MenuItemId` to already exist.
- Categories, sections, or ordering/display position within a menu.
- Availability toggles ("86'd" / out-of-stock items).
- Images.
- Per-item modifiers or notes (e.g. "no onions") — already out of scope of `add-item.md` itself.
- Pricing history, promotions, or discounts.
- Idempotency-key support (unlike `CreateOrder`, a duplicate menu item from a double-submission is a low-stakes, easily correctable annoyance, not a data-integrity or business-rule violation — same reasoning `create-table.md` used).

## Open Questions

- Should menu item names be unique per tenant? Left unenforced in this version, mirroring `create-table.md`'s equivalent decision for table labels — revisit if duplicate names turn out to be a practical problem (e.g. once categories exist and two items with the same name in different categories become ambiguous).
- Should `CreateMenuItem` accept an `Idempotency-Key` like `CreateOrder`? Not included in this version, for the same reason `create-table.md` excluded it.
