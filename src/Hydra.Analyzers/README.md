# Hydra Result analyzers

`Hydra.Analyzers` is a `netstandard2.0` compiler analyzer. It has no runtime reference to `Hydra.Kernel`. `Directory.Build.targets` loads it as a compiler analyzer in Kernel, Web API, Sample, and future projects under this repository. The analyzer project excludes itself; the analyzer tests project keeps its ordinary reference for the test harness. The tests use the real Kernel assembly and C# 14 compilation.

## Diagnostics

| ID | Meaning | Typical fix |
| --- | --- | --- |
| RES001 | A Result outcome is ignored, overwritten, or lost. | Check the error, propagate the Result, or explicitly discard an already awaited Result with `_ =`. |
| RES002 | A deconstructed value can be used while its Result might have failed. | Handle the matching error or assign a replacement first. |
| RES003 | The Result state cannot be proven safe at this operation. | Propagate failure, recover, or simplify the flow. |
| HYD001 | An expression-bodied member's expression spans several lines (a multi-line ternary or LINQ chain). | Use a block body, or keep the expression on one line. One expression that starts on the line after `=>` is accepted. |

HYD001 is a coding-standard rule, not a Result rule; it is reported as a warning in `.editorconfig`. See `docs/CODING_STANDARDS.md`.

The RES diagnostics are errors in `.editorconfig`. `_ = result` and `_ = await operation()` intentionally discard a Result. `_ = operation()` is rejected when `operation()` returns `Task<Result>` or `ValueTask<Result>` because it drops the task before awaiting it. Ordinary tasks remain the compiler's [CS4014](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/async-await-errors) concern.

## Supported flow

The analyzer tracks each deconstructed value with its own error symbol. It analyzes blocks, `if`/`else`, negated null guards, short-circuit `&&`/`||`, switches, conditional returns, early returns, throws, and replacement assignments. It catches reads in method calls, aliases, member access, arrays, interpolated strings, and lambda captures. It tracks local Result aliases and overwrites. Success with a nullable value is allowed; C# nullable analysis still applies to that value.

## Current boundary

This is not an interprocedural proof of arbitrary C#. Result work inside loops, `try`/`catch`/`finally`, `goto`, or other unmodeled statements receives RES003. A safe program using one of those constructs can therefore be rejected until that construct is modeled. `ref`/`out` mutation of an extracted value or error receives RES003. The analyzer does not infer that an arbitrary method handles a Result passed to it. It may reject such a transfer.

Field and property Result initializers are diagnosed because their outcomes can cross method boundaries. The current analysis still does not cover every possible transfer through complex object graphs or dynamic calls. It also cannot prove that a replacement produced by arbitrary application code is semantically valid. Nullability warnings and ordinary code review still matter. Do not treat these diagnostics as a complete safety proof yet.

## Build and inspect

```bash
dotnet build Hydra.slnx -m:1
dotnet test --project tests/Hydra.Analyzers.Tests/Hydra.Analyzers.Tests.csproj --no-build
```

`global.json` selects Microsoft Testing Platform for xUnit v3 on .NET 10. The test executable can also run directly with `dotnet run --project tests/Hydra.Analyzers.Tests/Hydra.Analyzers.Tests.csproj`.

The test project retains `Microsoft.NET.Test.Sdk` because Rider 2025.3 has a known Microsoft Testing Platform v2 discovery issue without it; JetBrains documents that package as a workaround. Rider may also require **Settings → Build, Execution, Deployment → Unit Testing → Testing Platform → Enable Testing Platform support**.

The sample builds normally. To trigger intentional build errors:

```bash
dotnet build samples/Hydra.Analyzers.Sample/Hydra.Analyzers.Sample.csproj -p:DefineConstants=HYDRA_DIAGNOSTIC_DEMO
dotnet build src/Hydra.Kernel/Hydra.Kernel.csproj -p:DefineConstants=HYDRA_DIAGNOSTIC_DEMO
```

In Rider, enable `HYDRA_DIAGNOSTIC_DEMO` in the Sample or Kernel project to inspect the diagnostics in `Examples.cs` or `RSA_ResultTest.cs`. Both files are inactive in normal builds. The command-line build has been verified; Rider's editor display needs a manual check in Rider.
