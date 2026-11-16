# Author C# in this repository

**Use for every C# change.** [Full coding standards](../CODING_STANDARDS.md)
list the build diagnostics; this guide highlights choices tooling cannot make.

- Keep related short types in one file when they change together. Split a file
  for independent responsibilities, separate public APIs, or navigability;
  do not enforce one type per file.
- Use the smallest useful visibility. A capability can expose a public named
  contract and necessary input/output types while keeping its implementation
  and provider models `internal`. `internal` is assembly-level, not folder-level.
- Name a class for the capability it performs. Avoid `Helpers`, `Services`, or
  `Interfaces` as unowned global collections. An area-scoped `Common/` is fine
  for actual shared code.
- Let the existing IDE0290 diagnostic guide primary constructors that only
  assign fields. Use block bodies for meaningful logic even when a one-line
  expression would compile. Do not add `ConfigureAwait(false)` in application
  code. Use the `Async` suffix for `Task` methods except Mediator handlers,
  endpoints, and tests.
- Forward cancellation tokens, handle `Result` outcomes explicitly, and let
  unexpected technical exceptions reach the host boundary. RES001–003 and
  HYD001/HYD002 provide build feedback; do not suppress diagnostics or change
  severities to make a change pass.
- Inspect actual API names, `GlobalUsings.cs`, neighboring files, and tests.
  Do not fill illustrative trees with placeholder interfaces, DTOs, folders,
  adapters, or configuration classes.

The repository's `.editorconfig` and build are authoritative for mechanically
enforced style. Run scoped formatting only when needed, review its diff, and
finish with the required build and tests.
