# Hydra

.NET 10 / C# 14. Style rules are enforced by the build (`.editorconfig`, analyzers); read the diagnostics it prints and fix
the code. Reference: `docs/CODING_STANDARDS.md`.

Before finishing, run `dotnet build Hydra.slnx --no-incremental` and `dotnet test --solution Hydra.slnx`.
The solution builds with zero warnings; your change must keep it that way (`-warnaserror` is the gate).

- Never suppress a diagnostic or change a severity in `.editorconfig` to get green without asking.
- Never commit, stash or reset unless asked. Never add AI attribution to commits, PRs or code.
- Never run bare `dotnet format` on the solution; scope it with `--include <files>` and review the diff.

Not checked by the build:

- Use a block body when a member has real logic; do not convert methods to `=>` just because you can.
- No `ConfigureAwait(false)` in application code.
- `Async` suffix on `Task`-returning methods, except Mediator handlers, endpoints and tests.
- Result pattern: see `src/Hydra.Analyzers/README.md` (RES001-003 are build errors).
