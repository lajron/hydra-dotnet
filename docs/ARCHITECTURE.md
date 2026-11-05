# Solution architecture

Hydra is a pragmatic, opinionated .NET architecture. The production projects are
`Hydra.Kernel`, `Hydra.Domain`, `Hydra.Infrastructure`, `Hydra.Application`, and
`Hydra.WebApi`. `Hydra.Analyzers` enforces established conventions at build time.

The current project references are:

```text
Hydra.WebApi         -> Hydra.Application, Hydra.Infrastructure, Hydra.Kernel
Hydra.Application    -> Hydra.Infrastructure, Hydra.Domain, Hydra.Kernel
Hydra.Infrastructure -> Hydra.Domain, Hydra.Kernel
Hydra.Domain         -> Hydra.Kernel
```

Application intentionally references Infrastructure and may use its public
capabilities directly. These dependencies are part of Hydra's design.

## Responsibilities

| Project | Responsibility |
| --- | --- |
| Kernel | Reusable `Result`, `AppError`, `IAsyncOperation` contracts, and validation behavior. |
| Domain | Business entities, relationships, invariants, calculations, policies, and state transitions. Business rules are not limited to database entities. |
| Infrastructure | Reusable Operations, persistence, external integrations, and their configuration. |
| Application | Features, Commands, Queries, Handlers, Validators, and application workflows. |
| WebApi | Controllers, HTTP mapping, authorization boundaries, service composition, and hosting. |
| Analyzers | Compile-time coding conventions and Result safety checks. |

Domain currently has no business types. Infrastructure currently has Operation
registration but no concrete production Operations, persistence, or integrations.
Application has no production feature handlers yet, and WebApi has no controllers.
The responsibilities below guide future features; examples are illustrative.

### Workflows and business rules

Handlers own application workflows. A Handler coordinates Domain behavior,
Infrastructure Operations and integrations, persistence, and the final Result.
It may access a DbContext directly when that is the simplest implementation.
Domain owns the business rules the workflow applies. Ordinary Domain classes do
not require interfaces, Domain Services, or DDD aggregates.

An Operation is a focused, reusable executable capability. Extract one when
multiple workflows need the same execution logic or when the capability is
cohesive enough to stand on its own. Operations are neither generic repositories
nor mandatory wrappers around database access. Keep each Operation's named
interface, implementation, and related models together. Prefer concrete classes
elsewhere unless an interface supplies a useful contract or boundary. Do not
add a UseCases layer, generic repository, or Unit of Work wrapper by default.
Use explicit transactions where the workflow needs them.

Kernel provides `IAsyncOperation<TInput, TOutput>` returning `Task<Result<TOutput>>`
and `IAsyncOperation<TInput>` returning `Task<Result>`. Infrastructure's
`AddInfrastructureServices` scans its assembly for concrete implementations of
named interfaces that inherit exactly one of these contracts. It registers each
named interface as scoped, supports internal implementations, and rejects
ambiguous or duplicate registrations. The generic Kernel contracts themselves
are not registered as services. Keep this named-interface registration behavior
when adding production Operations.

### Results and request boundaries

Use `Result` for expected failures and propagate unexpected technical exceptions
normally. Kernel's validation behavior turns FluentValidation failures into
`AppError` validation Results when the response type supports them. WebApi
registers Mediator with that behavior, discovers validators from Application,
and registers Infrastructure services at the composition root. WebApi maps
Results to HTTP responses and localizes enum error codes through resources.
`Hydra.Analyzers` checks Result flow (RES001-003) and coding conventions; see
the [analyzer guide](../src/Hydra.Analyzers/README.md) and
[coding standards](CODING_STANDARDS.md). Projects inherit the root build
configuration, analyzer reference, and central package versions.

## Integrations

An integration is a specialized Infrastructure capability. Treat it as a
self-contained library conceptually, even while it lives in
`Hydra.Infrastructure`. Its public facade is the entry point for consumers; an
interface is optional. Public input and output contracts belong to the
integration. Internal clients communicate with external systems, while internal
components handle provider protocols, mapping, processing, recovery, and
integration-specific rules. The integration may also own errors, localization
resources, configuration, and dependency registration.

For example, a future fiscalization integration could be organized as follows:

```text
Integrations/
└── Fiscalization/
    ├── IFiscalization.cs
    ├── FiscalizationService.cs
    ├── FiscalizationErrors.cs
    ├── Contracts/
    ├── Clients/
    │   ├── Core/
    │   ├── DevEsdc/
    │   └── MyLpfr/
    ├── Operations/
    │   ├── Resolution/
    │   ├── Signers/
    │   └── Recovery/
    ├── Configuration/
    └── Resources/
```

This is an example, not a required layout or an implemented feature. Internal
integration Operations need not implement Hydra's top-level `IAsyncOperation`
contracts. An integration should use integration-owned contracts rather than
depend unnecessarily on application-specific business models. Map business
models into those contracts near the consuming workflow. If extracted into an
assembly or NuGet package later, it remains an Infrastructure capability;
Application may still call its public API directly. Avoid adapters, wrappers,
and interfaces that only conceal this dependency.

## Background jobs and shared execution

Background jobs are entry points into Application, like Controllers. A job host
(initially WebApi if appropriate) schedules and dispatches Commands through
Mediator; Handlers perform business orchestration. Add a separate worker host
when there is a concrete need. Do not use nested Mediator dispatch to reuse
Handler logic.

For a possible fiscalization recovery workflow, a manual finalization Handler
and a scheduled recovery Handler could call the same focused
`FinalizeRestaurantOperation`. That Operation could use the Fiscalization
integration and persistence as needed. If both entry points perform exactly the
same application workflow, they can dispatch the same Command instead. The
integration owns provider-specific recovery; Application owns business-specific
finalization and coordination. Introduce the shared Operation only when the
execution logic actually needs reuse. Real workflows must design concurrency
control, idempotency, transaction boundaries, and safe recovery explicitly.

Favor cohesive, discoverable code and the fewest abstractions that serve the
workflow. Preserve the existing Kernel, Result, validation, localization, DI,
and analyzer behavior as features are added.
