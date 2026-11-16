# Own an error and its translation

**Use for:** `AppError` codes, localized messages, mapping failures across
layers, or shared mapping code. Error ownership follows the meaning of the
failure, not whichever class happens to detect it.

| Meaning | Place it |
| --- | --- |
| Business-area outcome shared by Features | `Hydra.Application/Features/<Area>/Common/Errors/` |
| Execution failure specific to an Infrastructure area | That area's scoped `Common/Errors/`, when shared |
| Provider or protocol failure | Owning integration, usually its scoped `Common/Errors/` |
| Domain invariant | Enforce in Domain; map an expected outcome at its caller |

## Before adding a code

1. Search existing codes in the owning area. Reuse a code only if it has the
   same meaning, not merely a similar message. Do not define the same failure
   again in both Application and Infrastructure.
2. Decide whether the consumer should see the original integration failure or
   a business outcome. Translate only when the semantics change. Expected
   failures use `Result`; unexpected technical exceptions normally propagate.
3. Put shared mappings near their owning area in `Common/Mappings/`. Similar
   fields do not automatically make two response contracts the same contract.
4. If the code is an enum, keep the resource path aligned with its namespace.
   `EnumErrorLocalizer` looks for `{EnumNamespace}.Resources.{EnumName}` in the
   enum's assembly. For an enum in
   `Hydra.Application.Features.Properties.Common.Errors`, place
   `PropertiesErrors.en.resx` and `PropertiesErrors.sr-Latn.resx` in the sibling
   `Resources/` folder. Resource keys match enum member names.

**Skip:** one error file and resource set per Feature, Operation, Domain method,
or entity. Do not introduce a separate localizer per package. Preserve WebApi's
central Result-to-HTTP mapping and test resource resolution when adding the
first resource in a new assembly or namespace.
