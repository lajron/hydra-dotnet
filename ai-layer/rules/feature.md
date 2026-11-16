# Implement an Application Feature

**Use for:** a Command or Query and its workflow. **Owner:**
`src/Hydra.Application/Features/<BusinessArea>/<Feature>/`. Add a module level
only when the business structure needs it. The repository has no production
Features yet, so inspect Mediator registration and existing tests for actual
request/response conventions before writing the first one.

## Build the smallest complete Feature

1. Name the business capability, such as `GetProperty` or `PublishListing`,
   then colocate its request, Handler, optional Validator, and any genuinely
   feature-specific response. Do not create all four files automatically.
2. Keep input shape checks in FluentValidation. Invoke Domain behavior for
   invariants; protect concurrency-sensitive facts with the execution path and
   database constraints where needed.
3. Put orchestration in the Handler: load state, call meaningful Operations or
   integrations, inspect Results, persist, and return the final Result. A simple
   read or CRUD Handler may use `AppDbContext` directly and project its response.
4. Decide whether each write can commit independently. If multiple database
   steps must be atomic, the Handler owns the explicit transaction. Read
   [persistence](persistence.md) before composing multiple saves.
5. Put code shared by several Features in the nearest owner: Domain rule,
   Infrastructure Operation, integration capability, or area-scoped
   `Common/`. Similar looking DTOs alone do not justify a shared contract.

```text
Features/Properties/
├── GetProperty/
│   ├── GetPropertyQuery.cs
│   └── GetPropertyHandler.cs
└── Common/                  # Only after real area-level reuse appears
    ├── Errors/
    └── Mappings/
```

**Review:** The Handler must not call another Handler directly or dispatch one
through Mediator for reuse. Do not create an Operation that just forwards a
query or integration call. Keep HTTP status mapping in WebApi, not the Handler.

For a complex `PublishListing` workflow, the Handler can load Domain state,
invoke `PrepareListingImagesOperation`, then invoke `PublishListingOperation`,
checking each Result. Neither Operation invokes the other. If both save to the
same database and all changes must succeed together, the Handler owns one
transaction. If an image upload is external, the database transaction cannot
undo it; handle that actual partial-failure case deliberately.
