# Presencia Virtual Platform

Presencia Virtual Platform is a modular business software platform developed by Presencia Virtual.

It provides reusable technological capabilities and domain-specific solutions for different business verticals, initially:

- **Restaurant** Management
- **Retail** / Convenience Store Management
- **Academy** / Educational Management

The platform is designed as a **Modular Monolith**, with strong separation between business domains and shared platform capabilities. It is also a portfolio-quality reference implementation, demonstrating the ability to design, build, secure, test, operate and evolve professional business software — with security, testability, observability and simplicity prioritized over unnecessary technical complexity.

## Governance

This project is spec-driven and governed by two foundational documents. **In case of conflict, `constitution.md` takes precedence.**

- [`constitution.md`](constitution.md) — fundamental principles and constraints of the platform.
- [`architecture.md`](architecture.md) — structural and technical decisions.
- [`glossary.md`](glossary.md) — ubiquitous language shared across specifications, code and UI.
- [`technology.md`](technology.md) — concrete technology baseline.
- [`repository-structure.md`](repository-structure.md) — physical layout of this repository.
- [`development-workflow.md`](development-workflow.md) — branching, pull request and release workflow.

No significant implementation work begins without an approved specification. See `constitution.md` Article I and Article II for the full development lifecycle.

## Repository Layout

```text
/
├── constitution.md
├── architecture.md
├── glossary.md
├── technology.md
├── repository-structure.md
├── development-workflow.md
│
├── docs/
│   └── adr/            Architecture Decision Records
│
├── specs/              Specifications, organized by bounded context
│   ├── core/
│   ├── restaurant/
│   ├── retail/
│   └── academy/
│
└── src/                Source code
```

## Bounded Contexts

```text
Core          Platform-wide capabilities: tenancy, identity, users, auth, audit, notifications
Restaurant    Tables, reservations, menu, orders, kitchen, payments, inventory
Retail        POS, products, pricing, inventory, purchasing, customers
Academy       Students, teachers, courses, enrollment, attendance, evaluation
```

## Technology

| Layer | Stack |
|---|---|
| Backend | .NET 10, ASP.NET Core, Minimal APIs, PostgreSQL, Dapper |
| Frontend | React, TypeScript, Vite, Material UI, TanStack Query, React Router |
| Infrastructure | Docker, GitHub Actions, OpenTelemetry |

See [`technology.md`](technology.md) for details.

## Contributing

- `main` is protected. All changes are merged through pull requests — see [`development-workflow.md`](development-workflow.md).
- AI-assisted contributions (GitHub Copilot, Claude Code) follow the same review requirements as human-authored code (`constitution.md` Article XVI.4 and Article XVII).

## License

No license has been granted yet. All rights reserved by default — © Presencia Virtual.
