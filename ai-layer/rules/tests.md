# Test a Hydra change

**Use for:** selecting test ownership, test scope, or a verification command.
Tests mirror the production assembly and relevant folder hierarchy, without a
mandatory one-test-class-per-source-class rule.

| Production owner | Test owner |
| --- | --- |
| `Hydra.Kernel` | `tests/Hydra.Kernel.Tests/` |
| `Hydra.Application` | `tests/Hydra.Application.Tests/` |
| `Hydra.WebApi` | `tests/Hydra.WebApi.Tests/` |
| `Hydra.Analyzers` | `tests/Hydra.Analyzers.Tests/` |
| Future Domain or Infrastructure code | Matching test project when real tests require it; no placeholder project |

1. Test the behavior or architectural risk introduced by the change. Do not
   write a test that merely repeats the implementation line for line.
2. A test that needs PostgreSQL or another external system may use an xUnit
   `Category=Integration` trait. Ordinary unit tests need no `Unit` trait.
   Keep Docker-dependent tests out of the ordinary fast path as appropriate.
3. Use a real provider or Testcontainers when database mapping, constraints,
   transactions, or provider semantics are what matters. Shared fixtures can
   reduce setup; a universal `BaseTest` is not required.
4. For a Roslyn rule, test both a violation and legitimate nearby code. Keep
   false positives low and verify the diagnostic message and location when
   that distinction matters.
5. Before documenting a filter command, check the repository's current
   Microsoft Testing Platform/xUnit runner; VSTest trait syntax may differ.

Finish with `dotnet build Hydra.slnx --no-incremental` and
`dotnet test --solution Hydra.slnx` as required by `AGENTS.md`. Report counts
and warnings from the actual run, not an earlier baseline.
