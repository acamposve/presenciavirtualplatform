# Specification: Tables / ListTables

**Status:** Draft
**Bounded Context:** Restaurant
**Business Capability:** Tables
**Related ADRs:** [ADR 0002 — Tenant Isolation Strategy](../../../docs/adr/0002-tenant-isolation-strategy.md)
**Last Updated:** 2026-10-20

This specification follows `constitution.md` Article I. It requires human review and approval before implementation begins, per Article 1.4.

---

## Business Objective

Allow restaurant staff to see the tables a tenant has registered, so they can pick one to open an order on instead of having to remember a `TableId` returned by `CreateTable` at some earlier moment.

The same gap `GetMenuItems` (`specs/restaurant/menu/get-menu-items.md`) closes for the menu exists for tables: `CreateTable` can create a table but nothing can enumerate one, which is why `create-table.md` deferred "Listing or querying tables (`GetTable`, `ListTables`) — needed for a real UI to pick a table, but not required to unblock `CreateOrder`, which only needs a `TableId` to already exist." In practice a front of house cannot start service without it.

## Actors

- **Waiter / Front-of-house staff** — picks a table to open, check, or close an order on.
- **Restaurant Manager** — browses the floor's registered tables; not a different authorization boundary (see Security Requirements).

## Business Context

```text
CreateTable → ListTables → CreateOrder ─→ AddItem → (Kitchen) → CloseOrder → Payment
                   ↑            │
                   └────────────┘   (choose the TableId to open an order on)
```

`ListTables` is a read-only capability that sits alongside `CreateTable` (which owns the data) and `Ordering` (which consumes a `TableId`). It does not advance any lifecycle, and it deliberately does not change `CreateTable`'s or `CreateOrder`'s existing contracts.

## Functional Requirements

1. An authorized user MUST be able to retrieve a page of the tables belonging to their own tenant.
2. The response MUST contain, for each table, its `TableId`, `Label`, and `CreatedAt`. `TenantId` is persisted and enforced server-side but is not part of the response body, consistent with `CreateTable`'s response shape (its AC1 already states this explicitly).
3. The response MUST be paginated: it MUST accept an optional 1-based `page` and an optional `pageSize`, and MUST report the requested `page`, the effective `pageSize`, and the tenant's `totalCount` of tables — the same pagination contract `GetMenuItems` defines, deliberately kept identical so a client pages both resources the same way.
4. The system MUST reject a `page` below 1, a `pageSize` below 1, or a `pageSize` above 200, as a validation error — never silently clamp or default an invalid value.
5. When `page` or `pageSize` is omitted, the system MUST default them to `page` = 1 and `pageSize` = 50.
6. The response MUST be deterministically ordered by `Label` (case-insensitive ascending), with `TableId` ascending as the tiebreaker, so that paging over an unchanging set cannot repeat or skip a table. Floor sections, zones, and manual display order are out of scope (see Out of Scope).
7. A tenant with no tables — and a `page` beyond the last page — MUST both return an empty `items` array with `totalCount` still reported correctly, not an error.
8. The response MUST reflect the tenant's tables as of the time of the request: a table created by a prior `CreateTable` call MUST be visible to a subsequent `ListTables` request, and therefore immediately usable by `CreateOrder` (`create-table.md`'s own Consistency requirement).

## Non-Functional Requirements

- **Tenant isolation:** enforced per ADR 0002 on the read (application-level scoping plus PostgreSQL RLS); a caller MUST never see another tenant's table. As with `GetMenuItems`, there is no cross-tenant identifier in the request, so isolation cannot surface as a 404.
- **Consistency:** read-your-writes within the same tenant (FR8); no caching or stale snapshot.
- **Performance:** the query is bounded by `pageSize` (max 200) and MUST NOT return an unbounded result set. The existing `ix_restaurant_tables_tenant_id` index covers the tenant filter; the `lower(label)` ordering sort is acceptable at expected floor sizes (tens of tables), and no functional index is introduced (see Data Requirements).
- **Observability:** the request must be traceable via structured logging with a correlation ID, per `constitution.md` Article XI. As with every other capability in this module so far, no such mechanism exists yet anywhere in the codebase; this specification does not introduce one on its own.

## Business Rules

- **BR1:** A Table always belongs to exactly one Tenant (`create-table.md` BR1). A table listing is therefore always scoped to the caller's tenant and has no "all tenants" form.
- **BR2:** This is a read-only operation. It MUST NOT create, modify, or delete any table, order, or menu item.
- **BR3:** The response is a snapshot of the tenant's tables at the time of the request. Because tables are only ever created in this version (editing/deactivating are out of scope — `create-table.md`), a table's own fields cannot change under the caller, but the set can grow between two requests; that is why FR6's ordering must be total and stable.
- **BR4:** Pagination is offset-based and, exactly as `GetMenuItems` BR4 specifies, carries no cross-request snapshot guarantee: a caller paging through a floor that is being added to concurrently MAY see a table twice or miss one. Within a single request, `items` and `totalCount` are read in one transaction and are consistent with each other.
- **BR5:** `totalCount` is the tenant's total number of tables, not the number in the current page (there are no filters in this version).
- **BR6:** `ListTables` reports registered tables only. It deliberately does **not** report each table's current `Open` order or occupancy — that would couple the Tables capability's read to Ordering's state and belongs to a separate, not-yet-written capability (see Out of Scope and Open Questions).

## Acceptance Criteria

- **AC1 — Happy path:** Given an authenticated user with the `restaurant.tables.read` permission and a tenant with tables, when they request the table list, then the response contains those tables with `TableId`, `Label`, and `CreatedAt`, plus `page`, `pageSize`, and `totalCount`, and no `TenantId`.
- **AC2 — Empty floor:** Given a tenant with no tables, when the user requests the list, then the response is 200 with an empty `items` array and `totalCount` = 0.
- **AC3 — Tenant scoping:** Given two tenants that each have tables, when a user of the first tenant requests the list, then the response contains only the first tenant's tables and `totalCount` counts only those — no other tenant's table appears.
- **AC4 — Pagination:** Given a tenant with more tables than `pageSize`, when the user requests `page=1` then `page=2` with the same `pageSize`, then the two pages contain disjoint tables, in the same deterministic order, and `totalCount` is identical in both responses.
- **AC5 — Page beyond the end:** Given a `page` greater than the number of pages for that tenant, when the user requests it, then the response is 200 with an empty `items` array and the correct `totalCount`.
- **AC6 — Invalid paging values:** Given `page` = 0, `page` = -1, `pageSize` = 0, `pageSize` = 201, or a non-numeric `page`/`pageSize`, when the user requests the list, then the request is rejected as a validation error (400 Bad Request) — never silently defaulted or clamped.
- **AC7 — Default paging:** Given a request that omits both `page` and `pageSize`, when the user requests the list, then `page` = 1 and `pageSize` = 50 are used and echoed in the response.
- **AC8 — Deterministic ordering:** Given two tables whose labels differ only by case, or two tables with identical labels (permitted — `create-table.md` does not require uniqueness), then repeated requests return them in the same relative order every time, using `Label` (case-insensitive) then `TableId` as the total order.
- **AC9 — Missing permission:** Given an authenticated user without the `restaurant.tables.read` permission, when they request the list, then the request is rejected as Forbidden.
- **AC10 — Unauthenticated request:** Given no valid authentication, when list-tables is called, then the request is rejected as Unauthorized.
- **AC11 — Read-your-writes:** Given a table created by a prior `CreateTable` call in the same tenant, when the user next requests the list, then that table is present and its `TableId` is immediately usable by `CreateOrder`.

## Domain Concepts

No new domain concepts. Reuses `Table`, already defined in `glossary.md`; this specification adds no fields and no aggregate behaviour.

`glossary.md`'s `Table` entry currently reads "A physical table in a Restaurant, referenced by `CreateOrder` and owned by the Tables capability"; once this specification is approved, that entry may additionally note that tables can be browsed via `ListTables`.

## Security Requirements

- The endpoint requires authentication (Core Identity).
- The endpoint requires the `restaurant.tables.read` permission, scoped to the caller's tenant (Core Authorization/RBAC), per the permission naming convention in `architecture.md` §20 — the read counterpart of `restaurant.tables.create`. This is a new permission string; implementing this specification requires registering it alongside the existing ones.
- The Tenant is resolved exclusively from the authenticated context, never from client input, per ADR 0002 rules 2–3.
- Unlike the single-resource reads (`GetOrder`), there is no identifier in the request that could reference another tenant, so this capability cannot leak cross-tenant existence through a 404: it simply never returns another tenant's rows.

## Error Scenarios

| Scenario | Response |
|---|---|
| No authentication | 401 Unauthorized |
| Authenticated but missing `restaurant.tables.read` | 403 Forbidden |
| `page` or `pageSize` below 1, `pageSize` above 200, or either not a valid integer | 400 Bad Request |

An empty result set is **not** an error (AC2/AC5): 200 with `items: []`.

Internal implementation details MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Reads:** `restaurant.tables` rows scoped to the caller's tenant, ordered per FR6 and limited to `pageSize`, plus a `COUNT(*)` over the same tenant scope for `totalCount`. The two reads MUST happen within a single transaction so `items` and `totalCount` agree with each other.
- **Writes:** none. This is a pure read capability.
- No schema change and no grants migration are expected: `restaurant.tables` already exists (`0001_restaurant_tables.sql`), has `ix_restaurant_tables_tenant_id`, and the least-privilege application role already holds `SELECT` on it (`0004_grants.sql`). If implementation discovers a missing privilege, that is a finding to raise rather than a silent addition.
- Ordering by `lower(label)` cannot use a plain b-tree index on `label`; at expected floor sizes a sort after the tenant-filtered read is acceptable and no functional index is introduced.
- Tenant isolation MUST follow ADR 0002 (application-level scoping plus PostgreSQL RLS).

## Integration Requirements

- **Depends on:** Core Identity & Authentication, Core Authorization/RBAC, tenant isolation infrastructure (ADR 0002) — the same minimal Core slice every other Restaurant capability depends on.
- **Does not depend on:** Ordering (BR6 — occupancy is deliberately not read here), Core Organization/Location, Core Platform Billing, Notifications, Kitchen, Payments, Inventory.
- **Publishes:** none — this is a read-only capability.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** the query validator (FR4/AC6's bounds and defaults), and the handler's projection of the repository's rows into the response — no new domain behaviour exists to unit-test.
- **Integration tests:** AC1–AC11 above, executed against the real API and database, including:
  - **Multi-tenant scoping (AC3):** two tenants with tables, asserting the response and `totalCount` contain only the caller's.
  - **Pagination correctness (AC4/AC5):** seeded tables across several pages, asserting disjoint pages, stable ordering, a correct `totalCount`, and an empty page past the end.
  - **Ordering stability (AC8):** identical and case-differing labels, asserting repeated requests return an identical order — labels are not unique per tenant (`create-table.md`), so the `TableId` tiebreaker is what makes this test meaningful.
  - **Boundary validation (AC6):** `page=0`, negative, `pageSize=0`, `pageSize=201`, and non-numeric values each rejected with 400.
  - **Read-your-writes / CreateOrder integration (AC11):** a table listed here can immediately be used as `CreateOrder`'s `tableId`, proving the listing and Ordering agree on what exists.
  - **Read-side RLS:** the existing `RowLevelSecurityTests.AppRole_CannotSeeAnotherTenantsTableEvenWithoutApplicationFiltering` already proves `restaurant.tables` is isolated by RLS independently of application filtering; AC3 additionally covers application-level scoping, so no new RLS test is required unless implementation reveals a gap.

## Out of Scope

- Occupancy / current `Open` order per table, and any floor-status view (BR6) — a future, separate capability that would read Ordering state.
- Floor sections, zones, seating capacity, or any structural Location concept (Core Organization/Location per [ADR 0001](../../adr/0001-tenant-vertical-organization-location-model.md) remains unimplemented).
- Filtering and sorting by anything other than FR6's fixed order.
- Cursor/keyset pagination and cross-request snapshot consistency (BR4).
- `GetTable` (single table by id) — `CreateOrder` already validates a `TableId` internally, and no client today needs a by-id read distinct from this listing.
- Editing, deactivating, archiving, or deleting a table.
- Any caching layer, ETag, or conditional-request support.
- Real-time push of floor changes.

## Open Questions

- **Should the listing include occupancy?** Deliberately excluded (BR6): a floor plan that shows which tables are occupied is a genuine front-of-house need, but it requires reading Ordering state from a Tables read and would make this capability a cross-capability view. It belongs in its own specification (e.g. a `GetFloorStatus` capability) once Order already exists — which it now does, so that spec is unblocked whenever it is wanted.
- **Is offset paging the right shape here too?** Yes, for the same reasons `GetMenuItems` gives: a floor has tens of tables, not thousands, and the contract is deliberately identical so one client paging component serves both.
- **Should ordering be by label or by creation order?** Alphabetical label (FR6) matches how staff refer to tables ("Table 5"), with sections/zones deferred.
