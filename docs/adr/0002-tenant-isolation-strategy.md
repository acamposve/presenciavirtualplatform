# ADR 0002: Tenant Isolation Strategy

**Status:** Accepted
**Date:** 2026-09-05

## Context

`constitution.md` Article IX mandates tenant isolation as a security boundary that must never be implicit. `architecture.md` §4.4 and §13 state that the platform uses a single physical PostgreSQL database with modules maintaining only *logical* ownership of their data. Neither document specified the concrete technical mechanism enforcing that isolation. This was identified as the highest-priority open question in the Bounded Context & Business Capability Analysis, because retrofitting isolation after tenant-owned tables and data already exist is high-risk.

## Decision

Tenant isolation is enforced by two complementary layers, used together:

**Primary: application-level tenant enforcement.**

1. Every tenant-owned aggregate/table MUST contain an explicit tenant identifier where appropriate.
2. Application code MUST resolve the current Tenant from the authenticated request context — never from a value the caller supplies unchecked.
3. Tenant identifiers MUST NOT be accepted blindly from untrusted client input (e.g. a `tenantId` field in a request body or query string MUST NOT be trusted as-is).
4. Repository/data-access operations MUST enforce tenant scope on every read and write.

**Secondary defense: PostgreSQL Row-Level Security (RLS).**

5. RLS policies MUST be enabled on tenant-owned tables to provide defense-in-depth against accidental cross-tenant access — i.e. to catch the case where application-level scoping is missing or incorrectly implemented.

**Cross-tenant access:**

6. Cross-tenant data access is forbidden unless an explicitly authorized platform-level operation requires it.
7. Platform administrators do NOT automatically bypass tenant isolation. Any elevated cross-tenant operation must be explicit, intentional, and auditable — never a side effect of an administrator's elevated role.

**Testing:**

8. Automated tests MUST verify cross-tenant read/write isolation (i.e. a request authenticated as Tenant A must never be able to read or write Tenant B's data), for every tenant-owned capability.

## Alternatives Considered

- **Schema-per-tenant.** Rejected: contradicts the single-database, modular-monolith direction (`architecture.md` §3, §13) and does not scale operationally for a multi-tenant SaaS with an initially unknown, potentially large number of tenants; migrations would need to run per schema.
- **Application-level enforcement only (no RLS).** Rejected: a single missed `WHERE tenant_id = ...` clause in any repository method becomes a cross-tenant data leak with no second line of defense.
- **RLS only (no application-level enforcement).** Rejected: RLS alone does not prevent business-logic-level mistakes (e.g. resolving the wrong tenant context before a query even reaches the database), and pushes all isolation logic into the database layer where it is harder to unit test in isolation from infrastructure.

## Consequences

- Every tenant-owned table's DDL must define a tenant identifier column and a corresponding RLS policy from the moment the table is created; this cannot be added as an afterthought.
- The application must establish "current tenant" as part of the authenticated request pipeline (e.g. middleware) so that both the application-level scoping and the RLS session context can rely on the same resolved value.
- Integration tests for any tenant-owned capability must include explicit cross-tenant isolation cases, per rule 8; a capability's test suite is incomplete without them (`constitution.md` Article X.3).
- Any future platform-level operation that legitimately needs cross-tenant access (e.g. platform analytics, support tooling) must be designed as an explicit, audited exception — not a default administrator privilege.
- This ADR must be read before implementing the first tenant-owned table, including the first table required by `Restaurant → Ordering → CreateOrder`.

## Status

Accepted.
