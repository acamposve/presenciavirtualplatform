# Development Workflow

**Status:** Active
**Last Updated:** 2026-09-05

This document describes how changes flow through the repository, complementing `constitution.md` (Articles II, XVI, XVII).

## Branching

- `main` is the stable, protected branch. It always reflects reviewed, working code.
- Work happens on feature branches named after the capability being implemented:

```text
feature/restaurant-create-order
feature/retail-inventory-adjustment
feature/academy-student-enrollment
fix/<short-description>
docs/<short-description>
chore/<short-description>
```

`chore/` is for technical work with no direct business-capability owner (solution scaffolding, tooling, CI configuration) — it still requires a pull request like any other change.

## Pull Requests

- All changes to `main` MUST go through a pull request.
- Direct pushes to `main` are blocked for everyone except the repository owner, who may bypass this rule when necessary (e.g. initial repository setup, emergency fixes).
- A pull request SHOULD reference the relevant specification and describe the implementation and tests included.

## Branch Protection

`main` is configured with:

- Required pull requests before merging.
- No force pushes, no branch deletion.
- The repository owner is the only actor able to bypass these rules.

## AI-Assisted Contributions

- Changes authored by Claude Code or GitHub Copilot follow the same branch and PR workflow as human-authored changes.
- AI-generated code MUST be reviewed before merging, per Constitution Article XII and XVI.4.

## Lifecycle Flow

Every non-trivial change SHOULD be traceable through the flow defined in `constitution.md` Article II:

```text
Specification → Tasks → Vertical Slice → Implementation → Tests → Validation → Pull Request → main
```
