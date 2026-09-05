# Repository Structure

**Status:** Active
**Last Updated:** 2026-09-05

This document describes the physical layout of the repository, mirroring the logical structure defined in `architecture.md` (Section 44).

```text
/
├── constitution.md            Fundamental principles and constraints (source of truth)
├── architecture.md            Structural and technical decisions
├── glossary.md                Ubiquitous language shared across the platform
├── technology.md              Concrete technology baseline
├── repository-structure.md    This document
├── development-workflow.md    Branching, PR and release workflow
├── README.md                  Project overview and getting started
│
├── docs/
│   └── adr/                   Architecture Decision Records
│
├── specs/                     Feature specifications, organized by bounded context
│   ├── core/
│   ├── restaurant/
│   ├── retail/
│   └── academy/
│
└── src/                       Source code (backend, frontend, and shared modules)
```

## Notes

- `constitution.md` and `architecture.md` are governance documents and take precedence over any other document in case of conflict.
- `specs/` holds one specification per use case, grouped by bounded context and business capability: `specs/<context>/<capability>/<use-case>.md` (e.g. `specs/restaurant/ordering/create-order.md`). `specs/template.md` defines the required structure. A specification MUST exist and be reviewed before implementation begins (Constitution Article I).
- `docs/adr/` holds Architecture Decision Records for significant architectural decisions (Constitution Article XXI, Architecture Section 38).
- `src/` will be organized internally following the Modular Monolith and Vertical Slice principles once implementation begins; its internal structure is not duplicated here to avoid documentation drift — see `architecture.md` Sections 4, 7 and 9 for the target shape.

This document MUST be updated whenever the top-level repository layout changes.
