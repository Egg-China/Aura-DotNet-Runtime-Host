namespace Aura.Runtime.DotNet;

/// <summary>Asynchronous payload contract implemented by Aura .NET plugins.</summary>
public interface IAuraPluginV1
{
    /// <summary>Loads plugin-owned resources and captures the launcher Bridge.</summary>
    ValueTask LoadAsync(IAuraPluginContext context, CancellationToken cancellationToken);

    /// <summary>Enables the loaded plugin.</summary>
    ValueTask EnableAsync(CancellationToken cancellationToken);

    /// <summary>Invokes one generic operation.</summary>
    ValueTask<AuraValue> InvokeAsync(
        string operation,
        AuraValue input,
        long callbackId,
        CancellationToken cancellationToken);

    /// <summary>Disables the enabled plugin.</summary>
    ValueTask DisableAsync(CancellationToken cancellationToken);

    /// <summary>Unloads all plugin-owned resources.</summary>
    ValueTask UnloadAsync(CancellationToken cancellationToken);
}

/// <summary>Payload-scoped context that exposes no JVM capability token.</summary>
public interface IAuraPluginContext
{
    /// <summary>Stable payload plugin identifier.</summary>
    string PluginId { get; }

    /// <summary>Launcher-owned language-neutral Bridge.</summary>
    IAuraBridgeV1 Bridge { get; }
}

/// <summary>Language-neutral Bridge callback surface.</summary>
public interface IAuraBridgeV1
{
    /// <summary>Invokes one launcher Bridge operation.</summary>
    ValueTask<AuraValue> InvokeAsync(
        string operation,
        AuraValue input,
        CancellationToken cancellationToken = default);

    /// <summary>Retains one generation-safe launcher handle.</summary>
    ValueTask RetainAsync(AuraHandle handle, CancellationToken cancellationToken = default);

    /// <summary>Releases one generation-safe launcher handle.</summary>
    ValueTask ReleaseAsync(AuraHandle handle, CancellationToken cancellationToken = default);
}
