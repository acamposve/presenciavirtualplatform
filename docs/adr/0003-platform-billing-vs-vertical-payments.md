# ADR 0003: Platform Billing vs. Vertical Payments

**Status:** Accepted
**Date:** 2026-09-05

## Context

`architecture.md` §5.1 listed "Billing" as a Core capability, while "Payments" appeared separately under Restaurant and Academy. Nothing in the documentation distinguished these two concepts, creating a risk that an implementer would conflate Presencia Virtual's own subscription billing with a tenant's business collecting money from its own customers or students.

## Decision

The two concepts are explicitly distinct and MUST NOT be merged into a single domain module.

**Core capability, renamed to "Platform Billing":**

- Relationship: Presencia Virtual → Tenant.
- Concerns: subscription, plan, usage, entitlement, platform invoice.
- Wherever `architecture.md` referred to the Core capability simply as "Billing," it is renamed to **Platform Billing** to make the direction of the relationship unambiguous.

**Vertical capability, "Payments" (kept per vertical, not renamed):**

- Relationship: Tenant's business → its own customer/student.
- Restaurant: order payment.
- Retail: sale/POS payment.
- Academy: tuition payment.
- Each vertical's Payments capability is a distinct domain concept with its own rules (an order bill is not a tuition invoice); they are not unified into one shared "Payments" module.

**Shared infrastructure, not shared domain:**

- A technical abstraction such as `IPaymentProvider` (`architecture.md` §34) MAY exist in infrastructure to avoid duplicating provider-integration code (e.g. talking to a payment gateway).
- This abstraction MUST remain infrastructure-only. It MUST NOT become a shared business/domain model. Each vertical's Payments capability owns its own domain logic (what is being paid for, when it is considered settled, what happens on failure) and merely calls the shared infrastructure contract to execute the transaction.

## Alternatives Considered

- **A single Core "Payments" module used by all verticals.** Rejected: order payments, POS sales, and tuition payments have different domain rules, different aggregates, and different lifecycles; forcing them into one module would violate the Core-focus principle (`constitution.md` §5.1) and couple unrelated business domains through a shared table/model, which `architecture.md` §6 explicitly prohibits.
- **Leaving the Core capability named "Billing" without qualification.** Rejected: the ambiguity is exactly what caused the risk identified in the analysis; an unqualified name invites future confusion regardless of how well it is documented elsewhere.

## Consequences

- `architecture.md` §5.1's Core capability list is updated to read "Platform Billing" instead of "Billing."
- Restaurant, Retail, and Academy each keep their own "Payments" capability, scoped to their own domain concept, as already implied by `architecture.md` §5.2 and §5.4 (Retail's equivalent is its POS/sale transaction, not a separately named "Payments" capability).
- Any future `IPaymentProvider`-style abstraction is documented and reviewed as infrastructure, not as a domain capability, and does not require its own bounded context.
- No change to `constitution.md` is required; this ADR only disambiguates naming already present in `architecture.md`.

## Status

Accepted.
