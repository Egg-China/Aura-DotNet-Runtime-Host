# Isolated .NET Hook and Patch

This schema-v5 payload targets `net10.0` and is loaded by `dev.hmclce.runtime.dotnet-host`. Its `aura-dotnet.json` names the entry assembly and complete `IAuraPluginV1` type.

The example handles `hook.before-game-launch` and an `after` Patch for
`org.jackhuang.hmcl.util.io.FileUtils.getName(java.nio.file.Path)`. Both callbacks return their canonical
language-neutral `unchanged` result, so the example observes launcher behavior without modifying it.

Patch callbacks arrive through operation `aura.patch.v1`. Invocation-local handles are opaque and valid only while
Aura is processing that callback. The JVM capability token never crosses into the child process; Aura reauthorizes
every Hook, Patch, and Bridge callback against the original payload context.
