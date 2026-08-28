# Isolated .NET Launch Hook

This schema-v5 payload targets `net10.0` and is loaded by `dev.hmclce.runtime.dotnet-host`. Its `aura-dotnet.json` names the entry assembly and complete `IAuraPluginV1` type.

The example handles `hook.before-game-launch` and returns the language-neutral `unchanged` result. The JVM capability token never crosses into the child process; Bridge calls are reauthorized by Aura against the original payload context.
