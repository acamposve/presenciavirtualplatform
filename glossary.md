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
| Tenant | An independent business organization using the platform. The primary security and data-ownership boundary. |
| Bounded Context | A business domain with an explicit boundary and its own ubiquitous language (`Core`, `Restaurant`, `Retail`, `Academy`). |
| Module | The implementation unit corresponding to a bounded context within the modular monolith. |
| Vertical Slice | An end-to-end implementation of a single use case, containing everything needed to fulfill it. |
| Aggregate | A cluster of domain objects treated as a single unit for data changes, enforcing its own invariants. |
| Domain Event | A fact that has occurred within the domain (e.g. `OrderCreated`). |
| Integration Event | An event published across module or system boundaries to enable eventual consistency. |

## Core

_No terms defined yet. Add terms as Core specifications are approved._

## Restaurant

_No terms defined yet. Add terms as Restaurant specifications are approved._

## Retail

_No terms defined yet. Add terms as Retail specifications are approved._

## Academy

_No terms defined yet. Add terms as Academy specifications are approved._
