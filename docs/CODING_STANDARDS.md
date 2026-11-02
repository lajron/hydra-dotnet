# Coding standards

Enforced by the build. Severities are in `.editorconfig`; MSBuild switches in `Directory.Build.props`; Roslynator is pinned in
`Directory.Packages.props`; `global.json` pins the SDK and `AnalysisLevel` is pinned to `10.0` so results do not drift.

## Tiers

| Tier | `dotnet build` | Used for |
| --- | --- | --- |
| error | fails | RES001-003, nullable warnings, CS9107/CS9113/CS9124 |
| warning | prints; fails with `-warnaserror` | every style rule below |
| suggestion | editor only | optional sugar (collection expressions, property/accessor `=>`) |

Style rules are warnings so existing code can be migrated incrementally and nothing is hidden by an exemption.
There is no blanket `TreatWarningsAsErrors`, so NuGet audit warnings cannot break a build.

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

Deliberately off: IDE0045/0046 (they push `if`/`else` into ternaries), IDE0130 (namespaces do not mirror every folder),
IDE2000/2003 (blank lines are unconstrained, so `var (v, error) = Get();` directly above `if (error ...)` is fine), CA2007.

## Commands

```bash
dotnet build Hydra.slnx --no-incremental
dotnet build Hydra.slnx --no-incremental -warnaserror     # strict gate; fails until the backlog is cleared
dotnet test --solution Hydra.slnx
dotnet format whitespace Hydra.slnx --verify-no-changes --no-restore
```

Fix a backlog rule with a scoped run and review the diff:
`dotnet format style Hydra.slnx --no-restore --diagnostics IDE0011 --include <file>`.

## Limits

- "Meaningful logic" in an expression body is a judgement; the tooling only rejects multi-line ones.
- HYD001 does not run in `Hydra.Analyzers`, `Hydra.Analyzers.Tests` or `Hydra.Kernel.Tests` (excluded in `Directory.Build.targets`, as for RES).
- RCS1238 does not report a ternary inside another ternary's condition, or inside a lambda inside a ternary.
- IDE0005 is reported on build only because `GenerateDocumentationFile` is on.
