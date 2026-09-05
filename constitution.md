# Presencia Virtual Platform Constitution

**Version:** 1.1  
**Status:** Active  
**Project:** Presencia Virtual Platform  
**Last Updated:** 2026-09-05

---

# Preamble

Presencia Virtual Platform is a modular business software platform developed by Presencia Virtual.

The platform provides reusable technological capabilities and domain-specific solutions for different business verticals, initially:

- Restaurant Management
- Retail / Convenience Store Management
- Academy / Educational Management

The platform is designed as a **Modular Monolith**, with strong separation between business domains and shared platform capabilities.

The primary objective is not to demonstrate the maximum number of technologies, but to demonstrate the ability of Presencia Virtual to design, build, secure, test, operate and evolve high-quality business software.

The system MUST prioritize:

- Business clarity
- Maintainability
- Security
- Testability
- Observability
- Simplicity
- Evolutionary architecture

over unnecessary technical complexity.

Presencia Virtual Platform is also a portfolio-quality reference implementation. It MUST demonstrate professional engineering practices without introducing artificial complexity merely for demonstration purposes.

---

# Article I — Specification Driven Development

## 1.1 Specification Before Implementation

Implementation MUST NOT precede specification.

Every meaningful feature MUST have an associated specification before implementation begins.

A feature specification SHOULD define, when applicable:

- Business objective
- Actors
- Business context
- Functional requirements
- Non-functional requirements
- Business rules
- Acceptance criteria
- Domain concepts
- Security requirements
- Error scenarios
- Data requirements
- Integration requirements
- Testing requirements

The specification is the primary source of truth for feature behavior.

## 1.2 Specification Changes

When implementation reveals that the specification is incomplete, ambiguous or incorrect, the specification MUST be updated before or together with the implementation.

Code MUST NOT silently redefine business requirements.

## 1.3 Traceability

Every implemented feature SHOULD be traceable through:

```text
Specification
      ↓
Tasks
      ↓
Implementation
      ↓
Tests
```

The project SHOULD maintain sufficient traceability to determine why a piece of code exists and which requirement it satisfies.

## 1.4 Approved Work

No significant implementation work SHOULD begin unless the corresponding specification has been reviewed and considered ready for implementation.

---

# Article II — Development Lifecycle and Architectural Flow

The project MUST follow the following development flow:

```text
CONSTITUTION
      ↓
ARCHITECTURE
      ↓
BOUNDED CONTEXTS
      ↓
BUSINESS CAPABILITIES
      ↓
SPECIFICATIONS
      ↓
TASKS
      ↓
VERTICAL SLICES
      ↓
IMPLEMENTATION
      ↓
TESTS
      ↓
VALIDATION
```

This flow is mandatory for significant functionality.

## 2.1 Constitution

The Constitution defines the fundamental principles and constraints of the platform.

All architectural and implementation decisions MUST comply with the Constitution.

## 2.2 Architecture

The architecture defines the structural and technical decisions required to implement the platform.

Architecture documentation MUST remain consistent with the Constitution.

## 2.3 Bounded Contexts

Business domains MUST be identified before large-scale feature implementation.

Initial bounded contexts are:

- Platform Core
- Restaurant
- Retail
- Academy

## 2.4 Business Capabilities

Each bounded context MUST be decomposed into meaningful business capabilities.

Example:

```text
Restaurant
├── Ordering
├── Tables
├── Kitchen
├── Inventory
├── Purchasing
└── Billing
```

Capabilities SHOULD represent meaningful business concepts rather than technical components.

## 2.5 Specifications

Business capabilities MUST be translated into implementable specifications.

Specifications MUST describe observable behavior and acceptance criteria.

## 2.6 Tasks

Specifications MUST be decomposed into implementation tasks when the feature complexity justifies it.

Tasks SHOULD be small enough to be independently understood, implemented and validated.

## 2.7 Vertical Slices

Implementation SHOULD occur through vertical slices.

A vertical slice represents an end-to-end business capability or use case.

Example:

```text
CreateOrder
├── API
├── Application
├── Domain
├── Persistence
└── Tests
```

## 2.8 Implementation

Implementation MUST satisfy the approved specification.

Implementation MUST NOT introduce unrelated architectural changes.

## 2.9 Tests

Tests MUST validate the behavior defined by the specification.

Tests are part of the implementation and MUST NOT be treated as optional post-development work.

## 2.10 Validation

A feature MUST be validated against:

- Specification
- Acceptance criteria
- Security requirements
- Tests
- Architectural principles

before being considered complete.

---

# Article III — Domain Driven Design

## 3.1 Domain First

Business domains MUST drive the organization of the application.

Technical concerns MUST NOT dictate the business model.

The system MUST use domain language consistently across:

- Specifications
- Code
- APIs
- Database concepts
- UI
- Tests
- Documentation

## 3.2 Bounded Contexts

Each business vertical SHOULD be treated as an independent bounded context.

Initial bounded contexts include:

- Platform Core
- Restaurant
- Retail
- Academy

Additional bounded contexts MAY be introduced when justified by domain complexity.

## 3.3 Business Rules

Business rules MUST live as close as reasonably possible to the domain model.

Endpoints, UI components and infrastructure code MUST NOT become the primary location for business rules.

## 3.4 Domain Independence

The domain model MUST NOT depend directly on:

- Database implementations
- HTTP frameworks
- UI frameworks
- Cloud providers
- External APIs
- AI providers

Infrastructure MAY depend on the domain, but the domain MUST NOT depend on infrastructure.

---

# Article IV — Modular Monolith

## 4.1 Architectural Style

Presencia Virtual Platform MUST be implemented as a **Modular Monolith**.

The system MUST initially use a single deployable backend/application boundary unless a future requirement provides a clear reason to introduce separate deployables.

Microservices MUST NOT be introduced merely to demonstrate knowledge of microservices.

## 4.2 Module Boundaries

Modules MUST have explicit boundaries.

Initial modules:

```text
Core
Restaurant
Retail
Academy
```

Each module MUST own its domain behavior and SHOULD minimize dependencies on other modules.

## 4.3 Cross-Module Communication

Modules SHOULD communicate through explicit contracts.

Direct access to another module's internal implementation is prohibited.

When appropriate, communication SHOULD use:

- Application contracts
- Domain events
- Integration events
- Explicit interfaces

## 4.4 Database Boundaries

The platform MAY use a single PostgreSQL database.

However, modules MUST maintain logical ownership of their data.

A module MUST NOT directly manipulate another module's tables as a shortcut for cross-module communication.

---

# Article V — Vertical Slice Architecture

## 5.1 Feature-Oriented Organization

Application code MUST be organized primarily around business capabilities rather than technical layer categories.

Features SHOULD follow a structure similar to:

```text
Restaurant/
└── Ordering/
    ├── CreateOrder/
    ├── AddOrderItem/
    ├── CancelOrder/
    └── CloseOrder/
```

rather than organizing the entire application primarily as:

```text
Controllers/
Services/
Repositories/
DTOs/
Validators/
```

## 5.2 Vertical Slice Independence

A vertical slice SHOULD contain the code required to implement its use case without unnecessary dependencies on unrelated features.

A slice MAY contain:

- Endpoint
- Command / Query
- Handler
- Validator
- Domain interaction
- Persistence logic
- Mapping
- Tests

## 5.3 Shared Code

Shared abstractions MUST be introduced only when there is a demonstrated need.

Premature generic abstractions SHOULD be avoided.

DRY MUST NOT be used as justification for abstractions that obscure the domain or increase coupling.

---

# Article VI — Clean Architecture

## 6.1 Dependency Rule

Dependencies MUST point toward stable business rules.

The domain MUST remain independent of infrastructure and presentation technologies.

Conceptually:

```text
Presentation
     │
     ▼
Application
     │
     ▼
Domain
     ▲
     │
Infrastructure
```

Infrastructure implementations MAY satisfy application/domain contracts without becoming part of the domain model.

## 6.2 Pragmatic Application

Clean Architecture MUST be applied pragmatically.

The project MUST NOT create layers, interfaces or abstractions solely to satisfy an architectural diagram.

Every abstraction SHOULD have a clear reason to exist.

## 6.3 Framework Independence

Core business logic SHOULD remain testable without requiring:

- HTTP
- Database connections
- External services
- UI
- Cloud infrastructure

---

# Article VII — API First

## 7.1 API Contract

The backend API MUST be designed as a first-class contract.

API behavior MUST be defined before implementation.

## 7.2 Consistency

APIs SHOULD follow consistent conventions for:

- Resource naming
- HTTP semantics
- Error responses
- Validation
- Pagination
- Filtering
- Sorting
- Authentication
- Authorization
- Versioning

## 7.3 Frontend Independence

The frontend MUST consume the API through explicit contracts.

Business rules MUST NOT be duplicated unnecessarily in the frontend.

Client-side validation MAY improve user experience but MUST NOT replace server-side validation.

---

# Article VIII — Security by Design

Security MUST be considered during specification, not added after implementation.

The platform MUST support, as appropriate:

- Authentication
- Authorization
- Role-Based Access Control
- Tenant isolation
- Secure password/token handling
- Input validation
- Audit logging
- Secure file handling
- Protection against common web vulnerabilities
- Secure secrets management

Security-sensitive functionality MUST have explicit tests.

No feature is considered complete if its authorization requirements are undefined.

---

# Article IX — Multi-Tenancy

The platform SHOULD be designed as a multi-tenant SaaS platform.

A tenant represents an independent business organization using Presencia Virtual Platform.

Tenant isolation MUST be treated as a security boundary.

Every feature handling tenant-owned data MUST explicitly define:

- Tenant ownership
- Access rules
- Administrative access
- Cross-tenant restrictions

Cross-tenant data access MUST NEVER occur implicitly.

---

# Article X — Testing

## 10.1 Testing Is Part of Implementation

Tests MUST be implemented as part of the feature.

Every feature specification MUST define appropriate acceptance criteria and testing expectations.

## 10.2 Test Pyramid

The project SHOULD use a balanced testing strategy:

```text
        E2E
       /   \
  Integration
     /       \
   Unit Tests
```

Unit tests SHOULD provide fast validation of domain and application behavior.

Integration tests SHOULD validate persistence, API contracts and important infrastructure behavior.

End-to-end tests SHOULD validate critical user journeys.

## 10.3 Business Rules

Important business rules MUST have automated tests.

Tests SHOULD describe business behavior rather than implementation details.

## 10.4 Regression Protection

A bug fix SHOULD include a regression test whenever practical.

## 10.5 Quality Gates

The CI pipeline SHOULD prevent merging code that:

- Does not compile
- Fails required tests
- Violates critical quality checks
- Introduces known security problems

Coverage targets MAY be defined per module or feature where appropriate.

Coverage percentage MUST NOT be treated as the sole measure of test quality.

---

# Article XI — Observability

The platform MUST be observable.

The system SHOULD provide:

- Structured logging
- Correlation IDs
- Metrics
- Distributed tracing where applicable
- Health checks
- Dependency health information
- Error tracking

Observability MUST NOT expose secrets, credentials or sensitive business data unnecessarily.

The platform SHOULD use OpenTelemetry where appropriate.

---

# Article XII — AI as a First-Class Capability

AI MAY be used both as:

1. A development accelerator.
2. A product capability.

These uses MUST remain conceptually separate.

## 12.1 AI-Assisted Development

GitHub Copilot and Claude Code are authorized development assistants.

They MAY:

- Generate code
- Generate tests
- Analyze code
- Propose refactorings
- Generate documentation
- Identify potential defects
- Implement approved specifications

They MUST NOT override the Constitution or approved specifications.

AI-generated code MUST be reviewed before being considered complete.

## 12.2 Product AI

AI functionality MUST solve an identifiable business problem.

AI MUST NOT be added merely because it is technologically fashionable.

AI features MUST define:

- Input
- Expected output
- Failure behavior
- Security considerations
- Cost considerations
- Data/privacy considerations
- Evaluation criteria

AI-generated results MUST be treated as potentially incorrect unless the feature explicitly guarantees otherwise.

---

# Article XIII — Technology Principles

The initial technology baseline is:

## Backend

- .NET 10
- ASP.NET Core
- C#
- Minimal APIs
- PostgreSQL
- Dapper
- Redis where justified
- SignalR where real-time communication is required

## Frontend

- React
- TypeScript
- Vite
- Material UI
- TanStack Query
- React Router
- Zod where appropriate

## Infrastructure

- Docker
- GitHub Actions
- Azure where appropriate
- OpenTelemetry
- Structured logging

Technology choices MAY evolve.

Changing technology MUST be justified by a documented technical or business reason.

---

# Article XIV — Simplicity Over Complexity

The simplest architecture that satisfies the current requirements SHOULD be preferred.

The project MUST avoid:

- Premature microservices
- Premature distributed systems
- Unnecessary abstractions
- Generic frameworks built internally without need
- Excessive design patterns
- Technology introduced solely for portfolio decoration

Complexity MUST have a reason.

## 14.1 Evolutionary Architecture

The architecture MUST allow the system to evolve.

Future extraction of a module into an independent service MAY be possible, but such extraction MUST be driven by an actual business or technical requirement.

The possibility of future extraction MUST NOT justify introducing distributed-system complexity prematurely.

---

# Article XV — Code Quality

Code MUST prioritize:

- Readability
- Explicitness
- Maintainability
- Testability
- Consistency
- Appropriate performance

Code SHOULD be easy for another developer to understand without requiring knowledge of the original author's intentions.

Comments SHOULD explain why something exists, not merely repeat what the code does.

Dead code MUST NOT be intentionally retained.

Warnings SHOULD be treated as defects unless explicitly justified.

---

# Article XVI — Git and Collaboration

## 16.1 Main Branch

The `main` branch MUST represent stable, reviewed code.

Direct commits to `main` SHOULD be prohibited.

## 16.2 Feature Branches

Changes SHOULD be developed through feature branches.

Examples:

```text
feature/restaurant-create-order
feature/retail-inventory-adjustment
feature/academy-student-enrollment
```

## 16.3 Pull Requests

Significant changes SHOULD be introduced through pull requests.

A pull request SHOULD contain:

- Description
- Specification reference
- Implementation summary
- Tests
- Relevant architectural considerations

## 16.4 AI-Generated Changes

Code generated by Copilot or Claude Code follows the same review requirements as manually written code.

AI authorship does not reduce responsibility for correctness.

---

# Article XVII — AI Agent Coordination

The project may be developed using multiple AI coding assistants, including:

- GitHub Copilot
- Claude Code

Both assistants MUST follow this Constitution and the project's specifications.

## 17.1 Single Source of Truth

The following hierarchy MUST be respected:

```text
Constitution
      ↓
Architecture
      ↓
Bounded Contexts
      ↓
Business Capabilities
      ↓
Specifications
      ↓
Tasks
      ↓
Vertical Slices
      ↓
Implementation
      ↓
Tests
```

Lower-level artifacts MUST NOT contradict higher-level artifacts.

## 17.2 Agent Context

AI agents SHOULD be provided with:

- Constitution
- Architecture documentation
- Technology decisions
- Repository structure
- Relevant specifications
- Relevant existing code

Agents MUST NOT infer major architectural decisions from isolated code.

## 17.3 Architectural Decisions

AI agents MAY propose architectural changes.

They MUST NOT unilaterally implement significant architectural changes.

Significant architectural changes MUST first be documented through the appropriate architecture documentation or ADR and approved before implementation.

## 17.4 Parallel Development

Copilot and Claude Code MUST NOT independently modify the same feature simultaneously unless the work has been intentionally coordinated.

Parallel development SHOULD use separate branches.

## 17.5 Human Responsibility

The human developer remains responsible for:

- Architectural decisions
- Business decisions
- Security decisions
- Acceptance of generated code
- Merging changes
- Production deployment

AI assistants are collaborators, not autonomous project owners.

---

# Article XVIII — Documentation

Architecture decisions MUST be documented when they materially affect the system.

The project MUST maintain, at minimum:

```text
constitution.md
architecture.md
glossary.md
technology.md
repository-structure.md
```

Important architectural decisions SHOULD be recorded as ADRs.

Documentation MUST evolve together with the system.

Outdated documentation MUST be considered a defect.

---

# Article XIX — Portfolio Quality

Presencia Virtual Platform is also a public demonstration of engineering capability.

The system SHOULD demonstrate:

- Real business modeling
- Professional architecture
- Security
- Automated testing
- Observability
- API design
- CI/CD
- Documentation
- AI integration
- Responsive UX
- Production-oriented engineering practices

Portfolio visibility MUST NOT justify compromising architectural integrity.

The objective is to demonstrate **real engineering**, not architectural theater.

---

# Article XX — Definition of Done

A feature is considered complete only when:

- The specification is approved.
- Business rules are implemented.
- Validation is implemented.
- Authorization requirements are satisfied.
- Appropriate automated tests exist.
- API behavior is tested where applicable.
- Persistence behavior is tested where applicable.
- Observability requirements are satisfied.
- Documentation is updated when necessary.
- No known critical defects remain.
- The implementation complies with this Constitution.
- The acceptance criteria are satisfied.

A feature is NOT complete merely because it compiles or works in a happy-path demonstration.

---

# Article XXI — Architecture Governance

Architectural consistency MUST be actively maintained throughout the project.

## 21.1 Architecture Review

Significant changes SHOULD be reviewed against:

- Constitution
- Architecture
- Domain boundaries
- Module boundaries
- Security principles
- Testing strategy
- Operational requirements

## 21.2 Architecture Decision Records

An ADR SHOULD be created when a decision:

- Introduces a new architectural pattern
- Changes a module boundary
- Introduces a significant dependency
- Changes persistence strategy
- Changes authentication/authorization architecture
- Introduces a new infrastructure component
- Changes an important technology choice
- Creates a significant trade-off

## 21.3 No Accidental Architecture

Architecture MUST emerge from intentional decisions, not from accumulated implementation shortcuts.

---

# Article XXII — Evolution

This Constitution is a living document.

Architectural principles MAY evolve as the platform evolves.

Changes to this Constitution MUST:

1. Be explicitly proposed.
2. Include a rationale.
3. Be reviewed.
4. Update affected documentation.
5. Identify potential impact on existing specifications and implementation.

No implementation change may silently invalidate constitutional principles.

---

# Final Principle

> **Build software as if another developer will have to maintain it five years from now.**

Presencia Virtual Platform exists to demonstrate that Presencia Virtual can transform real business problems into secure, maintainable and scalable software solutions using modern engineering practices and AI-assisted development.

Technology serves the business.

Architecture serves the technology.

Specifications guide the implementation.

Vertical slices organize the implementation.

Tests validate the behavior.

AI accelerates the work.

And humans remain responsible for the result.