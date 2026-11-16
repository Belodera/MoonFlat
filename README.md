# MoonFlat

MoonFlat is a MoonSharp-derived Lua interpreter for .NET: a standalone Lua scripting library with a MoonSharp-compatible API, CLR interop, compact table storage, configurable VM stacks, and selected Luau-style iteration support. It keeps the `MoonSharp.Interpreter` namespace and assembly identity for existing integrations, with its own project and NuGet package identity.

## Included

- Lua parsing, execution, coroutines, and CLR interop inherited from MoonSharp.
- Compact table storage, configurable VM stack capacities, fixed-argument binding, and cached call metadata.
- An explicit fast representation for host-provided 3D vector tables.
- Selected Luau-style generalized table iteration. This is not full Luau language compatibility.
- Opt-in experimental VM optimizers. They remain off by default and are not part of the stable performance claims.
- A no-dependency regression runner and a small C# example.

## Try the example

```powershell
dotnet run --project examples/Basic/Basic.csproj
```

Expected output: `Hello, MoonFlat`.

Run the regression checks:

```powershell
dotnet run --project tests/MoonFlat.Regression/MoonFlat.Regression.csproj
```

Build the library:

```powershell
dotnet build src/MoonSharp.Interpreter/MoonFlat.csproj -c Release
```

Verify the packed NuGet package from a separate consumer project:

```powershell
dotnet pack src/MoonSharp.Interpreter/MoonFlat.csproj -c Release -o artifacts/packages
dotnet restore tests/MoonFlat.PackageSmoke/MoonFlat.PackageSmoke.csproj --source artifacts/packages
dotnet run --project tests/MoonFlat.PackageSmoke/MoonFlat.PackageSmoke.csproj -c Release --no-restore
```

The package metadata is recorded in [moonflat-release.json](moonflat-release.json); no package has been published. The assembly remains `MoonSharp.Interpreter` version `2.0.0.0` to retain its inherited identity. A clean frozen-source run on Cloudflare Containers with .NET SDK 9.0.110 built the library, passed all 12 regression checks, ran the example, and verified a separate consumer could restore and use the packed NuGet package. The library package targets `netstandard2.1`; the example and verification apps target .NET 9.

## Install after publication

```powershell
dotnet add package MoonFlat --version 0.1.0-alpha.1
```

## Compatibility and license

See [COMPATIBILITY.md](COMPATIBILITY.md) for retained features and known gaps, and [UPSTREAMS.md](UPSTREAMS.md) for lineage and third-party notices. The inherited [LICENSE](LICENSE) is included in built packages.


