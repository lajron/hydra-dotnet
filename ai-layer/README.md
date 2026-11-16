# Working in Hydra

Use this index to load only the instructions for the task at hand. The guides
below give placement, steps, and review checks. [Architecture](ARCHITECTURE.md)
records the agreed design and resolves edge cases; [coding standards](CODING_STANDARDS.md)
record C# style and build diagnostics. The guides do not turn illustrative
future code into required scaffolding.

## Choose an owner first

| If the change is about... | Start in... | Read... |
| --- | --- | --- |
| Business state, invariant, or pure policy | `src/Hydra.Domain/<Concept>/` | [Domain](rules/domain.md) |
| Command, Query, Handler, Validator, or workflow | `src/Hydra.Application/Features/<Area>/<Feature>/` | [Feature](rules/feature.md) |
| Reusable execution step | `src/Hydra.Infrastructure/Operations/<Area>/<Capability>/` | [Operation](rules/operation.md) |
| External provider or technical capability | `src/Hydra.Infrastructure/Integrations/<Capability>/` | [Integration](rules/integration.md) |
| DbContext, entity mapping, migration, or transaction | `src/Hydra.Infrastructure/Persistence/` and its caller | [Persistence](rules/persistence.md) |
| Controller, HTTP response, or host composition | `src/Hydra.WebApi/` | [HTTP](rules/webapi.md) |
| Error code, translation, or mapping | The owning business area or integration | [Errors](rules/errors.md) |
| Tests for any of the above | Matching test project and hierarchy | [Tests](rules/tests.md) |
| Any C# implementation | The owning project | [Code quality](rules/code.md) plus the relevant task guide |
| Reusable framework primitive or analyzer | Existing `Hydra.Kernel` or `Hydra.Analyzers` area | Inspect its implementation and tests; then read the relevant architecture section |

Read multiple guides when a change crosses boundaries. For example, a write
Feature using an Operation needs **Feature + Operation + Persistence**. An
integration with localized failures needs **Integration + Errors**. A simple
read Handler usually needs **Feature + Tests**; it need not acquire an Operation.

## Working sequence

1. Find the current owner and nearest analogous source and test. Check the actual
   APIs and visibility before creating files.
2. Choose the smallest complete implementation. Add a contract, facade, adapter,
   subfolder, or separate type only when its responsibility is identifiable.
3. Keep public surface small and put shared code in the nearest meaningful
   owner. A scoped `Common/` is allowed; an unowned global dumping ground is not.
4. Apply the relevant task guides, then run the build and test commands in
   `AGENTS.md`. Report what was verified and any unresolved boundary.

`AGENTS.md` is the Codex entry point and directs Codex here. `CLAUDE.md`
imports that file and this index for Claude Code. The task guides are selected
from this table; they are not automatically loaded by their source path. Keep
the selection explicit and small. This layer is temporary until the separate
Hydra AI Framework is integrated.
