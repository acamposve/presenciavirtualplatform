# Specification: Settings / UpdateRestaurantSettings

**Status:** Draft
**Bounded Context:** Restaurant
**Business Capability:** Settings
**Related ADRs:** [ADR 0002 — Tenant Isolation Strategy](../../../docs/adr/0002-tenant-isolation-strategy.md)
**Last Updated:** 2026-09-13

This specification follows `constitution.md` Article I. It requires human review and approval before implementation begins, per Article 1.4.

---

## Business Objective

Allow a restaurant to configure its own per-tenant Ordering settings, so that `add-item.md` BR7's `MaxAlcoholicItemQuantityPerLine` limit — today only configurable by seeding `restaurant.settings` directly in the database — can be set through the API. `add-item.md` explicitly deferred "any API or UI to configure `RestaurantSettings`" as its own future specification; this is that specification.

## Actors

- **Restaurant Manager** — configures the restaurant's Ordering settings as part of setting it up or adjusting policy (e.g. a legal change to how much alcohol can be added to a single line).

## Business Context

```text
UpdateRestaurantSettings → (limit configured) → AddItem observes it immediately (add-item.md BR7)
```

This specification covers only setting the tenant's `MaxAlcoholicItemQuantityPerLine`. It is the only configurable setting today — `restaurant.settings` has no other column. Reading the current settings back through the API (`GetRestaurantSettings`) is a separate, not-yet-written specification (see Out of Scope), mirroring the same minimal-first-increment approach `create-table.md` and `create-menu-item.md` already took for their own resources.

## Functional Requirements

1. An authorized user MUST be able to set their own tenant's `MaxAlcoholicItemQuantityPerLine` to a positive integer.
2. An authorized user MUST be able to explicitly clear the limit (no maximum enforced), by supplying `null`.
3. The system MUST reject the request if a non-null value is supplied that is not a positive integer (zero or negative).
4. The system MUST create the tenant's settings row if none exists yet, or update it if one already exists — the caller does not need to know or care which case applies.
5. The system MUST return the tenant's `MaxAlcoholicItemQuantityPerLine` as it stands after the request.
6. The updated value MUST be observed by the very next `AddItem` call that needs it (`add-item.md` BR7) — no caching or delay.

## Non-Functional Requirements

- **Tenant isolation:** enforced per ADR 0002 on the write (and on the read `AddItem` already performs).
- **Consistency:** the updated setting must be immediately visible to a subsequent `AddItem` call by the same tenant, per FR6.
- **Observability:** the request must be traceable via structured logging with a correlation ID, per `constitution.md` Article XI. As with every other capability in this module so far, no such mechanism exists yet anywhere in the codebase; this specification does not introduce one on its own.
- **Idempotency:** this operation is a full replace of a single-valued, per-tenant setting (an upsert keyed by `TenantId` alone) — applying the same request twice always produces the same end state. Unlike `CreateOrder`/`AddItem`/`CloseOrder`, no `Idempotency-Key` mechanism is needed: there is no "apply twice" side effect to guard against when the operation is already naturally idempotent by construction.

## Business Rules

- **BR1:** A tenant's Ordering settings belong to exactly that tenant — `restaurant.settings` is keyed by `tenant_id` alone (at most one row per tenant).
- **BR2:** `MaxAlcoholicItemQuantityPerLine`, when supplied as a value, MUST be a positive integer (matching `restaurant.settings`'s own `CHECK (max_alcoholic_item_quantity_per_line IS NULL OR max_alcoholic_item_quantity_per_line > 0)`). Supplying `null` explicitly clears any existing limit — a tenant with no configured limit (whether because no row exists yet, or because it was explicitly cleared) is treated identically as "no limit enforced," per `add-item.md` BR7.
- **BR3:** The write MUST be an upsert: create the tenant's row if it does not exist, or update it if it does. The caller never needs to know which case applies — from the caller's perspective, this endpoint always just "sets the value."

## Acceptance Criteria

- **AC1 — Happy path, first time (no prior row):** Given an authenticated user with the `restaurant.settings.update` permission and a tenant with no existing settings row, when they set `MaxAlcoholicItemQuantityPerLine` to a positive integer, then a settings row is created for that tenant and the response reflects the value just set.
- **AC2 — Happy path, updating an existing value:** Given a tenant with an already-configured limit, when the user sets it to a different positive integer, then the response reflects the new value, replacing the old one.
- **AC3 — Clearing the limit:** Given a tenant with an already-configured limit, when the user sets `MaxAlcoholicItemQuantityPerLine` to `null`, then the response reflects `null`, and a subsequent `AddItem` call for an alcoholic item enforces no limit, per `add-item.md` AC15.
- **AC4 — Invalid value:** Given a request with `MaxAlcoholicItemQuantityPerLine` of zero or negative, when the user attempts to set it, then the request is rejected as a validation error and no change is made.
- **AC5 — Missing permission:** Given an authenticated user without the `restaurant.settings.update` permission, when they attempt to update settings, then the request is rejected as Forbidden and no change is made.
- **AC6 — Unauthenticated request:** Given no valid authentication, when update-restaurant-settings is called, then the request is rejected as Unauthorized.
- **AC7 — Immediately observed by AddItem:** Given a tenant with no configured limit, when the user sets `MaxAlcoholicItemQuantityPerLine` to `N`, then the very next `AddItem` call that would bring an alcoholic item's line to more than `N` is rejected as Conflict, per `add-item.md` AC14 — with no delay or caching between the two calls.

## Domain Concepts

- **RestaurantSettings** (existing minimal reference concept, formalized here as an aggregate — `add-item.md` introduced it as "not a full Restaurant configuration aggregate") — fields relevant to this specification: `TenantId`, `MaxAlcoholicItemQuantityPerLine`. This specification is that aggregate's first configuration capability; `restaurant.settings` gains no new column, since `MaxAlcoholicItemQuantityPerLine` is still the only setting that exists.

`RestaurantSettings` is already listed in `glossary.md` under Restaurant (added when `add-item.md` was approved), described there as configurable only "for now... seeded directly." This specification is that capability's first increment, so the glossary entry is updated accordingly once this specification is approved.

## Security Requirements

- The endpoint requires authentication (Core Identity).
- The endpoint requires the `restaurant.settings.update` permission, scoped to the caller's tenant (Core Authorization/RBAC), per the permission naming convention in `architecture.md` §20. This is a new permission string; implementing this specification requires registering it alongside the existing ones.
- The Tenant is resolved exclusively from the authenticated context, never from client input, per ADR 0002 rules 2–3.
- As with `create-table.md`'s and `create-menu-item.md`'s identical resolutions of the same question: "Restaurant Manager" in Actors is descriptive of who typically does this, not an additional authorization boundary AC5 must check — modeled here purely as the `restaurant.settings.update` permission.

## Error Scenarios

| Scenario | Response |
|---|---|
| No authentication | 401 Unauthorized |
| Authenticated but missing `restaurant.settings.update` | 403 Forbidden |
| `MaxAlcoholicItemQuantityPerLine` present and zero or negative | 400 Bad Request |

Internal implementation details MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Reads:** none beyond what the write itself needs (an upsert does not require a prior read to decide create-vs-update).
- **Writes:** an upsert (`INSERT ... ON CONFLICT (tenant_id) DO UPDATE ...`) into `restaurant.settings`, scoped to the caller's tenant.
- This reuses the `restaurant.settings` table already created by `0008_restaurant_settings.sql` for `add-item.md` — no schema migration is expected. A **grants migration is required**, however: no migration so far has granted the least-privilege application role (`presenciavirtual_app`) any write access to `restaurant.settings` — `add-item.md` only ever reads it (`GetMaxAlcoholicItemQuantityPerLineAsync`). This specification's `INSERT`/`UPDATE` MUST be granted explicitly, following the same forward-grant pattern `create-menu-item.md` already needed for `restaurant.menu_items`.
- Tenant isolation on the write MUST follow ADR 0002 (application-level scoping plus PostgreSQL RLS), consistent with the existing table.

## Integration Requirements

- **Depends on:** Core Identity & Authentication, Core Authorization/RBAC, tenant isolation infrastructure (ADR 0002) — the same minimal Core slice every other Restaurant capability depends on.
- **Does not depend on:** Core Organization/Location, Core Platform Billing, Notifications, Restaurant Ordering itself (this specification does not call `AddItem`; FR6/AC7 describe a consequence of `AddItem`'s own existing read, not a dependency this specification introduces), Kitchen, Payments, Inventory.
- **Publishes:** no event is defined for this version — no capability yet needs to react to a settings change.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** `RestaurantSettings` aggregate invariants (BR2 — `MaxAlcoholicItemQuantityPerLine` must be a positive integer when supplied; `null` is valid and means no limit).
- **Integration tests:** AC1–AC7 above, executed against the real API and database, including AC7 as a genuine cross-capability regression (a real `AddItem` call after a real settings update, not a mock).
- **Tenant isolation (ADR 0002 rule 8 — write):** `restaurant.settings` has had RLS enabled since `add-item.md`, but only ever been exercised as a read (by `AddItem`'s own lookup); this is its first write-isolation test. Following the pattern used for `CreateMenuItem` (`RowLevelSecurityTests`): a successful same-tenant upsert through the least-privileged role MUST succeed first (proving the new grants migration actually granted write access) — only then, with the tenant context switched, does an attempt to upsert a row carrying a different tenant's `tenant_id` get asserted as rejected by Row-Level Security (the `WITH CHECK` side of the policy). Without the first, successful, same-tenant write, the test cannot tell "blocked by RLS" apart from "blocked because the grants migration was never applied."

## Out of Scope

- Reading the current settings back through the API (`GetRestaurantSettings`) — not required to unblock configuring the limit; `AddItem`'s own enforcement is the only consumer that needs to read it today.
- Any setting other than `MaxAlcoholicItemQuantityPerLine` — none exist yet.
- A history or audit trail of settings changes.
- Per-location or per-user overrides of a tenant-wide setting (Core Organization/Location, per ADR 0001, remains unimplemented).
- Idempotency-key support — not applicable, per the Non-Functional Requirements' Idempotency note above.

## Open Questions

None remaining that block this specification.
