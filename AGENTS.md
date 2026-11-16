# Hydra

.NET 10 / C# 14. Style rules are enforced by the build (`.editorconfig`, analyzers); read the diagnostics it prints and fix
the code. Reference: `ai-layer/CODING_STANDARDS.md`.

Before creating a component, open `ai-layer/README.md` and read the relevant task guide.
`ai-layer/ARCHITECTURE.md` and `ai-layer/CODING_STANDARDS.md` are the source of truth when a guide needs more detail.
Accepted architecture rules must not be silently overridden. Keep packages cohesive; avoid placeholder files, speculative
interfaces, adapters, repositories, and wrappers. Application Handlers own workflows and may use `AppDbContext` directly;
Operations never call other Operations.

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
- For configuration and persistence work, follow `ai-layer/rules/persistence.md`.
