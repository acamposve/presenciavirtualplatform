# Specification: <Capability> / <Use Case>

**Status:** Draft | In Review | Approved | Superseded
**Bounded Context:** Core | Restaurant | Retail | Academy
**Business Capability:** <capability name>
**Related ADRs:** <links, if any>
**Last Updated:** YYYY-MM-DD

This specification follows `constitution.md` Article I. It MUST be reviewed and approved before implementation begins.

---

## Business Objective

What business problem does this use case solve, and why does it matter?

## Actors

Who initiates or is affected by this use case (roles, not specific users)?

## Business Context

Where does this use case sit in the broader business process? What happens immediately before and after it?

## Functional Requirements

Numbered, testable statements of what the system must do.

## Non-Functional Requirements

Performance, consistency, observability, or other quality attributes relevant to this use case.

## Business Rules

Explicit invariants the domain must enforce, independent of any particular request.

## Acceptance Criteria

Given/When/Then scenarios covering the happy path and the significant edge cases.

## Domain Concepts

New or reused ubiquitous-language terms this specification introduces or depends on. Add new terms to `glossary.md`.

## Security Requirements

Authentication, authorization (specific permissions), tenant isolation, and any other security-relevant requirement (per `constitution.md` Article VIII).

## Error Scenarios

What can go wrong, and what the system must do in each case (validation, not found, conflict, unauthorized, etc.).

## Data Requirements

What data this use case reads or writes, and any constraints on it (uniqueness, tenant scoping, required fields).

## Integration Requirements

Dependencies on other bounded contexts or capabilities, and any events published or consumed.

## Testing Requirements

What must be covered by automated tests (unit, integration, end-to-end) per `constitution.md` Article X.

## Out of Scope

What this specification explicitly does not cover, including related capabilities deferred to future specifications.

## Open Questions

Anything left unresolved that should be answered before or during implementation.
