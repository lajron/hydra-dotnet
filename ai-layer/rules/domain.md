# Model a Domain concept

**Use for:** entities, value objects, invariants, state changes, business
calculations, and policies. **Owner:** `src/Hydra.Domain/<BusinessConcept>/`.
Domain may reference Kernel; it must not reference Application, Infrastructure,
EF Core provider code, or integration contracts owned by Infrastructure.

## Place and implement

1. Find the business concept that owns the rule. Use `Properties/Property.cs`
   as the shape of a future path, not as an existing class or required layout.
   A workflow spanning several concepts can apply a pure policy here while its
   Handler coordinates loading and persistence.
2. Put rules that must always hold in Domain behavior or construction. A
   Validator's input checks and a Handler's prechecks do not replace an invariant.
3. Keep related short types together when that helps navigation. Split a file
   when its responsibilities or consumers diverge. Add subfolders only after
   the concept actually grows.
4. Keep expected failure semantics clear to the caller. Domain need not own
   localized `AppError` resources. The invoking Operation or Handler can map an
   expected Domain outcome to the error owned by its business area.

## Boundary checks

- A `Property` is a physical/business concept; an advertisement or `Listing`
  can have its own price, photos, and publication state. Do not merge those
  merely because the first Feature touches both.
- A SaaS tenant is an access boundary, not automatically an agency or owner.
- Do not add aggregate, repository, factory, service, or `Common/` scaffolding
  for symmetry. Use `Common/` only for a real cross-domain concept.
- Test behavior and invalid transitions in the matching Domain test hierarchy
  when those types exist. Do not create a test project solely for a category.
