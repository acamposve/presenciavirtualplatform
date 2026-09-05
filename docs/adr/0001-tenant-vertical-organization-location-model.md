# ADR 0001: Tenant Vertical Activation, Organization and Location Model

**Status:** Accepted
**Date:** 2026-09-05

## Context

The Bounded Context & Business Capability Analysis identified three related open questions:

1. Whether a Tenant may activate exactly one vertical (Restaurant, Retail, Academy) or several simultaneously. `architecture.md` §19 illustrated a 1:1 Tenant→Vertical relationship, but no document stated this as a rule.
2. `architecture.md` §5.1 listed "Organizations" and "Tenants" as two separate Core capabilities without defining the relationship between them.
3. "Branch/Location" was described only under Retail (§5.3) even though multi-location operation is not a Retail-specific concern — restaurant chains and multi-campus academies have the same structural need.

These three questions share a single underlying model and are resolved together.

## Decision

**Tenant** is the platform-level boundary:

- Security boundary.
- Ownership boundary — every tenant-owned record ultimately belongs to exactly one Tenant.
- Commercial/billing boundary (see ADR 0003).

**Vertical activation is explicit and tenant-scoped, and a Tenant MAY activate one or multiple verticals simultaneously.** Examples:

```text
Tenant A
└── Restaurant

Tenant B
├── Retail
└── Academy

Tenant C
├── Restaurant
├── Retail
└── Academy
```

The diagram in `architecture.md` §19 (Tenant A → Restaurant, Tenant B → Retail, Tenant C → Academy) is illustrative only and MUST NOT be read as a 1:1 constraint. No separate "Vertical Tenant" abstraction is introduced — which verticals are active is a property of the Tenant (a Core capability, "Tenant Vertical Activation"), not a new bounded context or aggregate root.

**Organization** is an optional structure inside a Tenant:

- Represents a business organization that MAY contain one or more Locations/Branches.
- Is NOT a security boundary and is NOT independently billed.
- MUST NOT weaken or replace Tenant isolation — every record under an Organization still resolves to exactly one Tenant.
- Is OPTIONAL. The architecture supports both:

```text
Tenant
└── Organization (optional)
    └── Location / Branch

Tenant
└── Vertical data directly   (no Organization required)
```

**Location/Branch** is a Core structural concept, not a Retail-specific one, because Restaurant, Retail, and Academy all have the same underlying need for multi-location operation.

- **Core owns** the structural identity of `Organization` and `Location` (existence, naming, hierarchy, which Tenant/Organization they belong to).
- **Each vertical owns its own operational concepts** associated with a Location. Core does not model vertical-specific behavior:

```text
Core
├── Organization
└── Location

Restaurant (per Location)
├── Table
├── Kitchen
└── Restaurant operating configuration

Retail (per Location)
├── Store inventory
├── Cash register
└── POS configuration

Academy (per Location)
├── Classroom
├── Academic facilities
└── Course/class location rules
```

## Alternatives Considered

- **Strict 1:1 Tenant→Vertical.** Rejected: contradicts the explicit business requirement that a Tenant may operate multiple verticals (e.g. a single company running both a restaurant and a retail store).
- **A separate "Vertical Tenant" abstraction** (treating each activated vertical as its own tenant-like entity). Rejected as unnecessary: vertical activation is adequately modeled as tenant-scoped state; introducing a second isolation-like concept would duplicate the Tenant boundary without a demonstrated need.
- **Mandatory Organization for every Tenant.** Rejected: forces unnecessary structure on small, single-location tenants that have no organizational hierarchy to model.
- **A generic "Location module" owning vertical-specific behavior** (tables, store inventory, classrooms, etc.). Rejected: this would turn Location into a "god module" violating the Core-focus principle (`constitution.md` §5.1) and the module boundary rules (`constitution.md` Article IV).

## Consequences

- Core must expose a Tenant Vertical Activation capability (which verticals are enabled for a Tenant) as part of Tenant Management; this is not a new bounded context.
- Every tenant-owned aggregate needs a Tenant identifier; Organization and Location identifiers are additional, optional scoping attributes, not replacements for the Tenant identifier.
- Verticals that reference a Location do so through Core's structural identity (a Core contract), while keeping their own operational data local to their own module — consistent with `constitution.md` Article IV.3 (explicit contracts, no direct cross-module access).
- Full Organization and Location management (creation UI, hierarchy administration) is **not required** for the first implementation target (`Restaurant → Ordering → CreateOrder`); that capability may reference a Tenant directly without an Organization or Location.

## Status

Accepted.
