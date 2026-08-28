package dev.hmclce.runtime.dotnet;

import org.jackhuang.hmcl.plugin.Plugin;
import org.jackhuang.hmcl.plugin.PluginContext;
import org.jackhuang.hmcl.plugin.PluginManifest;
import org.jackhuang.hmcl.plugin.runtime.PluginPlatformTarget;
import org.jackhuang.hmcl.plugin.runtime.RuntimeProviderRegistration;
import org.jetbrains.annotations.NotNullByDefault;
import org.jetbrains.annotations.Nullable;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Objects;

/// Bootstraps the separately packaged isolated .NET Runtime Provider.
@NotNullByDefault
public final class DotNetRuntimeHostPlugin implements Plugin {
    /// Authoritative manifest captured during load.
    private @Nullable PluginManifest manifest;

    /// Active launcher-owned Provider registration.
    private @Nullable RuntimeProviderRegistration registration;

    /// Loads and registers the platform-specific .NET process Host.
    ///
    /// @param context launcher-owned Host plugin context
    @Override
    public synchronized void onLoad(PluginContext context) {
        if (registration != null) {
            throw new IllegalStateException("Aura .NET Runtime Host is already loaded");
        }
        PluginManifest loadedManifest = context.getManifest();
        Path executable = resolveExecutable(context.getPackageDirectory(), PluginPlatformTarget.current());
        if (!Files.isRegularFile(executable)) {
            throw new UncheckedIOException(new IOException(".NET process Host is missing: " + executable));
        }
        DotNetRuntimeProvider provider = new DotNetRuntimeProvider(
                loadedManifest.getId(),
                loadedManifest.getVersion(),
                loadedManifest.getProvidesRuntimes(),
                executable,
                DotNetRuntimeProvider.AuraSession::start
        );
        registration = context.registerRuntimeProvider(provider);
        manifest = loadedManifest;
    }

    /// Leaves Provider activation to Aura's Runtime Supervisor.
    @Override
    public void onEnable() {
    }

    /// Leaves dependent payload shutdown to Aura's Runtime Supervisor.
    @Override
    public void onDisable() {
    }

    /// Unregisters the Provider and closes all remaining payload processes.
    @Override
    public synchronized void onUnload() {
        @Nullable RuntimeProviderRegistration closing = registration;
        registration = null;
        if (closing == null) {
            return;
        }
        try {
            closing.close();
        } catch (IOException exception) {
            throw new UncheckedIOException("Failed to unload the Aura .NET Runtime Host", exception);
        }
    }

    /// Returns the authoritative loaded Host manifest.
    ///
    /// @return Host package manifest
    @Override
    public PluginManifest getManifest() {
        return Objects.requireNonNull(manifest, "Aura .NET Runtime Host has not loaded");
    }

    /// Resolves the exact process Host path for one launcher platform.
    ///
    /// @param packageRoot extracted Host package root
    /// @param platform exact Aura platform target
    /// @return normalized platform executable path
    static Path resolveExecutable(Path packageRoot, PluginPlatformTarget platform) {
        String name = platform.getOperatingSystem().equals("windows")
                ? "aura-dotnet-runtime-host.exe"
                : "aura-dotnet-runtime-host";
        return packageRoot.resolve("native").resolve(platform.getId()).resolve(name).normalize();
    }
}
