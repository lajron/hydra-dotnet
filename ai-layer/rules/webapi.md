# Add an HTTP entry point or host service

**Use for:** Controllers, routes, response mapping, localization, or WebApi
composition. **Owner:** `src/Hydra.WebApi/`. There are no production Controllers
yet. Inspect `Program.cs`, `Configuration/MediatorConfiguration.cs`, and
`Mappings/Response/ResultHttpExtensions.cs` before implementing the first one.

## Controller path

1. Put a Controller in `Controllers/`, initially flat. It can expose multiple
   related endpoints. Group Controllers by business module only when the folder
   becomes difficult to navigate; do not mirror each Application Feature.
2. Parse HTTP input and dispatch an Application request through the existing
   source-generated Mediator. Leave business decisions and transaction
   ownership in its Handler.
3. Map returned Results through the existing centralized HTTP extension and
   localized error handling. Do not reimplement status and error mapping in
   every Controller. A response extension is usually simpler than inheritance.
4. Keep HTTP-only concerns, such as authorization policy and route shape, at
   this boundary. The Application Feature should remain usable from a job or
   another entry point.

## Composition path

- WebApi owns final service composition, Mediator generation and registration,
  validator discovery, HTTP localization, and hosting concerns.
- Infrastructure owns persistence, Operation scanning, integration services,
  and their package registrations. Keep `InfrastructureConfiguration` thin.
- Do not move Mediator registration to Kernel or add a second configuration
  route for EF tooling without demonstrating a need.

**Skip:** Controller interfaces, one-endpoint-per-class infrastructure, Minimal
API endpoint hierarchies, a `BaseController` without shared behavior, and
business orchestration in Controllers. Preserve the existing Result and
localization paths when adding routes.
