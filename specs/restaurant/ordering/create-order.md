# Specification: Ordering / CreateOrder

**Status:** Approved
**Bounded Context:** Restaurant
**Business Capability:** Ordering
**Related ADRs:** [ADR 0002 — Tenant Isolation Strategy](../../../docs/adr/0002-tenant-isolation-strategy.md), [ADR 0001 — Tenant Vertical Activation, Organization and Location Model](../../../docs/adr/0001-tenant-vertical-organization-location-model.md)
**Last Updated:** 2026-09-05

This specification follows `constitution.md` Article I. It requires human review and approval before implementation begins, per Article 1.4.

---

## Business Objective

Allow restaurant staff to open a new order for a table so that a dine-in service can begin. This is the entry point of the Ordering capability: every subsequent action on an order (adding items, sending to kitchen, closing, paying) depends on an order having been opened first.

## Actors

- **Waiter / Front-of-house staff** — initiates the use case by opening an order for a table they are serving.
- **Restaurant Manager** — indirectly affected (oversight of open orders), not a direct actor of this use case.

## Business Context

This is the first step of the order lifecycle:

```text
CreateOrder → AddItem → (Kitchen) → CloseOrder → Payment
```

Only `CreateOrder` is in scope of this specification. `AddItem`, `RemoveItem`, `CancelOrder`, `CloseOrder`, kitchen routing, and payment are separate, not-yet-written specifications (see Out of Scope).

This use case assumes a Table already exists for the tenant. Table Management (creating/editing tables) is a separate, not-yet-written specification; this specification only depends on a Table's identifier and tenant ownership already existing.

## Functional Requirements

1. An authorized user MUST be able to open a new order for a specific table belonging to their own tenant.
2. The system MUST reject the request if the table does not exist within the caller's tenant.
3. The system MUST reject the request if the table already has an order in `Open` status.
4. The system MUST generate a unique identifier for the new order.
5. The system MUST record the tenant, the table, the creating user, and the creation timestamp on the order.
6. The system MUST return the created order's identifier and status to the caller.
7. The system MUST support an optional `Idempotency-Key` request header. If a client supplies one, and a prior successful request with the same key and tenant already produced an order, the system MUST return that original order instead of creating a duplicate.

## Non-Functional Requirements

- **Tenant isolation:** enforced per ADR 0002 on every read and write involved in this use case (table lookup and order creation).
- **Consistency:** the created order must be immediately visible to a subsequent read by the same tenant (no eventual consistency for this operation).
- **Observability:** the request must be traceable via structured logging with a correlation ID (per `constitution.md` Article XI). Persisted audit trail (Core Audit Logging capability) is explicitly out of scope for this slice — see Out of Scope.
- **Idempotency:** guaranteed for repeated requests carrying the same `Idempotency-Key` (FR7, BR6); a request without one has no idempotency guarantee.

## Business Rules

- **BR1:** An Order always belongs to exactly one Tenant.
- **BR2:** A Table MUST have at most one Order in `Open` status at any time.
- **BR3:** An Order MUST NOT be created referencing a Table that belongs to a different Tenant than the authenticated user's.
- **BR4:** A newly created Order's status is always `Open`; there is no way to create an Order directly in any other status through this capability.
- **BR5:** A newly created Order starts with zero items and a total of zero.
- **BR6:** An `Idempotency-Key`, when supplied, uniquely identifies a single logical create-order attempt within a tenant. Replaying the same key MUST NOT create more than one Order. Reusing a key with a different request (e.g. a different `TableId`) MUST be rejected rather than silently accepted.

## Acceptance Criteria

- **AC1 — Happy path:** Given an authenticated user with the `restaurant.orders.create` permission and a Table with no existing `Open` order, when the user creates an order for that table, then a new Order is created with status `Open`, zero items, and the correct `TenantId`, `TableId`, and `CreatedBy`.
- **AC2 — Table already has an open order:** Given a Table that already has an `Open` order, when a user attempts to create another order for it, then the request is rejected with a Conflict error and no new Order is created.
- **AC3 — Cross-tenant table reference:** Given a Table that belongs to a different Tenant than the authenticated user, when the user attempts to create an order for it, then the request is rejected as Not Found (not Forbidden — the existence of another tenant's table must not be revealed).
- **AC4 — Missing permission:** Given an authenticated user without the `restaurant.orders.create` permission, when they attempt to create an order, then the request is rejected with Forbidden and no Order is created.
- **AC5 — Unauthenticated request:** Given no valid authentication, when create-order is called, then the request is rejected as Unauthorized.
- **AC6 — Table does not exist:** Given a `TableId` that does not exist at all, when a user attempts to create an order for it, then the request is rejected as Not Found.
- **AC7 — Idempotent replay:** Given a prior successful create-order request with `Idempotency-Key: K`, when the same request is repeated with the same key `K` and the same `TableId`, then the system returns the original Order (same `OrderId`) and does not create a new one.
- **AC8 — Idempotency key reuse conflict:** Given a prior successful create-order request with `Idempotency-Key: K` for `TableId: A`, when a new request reuses key `K` but specifies a different `TableId: B`, then the request is rejected as a Conflict.

## Domain Concepts

- **Order** (new) — aggregate root of the Ordering capability. Fields relevant to this specification: `OrderId`, `TenantId`, `TableId`, `Status`, `CreatedAt`, `CreatedByUserId`. `Items` and `Total` exist structurally but remain empty/zero until `AddItem` is specified.
- **OrderStatus** (new) — enumeration; only the `Open` value is relevant to this specification. Other values (`Closed`, `Cancelled`, etc.) are introduced by future specifications.
- **Table** (referenced, not owned by this spec) — assumed to already exist with `TableId` and `TenantId`.

Add `Order` and `OrderStatus` to `glossary.md` under Restaurant once this specification is approved.

## Security Requirements

- The endpoint requires authentication (Core Identity).
- The endpoint requires the `restaurant.orders.create` permission, scoped to the caller's tenant (Core Authorization/RBAC), per the permission naming convention in `architecture.md` §20.
- The Tenant is resolved exclusively from the authenticated context. It MUST NOT be accepted from client input (request body, query string, or header), per ADR 0002 rules 2–3.
- A Table belonging to a different tenant MUST be treated as if it does not exist (Not Found), not as a Forbidden/authorization failure, to avoid revealing cross-tenant existence.

## Error Scenarios

| Scenario | Response |
|---|---|
| No authentication | 401 Unauthorized |
| Authenticated but missing `restaurant.orders.create` | 403 Forbidden |
| `TableId` missing or malformed in request | 400 Bad Request |
| `TableId` does not exist, or belongs to a different tenant | 404 Not Found |
| Table already has an `Open` order | 409 Conflict |
| `Idempotency-Key` reused with a different request (e.g. different `TableId`) | 409 Conflict |

Internal implementation details (e.g. database constraint names) MUST NOT be exposed in any error response, per `constitution.md` Article VIII and `architecture.md` §26.

## Data Requirements

- **Reads:** the referenced Table, scoped to the caller's tenant.
- **Writes:** a new Order record scoped to the caller's tenant.
- The persistence layer MUST guarantee BR2 (at most one `Open` order per table) — e.g. via a uniqueness constraint over `(TenantId, TableId)` filtered to `Status = Open` — rather than relying solely on an application-level check-then-write, to avoid a race condition between concurrent requests.
- Tenant isolation on both the Table read and the Order write MUST follow ADR 0002 (application-level scoping plus PostgreSQL RLS).
- When an `Idempotency-Key` is supplied, the persistence layer MUST record it alongside the tenant and the resulting `OrderId`, and enough of the original request to detect reuse with a different payload (BR6). The retention window for idempotency records is an infrastructure detail, not a business rule, and may be defined at implementation time.

## Integration Requirements

- **Depends on:** Core Identity & Authentication (resolve current user), Core Authorization/RBAC (permission check), tenant isolation infrastructure (ADR 0002).
- **Does not depend on:** Core Organization/Location, Core Platform Billing, Core Notifications, Core File Storage, Core AI Provider Governance, Restaurant Kitchen, Restaurant Payments, Restaurant Inventory.
- **Publishes:** an `OrderCreated` domain event is defined for architectural consistency with `architecture.md` §15, but this specification does not require any subscriber to exist yet — consumption (e.g. by a future Kitchen or Audit capability) is deferred.
- **Consumes:** none.

## Testing Requirements

- **Unit tests:** Order aggregate creation invariants (BR4, BR5 — status is always `Open`, items/total start empty/zero).
- **Integration tests:**
  - AC1–AC6 above, executed against the real API and database.
  - Cross-tenant isolation: a request authenticated as Tenant A must never be able to create an order against a Table belonging to Tenant B (must observe AC3, not merely trust application logic), per ADR 0002 rule 8.
  - Concurrency: two simultaneous create-order requests for the same table must not both succeed (BR2 enforced under race conditions).
  - Idempotency: repeating a request with the same `Idempotency-Key` returns the original order without creating a duplicate (AC7); reusing a key with a different `TableId` is rejected (AC8, BR6).
- **End-to-end tests:** deferred until `AddItem` and `CloseOrder` exist, so the full order lifecycle (`architecture.md` §29) can be exercised together.

## Out of Scope

- `AddItem`, `RemoveItem`, `CancelOrder`, `CloseOrder` (future Ordering specifications).
- Table Management (creation/editing of tables) — assumed to already exist.
- Menu Management — not referenced by this use case (no items are added here).
- Kitchen routing/ticket generation.
- Payments.
- Inventory decrement.
- Notifications.
- Reporting.
- Core Audit Logging capability (persisted audit trail) — structured logging only for this slice.
- Organization/Location modeling — the Order references a Tenant and a Table directly; no Organization or Location entity is required.
- Reservations linkage (an order created from a reservation).
- Counter/take-away orders without a table — **decided:** every order must correspond to a table; this is not a future capability, it is a permanent constraint of this capability.
- Table Assignment (which waiter is responsible for which table, including a maximum number of tables per waiter) — future specification, not yet written. `CreateOrder` does **not** currently require the table to be assigned to the requesting user: any authenticated user with `restaurant.orders.create` may create an order for any table in their tenant. The business intent behind limiting concurrent orders per waiter is to cap how many *tables* a waiter is assigned, not to cap order count directly — once Table Assignment exists, an order-count limit per waiter follows automatically from BR2 (at most one open order per table) and the waiter's assigned-table count, without needing a separate limit rule here.

## Open Questions

- Once Table Assignment exists, should `CreateOrder` be updated to require that the table is assigned to the requesting waiter? Deferred until that specification is written.
