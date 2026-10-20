# Specification: Menu / GetMenuItems

**Status:** Approved
**Bounded Context:** Restaurant
**Business Capability:** Menu
**Related ADRs:** [ADR 0002 — Tenant Isolation Strategy](../../../docs/adr/0002-tenant-isolation-strategy.md)
**Last Updated:** 2026-10-20

This specification follows `constitution.md` Article I. It requires human review and approval before implementation begins, per Article 1.4. Approved after an author self-review — the same contributor wrote the draft and this approval, so this is recorded as a self-review rather than an independent one. That review round (commit on [PR #30](https://github.com/acamposve/presenciavirtualplatform/pull/30)) pinned the route, excluded `TenantId` from the response, made `page`/`pageSize` default independently, added AC6b so `pageSize=200` cannot be rejected by an off-by-one, and documented the collation assumption behind `lower(name)` ordering.

---

## Business Objective

Allow restaurant staff to see the menu items a tenant has registered, so a waiter taking an order can pick an item instead of having to remember a `MenuItemId` handed back by `CreateMenuItem` at some earlier moment.

Today the Restaurant API can create a menu item but never enumerate one: a client that loses an id — a page reload, a different device, a different shift — has no way to recover it. That is the gap `create-menu-item.md` explicitly deferred ("Listing or querying menu items (`GetMenuItem`, `ListMenuItems`) — needed for a real UI to pick a menu item, but not required to unblock `AddItem`"), and `AddItem` is the capability it blocks in practice.

## Actors

- **Waiter / Front-of-house staff** — browses the menu while adding items to an order.
- **Restaurant Manager** — browses the menu to confirm what is registered; not a different authorization boundary (see Security Requirements).

## Business Context

```text
CreateMenuItem → GetMenuItems → AddItem ─→ (Kitchen) → CloseOrder → Payment
                      ↑              │
                      └──────────────┘   (look up the MenuItemId to add)
```

`GetMenuItems` is a read-only capability that sits alongside `CreateMenuItem` (which owns the data) and `AddItem` (which consumes a `MenuItemId`). It does not advance any lifecycle, and it deliberately does not change `CreateMenuItem`'s or `AddItem`'s existing contracts.

## Functional Requirements

1. An authorized user MUST be able to retrieve a page of the menu items belonging to their own tenant, via `GET /api/v1/restaurants/menu-items` — the same collection route group `CreateMenuItem` already uses for `POST`.
2. The response MUST contain, for each item, its `MenuItemId`, `Name`, `Price`, `IsAlcoholic`, and `CreatedAt` — the same fields `CreateMenuItem` writes, so the response is a complete representation of a menu item rather than a lookup stub. `TenantId` is persisted and enforced server-side but is not part of the response body, consistent with `CreateMenuItem`'s response shape.
3. The response MUST be paginated: it MUST accept an optional 1-based `page` and an optional `pageSize`, and MUST report the requested `page`, the effective `pageSize`, and the tenant's `totalCount` of menu items.
4. The system MUST reject a `page` below 1, a `pageSize` below 1, or a `pageSize` above 200, as a validation error — never silently clamp or default an invalid value.
5. When `page` or `pageSize` is omitted, the system MUST default each independently — `page` to 1, `pageSize` to 50 — so supplying only one of the two still yields a well-defined request.
6. The response MUST be deterministically ordered by `Name` (case-insensitive ascending), with `MenuItemId` ascending as the tiebreaker, so that paging over an unchanging set cannot repeat or skip an item. Categories, sections, manual display order, and any other presentation ordering are out of scope (see Out of Scope).
7. A tenant with no menu items — and a `page` beyond the last page — MUST both return an empty `items` array with `totalCount` still reported correctly, not an error.
8. The response MUST reflect the tenant's menu items as of the time of the request: an item created by a prior `CreateMenuItem` call MUST be visible to a subsequent `GetMenuItems` request.

## Non-Functional Requirements

- **Tenant isolation:** enforced per ADR 0002 on the read (application-level scoping plus PostgreSQL RLS); a caller MUST never see another tenant's menu item, and there is no cross-tenant identifier in the request that could make isolation observable as a 404 (see Security Requirements).
- **Consistency:** read-your-writes within the same tenant (FR8); no caching or stale snapshot.
- **Performance:** the query is bounded by `pageSize` (max 200) and MUST NOT return an unbounded result set. There is no performance target beyond that bound in this version; the existing `ix_restaurant_menu_items_tenant_id` index covers the tenant filter, and a `lower(name)` ordering index is not required at the expected scale (see Data Requirements).
- **Observability:** the request must be traceable via structured logging with a correlation ID, per `constitution.md` Article XI. As with every other capability in this module so far, no such mechanism exists yet anywhere in the codebase; this specification does not introduce one on its own.

## Business Rules

- **BR1:** A MenuItem always belongs to exactly one Tenant (`create-menu-item.md` BR1). A menu listing is therefore always scoped to the caller's tenant and has no "all tenants" form.
- **BR2:** This is a read-only operation. It MUST NOT create, modify, or delete any menu item, order, or table (`create-menu-item.md`'s editing/deactivation behaviour remains out of scope).
- **BR3:** The response is a snapshot of the tenant's menu items at the time of the request, not a global one: because items are only ever created in this version (never edited or deleted — both out of scope), a given menu item's fields cannot change under the caller, but the *set* can grow between two requests. That is why FR6's ordering must be total and stable rather than incidental.
- **BR4:** Pagination is offset-based (`page`/`pageSize`), not cursor-based. A caller paging through a menu that is being added to concurrently MAY see an item twice or miss one; guaranteeing a consistent page-by-page snapshot across multiple requests is explicitly not required by this specification (see Out of Scope). Within a single request, the response is internally consistent: `items` and `totalCount` are read in one transaction.
- **BR5:** `totalCount` is the tenant's total number of menu items, not the number in the current page, and not a count of filtered items (there are no filters in this version).

## Acceptance Criteria

- **AC1 — Happy path:** Given an authenticated user with the `restaurant.menuitems.read` permission and a tenant with menu items, when they request the menu, then the response contains those items with `MenuItemId`, `Name`, `Price`, `IsAlcoholic`, and `CreatedAt`, plus `page`, `pageSize`, and `totalCount` — and no `TenantId`.
- **AC2 — Empty menu:** Given a tenant with no menu items, when the user requests the menu, then the response is 200 with an empty `items` array and `totalCount` = 0.
- **AC3 — Tenant scoping:** Given two tenants that each have menu items, when a user of the first tenant requests the menu, then the response contains only the first tenant's items and `totalCount` counts only those — no other tenant's item appears.
- **AC4 — Pagination:** Given a tenant with more items than `pageSize`, when the user requests `page=1` then `page=2` with the same `pageSize`, then the two pages contain disjoint items, in the same deterministic order, and `totalCount` is identical in both responses.
- **AC5 — Page beyond the end:** Given a `page` greater than the number of pages for that tenant, when the user requests it, then the response is 200 with an empty `items` array and the correct `totalCount`.
- **AC6 — Invalid paging values:** Given `page` = 0, `page` = -1, `pageSize` = 0, `pageSize` = 201, or a non-numeric `page`/`pageSize`, when the user requests the menu, then the request is rejected as a validation error (400 Bad Request) — never silently defaulted or clamped.
- **AC6b — Maximum page size is accepted:** Given `pageSize` = 200 (the ceiling), when the user requests the menu, then the request succeeds — the bound must reject 201 without also rejecting the largest legal value, the same boundary property `create-menu-item.md`'s AC5c pins for price.
- **AC7 — Default paging:** Given a request that omits both `page` and `pageSize`, when the user requests the menu, then `page` = 1 and `pageSize` = 50 are used and echoed in the response.
- **AC8 — Deterministic ordering:** Given two menu items whose names differ only by case, or two items with identical names, then repeated requests return them in the same relative order every time, using `Name` (case-insensitive) then `MenuItemId` as the total order.
- **AC9 — Missing permission:** Given an authenticated user without the `restaurant.menuitems.read` permission, when they request the menu, then the request is rejected as Forbidden.
- **AC10 — Unauthenticated request:** Given no valid authentication, when get-menu-items is called, then the request is rejected as Unauthorized.
- **AC11 — Read-your-writes:** Given a menu item created by a prior `CreateMenuItem` call in the same tenant, when the user next requests the menu, then that item is present.

## Domain Concepts

No new domain concepts. Reuses `MenuItem`, already defined in `glossary.md`; this specification adds no fields and no aggregate behaviour.

`glossary.md`'s `MenuItem` entry currently ends by listing editing, deactivating, and categorization as out of scope; once this specification is approved, that entry may additionally note that the menu can be browsed via `GetMenuItems`.

## Security Requirements

- The endpoint requires authentication (Core Identity).
- The endpoint requires the `restaurant.menuitems.read` permission, scoped to the caller's tenant (Core Authorization/RBAC), per the permission naming convention in `architecture.md` §20 — the read counterpart of `restaurant.menuitems.create`. This is a new permission string; implementing this specification requires registering it alongside the existing ones.
- The Tenant is resolved exclusively from the authenticated context, never from client input, per ADR 0002 rules 2–3.
- Unlike the single-resource reads (`GetOrder`), there is no identifier in the request that could reference another tenant, so this capability cannot leak cross-tenant existence through a 404: it simply never returns another tenant's rows.

## Error Scenarios

| Scenario | Response |
|---|---|
| No authentication | 401 Unauthorized |
| Authenticated but missing `restaurant.menuitems.read` | 403 Forbidden |
| `page` or `pageSize` below 1, `pageSize` above 200, or either not a valid integer | 400 Bad Request |

An empty result set is **not** an error (AC2/AC5): 200 with `items: []`.

Internal implementation details MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Reads:** `restaurant.menu_items` rows scoped to the caller's tenant, ordered per FR6 and limited to `pageSize`, plus a `COUNT(*)` over the same tenant scope for `totalCount`. The two reads MUST happen within a single transaction, so `items` and `totalCount` agree with each other.
- **Writes:** none. This is a pure read capability.
- No schema change is required: `restaurant.menu_items` already exists (`0005_restaurant_menu_items.sql`), has `ix_restaurant_menu_items_tenant_id` for the tenant filter, and the least-privilege application role already holds `SELECT` on it (`0010_add_item_grants.sql`). No grants migration is expected; if implementation discovers it needs one, that is a finding to raise, not a silent addition.
- Ordering by `lower(name)` cannot use a plain b-tree index on `name`; at the expected menu size (tens to low hundreds of items per tenant) a sort after the tenant-filtered read is acceptable and no functional index is introduced. Revisit only if a real tenant's menu makes this measurable.
- Ordering is performed by PostgreSQL under the database's default collation, not with a locale-aware comparison: accented names therefore sort by the database's rules rather than a Spanish-specific alphabet. That is accepted for this version (names are free text and case-insensitivity is the property that matters for a stable order); a locale-aware sort would be a deliberate change, not an incidental one.
- Tenant isolation MUST follow ADR 0002 (application-level scoping plus PostgreSQL RLS).

## Integration Requirements

- **Depends on:** Core Identity & Authentication, Core Authorization/RBAC, tenant isolation infrastructure (ADR 0002) — the same minimal Core slice every other Restaurant capability depends on.
- **Does not depend on:** Core Organization/Location, Core Platform Billing, Notifications, Ordering, Kitchen, Payments, Inventory.
- **Publishes:** none — this is a read-only capability.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** the query validator (FR4/AC6's bounds and defaults), and the handler's projection of the repository's rows into the response — no new domain behaviour exists to unit-test.
- **Integration tests:** AC1–AC11 above, executed against the real API and database, including:
  - **Multi-tenant scoping (AC3):** two tenants with items, asserting the response and `totalCount` contain only the caller's.
  - **Pagination correctness (AC4/AC5):** seeded items across several pages, asserting disjoint pages, stable ordering, a correct `totalCount`, and an empty page past the end — the property that makes offset paging usable at all.
  - **Ordering stability (AC8):** identical names and case-differing names, asserting repeated requests return an identical order.
  - **Boundary validation (AC6/AC6b):** `page=0`, negative, `pageSize=0`, `pageSize=201`, and non-numeric values each rejected with 400, while `pageSize=200` is accepted — the ceiling must not be off by one.
  - **Response shape (AC1):** the serialized item carries exactly the documented fields and no `tenantId`.
  - **Read-side RLS:** the existing `RowLevelSecurityTests.AppRole_CannotInsertOrSeeAnotherTenantsMenuItem` already proves `restaurant.menu_items` is isolated by RLS independently of application filtering; this capability's application-level scoping is additionally covered by AC3, so no new RLS test is required unless implementation reveals a gap.

## Out of Scope

- Filtering (by `IsAlcoholic`, by name search, by category) and sorting by anything other than FR6's fixed order — the first real client need defines the shape; inventing filters now risks specifying an API nobody uses.
- Cursor/keyset pagination and cross-request snapshot consistency (BR4).
- Categories, sections, display position, availability ("86'd"), images, modifiers — all still out of scope of `create-menu-item.md` too.
- `GetMenuItem` (single item by id) — `AddItem` already resolves a `MenuItemId` internally, and no client today needs a by-id read distinct from this listing.
- Editing, deactivating, or deleting a menu item — still future capabilities.
- Any caching layer, ETag, or conditional-request support.
- Real-time push of menu changes.

## Open Questions

- **Is offset paging the right shape for a menu?** Resolved here as *yes*, with a 200-item ceiling: menus are small and change rarely, so keyset paging's complexity buys nothing yet. Revisit if a tenant's menu grows into the thousands or if a client needs stable iteration while items are being added.
- **Should `totalCount` be returned at all?** It costs a second query per request. Kept because a UI needs a page count to render pagination, and the cost is a single indexed count on a small tenant-scoped table.
- **Should ordering be alphabetical or creation order?** Alphabetical (FR6) matches how a waiter scans a menu; creation order would match how a manager enters it. Chosen alphabetical, with categories/display position deferred — the point a real ordering model would be specified.
