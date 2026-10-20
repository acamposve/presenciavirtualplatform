# Specification: Identity / RegisterTenant

**Status:** Draft
**Bounded Context:** Core
**Business Capability:** Identity
**Related ADRs:** [ADR 0001 — Tenant, Vertical, Organization and Location Model](../../../docs/adr/0001-tenant-vertical-organization-location-model.md), [ADR 0002 — Tenant Isolation Strategy](../../../docs/adr/0002-tenant-isolation-strategy.md), [ADR 0005 — Minimal Authentication](../../../docs/adr/0005-minimal-authentication-for-first-implementation.md), [ADR 0006 — Core Identity: Local Credentials and Token Issuance](../../../docs/adr/0006-core-identity-local-credentials-and-token-issuance.md)
**Last Updated:** 2026-10-20

This specification follows `constitution.md` Article I. It requires human review and approval before implementation begins, per Article 1.4.

---

## Business Objective

Allow a new restaurant to create its own tenant, its owner account, and an authenticated session in a single request, so the platform can be used by a real client for the first time.

Today no tenant, user, or credential exists anywhere in the system: `tenant_id` and permissions live only as claims on a JWT that someone mints by hand ([ADR 0005](../../../docs/adr/0005-minimal-authentication-for-first-implementation.md) deferred issuance explicitly). Every endpoint is therefore unreachable outside the test suite. This is the capability that turns the platform from something that can be tested into something that can be used.

## Actors

- **Prospective restaurant owner** — the first user of a new tenant, acting before any account exists. This is the only capability in the platform that is legitimately reachable without authentication.
- **Platform operator (Presencia Virtual)** — not a direct actor. Operates the signing key and configuration this capability depends on; does not create tenants by hand in this increment.

## Business Context

```text
RegisterTenant → (tenant + owner + token) → any authenticated capability
                                             (tables, menu, orders, …)
                      ↑
                 Login (separate, not-yet-written specification)
```

This is the first Core capability and the first `specs/core/` specification. It precedes `Login`, which will let the same owner authenticate again later; this capability deliberately issues a token itself so that a freshly created tenant is immediately usable without a second round trip.

Registration is self-service in this increment. Inviting other users into an existing tenant, and creating a tenant on someone else's behalf, are separate capabilities (see Out of Scope).

## Functional Requirements

1. An unauthenticated caller MUST be able to register a new tenant by supplying a tenant name, a tenant slug, an owner email address, and a password, via `POST /api/v1/core/tenants`.
2. The system MUST create the tenant, the owner user, and nothing else, atomically: either all three records commit, or none do.
3. The system MUST reject the request if the tenant slug is already taken, and MUST NOT reuse or merge into the existing tenant.
4. The system MUST validate the tenant slug as 3–50 characters of lowercase ASCII letters, digits and single hyphens, not starting or ending with a hyphen.
5. The system MUST validate the password as at least 12 and at most 128 characters, and MUST NOT impose character-class composition rules.
6. The system MUST store only a PBKDF2-HMAC-SHA256 hash of the password — never the plaintext, and never in a log or error response.
7. The system MUST issue an access token for the new owner as part of the response, carrying the tenant id, the owner's user id, and the owner role's permissions, signed exactly as the existing authentication handler validates ([ADR 0006](../../../docs/adr/0006-core-identity-local-credentials-and-token-issuance.md) decision 4).
8. The response MUST contain the new tenant's `TenantId` and `Slug`, the owner's `UserId` and `Email`, the `AccessToken`, and its expiry.
9. The system MUST normalize the email address (trimmed, lowercased) before storing and comparing it.
10. The system MUST reject an email address that is not a valid address or exceeds 320 characters.

## Non-Functional Requirements

- **Tenant isolation:** the new tenant's rows are tenant-owned from the moment they are written; `core.users` MUST be RLS-protected exactly as ADR 0002 requires, and the tenant registry row itself is platform-level data with no tenant scope (ADR 0006 decision 2).
- **Atomicity:** FR2 requires a single transaction. A tenant row without its owner user (or the reverse) is a data-integrity defect, not a recoverable state.
- **Credential safety:** the password MUST NOT appear in logs, error responses, or the response body; the hash MUST NOT be returned either.
- **Timing:** password verification MUST be constant-time; registration itself need not hide whether the slug was taken (a 409 is the required behaviour), but it MUST NOT reveal whether an email already exists in *another* tenant — emails are per-tenant, so no such check occurs at all.
- **Observability:** the request must be traceable via structured logging with a correlation ID, per `constitution.md` Article XI. As with every capability so far, no such mechanism exists yet; this specification does not introduce one on its own, but it is the first capability where logging a credential accident would be catastrophic, so the constraint is stated explicitly.
- **Rate limiting:** not implemented in this increment, and explicitly required before public exposure (see Out of Scope). Stated here so its absence is a recorded decision rather than an oversight.

## Business Rules

- **BR1:** A Tenant is the platform's ownership and isolation boundary ([ADR 0001](../../../docs/adr/0001-tenant-vertical-organization-location-model.md)); every user belongs to exactly one tenant.
- **BR2:** A tenant slug is globally unique, immutable in this increment, and is the identifier used to select a tenant at login ([ADR 0006](../../../docs/adr/0006-core-identity-local-credentials-and-token-issuance.md) decision 2). Changing a slug is a separate, future capability — clients will come to depend on it.
- **BR3:** Email uniqueness is scoped to the tenant, not global: the same person may hold accounts in two tenants. Registration therefore performs no cross-tenant email check, and a `409` is never returned for a duplicate email from another tenant.
- **BR4:** The registering user is the tenant's **Owner**, and is granted the owner role's full permission set. There is exactly one owner at registration; additional owners are a future capability.
- **BR5:** Passwords are never recoverable: the system stores a salted, stretched hash only, and no capability returns or transmits a password.
- **BR6:** Registration never takes a `TenantId` from the caller — the tenant is created by the request, not selected by it. There is no code path in which a caller supplies an existing tenant id.
- **BR7:** The access token issued at registration is a normal token for the new owner: it carries no special "registration" scope, and grants exactly the owner role's permissions — no more.

## Acceptance Criteria

- **AC1 — Happy path:** Given a unique slug, a valid name, a valid email and a valid password, when an unauthenticated caller registers, then the response is 201 with `TenantId`, `Slug`, `UserId`, `Email`, `AccessToken` and its expiry, and the tenant + owner exist in the database.
- **AC2 — The issued token works:** Given a token returned by registration, when it is used to call an existing protected capability the owner role is entitled to (e.g. `GET /api/v1/restaurants/tables`), then the call succeeds — proving the token is genuinely consumable, not merely well-shaped.
- **AC3 — Slug already taken:** Given an existing tenant with slug `acme`, when another caller registers with slug `acme`, then the request is rejected as Conflict and no new tenant or user is created.
- **AC4 — Invalid slug:** Given a slug that is too short, too long, contains uppercase or non-ASCII characters, has consecutive or leading/trailing hyphens, then the request is rejected as a validation error (400) and nothing is created.
- **AC5 — Weak or malformed password:** Given a password shorter than 12 characters or longer than 128, when the caller registers, then the request is rejected as a validation error (400) and nothing is created.
- **AC6 — Invalid email:** Given an email address that is not a valid address or exceeds 320 characters, then the request is rejected as a validation error (400) and nothing is created.
- **AC7 — Email normalization:** Given an email supplied as `Owner@Example.COM` (with surrounding whitespace), when the tenant is created, then the stored and returned email is `owner@example.com`.
- **AC8 — Password never echoed:** Given any successful or failed registration, then no response body, error message, or persisted row contains the plaintext password; the stored credential is a PBKDF2 hash.
- **AC9 — Atomicity:** Given a failure while creating the owner after the tenant row has been written (e.g. a forced constraint violation), then no tenant row remains — the request leaves no partial registration.
- **AC10 — Unauthenticated by design:** Given no `Authorization` header, the request is accepted (it is the one capability that must work before any session exists) — a client sending a token anyway does not change the outcome or the identity created.
- **AC11 — Tenant isolation of the new user row:** Given a tenant created by registration, when a query is executed under another tenant's context through the least-privileged application role, then that tenant's `core.users` row is not visible.

## Domain Concepts

New terms, to be added to `glossary.md`'s currently empty **Core** section once this specification is approved:

- **Tenant** — the platform's ownership and isolation boundary ([ADR 0001](../../../docs/adr/0001-tenant-vertical-organization-location-model.md)): `TenantId`, `Slug`, `Name`, `CreatedAt`. Not itself tenant-owned.
- **User** — a person who can authenticate into exactly one tenant: `UserId`, `TenantId`, `Email`, `Role`, `PasswordHash`, `CreatedAt`.
- **Role** — an authorization grouping between Tenant and Permission (`architecture.md` §20), fixed in code as `Owner`, `Manager`, `Staff` ([ADR 0006](../../../docs/adr/0006-core-identity-local-credentials-and-token-issuance.md) decision 5).
- **Credential** — a User's password hash and the algorithm parameters it was produced with; never the plaintext.

## Security Requirements

- The endpoint is **unauthenticated by design** (AC10). It MUST NOT be reachable with a `tenant_id` or any other identity input from the caller (BR6).
- Passwords: PBKDF2-HMAC-SHA256, 210,000 iterations, 128-bit salt, 256-bit derived key, versioned self-describing storage, constant-time verification ([ADR 0006](../../../docs/adr/0006-core-identity-local-credentials-and-token-issuance.md) decision 3).
- The access token is signed with the configured signing key and carries `sub`, `tenant_id`, and the owner permissions — the exact claims `JwtAuthenticationSetup` validates. `tenant_id` is authoritative for tenant resolution; it is never read from the request body.
- Response payloads and logs MUST NOT contain the password or the hash ([ADR 0002](../../../docs/adr/0002-tenant-isolation-strategy.md) and `constitution.md` Article VIII).
- Error responses MUST NOT expose internal details; a slug conflict returns 409 without revealing who owns the slug.

## Error Scenarios

| Scenario | Response |
|---|---|
| Slug already taken | 409 Conflict |
| `tenantName` missing/empty | 400 Bad Request |
| `slug` missing, malformed, or out of bounds | 400 Bad Request |
| `email` missing, malformed, or longer than 320 characters | 400 Bad Request |
| `password` shorter than 12 or longer than 128 characters | 400 Bad Request |
| Concurrent registration with the same slug | 409 Conflict (one wins; the loser creates nothing) |

There is no 401 and no 403 on this endpoint: it is the one capability that must be callable before any session exists. Internal implementation details MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Writes:** exactly one `core.tenants` row, one `core.users` row, in one transaction (FR2/AC9).
- `core.tenants`: `id uuid PK`, `slug text NOT NULL`, `name text NOT NULL`, `created_at timestamptz NOT NULL DEFAULT now()`, with a **unique constraint on `slug`** (BR2) — the constraint, not a pre-check, is what makes AC3/AC12 correct under concurrency. Treated as platform-level data: no `tenant_id`, and therefore no tenant-scoped RLS policy ([ADR 0006](../../../docs/adr/0006-core-identity-local-credentials-and-token-issuance.md) decision 2). The application role is granted `SELECT` and `INSERT` only.
- `core.users`: `id uuid PK`, `tenant_id uuid NOT NULL`, `email text NOT NULL`, `role text NOT NULL`, `password_hash text NOT NULL`, `created_at timestamptz NOT NULL DEFAULT now()`, `UNIQUE (tenant_id, email)` (BR3), `CHECK (role IN ('Owner','Manager','Staff'))`, and RLS enabled **and forced** with the tenant-isolation policy every tenant-owned table uses (ADR 0002).
- The tenant/user tables are the first Core-owned tables, so a **grants migration** is required for the least-privilege application role (`presenciavirtual_app`): `SELECT, INSERT` on `core.tenants`, `SELECT, INSERT, UPDATE` on `core.users` (UPDATE because a later password upgrade rehashes on login, per ADR 0006 decision 3 — granted now so the login specification does not need a follow-up grants migration).
- **Migration numbering is global across module assemblies** (the runner collects every module's scripts into one ordered set, so two scripts may never share a number). The next free number is used regardless of which module owns the script; a branch that races another for the same number rebases, exactly as `0019`/`0020` were allocated for RemoveItem.
- Cascade/ownership: `core.users.tenant_id` references `core.tenants.id`; deleting a tenant is out of scope, so no cascade behaviour is specified here.
- Tenant isolation MUST follow ADR 0002: application-level scoping plus PostgreSQL RLS on `core.users`.

## Integration Requirements

- **Depends on:** Core Authentication infrastructure (JWT signing key and validation, already implemented via `JwtAuthenticationSetup`), tenant isolation infrastructure (ADR 0002), PostgreSQL migrations (DbUp).
- **Does not depend on:** any vertical (Restaurant, Retail, Academy), Core Organization/Location, Platform Billing, Notifications, Audit.
- **Publishes:** a `TenantRegistered` domain event is defined for consistency with `architecture.md` §15, with no subscriber required yet — a future Audit/Notifications capability is the plausible consumer.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** slug validation (boundaries, allowed characters, hyphen rules), email normalization, password-length validation, role→permission mapping (including a test asserting every permission the API registers is granted to `Owner`), and the password hasher itself (round-trip verification, rejection of a wrong password, self-describing format, different salts produce different hashes, constant-time comparison path).
- **Integration tests:** AC1–AC11 above, executed against the real API and database, including:
  - **Token usability (AC2):** the registration token must call an existing Restaurant endpoint successfully — the single most important assertion, because a well-shaped but unusable token would pass every other test here.
  - **Concurrency on the slug (AC3):** two simultaneous registrations with the same slug must produce exactly one 201 and one 409, proving the unique constraint (not a pre-check) is the guarantee.
  - **Atomicity (AC9):** a forced failure after the tenant insert must leave no tenant row.
  - **RLS (AC11):** `core.users` read isolation through the least-privileged application role, following `RowLevelSecurityTests.cs`'s established pattern, including a successful same-tenant read first so a failure can only mean RLS blocked it.
  - **Secret hygiene (AC8):** assert the plaintext password appears in neither the response body nor the persisted `password_hash`.

## Out of Scope

- `Login` — authenticating an existing user. The immediate next Core specification.
- Password reset, change-password, and email verification.
- Inviting additional users, managing users, and multiple owners per tenant.
- RBAC administration (custom roles, per-user permissions) — the role catalog is fixed in code for now ([ADR 0006](../../../docs/adr/0006-core-identity-local-credentials-and-token-issuance.md) decision 5).
- Refresh tokens, server-side sessions, logout, token revocation.
- MFA, SSO, OIDC/social login, passkeys.
- Rate limiting, CAPTCHA, abuse protection — explicitly required before public exposure, and deliberately deferred to keep this increment reviewable.
- Tenant deletion, slug changes, tenant suspension/activation, platform billing.
- Vertical activation (which verticals a tenant has enabled) — see `architecture.md` §19.1; this increment creates a tenant that can use every implemented vertical, and typing that activation explicitly is a separate capability.

## Open Questions

- **Should the caller supply the slug, or should it be derived from the tenant name?** Resolved here as *caller-supplied*, because it is what a client will use at login and should therefore be chosen deliberately, not silently generated. Revisit if slug conflicts at registration become a friction point.
- **What should the access token lifetime be?** Not fixed by this specification; it is configuration, and ADR 0006 decision 7 leaves it to the implementation to pick a short, explicit default. It must be short enough that stateless tokens without refresh are acceptable.
- **Should registration be gated (invite-only / approval) before public exposure?** Open. Rate limiting is already out of scope, and without either gate this endpoint is the platform's most exposed surface. A future capability — not a change to this one — should decide it.
