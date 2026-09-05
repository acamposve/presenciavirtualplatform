# ADR 0005: Minimal Authentication and Authorization Mechanism for the First Implementation

**Status:** Accepted
**Date:** 2026-09-05

## Context

The `CreateOrder` specification (`specs/restaurant/ordering/create-order.md`) requires authentication, a permission check (`restaurant.orders.create`), and tenant resolution from the authenticated context — but no specification for a Core Identity/Authentication/Authorization capability exists yet (registration, login, RBAC administration are all unspecified). `architecture.md` §21 explicitly leaves the authentication mechanism open ("may evolve... JWT, OpenID Connect, OAuth 2.0, external identity providers").

Implementing `CreateOrder` requires *something* real behind these requirements now, without either (a) building a full, speculative Identity capability with no approved specification, or (b) faking/mocking authentication in a way that wouldn't exercise the real security requirements the spec demands.

## Decision

For this first implementation, adopt the minimum mechanism that is genuinely functional, not a stand-in:

- **Authentication:** JWT Bearer, using ASP.NET Core's built-in `Microsoft.AspNetCore.Authentication.JwtBearer`. Tokens carry `tenant_id`, `sub` (user id), and one or more `permission` claims. Any correctly signed token with these claims authenticates successfully — there is no shortcut that bypasses signature validation.
- **Authorization:** claims-based policy per permission string (e.g. `restaurant.orders.create`), checked via ASP.NET Core's policy-based authorization. No role/permission catalog is persisted in the database yet — permissions are asserted directly as claims on the token.
- **Tenant resolution:** `TenantId` is read exclusively from the validated token's `tenant_id` claim via an `ICurrentUserContext` abstraction (owned by Core). It is never read from request body, query string, or header (ADR 0002 rule 3).
- **No token issuance endpoint is implemented.** There is no login/registration capability yet. Tokens for local development and automated tests are minted directly with a shared development-only signing key, documented in configuration. Issuing tokens through a real login flow is explicitly deferred to a future Core Identity specification.

## Alternatives Considered

- **Build a full Core Identity capability first** (registration, login, password hashing, RBAC administration). Rejected for this step: it has no approved specification, and building it speculatively to unblock `CreateOrder` would repeat the exact anti-pattern already rejected when choosing `CreateOrder` over a full Identity module as the first capability (see the Bounded Context & Business Capability Analysis, Section 11).
- **Mock/bypass authentication entirely for now** (e.g. a hardcoded fake user). Rejected: `CreateOrder`'s security requirements (401/403 behavior, tenant isolation, permission checks) are explicit acceptance criteria; mocking them would leave those criteria unverified rather than satisfied.
- **OpenID Connect / external identity provider now.** Rejected as premature: no external IdP has been chosen, and introducing one is a bigger commitment than this step requires. JWT Bearer is the simplest mechanism that already matches the direction `architecture.md` §21 anticipates.
- **Persist a full role/permission catalog in the database now.** Rejected: `CreateOrder` only needs one permission check; building permission administration now would be speculative generality (Constitution Article XIV).

## Consequences

- A future Core Identity/Authentication specification is still required before real users can log in; this ADR only unblocks `CreateOrder`'s implementation with a genuine (if minimal) security mechanism.
- The token issuer, claim shape, and signing key strategy defined here will very likely be revisited once that specification exists — this is expected, not a defect, and is why this decision is recorded as an ADR rather than left implicit.
- Integration tests for `CreateOrder` mint their own tokens directly (no login flow to call), which is acceptable for testing a single capability's authorization behavior but does not exercise a real issuance flow.
- RBAC administration (creating roles, assigning permissions to users) is out of scope until its own specification exists.

## Status

Accepted.
