# Change persistence or compose a write workflow

**Use for:** `AppDbContext`, EF mappings, migrations, configuration, tracked
entities, `SaveChangesAsync()`, or a Handler that coordinates multiple writes.
Current implementation: one `AppDbContext`, scoped `AddDbContext`, Npgsql,
`ConnectionStringsOptions.PostgreSql`, and no production entity mappings yet.

## Place the change

| Change | Owner |
| --- | --- |
| DbContext or migrations | `src/Hydra.Infrastructure/Persistence/` |
| `IEntityTypeConfiguration<TEntity>` | `src/Hydra.Infrastructure/Persistence/Configurations/` |
| Provider setup and related Options | `src/Hydra.Infrastructure/Configuration/PersistenceConfiguration.cs` |
| Simple query or CRUD workflow | Application Handler; direct DbContext access is allowed |
| Reusable or complex persistence execution | Focused Infrastructure Operation |
| Atomicity across several Operations | Application Handler's explicit transaction |

## Write workflow decision

1. Identify which calls change the shared scoped context and which call
   `SaveChangesAsync()`. One save flushes **all pending tracked changes** in
   that context, including changes made by earlier Handler or Operation steps.
2. Decide whether partial commits are acceptable. If yes, independent saves
   can remain explicit. If no, the Handler begins one database transaction,
   invokes the steps, and commits after all succeed. A save within it does not
   commit the outer transaction. Handle rollback and failure paths explicitly.
3. Check EF execution strategy behavior before combining retries with a manual
   transaction. If a workflow calls an external system, recognize that a DB
   rollback cannot undo that call; design retry/idempotency only for the actual
   workflow that needs it.
4. For reads, use projection or `AsNoTracking()` when tracking is unnecessary.
   `Result<T>` does not clone or detach an entity. Passing a tracked entity
   within a shared-context write workflow can be intentional.

## Skip unless needed

- No repository, DbContext wrapper, global transaction pipeline, automatic
  transaction per Mediator request, `ITransaction`, or second DbContext by
  default.
- No `Persistence/PostgreSql/`, design-time factory, `Seeding/`, or migration
  project until a concrete need appears.
- Do not put EF mapping or database constraints solely in a Validator. A
  precheck cannot replace a concurrency-sensitive database constraint.

Read existing `GlobalUsings.cs`, DI entry points, and `appsettings.json` before
adding configuration. A new setting needs a visible credential-free key; the
Options convention binds `*Options` by type name and validates actual required
settings on startup. Test persistence against real PostgreSQL when provider
behavior or mapping correctness is the risk.

Kernel owns generic Options discovery. An assembly opts in once through
`AddOptionsFromAssembly(assembly)`; `EmailOptions` binds `Email`. Keep related
registration and Options types together and shared usings in `GlobalUsings.cs`.
Do not add per-class binding, section constants, or an `IConfiguration`
parameter when the convention handles the setting. WebApi supplies normal
configuration; Infrastructure owns provider setup and its central registration
remains thin. Validate only actual requirements. `ConnectionStringsOptions.PostgreSql`
uses `[Required]`, because a non-nullable string initialized to `string.Empty`
does not reject missing, empty, or whitespace-only configuration. Use the C#
name `PostgreSql`, and supply the development connection string when running EF
tooling through WebApi DI before considering a design-time factory.
