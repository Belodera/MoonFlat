# Compatibility notes

## Identity and scope

- The library assembly is named `MoonSharp.Interpreter`; the namespace remains `MoonSharp.Interpreter` for existing integrations.
- The standalone NuGet package identity is `MoonFlat`. A distinct package ID does not imply that every MoonSharp or Luau workload has identical behavior; verify the scripts and interop your application depends on.
- The library project targets `netstandard2.1`. Older .NET Standard, Xamarin, Unity, browser-WASM, and AOT support have not been independently verified in this extraction.

## MoonFlat features retained

- The inherited MoonSharp parser, VM, core libraries, coroutine handling, and CLR interop remain in the library.
- Compact storage for small and dense tables, promoting mixed or sparse data to ordered indexed storage.
- Configurable processor stack capacities.
- Fixed-argument binding and cached small argument-count markers.
- Direct generalized table iteration and selected `__iter` / legacy `__iterator` behavior. This is targeted Luau-style compatibility, not full Luau support.
- `table.freeze` and `table.isfrozen`.
- An explicit `Table.CreatePristineVector3` fast representation for hosts that expose vector values as tables. The implementation has no game-engine type dependency; ordinary tables are unaffected unless a host opts into this factory.
- Experimental numeric fusion, register-expression, numeric-loop, affine, memoization, and constructor-hint paths. They are controlled by `ScriptOptions.Research*`, default off, and are not part of the stable performance claims.

## Host integrations

- Interpolation and Luau conditional-expression lowering are host-side preprocessing features; they are not in this interpreter library.
- `ScriptOptions.CollectionPolicy` is a generic callback hook that hosts may use to control `collectgarbage` behavior; it does not depend on host-specific types.

## Verification state

The frozen standalone source passed a Cloudflare build with .NET SDK 9.0.110, all 12 regression checks, the Basic example, NuGet package-content checks, and a separate consumer restore-and-run check against the produced package. GitHub Actions repeats these checks on pushes and pull requests and attaches the package to a matching published GitHub release.

This verifies the package build and the listed behaviors on the tested toolchain. It does not claim full Luau compatibility or independently verify every runtime that implements .NET Standard 2.1, Unity configuration, AOT, or older Xamarin targets.
