# Glossary

**Status:** Living document
**Last Updated:** 2026-09-05

This glossary defines the ubiquitous language of the Presencia Virtual Platform, as required by `constitution.md` (Article III — Domain Driven Design) and `architecture.md` (Section 44).

Terms MUST be used consistently across specifications, code, APIs, database concepts, UI, tests and documentation.

New terms MUST be added here when a specification introduces a domain concept for the first time.

---

## Platform-Wide Terms

| Term | Definition |
|---|---|
| Tenant | The platform-level security, ownership, and billing boundary. An independent business using the platform. Every tenant-owned record ultimately belongs to exactly one Tenant. See [ADR 0001](docs/adr/0001-tenant-vertical-organization-location-model.md). |
| Vertical | One of the platform's business domains available to a Tenant — `Restaurant`, `Retail`, or `Academy`. A Tenant MAY activate one or several verticals simultaneously (Tenant Vertical Activation). |
| Organization | An optional business/organizational structure inside a Tenant, which MAY contain one or more Locations. Not a security boundary and not independently billed. See [ADR 0001](docs/adr/0001-tenant-vertical-organization-location-model.md). |
| Location / Branch | A physical location or branch belonging to a Tenant (optionally grouped under an Organization). Core owns its structural identity; each vertical owns its own operational data associated with a Location (e.g. a restaurant's tables, a store's inventory). |
| Platform Billing | Core's capability covering the Presencia Virtual → Tenant commercial relationship (subscription, plan, usage, entitlement, platform invoice). Distinct from a vertical's own "Payments" capability. See [ADR 0003](docs/adr/0003-platform-billing-vs-vertical-payments.md). |
| Bounded Context | A business domain with an explicit boundary and its own ubiquitous language (`Core`, `Restaurant`, `Retail`, `Academy`). |
| Module | The implementation unit corresponding to a bounded context within the modular monolith. |
| Vertical Slice | An end-to-end implementation of a single use case, containing everything needed to fulfill it. |
| Aggregate | A cluster of domain objects treated as a single unit for data changes, enforcing its own invariants. |
| Domain Event | A fact that has occurred within the domain (e.g. `OrderCreated`). |
| Integration Event | An event published across module or system boundaries to enable eventual consistency. |

## Core

_No terms defined yet. Add terms as Core specifications are approved._

## Restaurant

| Term | Definition |
|---|---|
| Order | Aggregate root of the Ordering capability. Represents a table's tab, from opening (`CreateOrder`) through future item additions and closing. |
| OrderStatus | The lifecycle state of an Order. Only `Open` exists so far (`specs/restaurant/ordering/create-order.md`); further values are introduced by future specifications (AddItem, CloseOrder, CancelOrder). |
| Table | A physical table in a Restaurant, referenced by `CreateOrder` but owned by a separate, not-yet-written Table Management specification. |

## Retail

_No terms defined yet. Add terms as Retail specifications are approved._

## Academy

_No terms defined yet. Add terms as Academy specifications are approved._
