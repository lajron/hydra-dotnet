# Add a reusable Operation

**Use for:** a focused execution capability with reuse, technical complexity,
or a meaningful persistence/integration boundary. **Owner:**
`src/Hydra.Infrastructure/Operations/<BusinessArea>/<Capability>/`.
For a simple database query, first consider doing it directly in the Handler.

## Implement

1. Name the action (`CheckAvailability`, `PrepareListingImages`) rather than
   creating a broad `PropertyService` or a many-method repository.
2. Keep a public named interface beside the implementation when Application
   needs a contract. It inherits exactly one Kernel `IAsyncOperation<...>`
   contract. Keep the concrete implementation `internal` where possible and
   colocate necessary input/output types.
3. The existing `AddOperationServices` scanner discovers one implementation
   per named interface from the Infrastructure assembly and registers it as
   scoped. It rejects duplicate/conflicting registrations. Do not add a parallel
   manual registration or modify the scanner for integration provider selection.
4. An Operation **never depends on or executes another Operation**. A Handler
   orchestrates several Operations. Use ordinary private methods or internal
   technical helpers for steps inside one Operation, including integration work.
5. If the Operation calls `SaveChangesAsync()`, its API and caller must account
   for the shared DbContext tracker and any enclosing Handler transaction.
   Return a tracked entity only when continued tracking is intentional; use a
   projection for a read contract that does not need tracking.

```text
Operations/Properties/PrepareListingImages/
├── IPrepareListingImagesOperation.cs
├── PrepareListingImagesOperation.cs
└── PrepareListingImagesResult.cs       # Only if a distinct result is useful
```

**Review:** HYD002 catches direct dependencies between types implementing
Kernel Operation contracts. It cannot infer an Operation solely from a class
name or folder; review contractless integration Operations manually. Test the
execution behavior and the existing registration convention as appropriate.
