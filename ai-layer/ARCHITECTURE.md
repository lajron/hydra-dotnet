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
| Kernel | Reusable `Result`, `AppError`, `IAsyncOperation` contracts, validation behavior, and Options registration. |
| Domain | Business entities, relationships, invariants, calculations, policies, and state transitions. Business rules are not limited to database entities. |
| Infrastructure | Reusable Operations, persistence, external integrations, and their configuration. |
| Application | Features, Commands, Queries, Handlers, Validators, and application workflows. |
| WebApi | Controllers, HTTP mapping, authorization boundaries, service composition, and hosting. |
| Analyzers | Compile-time coding conventions and Result safety checks. |

Domain currently has no business types. Infrastructure has `AppDbContext`,
scoped EF Core/Npgsql registration, Options discovery, and Operation registration,
but no production Operations or integrations.
Application has no production feature handlers yet, and WebApi has no controllers.
The responsibilities below guide future features; examples are illustrative.

## Where code belongs

A package is a cohesive capability within an existing assembly, usually a folder
and namespace with a clear owner. It needs only the files its current behavior
requires. Its public contract and necessary input/output types stay beside the
implementation; implementation and provider wire types are normally `internal`.
`internal` protects an assembly, not an individual folder. Extract an assembly
only for a demonstrated independent distribution or dependency need.

| Need | Owner and starting location |
| --- | --- |
| Business entity, invariant, calculation, or policy | `Hydra.Domain/<BusinessConcept>/` |
| Request, Handler, Validator, feature response | `Hydra.Application/Features/<Area>/<Feature>/` |
| Shared application code within one area | `Hydra.Application/Features/<Area>/Common/` |
| Reusable execution capability | `Hydra.Infrastructure/Operations/<Area>/<Capability>/` |
| External system or technical service | `Hydra.Infrastructure/Integrations/<Capability>/` |
| EF Core context, mapping, migration | `Hydra.Infrastructure/Persistence/` |
| HTTP route and response boundary | `Hydra.WebApi/Controllers/` and existing HTTP mappings |
| Framework primitive independent of business concepts | `Hydra.Kernel/` |

Start flat within the owner. Add module, area, or subpackage levels when they
make a real distinction or a folder becomes difficult to navigate. The same
business names may appear in several assemblies, but their folders need not
mirror one another exactly. A scoped `Common/` may hold genuinely shared errors,
mappings, contracts, or other code for its parent area; avoid an application-wide
dumping ground. Do not create empty folders or placeholder types.

For example, a simple read can live entirely in
`Features/Properties/GetProperty/` as query and Handler using `AppDbContext`
directly. A more complex listing workflow belongs to one Handler that invokes
Domain behavior and any meaningful Operations or integrations. A Controller may
expose several related routes and does not mirror each Feature.

```text
Hydra.Application/Features/Properties/
├── GetProperty/
│   ├── GetPropertyQuery.cs
│   └── GetPropertyHandler.cs
├── PublishListing/
│   ├── PublishListingCommand.cs
│   └── PublishListingHandler.cs
└── Common/                         # Only when code is actually shared in this area
    ├── Errors/
    └── Mappings/

Hydra.Infrastructure/Operations/Properties/PrepareListingImages/
├── IPrepareListingImagesOperation.cs
└── PrepareListingImagesOperation.cs
```

These are placement examples, not implemented Features or required files. A
simple integration may likewise begin as one contract and one implementation
under `Integrations/<Capability>/`, adding a facade, provider folders, or
`Common/` only when real behavior warrants them.

### Workflows and business rules

Handlers own application workflows. A Handler coordinates Domain behavior,
Infrastructure Operations and integrations, persistence, and the final Result.
It may access a DbContext directly when that is the simplest implementation.
Domain owns the business rules the workflow applies. Ordinary Domain classes do
not require interfaces, Domain Services, or DDD aggregates.

An Operation is a focused executable capability. Extract one for real reuse,
execution complexity, or useful encapsulation. Operations are neither generic repositories
nor mandatory wrappers around database access. Keep each Operation's named
interface, implementation, and related models together. Prefer concrete classes
elsewhere unless an interface supplies a useful contract or boundary. Do not
add a UseCases layer, generic repository, or Unit of Work wrapper by default.
**An Operation never depends on or executes another Operation.** Handlers
orchestrate Operations; internal technical helpers need not be Operations. Do
not route Operation calls through Mediator. Handlers also must not call another
Handler directly or dispatch one merely to reuse code.

Handlers may inject `AppDbContext` for straightforward CRUD and queries. A shared
scoped DbContext tracks entities across the Handler and its Operations. A
`Result<T>` wrapper does not detach an entity. Prefer projections or
`AsNoTracking()` for reads that need neither tracking nor later updates; tracked
entities may intentionally cross internal write steps. An Operation may call
`SaveChangesAsync()`, which flushes **all pending tracked changes** in that
context, including changes made elsewhere in the workflow. Without a wider
transaction, an earlier save can remain committed if a later step fails.
When several database steps must be atomic, the Handler establishes and commits
an explicit EF Core transaction around the shared context. Saves inside it do
not themselves commit the outer transaction. Use the provider's execution
strategy correctly if retries are enabled; a database transaction cannot roll
back an external service call. No automatic Mediator transaction, repository,
or `ITransaction` abstraction is required.

Kernel provides `IAsyncOperation<TInput, TOutput>` returning `Task<Result<TOutput>>`
and `IAsyncOperation<TInput>` returning `Task<Result>`. Infrastructure's
`AddInfrastructureServices` scans its assembly for concrete implementations of
named interfaces that inherit exactly one of these contracts. It registers each
named interface as scoped, supports internal implementations, and rejects
ambiguous or duplicate registrations. The generic Kernel contracts themselves
are not registered as services. Keep this named-interface registration behavior
when adding production Operations.

### Configuration options

Kernel's `AddOptionsFromAssembly(assembly)` scans only the supplied assembly for
concrete, non-generic classes with a public parameterless constructor and a name
ending in `Options`. It removes that suffix to find the configuration section:
`EmailOptions` binds `Email`, and `ConnectionStringsOptions` binds
`ConnectionStrings`. It registers each type through Microsoft's Options Pattern,
enables DataAnnotations validation and validates the options when the host starts.
Repeated scans do not add duplicate Options registrations. No marker interface,
attribute, or section-name constant is required.

Each consuming assembly opts in once from its DI composition. Infrastructure's
`AddInfrastructureServices` scans the Infrastructure assembly before registering
persistence and Operations. WebApi supplies `IConfiguration` through its normal
ASP.NET Core configuration providers. Define Options classes alongside the
capability that uses them and inject `IOptions<T>` where needed.

### Results, errors, and request boundaries

Use `Result` for expected failures and propagate unexpected technical exceptions
normally. Kernel's validation behavior turns FluentValidation failures into
`AppError` validation Results when the response type supports them. WebApi
registers Mediator with that behavior, discovers validators from Application,
and registers Infrastructure services at the composition root. WebApi maps
Results to HTTP responses and localizes enum error codes through resources.
Errors and mappings shared within an Application business area generally belong
in its `Common/Errors` and `Common/Mappings`; Infrastructure Operations and
integrations can use their own scoped `Common/` where needed. Do not create
per-Feature or per-Operation error resource files by default. Define an error
once at the layer that owns its meaning. Map an integration failure to a business
error only when its meaning changes. Domain enforces invariants but does not
need localized `AppError` types; a caller can translate an expected Domain
outcome. Unexpected exceptions still propagate.

`EnumErrorLocalizer` looks for `{enum namespace}.Resources.{enum name}` in the
enum's assembly. Thus an enum in `Hydra.Application.Features.Properties.Common.Errors`
uses resources in `Features/Properties/Common/Errors/Resources/` with files
named after the enum, such as `PropertiesErrors.en.resx`. The enum member name
is the resource key. ResourceManager first uses the current UI culture, then
English, then the original message, then an empty string. Keep resource logical
names aligned with the enum namespace when adding translations. HTTP mapping
stays centralized in WebApi.

`Hydra.Analyzers` checks Result flow (RES001-003) and coding conventions; see
the [analyzer guide](../src/Hydra.Analyzers/README.md) and
[coding standards](CODING_STANDARDS.md). Projects inherit the root build
configuration, analyzer reference, and central package versions.

## Integrations and provider composition

An integration is a specialized Infrastructure capability. Its public contract
and necessary input/output models belong beside its implementation. A facade
is useful when it presents one coherent capability across several providers,
clients, or execution steps; it is optional for a simple integration. Internal
clients handle protocols, while internal components handle mappings and recovery.
No `Facade -> Operation -> Client -> Provider` chain is required. An integration
owns its configuration and DI registration; Infrastructure's central registration
composes substantial package registrations. Small packages need no DI extension.

For example, a future fiscalization integration could be organized as follows:

```text
Integrations/
└── Fiscalization/
    ├── IFiscalization.cs
    ├── FiscalizationService.cs
    ├── Contracts/
    ├── Clients/
    ├── Providers/
    │   ├── DevEsdc/
    │   └── MyLpfr/
    ├── Operations/
    │   ├── Submission/
    │   └── Recovery/
    ├── Common/
    │   ├── Errors/
    │   └── Mappings/
    └── Configuration/
```

This is an example, not a required layout or an implemented feature. Integration
Operations obey the no-Operation-to-Operation rule; technical helpers may use
ordinary class names and need not implement Kernel Operation contracts. An
integration should use integration-owned contracts rather than
depend unnecessarily on application-specific business models. Map business
models into those contracts near the consuming workflow. If extracted into an
assembly or NuGet package later, it remains an Infrastructure capability;
Application may still call its public API directly. Avoid adapters, wrappers,
and interfaces that only conceal this dependency.

When several implementations of one integration contract must coexist, register
them explicitly with native .NET Keyed Services in that integration's DI code.
Consumers may inject known keys; a small resolver can own dynamic key selection
when runtime data requires it. Do not scatter `IServiceProvider` resolution
through Handlers or force a global single-provider setting. The named Operation
scanner remains a single-implementation convention and is unrelated to keyed
integration providers. An Application-owned port or adapter is justified when
it translates a distinct application meaning; a forwarding duplicate interface
is not. Domain must not reference Infrastructure integration contracts: project
the Domain facts into the integration input at the consuming boundary.

## Background jobs and shared execution

Background jobs are entry points into Application, like Controllers. A job host
(initially WebApi if appropriate) schedules and dispatches Commands through
Mediator; Handlers perform business orchestration. Add a separate worker host
when there is a concrete need. Do not use nested Mediator dispatch to reuse
Handler logic.

If a manual API and a scheduled job perform the same workflow, both may dispatch
the same Command. The integration owns provider-specific recovery; Application
owns business-specific finalization and coordination. Real workflows must design
concurrency control, idempotency, transaction boundaries, and safe recovery
explicitly.

## Persistence, HTTP, and tests

The implemented persistence foundation is one `AppDbContext` in
`Infrastructure/Persistence/`, scoped Npgsql registration in
`Infrastructure/Configuration/PersistenceConfiguration.cs`, and required
`ConnectionStringsOptions.PostgreSql` bound through Kernel's Options convention.
Future EF mappings can begin in a flat `Persistence/Configurations/` folder;
migrations stay with this context and provider. Add `Seeding/` only when needed.
There is no PostgreSql subfolder or design-time factory today.

WebApi uses Controllers as HTTP entry points, source-generated Mediator
registration in WebApi, and centralized Result-to-HTTP mapping and localization.
Start with a flat `Controllers/` folder; group by module when the API surface
justifies it. Shared HTTP code may use a scoped `Common/`, but do not introduce
controller interfaces or a parallel endpoint-per-class hierarchy by default.

Tests belong in projects matching production assemblies and folders matching
the code under test. Existing test projects need not be renamed or multiplied
to distinguish unit and integration tests. Use an xUnit `Category=Integration`
trait for tests requiring real infrastructure, and verify the actual runner's
filter syntax before documenting a command. Shared fixtures and Testcontainers
are optional tools for real setup needs. Source analyzers enforce objective
C# rules; tests verify runtime behavior and structural boundaries where useful.

Favor cohesive, discoverable code and the fewest abstractions that serve the
workflow. Preserve the existing Kernel, Result, validation, localization, DI,
and analyzer behavior as features are added.

## Local development

Run PostgreSQL in Docker and WebApi locally. The development connection string
matches the fixed Compose credentials; these public credentials are for local
development only and must not be reused in production.

```bash
docker compose up -d
docker compose ps                 # Check container health
docker compose logs -f            # View PostgreSQL logs
dotnet run --project src/Hydra.WebApi
docker compose down               # Stop PostgreSQL; keep database data
docker compose down -v            # Reset the development database volume
```

`docker compose down` preserves the database data. `docker compose down -v`
deletes the development database volume and its data.
