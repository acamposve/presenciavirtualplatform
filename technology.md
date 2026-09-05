# Technology

**Status:** Active
**Last Updated:** 2026-09-05

This document records the concrete technology baseline for the Presencia Virtual Platform, as defined in `constitution.md` (Article XIII) and `architecture.md`.

Technology choices MAY evolve. Changing an entry in this document MUST be justified by a documented technical or business reason (see `docs/adr/`).

---

## Backend

| Concern | Technology |
|---|---|
| Runtime / Language | .NET 10 / C# |
| Web framework | ASP.NET Core, Minimal APIs |
| Database | PostgreSQL |
| Data access | Dapper |
| Caching / coordination | Redis (where justified) |
| Real-time | SignalR (where required) |

## Frontend

| Concern | Technology |
|---|---|
| Library | React |
| Language | TypeScript |
| Build tool | Vite |
| Components | Material UI |
| Server state | TanStack Query |
| Routing | React Router |
| Validation | Zod (where appropriate) |

## Infrastructure

| Concern | Technology |
|---|---|
| Containerization | Docker |
| CI/CD | GitHub Actions |
| Cloud | Azure (where appropriate) |
| Observability | OpenTelemetry, structured logging |

## AI-Assisted Development

| Tool | Purpose |
|---|---|
| GitHub Copilot | Authorized development assistant |
| Claude Code | Authorized development assistant |

Both are subject to `constitution.md` Article XII and Article XVII.
