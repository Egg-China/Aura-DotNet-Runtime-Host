# Aura .NET Runtime Host

Aura .NET Runtime Host is an optional schema-v5 runtime provider for Aura Launcher. Each .NET 10 payload runs in its own supervised process and collectible `AssemblyLoadContext`.

The compatibility plugin ID is `dev.hmclce.runtime.dotnet-host`. The Host supports Bridge ABI 1, Hooks, declarative
Patches, native payload dependencies, and isolated execution only. Runtime Patch callbacks use the canonical
`aura.patch.v1` Bridge Value v1 exchange; JVM capability tokens remain inside Aura Launcher.

## Requirements

- Aura Launcher 27.1 Next or newer
- A Host package matching Windows, Linux, or macOS on x64 or ARM64
- .NET 10 SDK for payload development; end users do not need a system .NET installation

## Development

```powershell
dotnet restore tests/Aura.Runtime.DotNet.Tests/Aura.Runtime.DotNet.Tests.csproj
dotnet test tests/Aura.Runtime.DotNet.Tests/Aura.Runtime.DotNet.Tests.csproj --no-restore
dotnet pack src/Aura.Runtime.DotNet.Abstractions/Aura.Runtime.DotNet.Abstractions.csproj -c Release
```

Java Provider tests compile against the exact Aura Next Shadow JAR selected by `AURA_JAR`.

## License

Aura .NET Runtime Host is licensed under GPL-3.0-or-later. Compatibility identifiers and upstream Aura/HMCL protocol identifiers are retained where required.
