# ADR 0004: Data Lifecycle and Retention Policy (Placeholder)

**Status:** Accepted
**Date:** 2026-09-05

## Context

The platform is a multi-tenant SaaS handling personal data (guests, retail customers, students, teachers, employees). Neither `constitution.md` nor `architecture.md` currently defines retention rules, deletion/anonymization semantics, or auditability requirements for destructive data operations. This was identified as an architectural gap in the Bounded Context & Business Capability Analysis. It is not an implementation blocker for the current roadmap, but it must not be forgotten once capabilities handling personal data at scale approach production.

## Decision

The platform MUST eventually support, as a platform-level data lifecycle policy:

- Retention rules for tenant-owned personal data.
- Deletion or anonymization where legally or business appropriate.
- Auditability of destructive operations (deletion/anonymization events must be recorded, consistent with `constitution.md` Article VIII audit logging requirements).
- Tenant-aware data lifecycle handling — retention and deletion operate within a single Tenant's data and must respect the tenant isolation strategy defined in ADR 0002.

This ADR is a **placeholder**: it records the obligation and its scope without inventing specific retention periods, legal bases, or deletion mechanics. Detailed retention periods and deletion semantics MUST be defined — with appropriate legal/business input — before production launch of any capability handling personal data at scale, particularly:

- Academy (student and teacher records).
- Restaurant Customer Management.
- Retail Customer Management.

## Alternatives Considered

- **Defining specific retention periods now.** Rejected: retention periods are jurisdiction- and business-dependent; inventing numbers without legal/business input would create false confidence and would likely need to be revisited, contradicting `constitution.md`'s preference for decisions grounded in actual requirements rather than speculation.
- **Deferring this topic with no documentation at all.** Rejected: an undocumented gap in a security- and compliance-relevant area is worse than a documented placeholder; the analysis explicitly surfaced this risk and it should remain visible until resolved.

## Consequences

- This obligation does not block `Restaurant → Ordering → CreateOrder` or any other MVP capability that does not yet handle personal data at scale.
- Before Academy, Restaurant Customer Management, or Retail Customer Management are specified for production use, their specifications MUST address retention and deletion/anonymization explicitly, per `constitution.md` Article I (specifications must define data requirements).
- Future work to define concrete retention rules should produce either an update to this ADR or a superseding ADR — not a new, disconnected policy document.

## Status

Accepted (as a placeholder obligation; detailed retention rules remain an open item, not resolved by this ADR).
