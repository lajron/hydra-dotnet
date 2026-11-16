# Add or extend an integration

**Use for:** an external provider, protocol, or reusable technical capability.
**Owner:** `src/Hydra.Infrastructure/Integrations/<Capability>/`. Application
uses the integration's public contract; Domain never depends on that contract.

## Choose the smallest useful boundary

1. Define what the capability provides to consumers, such as file storage or
   fiscal receipt submission. Keep necessary public input/output models beside
   the contract. Translate Domain facts into these models at the consuming
   boundary; keep provider wire formats internal.
2. For one small provider, start with a contract only if it gives a meaningful
   boundary, one implementation, and registration. Add `Clients/`, `Providers/`,
   `Operations/`, or a facade when actual responsibilities make navigation
   easier. A facade coordinates real internal behavior; it does not simply
   forward every client method.
3. Keep provider protocol, authentication, serialization, response mapping,
   settings, and DI registration inside the integration. The Application owns
   business choices: when to call it, which data matters, and what to do on an
   expected failure.
4. If several implementations of one contract coexist, register them explicitly
   with native .NET Keyed Services. Inject known keys in consumers. Add a small
   resolver only for real runtime selection; do not spread `IServiceProvider`
   calls through Handlers or force one global provider setting.
5. Add an Application-owned port or adapter only when it expresses a different
   business meaning or substantial translation. A duplicate forwarding
   interface is not a boundary worth maintaining.

```text
Integrations/Storage/
├── IFileStorage.cs
├── Providers/                 # Only when multiple providers need grouping
│   ├── AzureBlobStorage.cs
│   └── LocalFileStorage.cs
└── Configuration/
    └── StorageConfiguration.cs
```

**Review:** Integration-internal Operations also never call other Operations.
Package registration belongs to the integration when it is substantial;
Infrastructure's central registration composes it. Read [errors](errors.md)
for failure ownership and [persistence](persistence.md) if it writes application
state. An EF transaction cannot roll back an external call.

For a larger fiscalization subsystem, keep a root `IFiscalization` facade and
its implementation together. Add `Contracts/` for consumer models, `Clients/`
for protocol communication, `Providers/` for concrete devices, `Operations/`
for substantial internal submission or recovery steps, and scoped `Common/`
for shared errors or mappings. Create only the subdivisions the subsystem
actually uses. A fiscalization client should not decide Reception or Restaurant
workflow policy.

An Application-owned adapter is useful when a generic identity registry result
must become a business-specific guest eligibility decision. That adapter has
new semantics and can belong to the Application area. An interface that merely
renames `IFileStorage.SaveAsync` as `IApplicationStorage.SaveAsync` should be
skipped.
