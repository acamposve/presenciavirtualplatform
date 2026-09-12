# Specification: Tables / CreateTable

**Status:** Draft — Pending Review
**Bounded Context:** Restaurant
**Business Capability:** Tables
**Related ADRs:** [ADR 0002 — Tenant Isolation Strategy](../../../docs/adr/0002-tenant-isolation-strategy.md)
**Last Updated:** 2026-09-12

This specification follows `constitution.md` Article I. It requires human review and approval before implementation begins, per Article 1.4.

---

## Business Objective

Allow a restaurant to register the physical tables it operates, so that Ordering ([`specs/restaurant/ordering/create-order.md`](../ordering/create-order.md)) has a real table to reference instead of one seeded directly in the database. Today, `CreateOrder` explicitly assumes a table already exists — this specification is what makes that assumption true in practice.

## Actors

- **Restaurant Manager** — registers and maintains the restaurant's tables as part of setting up the floor. This is treated as a setup/configuration action, distinct from day-to-day order-taking.

## Business Context

```text
CreateTable → (table exists) → CreateOrder → AddItem → CloseOrder
```

This specification covers only creating a table. Editing a table's label, deactivating/removing a table, listing/querying tables, seating capacity, floor sections, and reservations are all separate, not-yet-written specifications (see Out of Scope).

## Functional Requirements

1. An authorized user MUST be able to register a new table within their own tenant, given a label.
2. The system MUST reject the request if the label is empty or exceeds 100 characters.
3. The system MUST generate a unique identifier for the new table.
4. The system MUST record the tenant that owns the table and when it was created.
5. The system MUST return the created table's identifier and label to the caller.

## Non-Functional Requirements

- **Tenant isolation:** enforced per ADR 0002 on the write (and on any future read of this data).
- **Consistency:** the created table must be immediately visible to a subsequent read by the same tenant — in particular, immediately usable by `CreateOrder`.
- **Observability:** the request must be traceable via structured logging with a correlation ID (per `constitution.md` Article XI). Persisted audit trail (Core Audit Logging capability) remains out of scope, consistent with `create-order.md`.

## Business Rules

- **BR1:** A Table always belongs to exactly one Tenant.
- **BR2:** A Table's label MUST NOT be empty and MUST NOT exceed 100 characters.

Table labels are **not** required to be unique within a tenant in this version — see Open Questions.

## Acceptance Criteria

- **AC1 — Happy path:** Given an authenticated user with the `restaurant.tables.create` permission, when they create a table with a valid label, then a new Table is created, scoped to the caller's tenant, and the response contains a generated `TableId` and the given `Label` (per FR5 — `TenantId` is persisted and enforced server-side, per Security Requirements, but is not part of the response body, consistent with `CreateOrder`'s response shape).
- **AC2 — Empty label:** Given a request with an empty or whitespace-only label, when the user attempts to create the table, then the request is rejected as a validation error and no Table is created.
- **AC3 — Label too long:** Given a label longer than 100 characters, when the user attempts to create the table, then the request is rejected as a validation error and no Table is created.
- **AC4 — Missing permission:** Given an authenticated user without the `restaurant.tables.create` permission, when they attempt to create a table, then the request is rejected as Forbidden and no Table is created.
- **AC5 — Unauthenticated request:** Given no valid authentication, when create-table is called, then the request is rejected as Unauthorized.

## Domain Concepts

- **Table** (already referenced by `create-order.md`, formalized here as an aggregate) — fields relevant to this specification: `TableId`, `TenantId`, `Label`, `CreatedAt`.

`Table` is already listed in `glossary.md` under Restaurant (added when `create-order.md` was approved). Its definition previously said it was "owned by a separate, not-yet-written Table Management specification" — this specification is that capability's first increment, so the glossary entry has been updated accordingly as part of this change.

## Security Requirements

- The endpoint requires authentication (Core Identity).
- The endpoint requires the `restaurant.tables.create` permission, scoped to the caller's tenant (Core Authorization/RBAC), per the permission naming convention in `architecture.md` §20.
- The Tenant is resolved exclusively from the authenticated context, never from client input, per ADR 0002 rules 2–3.

## Error Scenarios

| Scenario | Response |
|---|---|
| No authentication | 401 Unauthorized |
| Authenticated but missing `restaurant.tables.create` | 403 Forbidden |
| Label missing, empty, or longer than 100 characters | 400 Bad Request |

Internal implementation details MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Writes:** a new Table record scoped to the caller's tenant.
- This reuses the `restaurant.tables` table already created by `0001_restaurant_tables.sql` for `CreateOrder` — no new migration is expected, only a write path where today only a manual/test seed exists.
- Tenant isolation on the write MUST follow ADR 0002 (application-level scoping plus PostgreSQL RLS), consistent with the existing table.

## Integration Requirements

- **Depends on:** Core Identity & Authentication, Core Authorization/RBAC, tenant isolation infrastructure (ADR 0002) — the same minimal Core slice `CreateOrder` depends on.
- **Does not depend on:** Core Organization/Location, Core Platform Billing, Notifications, Restaurant Ordering, Kitchen, Payments, Inventory.
- **Publishes:** no event is defined for this version — no capability yet needs to react to a table being created.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** Table aggregate creation invariants (BR2 — label required, length bound).
- **Integration tests:** AC1–AC5 above, executed against the real API and database.
- **Tenant isolation (ADR 0002 rule 8 — both read and write):** `CreateTable` takes no table or tenant identifier as input and `GetTable`/`ListTables` are out of scope, so isolation cannot be exercised through the public API alone. Following the same pattern used for `CreateOrder` (`RowLevelSecurityTests`), verify directly through the application's least-privileged database role:
  - **Read:** a table created under Tenant A is not returned by a query scoped to Tenant B's tenant context.
  - **Write:** with the database tenant context set to Tenant B, an attempt to insert a table row carrying Tenant A's `tenant_id` is rejected by Row-Level Security (the `WITH CHECK` side of the policy, not just the `USING`/read side) — this is the one CreateTable-specific case worth its own test, since CreateTable is the platform's first *write* capability exercised against this policy; `CreateOrder`'s own RLS tests did not cover this side and should eventually gain an equivalent case too.

## Out of Scope

- Editing a table's label.
- Deactivating, archiving, or deleting a table.
- Listing or querying tables (`GetTable`, `ListTables`) — needed for a real UI to pick a table, but not required to unblock `CreateOrder`, which only needs a `TableId` to already exist.
- Seating capacity, floor sections/zones, and any structural Location concept (Core Organization/Location per [ADR 0001](../../../docs/adr/0001-tenant-vertical-organization-location-model.md) remains unimplemented).
- Reservations linkage.
- Idempotency-key support (unlike `CreateOrder`, a duplicate table from a double-submission is a low-stakes, easily correctable annoyance, not a data-integrity or business-rule violation — see Open Questions).

## Open Questions

- Should table labels be unique per tenant? Left unenforced in this version — real restaurants may legitimately have tables named similarly across sections not yet modeled (see Core Location, ADR 0001). Revisit once floor sections exist.
- Should `CreateTable` accept an `Idempotency-Key` like `CreateOrder`? Not included in this version; add if double-submission from a real client turns out to be a practical problem.
- Is table creation exclusively a Manager action, or should any authenticated staff member with the right permission do it? Modeled here purely as a permission check (`restaurant.tables.create`), not a role name — no additional decision needed unless a future RBAC administration spec says otherwise.
