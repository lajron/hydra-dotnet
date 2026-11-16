# Coding standards

Enforced by the build. Severities are in `.editorconfig`; MSBuild switches in `Directory.Build.props`; Roslynator is pinned in
`Directory.Packages.props`; `global.json` pins the SDK and `AnalysisLevel` is pinned to `10.0` so results do not drift.

## Tiers

| Tier | `dotnet build` | Used for |
| --- | --- | --- |
| error | fails | RES001-003, nullable warnings, CS9107/CS9113/CS9124 |
| warning | prints; fails with `-warnaserror` | every style rule below |
| suggestion | editor only | optional sugar (collection expressions, property/accessor `=>`) |

The repo builds with zero warnings. Style rules are warnings rather than errors so a half-finished edit does not block a local build;
`-warnaserror` is the gate. There is no blanket `TreatWarningsAsErrors`, so NuGet audit warnings cannot break a build.

## Rules (all warning unless noted)

| ID | Enforces |
| --- | --- |
| RCS1238 (Roslynator) | No nested `?:` |
| HYD001 (Hydra.Analyzers) | No multi-line expression body; one expression wrapped after `=>` is fine |
| IDE0021 | Constructors use block bodies |
| IDE0290 | Primary constructor where a constructor only assigns |
| IDE0011 | Braces on every `if`/`else`/loop |
| IDE0161, IDE0065, IDE0005 | File-scoped namespaces; usings outside the namespace; no unused usings |
| IDE0036, IDE0040, IDE0044 | Modifier order; explicit accessibility; `readonly` fields |
| IDE0055 | Formatting |
| IDE1006 | `_camelCase` instance fields, PascalCase `static`/`const` fields, `IName` interfaces, PascalCase types |
| IDE0051/52/59/60 | Unused members, values, parameters |
| CA2016, CA1068 | Forward `CancellationToken`; token is the last parameter |
| CA1806 | Do not ignore the result of string operations, LINQ, `Try*` calls, unused `new`, `[Pure]` methods |
| HYD002 (Hydra.Analyzers) | An Operation implementing a Kernel Operation contract cannot depend on or call another such Operation (error) |

Deliberately off: IDE0058 (would force `_ =` on builders and DI registrations), IDE0045/0046 (they push `if`/`else` into ternaries), IDE0130 (namespaces do not mirror every folder),
IDE2000/2003 (blank lines are unconstrained, so `var (v, error) = Get();` directly above `if (error ...)` is fine), CA2007.

## Commands

```bash
dotnet build Hydra.slnx --no-incremental
dotnet build Hydra.slnx --no-incremental -warnaserror     # strict gate
dotnet test --solution Hydra.slnx
dotnet format whitespace Hydra.slnx --verify-no-changes --no-restore
```

Auto-fix a rule with a scoped run and review the diff:
`dotnet format style Hydra.slnx --no-restore --diagnostics IDE0011 --include <file>`.

## Limits

- "Meaningful logic" in an expression body is a judgement; the tooling only rejects multi-line ones.
- HYD001 does not run in `Hydra.Analyzers`, `Hydra.Analyzers.Tests` or `Hydra.Kernel.Tests` (excluded in `Directory.Build.targets`, as for RES).
- RCS1238 does not report a ternary inside another ternary's condition, or inside a lambda inside a ternary.
- CA1806 does not flag an ignored `ImmutableList.Add(...)` result or `Convert.ToInt32(...)`.
- IDE0005 is reported on build only because `GenerateDocumentationFile` is on.
- HYD002 checks symbol-resolved Operation contracts; integration components called Operations without these contracts require review.

## Authoring conventions

- Prefer cohesion over one type per file. Keep short, tightly related types together, such as Options and their registration; split independent responsibilities or a file that becomes hard to navigate.
- Use `public` only for a needed assembly boundary or entry contract. Keep concrete implementations and provider wire models `internal` where possible. A contract normally lives beside the capability it exposes, not in a global Interfaces folder. `internal` does not provide folder privacy.
- A scoped `Common/` is appropriate for code genuinely shared within its parent business area or subsystem. Name the owner in the path and avoid generic global Helpers, Services, or Common dumping grounds. See [architecture](ARCHITECTURE.md) for placement decisions.
- Name types and methods for the capability they provide. Avoid pass-through wrappers, speculative interfaces, empty folder trees, and DTOs that duplicate an existing appropriate contract. Keep Feature request, Handler, Validator, and response code together when cohesive.
- Follow the existing IDE0290 primary-constructor diagnostic when a constructor only assigns fields. Use a block body for a member with meaningful logic; HYD001 catches multi-line expression bodies, but the one-line case still requires judgment.
- Do not use `ConfigureAwait(false)` in application code. Give `Task`-returning methods an `Async` suffix except Mediator handlers, endpoints, and tests. Forward cancellation tokens to asynchronous dependencies.
- AI-generated changes must use the actual project API and naming, read the owning package and its tests before extending it, and run the documented build and test checks. Do not manufacture abstractions to match an illustrative tree.
