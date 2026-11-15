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

Configuration and persistence conventions:

- Inspect `GlobalUsings.cs`, the existing DI entry points, `appsettings.json`, and `docs/ARCHITECTURE.md` before adding configuration code. Put shared usings in the project's `GlobalUsings.cs` and keep related registration and options types together.
- Kernel owns generic Options discovery. A consuming assembly opts in once with `AddOptionsFromAssembly(assembly)`; `EmailOptions` binds the `Email` section. Do not add per-class binding, section-name constants, or an `IConfiguration` parameter when this convention handles the setting.
- Infrastructure owns EF Core provider setup; WebApi supplies configuration. Keep `InfrastructureConfiguration` a thin composition point. Add a visible, credential-free `appsettings.json` key when introducing a new setting.
- Add validation only for an actual requirement. `ConnectionStringsOptions.PostgreSql` is required: use `[Required]` so missing, empty, and whitespace-only values fail Options validation at startup. A non-nullable string initialized to `string.Empty` does not itself validate configuration. Supply a development connection string when running EF tooling through WebApi DI.
- Check C# naming for public properties and configuration keys (`PostgreSql`, not `PostgreSQL`). Verify EF tooling through WebApi's DI path before considering a design-time factory; do not add a second configuration path without a demonstrated need.
