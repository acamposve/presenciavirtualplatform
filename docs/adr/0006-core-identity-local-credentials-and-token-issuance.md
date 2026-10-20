# ADR 0006: Core Identity — Local Credentials and Token Issuance for the First Increment

**Status:** Proposed
**Date:** 2026-10-20

## Context

[ADR 0005](0005-minimal-authentication-for-first-implementation.md) made JWT bearer validation real but explicitly deferred issuing tokens: "No token issuance endpoint is implemented. There is no login/registration capability yet." That was a deliberate, temporary position, and it has now become the platform's defining limitation:

- Every one of the API's endpoints requires a valid JWT, and the only way to obtain one is the integration-test token factory or a hand-minted development token. The platform cannot be exercised by any real client — not a frontend, not a smoke test, not a demo.
- `architecture.md` §20 places `Role` between `Tenant` and `Permission`, but no role, user, or tenant is persisted anywhere: `tenant_id` and permissions exist only as claims on a token someone else minted.
- Identity is Core's first capability. Where the `Tenant` aggregate lands, how tenant-owned identity data is isolated, and how credentials are stored all constrain every later Core capability (users, invitations, RBAC administration, audit).

The forces at play:

- **Multi-tenancy is already binding.** [ADR 0001](0001-tenant-vertical-organization-location-model.md) makes the Tenant the security/ownership boundary, and [ADR 0002](0002-tenant-isolation-strategy.md) requires every tenant-owned table to be RLS-protected. Identity data is tenant-owned, except the tenant registry itself.
- **Authentication happens before a tenant context exists.** RLS needs `app.tenant_id`; a login request arrives with no tenant selected. This is the central design tension this ADR resolves.
- **No external identity provider has been chosen**, and adopting one now would decide vendor, cost, and operational model on no evidence.
- **The platform is a reference implementation.** A speculative full Identity stack (email verification, MFA, session management, token refresh) would be exactly the "artificial complexity" Constitution Article XIV warns against.

## Decision

Adopt the smallest identity mechanism that is genuinely functional and honest about its limits.

1. **Local credentials — email + password.** No external IdP, no social login, no OIDC in this increment.

2. **Tenant is selected by an explicit slug in authentication requests.** A `core.tenants` registry row carries `id`, `slug`, `name`, `created_at`; it is **platform-level data with no `tenant_id` column and therefore no tenant-scoped RLS** — it exists to map a slug to a tenant id before a tenant context can be established. All other identity tables (`core.users`) are tenant-owned and RLS-protected like every other tenant-owned table.

   Login is therefore `(slug, email, password)`, not `(email, password)`: the slug establishes the tenant context, and the user lookup then happens inside RLS. This is why emails are unique **per tenant**, not globally — the same person may legitimately exist under two tenants.

3. **Passwords are stored with PBKDF2-HMAC-SHA256** (210,000 iterations, 128-bit random salt, 256-bit derived key) using only the BCL — no new dependency. The stored value is algorithm-agile and self-describing (`pbkdf2-sha256$<iterations>$<salt>$<key>`, base64), so iteration counts can be raised and old hashes upgraded on next successful login. Verification is constant-time. An unknown user is verified against a fixed dummy hash so that response timing does not reveal whether the account exists.

4. **Token issuance reuses ADR 0005's token shape unchanged**: HS256, signed with the already-configured signing key, carrying `sub`, `tenant_id`, and one `permission` claim per granted permission. `JwtAuthenticationSetup` and every endpoint's authorization policy stay exactly as they are — this ADR adds the missing producer, not a second consumer contract.

5. **Roles are a fixed catalog in code, not persisted administration data.** `Owner`, `Manager`, `Staff`, each mapped in code to the permission strings the API registers. `core.users` stores the role, not a permission list. This keeps `architecture.md` §20's `Tenant → Role → Permission` chain honest while avoiding a speculation-heavy RBAC administration capability. A test enforces that every permission the API registers is granted to at least `Owner`, so adding a capability cannot silently leave it unreachable.

6. **`RegisterTenant` creates the tenant, its owner user, and a token atomically.** A fresh deployment is usable through the API alone, with no seeding and no hand-minted token.

7. **No session state.** Access tokens are short-lived and stateless; there is no refresh token, no server-side session, no logout endpoint. Logging out means discarding the token client-side.

## Alternatives Considered

- **External Identity Provider (Entra ID, Auth0, Keycloak) now.** Rejected for this increment: it decides vendor and operational model with no requirement driving it, and would leave the platform unable to run without external infrastructure — a poor property for a reference implementation. The token contract chosen here (JWT + claims) is the one an IdP would need to produce, so migrating later remains possible.
- **Login by email alone, with email globally unique.** Rejected: it makes a person's identity global across tenants, which contradicts ADR 0001's tenant-as-boundary model (one person may work for two tenants), and it forces the user lookup to run across all tenants — either bypassing RLS or requiring a privileged path in the authentication code itself, the worst place for one.
- **Keep tenant selection out of login and derive it from a subdomain.** Deferred, not rejected: it is a hosting/URL concern that a frontend can add later (resolve slug → pass it as the request field). The API contract stays the same.
- **Persist per-user permission rows instead of a role.** Rejected for now: it invites permission drift with no administration capability to manage it, and reproduces what a role catalog expresses more compactly. A future RBAC administration capability can introduce it deliberately.
- **Full ASP.NET Core Identity** (lockout, 2FA, token providers, `UserManager`). Rejected: it brings schema and behaviour (email confirmation, lockout counters) that no specification requires yet, and its password hasher is the one part actually needed — which PBKDF2 provides without the surface area.
- **Opaque server-side sessions instead of JWTs.** Rejected: ADR 0005 already established stateless bearer tokens, and changing the consumption contract would touch every endpoint for no benefit in this increment.

## Consequences

- **Login requires the tenant slug.** Any client must know it — from the registration response, a user-entered field, or a subdomain. This is a deliberate API constraint, not an implementation detail.
- **The tenant registry is the one identity table outside RLS.** It must stay minimal (id, slug, name, created_at) and must never hold tenant-owned data; anything richer belongs in a tenant-scoped table.
- **Adding a capability now requires a deliberate role-catalog change**, enforced by a test. That friction is the point: authorization must be explicit.
- **No password reset, email verification, MFA, rate limiting, or lockout** in this increment. Registration and login are therefore not production-ready against a hostile internet; this must be closed before public exposure, and is recorded as a known gap rather than an oversight.
- **No refresh token** means short sessions and re-authentication; a refresh capability will be a deliberate later decision.
- **ADR 0005's "no token issuance endpoint" decision is superseded** by this ADR; its token *shape* and validation decisions stand unchanged.
- The `Tenant` aggregate now exists in code for the first time, and `core.tenants`/`core.users` become the first Core-owned tables — the first test of whether ADR 0002's RLS pattern generalizes beyond the Restaurant module.

## Status

Proposed — awaiting review. On acceptance, it supersedes ADR 0005's token-issuance decision and unblocks `specs/core/identity/register-tenant.md`.
