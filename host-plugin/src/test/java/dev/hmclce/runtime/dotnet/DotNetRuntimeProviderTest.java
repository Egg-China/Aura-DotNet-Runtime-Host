package dev.hmclce.runtime.dotnet;

import org.jackhuang.hmcl.plugin.PluginArtifactIdentity;
import org.jackhuang.hmcl.plugin.runtime.PluginExecutionMode;
import org.jackhuang.hmcl.plugin.runtime.RuntimePayloadContext;
import org.jackhuang.hmcl.plugin.runtime.RuntimePayloadHandle;
import org.jackhuang.hmcl.plugin.runtime.RuntimeFeature;
import org.jackhuang.hmcl.plugin.runtime.RuntimeProviderDeclaration;
import org.jetbrains.annotations.NotNullByDefault;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.nio.file.Path;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

/// Verifies Provider ownership and delegation to one Aura process session per payload.
@NotNullByDefault
final class DotNetRuntimeProviderTest {
    /// Test-owned package and executable root.
    @TempDir
    Path temporaryDirectory;

    /// Drives the complete isolated lifecycle through one opaque handle.
    @Test
    void delegatesLifecycleAndRejectsForeignHandles() throws Exception {
        RecordingSession session = new RecordingSession();
        DotNetRuntimeProvider provider = new DotNetRuntimeProvider(
                "dev.hmclce.runtime.dotnet-host", "0.1.0-beta.1", List.of(new RuntimeProviderDeclaration(
                        "dotnet", Set.of(1), 1, Set.of(PluginExecutionMode.ISOLATED),
                        Set.of(RuntimeFeature.BRIDGE, RuntimeFeature.HOOKS, RuntimeFeature.NATIVE))),
                temporaryDirectory.resolve("host.exe"), (path, context) -> session);
        RuntimePayloadContext context = new RuntimePayloadContext(
                new PluginArtifactIdentity("dev.hmclce.test.dotnet", "1.0.0", "a".repeat(64)),
                temporaryDirectory, "aura-dotnet.json", PluginExecutionMode.ISOLATED,
                temporaryDirectory.resolve("data"), () -> { throw new AssertionError("token resolved"); });

        RuntimePayloadHandle handle = provider.loadPayload(context);
        provider.enablePayload(handle);
        assertArrayEquals(new byte[]{3, 2, 1}, provider.invokePayload(handle, "echo", new byte[]{1, 2, 3}, 7));
        provider.disablePayload(handle);
        provider.unloadPayload(handle);

        assertEquals(List.of("enable", "invoke:echo:7", "disable", "shutdown"), session.events);
        RuntimePayloadHandle foreign = new RuntimePayloadHandle("payload", "foreign", handle.payloadId());
        assertThrows(java.io.IOException.class, () -> provider.enablePayload(foreign));
    }

    /// Records the exact Provider-to-session operation order.
    @NotNullByDefault
    private static final class RecordingSession implements DotNetRuntimeProvider.Session {
        /// Ordered delegated calls.
        private final List<String> events = new ArrayList<>();

        /// Records enable.
        @Override public void enable() { events.add("enable"); }

        /// Records invocation and reverses its test bytes.
        @Override public byte[] invoke(String operation, byte[] input, long callbackId, Duration timeout) {
            events.add("invoke:" + operation + ":" + callbackId);
            return new byte[]{input[2], input[1], input[0]};
        }

        /// Records disable.
        @Override public void disable() { events.add("disable"); }

        /// Records graceful shutdown.
        @Override public void shutdown() { events.add("shutdown"); }

        /// Records fallback close only when used.
        @Override public void close() { events.add("close"); }
    }
}
