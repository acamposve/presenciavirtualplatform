# Presencia Virtual Platform — Architecture

**Version:** 1.0  
**Status:** Approved  
**Last Updated:** 2026-09-05

---

## 1. Purpose

This document defines the software architecture of the **Presencia Virtual Platform**.

The platform is a modular business software platform designed to support multiple business domains while maintaining a single coherent technical foundation.

The initial business domains are:

- Restaurant
- Retail / Convenience Store
- Academy / Educational Institution

The architecture is designed to demonstrate production-oriented software engineering practices while remaining pragmatic and avoiding unnecessary complexity.

This document complements `constitution.md`.

Where a conflict exists, `constitution.md` has precedence.

---

# 2. Architectural Principles

The platform follows these primary architectural principles:

1. Specification Driven Development
2. Domain-Driven Design
3. Modular Monolith
4. Vertical Slice Architecture
5. Clean Architecture principles
6. API First
7. Security by Design
8. Multi-Tenancy
9. Event-Driven communication where appropriate
10. Observability by default
11. Automated testing
12. AI-assisted development under human architectural governance
13. Simplicity over unnecessary complexity

The architecture must evolve incrementally as business requirements become clearer.

---

# 3. High-Level Architecture

The platform is implemented as a **Modular Monolith**.

There is initially:

- One backend application
- One frontend application
- One PostgreSQL database
- Optional Redis infrastructure
- Optional external services such as AI providers, email, payment providers, or file storage

The system is logically divided into bounded contexts/modules.

```text
                        ┌───────────────────────┐
                        │       Clients         │
                        │                       │
                        │ Web / Mobile / Future  │
                        └───────────┬───────────┘
                                    │
                                    │ HTTPS / JSON
                                    ▼
                        ┌───────────────────────┐
                        │      API Layer        │
                        │                       │
                        │ ASP.NET Core          │
                        │ Minimal APIs           │
                        └───────────┬───────────┘
                                    │
              ┌─────────────────────┼─────────────────────┐
              │                     │                     │
              ▼                     ▼                     ▼
       ┌─────────────┐       ┌─────────────┐       ┌─────────────┐
       │    Core     │       │ Restaurant  │       │   Retail    │
       │             │       │             │       │             │
       │ Identity    │       │ Orders      │       │ POS         │
       │ Tenancy     │       │ Tables      │       │ Products    │
       │ Users       │       │ Kitchen     │       │ Inventory   │
       │ Auth        │       │ Menu        │       │ Purchasing  │
       │ Audit       │       │ Inventory   │       │ Suppliers   │
       │ Notifications│      │ Payments   │       │ Customers   │
       └─────────────┘       └─────────────┘       └─────────────┘
                                    │
                                    │
                              ┌─────┴─────┐
                              │           │
                              ▼           ▼
                       ┌─────────────┐ ┌─────────────┐
                       │  Academy    │ │ Shared      │
                       │             │ │ Infrastructure│
                       │ Students    │ │ PostgreSQL  │
                       │ Teachers    │ │ Redis       │
                       │ Courses     │ │ Messaging   │
                       │ Attendance  │ │ Storage     │
                       │ Evaluation  │ │ Observability│
                       └─────────────┘ └─────────────┘
```

The physical deployment is simple.

The logical architecture is strongly modular.

---

# 4. Modular Monolith

## 4.1 Definition

The platform is a **Modular Monolith**, not a collection of microservices.

All modules run inside the same backend process and are deployed together.

However, modules maintain explicit boundaries.

```text
PresenciaVirtual.Api
        │
        ├── Core
        ├── Restaurant
        ├── Retail
        └── Academy
```

The purpose is to obtain many of the architectural benefits of modularity without introducing distributed-system complexity prematurely.

---

## 4.2 Why a Modular Monolith

A modular monolith provides:

- Simple deployment
- Simple local development
- Simple debugging
- Low operational overhead
- Transactions where appropriate
- Fast development
- Clear business boundaries
- Future extraction possibilities

The platform should **not** introduce microservices merely to demonstrate microservices.

A module may eventually become a separate service if there is a genuine business or technical reason.

Such a decision requires an ADR.

---

# 5. Bounded Contexts

The initial bounded contexts are:

```text
Core
Restaurant
Retail
Academy
```

## 5.1 Core

Core contains capabilities shared across the platform.

Examples:

- Organizations
- Tenants
- Users
- Identity
- Authentication
- Authorization
- Roles
- Permissions
- Audit
- Notifications
- Configuration
- Files
- Billing
- Platform-level AI capabilities where appropriate

Core must remain focused.

A concept must not be placed in Core merely because multiple modules currently use it.

Shared concepts belong in Core only when they are genuinely platform-wide.

---

## 5.2 Restaurant

The Restaurant bounded context contains restaurant-specific business capabilities.

Potential capabilities include:

- Tables
- Reservations
- Menu
- Orders
- Kitchen
- Payments
- Delivery
- Customers
- Inventory
- Purchasing
- Suppliers
- Promotions
- Cash management
- Reporting

Example:

```text
Restaurant
├── Tables
├── Reservations
├── Menu
├── Orders
├── Kitchen
├── Payments
├── Delivery
├── Inventory
├── Purchasing
└── Customers
```

---

## 5.3 Retail

The Retail bounded context represents convenience stores and similar retail businesses.

Potential capabilities include:

- Point of Sale
- Products
- Product Categories
- Pricing
- Inventory
- Purchasing
- Suppliers
- Customers
- Promotions
- Cash Registers
- Returns
- Stock Transfers
- Multi-branch operations
- Offline operation and synchronization

Example:

```text
Retail
├── POS
├── Products
├── Pricing
├── Inventory
├── Purchasing
├── Suppliers
├── Customers
├── Promotions
├── Cash
├── Returns
└── Branches
```

Offline operation is a potential architectural capability but must be specified before implementation.

---

## 5.4 Academy

The Academy bounded context represents educational institutions.

Potential capabilities include:

- Students
- Teachers
- Courses
- Classes
- Schedules
- Enrollment
- Attendance
- Evaluations
- Grades
- Certificates
- Payments
- Communications
- Student portal
- Teacher portal

Example:

```text
Academy
├── Students
├── Teachers
├── Courses
├── Classes
├── Enrollment
├── Scheduling
├── Attendance
├── Evaluation
├── Certificates
└── Payments
```

---

# 6. Module Boundaries

Each module owns its own business logic.

Modules must not access another module's internal implementation directly.

Incorrect:

```text
Restaurant
   ↓
Retail.Infrastructure.SomeRepository
```

Incorrect:

```text
Academy
   ↓
Restaurant.Domain.SomeEntity
```

Preferred:

```text
Restaurant
   ↓
Core public contract
```

or:

```text
Restaurant
   ↓
Published application contract / event
   ↓
Retail
```

or:

```text
Restaurant
   ↓
Shared integration contract
   ↓
Academy
```

Cross-module communication must use an explicit contract.

---

# 7. Vertical Slice Architecture

Within each module, functionality is organized by **business capability/use case**, not primarily by technical layer.

Example:

```text
Restaurant
└── Orders
    ├── CreateOrder
    │   ├── Endpoint.cs
    │   ├── Command.cs
    │   ├── Handler.cs
    │   ├── Validator.cs
    │   ├── Domain.cs
    │   ├── Repository.cs
    │   └── Tests.cs
    │
    ├── AddItem
    │   ├── Endpoint.cs
    │   ├── Command.cs
    │   ├── Handler.cs
    │   ├── Validator.cs
    │   └── Tests.cs
    │
    └── CloseOrder
        ├── Endpoint.cs
        ├── Command.cs
        ├── Handler.cs
        ├── Validator.cs
        └── Tests.cs
```

A vertical slice owns everything necessary to implement a particular use case.

This reduces unnecessary coupling between unrelated functionality.

---

# 8. Clean Architecture

Clean Architecture principles apply inside modules.

The fundamental dependency rule is:

```text
Presentation
     ↓
Application
     ↓
Domain

Infrastructure
     ↓
Application / Domain contracts
```

The Domain must not depend on:

- ASP.NET Core
- PostgreSQL
- Dapper
- Redis
- Azure
- OpenAI
- HTTP clients
- UI frameworks
- Infrastructure implementations

The Domain represents business rules.

---

# 9. Recommended Module Structure

A module may be organized approximately as:

```text
Modules/
├── Core/
├── Restaurant/
├── Retail/
└── Academy/
```

Within a module:

```text
Restaurant/
├── Orders/
│   ├── CreateOrder/
│   ├── AddItem/
│   ├── RemoveItem/
│   ├── CancelOrder/
│   └── CloseOrder/
│
├── Tables/
├── Reservations/
├── Menu/
├── Kitchen/
└── Inventory/
```

Infrastructure concerns should remain separate from business capabilities where practical.

The exact physical structure may evolve.

The architectural boundary is more important than a rigid folder convention.

---

# 10. API Architecture

The backend exposes an HTTP API using:

- ASP.NET Core
- Minimal APIs
- JSON
- OpenAPI

Example:

```text
/api/v1/restaurants/orders
/api/v1/restaurants/orders/{id}
/api/v1/restaurants/tables
/api/v1/retail/products
/api/v1/retail/sales
/api/v1/academy/students
/api/v1/academy/courses
```

API routes must represent business capabilities rather than internal implementation details.

The API is a public contract.

Breaking API changes require explicit review.

---

# 11. Command and Query Separation

CQRS is applied pragmatically.

Commands modify state.

Examples:

```text
CreateOrder
AddOrderItem
CloseOrder
RegisterSale
EnrollStudent
RecordAttendance
```

Queries retrieve information.

Examples:

```text
GetOrder
GetDailySales
GetAvailableTables
GetStudentProfile
GetCourseSchedule
```

CQRS does not require separate databases.

CQRS does not require event sourcing.

The simplest implementation that satisfies the requirement should be preferred.

---

# 12. Domain Model

DDD principles apply to business-critical functionality.

The domain model may contain:

- Entities
- Value Objects
- Aggregates
- Domain Services
- Domain Events
- Business Rules

Example:

```text
Order
 ├── OrderId
 ├── Customer
 ├── Table
 ├── Items
 ├── Status
 ├── Total
 └── Business Rules
```

Business rules should live as close as possible to the domain concept that owns them.

Example:

```text
Order.Close()
```

is preferable to:

```text
OrderService.CloseOrder(order)
```

when closing an order is fundamentally an invariant of the Order aggregate.

---

# 13. Persistence

The initial persistence technology is:

- PostgreSQL
- Dapper

The database is shared physically but logically partitioned by module ownership.

Example:

```text
PostgreSQL
│
├── core
├── restaurant
├── retail
└── academy
```

The exact PostgreSQL schema strategy may evolve.

The important architectural rule is:

> A module owns its data.

Another module must not directly query another module's tables merely because they are physically accessible.

---

# 14. Transactions

Transactions should be used where required by business consistency.

Within a module:

```text
Command
   ↓
Transaction
   ├── Business operation
   ├── Persistence
   └── Domain events
```

Cross-module distributed transactions should be avoided.

When cross-module consistency is required, prefer:

- Domain events
- Integration events
- Eventual consistency
- Explicit workflows

The chosen approach must be documented when complexity is significant.

---

# 15. Events

Events are used when they provide meaningful decoupling.

Examples:

```text
OrderCreated
OrderClosed
PaymentReceived
StockAdjusted
StudentEnrolled
AttendanceRecorded
```

Events should describe facts that occurred.

Events must not become a generic replacement for method calls.

---

# 16. Infrastructure

Infrastructure provides technical implementations for business-defined abstractions.

Potential infrastructure components include:

```text
PostgreSQL
Redis
File Storage
Email Provider
Payment Provider
AI Provider
Message Infrastructure
Observability
```

Infrastructure must not leak into the Domain.

External providers must be abstracted where the application has a meaningful reason to remain provider-independent.

---

# 17. Redis

Redis may be used for:

- Caching
- Distributed coordination where justified
- Session/state scenarios where appropriate
- Rate limiting
- Temporary data

Redis must not become the default persistence mechanism.

Every Redis use case should have an explicit purpose.

---

# 18. Real-Time Communication

SignalR may be used when real-time behavior provides meaningful business value.

Examples:

Restaurant:

```text
Kitchen
   ↑
New Order
   ↑
Waiter
```

Retail:

```text
POS
   ↓
Inventory
```

Academy:

```text
Teacher
   ↓
Attendance / Notification
```

Real-time functionality should only be introduced when required by a specification.

---

# 19. Multi-Tenancy

The platform is designed as a multi-tenant system.

Conceptually:

```text
Platform
│
├── Tenant A
│   ├── Restaurant
│   └── Users
│
├── Tenant B
│   ├── Retail
│   └── Users
│
└── Tenant C
    └── Academy
```

Every tenant-owned resource must have an unambiguous tenant boundary.

Tenant isolation is a security requirement.

The application must never trust a tenant identifier supplied by a client without validating it against the authenticated user's context.

---

# 20. Identity and Authorization

Authorization must operate at multiple levels where required:

```text
User
  ↓
Tenant
  ↓
Role
  ↓
Permission
  ↓
Resource
```

Examples:

```text
restaurant.orders.read
restaurant.orders.create
restaurant.orders.cancel

retail.products.read
retail.products.manage
retail.sales.create

academy.students.read
academy.students.manage
academy.attendance.record
```

Authorization rules must be explicit.

Security checks must not rely exclusively on frontend behavior.

---

# 21. Authentication

Authentication is an infrastructure concern exposed through Core.

The exact authentication mechanism may evolve.

The architecture should allow future integration with:

- JWT
- OpenID Connect
- OAuth 2.0
- External identity providers

The initial implementation should use the simplest secure mechanism appropriate to the application.

---

# 22. AI Architecture

AI is a platform capability, but AI must not become an architectural dependency of the entire system.

Potential AI capabilities include:

### Restaurant

- Demand prediction
- Menu recommendations
- Inventory suggestions
- Sales analysis

### Retail

- Reorder recommendations
- Product demand prediction
- Sales analysis
- Natural-language reporting

### Academy

- Student performance analysis
- Assessment generation
- Personalized study plans
- Educational recommendations

AI functionality must be implemented behind explicit application contracts.

Example:

```text
Application
    ↓
IAssistant
    ↓
OpenAI / Azure OpenAI / Other Provider
```

The Domain must not directly depend on an AI provider.

AI functionality must define:

- Input
- Output
- Failure behavior
- Security
- Privacy
- Cost controls
- Evaluation strategy
- Observability

---

# 23. Frontend Architecture

The frontend is a separate React application.

Technology:

- React
- TypeScript
- Vite
- Material UI
- TanStack Query
- React Router
- Zod

The frontend should mirror business modules conceptually.

Example:

```text
src/
├── core/
├── restaurant/
├── retail/
└── academy/
```

Within each module:

```text
restaurant/
└── orders/
    ├── pages/
    ├── components/
    ├── api/
    ├── hooks/
    ├── schemas/
    └── types/
```

The frontend must consume the API contract rather than directly accessing backend internals.

---

# 24. API Client Strategy

Frontend API access should be centralized enough to provide:

- Authentication handling
- Error normalization
- Request configuration
- API versioning support
- Type safety where practical

Business logic should not be duplicated unnecessarily between frontend and backend.

The backend remains authoritative for business rules.

---

# 25. Validation

Validation exists at multiple levels.

### API validation

Validates request shape and basic constraints.

### Application validation

Validates use-case-specific requirements.

### Domain validation

Protects business invariants.

Example:

```text
API
 ↓
"quantity must be positive"

Application
 ↓
"product must exist"

Domain
 ↓
"order cannot be closed twice"
```

Client-side validation improves UX but never replaces server-side validation.

---

# 26. Error Handling

The API should expose consistent error responses.

Errors should distinguish between:

- Validation errors
- Authentication failures
- Authorization failures
- Not found
- Conflict
- Business rule violations
- Unexpected system failures

Internal implementation details must not be exposed to clients.

Sensitive information must never appear in error responses.

---

# 27. Observability

Observability is part of the architecture.

The platform should support:

- Structured logging
- Correlation IDs
- Distributed/request tracing
- Metrics
- Health checks
- OpenTelemetry

Example:

```text
Request
  ↓
Correlation ID
  ↓
Endpoint
  ↓
Application Handler
  ↓
Database
  ↓
External Provider
```

Logs must never contain:

- Passwords
- Access tokens
- Secrets
- API keys
- Sensitive personal information unnecessarily

---

# 28. Health Checks

The backend should expose health endpoints.

Examples:

```text
/health
/health/ready
/health/live
```

Health checks should distinguish between:

- Process is alive
- Application is ready to serve traffic
- Critical dependencies are available

Health checks must not expose sensitive infrastructure details.

---

# 29. Testing Architecture

Testing follows the architectural boundaries.

```text
                 Tests
                   │
       ┌───────────┼───────────┐
       ▼           ▼           ▼
     Unit      Integration      E2E
```

## Unit Tests

Used primarily for:

- Domain rules
- Value objects
- Business logic
- Application handlers where appropriate

## Integration Tests

Used for:

- Database interactions
- API behavior
- Infrastructure
- Module boundaries

## End-to-End Tests

Used for critical business flows.

Example:

```text
Create Order
    ↓
Add Items
    ↓
Send to Kitchen
    ↓
Close Order
    ↓
Register Payment
```

Tests must validate behavior, not implementation details.

---

# 30. Dependency Injection

Dependency Injection is used for infrastructure and application dependencies.

Avoid creating abstractions merely to satisfy a pattern.

An interface should exist when it provides meaningful:

- Decoupling
- Testability
- Provider substitution
- Architectural boundary
- Business contract

---

# 31. Configuration

Configuration must be externalized.

Examples:

```text
Database connection
Redis connection
Authentication settings
AI provider settings
External service URLs
Feature flags
```

Secrets must never be committed to source control.

Local development should use appropriate secret-management mechanisms.

---

# 32. Feature Flags

Feature flags may be used when there is a genuine need for:

- Gradual rollout
- Experimental features
- Tenant-specific functionality
- Safe deployment

Feature flags should not replace proper architectural design.

---

# 33. Background Processing

Background jobs may be introduced for operations that should not block an HTTP request.

Examples:

```text
SendNotification
GenerateReport
ProcessDocument
SynchronizeInventory
GenerateAIAnalysis
```

The platform should initially use the simplest mechanism capable of satisfying the requirement.

A dedicated message broker is not required unless a specification demonstrates the need.

---

# 34. External Integrations

External systems must be isolated behind explicit contracts.

Example:

```text
Application
    ↓
IPaymentProvider
    ↓
Payment Provider
```

or:

```text
Application
    ↓
IEmailService
    ↓
Email Provider
```

External provider SDKs must not spread throughout the business codebase.

---

# 35. Offline Retail Architecture

Offline operation is considered a potential differentiating capability of the Retail module.

If implemented, the architecture may evolve toward:

```text
                    Cloud
                      │
                 PostgreSQL
                      │
                Sync Service
                      │
              ───── Internet ─────
                      │
                 Local Node
                      │
              Local Database
                      │
                    POS
```

Offline functionality must not be implemented until the complete business requirements are specified.

The specification must define:

- What works offline
- What data is cached locally
- Conflict resolution
- Synchronization
- Authentication
- Security
- Failure recovery
- Duplicate transactions
- Inventory consistency

This is intentionally an evolutionary capability.

---

# 36. Deployment Architecture

Initial deployment should remain simple.

```text
                    Internet
                       │
                       ▼
                Reverse Proxy
                       │
                       ▼
              PresenciaVirtual API
                       │
          ┌────────────┼────────────┐
          ▼            ▼            ▼
      PostgreSQL     Redis       Storage
```

Frontend:

```text
Browser
   │
   ▼
React Application
   │
   ▼
PresenciaVirtual API
```

Containerization should be supported through Docker.

The application should be deployable without requiring a complex orchestration platform.

---

# 37. CI/CD

The repository should support automated validation.

A pull request should validate at minimum:

```text
Restore
   ↓
Build
   ↓
Unit Tests
   ↓
Integration Tests
   ↓
Code Quality
   ↓
Security Checks
```

The main branch must remain stable.

Deployment should only occur from validated builds.

---

# 38. Architectural Decision Records

Architectural decisions that materially affect the platform must be documented.

Examples requiring an ADR:

- Changing persistence strategy
- Introducing a message broker
- Introducing microservices
- Changing authentication architecture
- Changing tenancy strategy
- Introducing event sourcing
- Introducing a new architectural pattern
- Extracting a module into a service
- Introducing a major external platform
- Changing module boundaries

ADRs should explain:

```text
Context
Decision
Alternatives
Consequences
Status
```

---

# 39. Architecture Evolution

The architecture is intentionally evolutionary.

The platform should progress through stages.

### Stage 1

```text
Modular Monolith
+
PostgreSQL
+
React
+
REST API
```

### Stage 2

Add capabilities only when justified:

```text
Redis
Background Jobs
SignalR
AI
Advanced Observability
```

### Stage 3

Introduce additional infrastructure only when real requirements justify it.

### Stage 4

A module may become an independent service only when justified by:

- Independent scaling
- Independent deployment
- Strong team ownership
- Isolation requirements
- Regulatory/security requirements
- Technical constraints
- Operational requirements

Microservices are an architectural consequence, not a starting goal.

---

# 40. Architecture Governance

Every significant architectural change must answer:

1. What problem are we solving?
2. Why does the current architecture not solve it?
3. What alternatives were considered?
4. What complexity does the change introduce?
5. How does it affect module boundaries?
6. How does it affect security?
7. How does it affect testing?
8. How does it affect operations?
9. Can the decision be reversed?
10. Does an ADR need to be created?

AI-generated architectural changes are subject to exactly the same rules.

---

# 41. AI Development Workflow

Claude Code and GitHub Copilot are development assistants.

They must operate within the following hierarchy:

```text
constitution.md
       ↓
architecture.md
       ↓
bounded contexts
       ↓
business capabilities
       ↓
specification
       ↓
tasks
       ↓
implementation
       ↓
tests
       ↓
validation
```

Neither Copilot nor Claude Code may silently redefine architecture.

If an implementation appears to require an architectural change:

```text
STOP
 ↓
Document the issue
 ↓
Propose alternatives
 ↓
Create/update ADR if required
 ↓
Obtain human approval
 ↓
Update architecture/specification
 ↓
Implement
```

AI-generated code is treated as normal code and must pass the same review and quality requirements.

---

# 42. Architecture Constraints

The following constraints are mandatory unless explicitly changed through the governance process.

### MUST

- Use Modular Monolith architecture.
- Maintain explicit module boundaries.
- Apply DDD to business-critical areas.
- Organize business functionality using Vertical Slices.
- Respect Clean Architecture dependency direction.
- Use API-first principles.
- Enforce tenant isolation.
- Keep domain logic independent of infrastructure.
- Test business-critical behavior.
- Maintain observability.
- Document significant architectural decisions.
- Follow the Constitution.

### MUST NOT

- Introduce microservices without an ADR.
- Introduce distributed infrastructure merely for demonstration.
- Allow modules to access another module's database tables directly.
- Put infrastructure dependencies into the Domain.
- Put business rules exclusively in controllers/endpoints.
- Trust client-side authorization.
- Commit secrets.
- Allow AI tools to override architectural governance.
- Add abstractions without a meaningful reason.
- Optimize for architectural complexity instead of business value.

---

# 43. Definition of Architectural Success

The architecture is successful when:

- A new developer can understand the system quickly.
- Business capabilities are easy to locate.
- Modules can evolve independently within the monolith.
- Business rules are explicit and testable.
- Infrastructure can evolve without rewriting the domain.
- New functionality can be implemented as isolated vertical slices.
- AI tools can contribute without destabilizing architecture.
- The system can evolve without premature distributed-system complexity.
- A module can eventually be extracted if a real need appears.
- The platform looks and behaves like software designed for real businesses rather than a technology demonstration.

---

# 44. Relationship With Other Documents

The architecture does not exist in isolation.

```text
constitution.md
      │
      ▼
architecture.md
      │
      ├── glossary.md
      ├── technology.md
      ├── repository-structure.md
      └── development-workflow.md
                  │
                  ▼
             specifications
                  │
                  ▼
                tasks
                  │
                  ▼
          vertical slices
                  │
                  ▼
            implementation
                  │
                  ▼
                tests
```

The following documents form the initial architectural foundation:

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
│   └── adr/
│
├── specs/
│   ├── core/
│   ├── restaurant/
│   ├── retail/
│   └── academy/
│
└── src/
```

---

# 45. Final Architectural Principle

Presencia Virtual Platform is intentionally designed as a **simple system with strong boundaries**, rather than a complex system with unnecessary infrastructure.

The architecture follows this principle:

> **Start modular. Start simple. Model the business correctly. Let complexity be earned by real requirements.**

The platform must be capable of growing from a portfolio-quality modular monolith into a production-scale system without requiring the architecture to be rewritten prematurely.