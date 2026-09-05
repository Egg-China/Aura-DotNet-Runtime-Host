using Aura.Runtime.DotNet;

namespace Aura.DotNet.LaunchHook;

public sealed class LaunchHookPlugin : IAuraPluginV1
{
    public ValueTask LoadAsync(IAuraPluginContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask EnableAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask<AuraValue> InvokeAsync(
        string operation,
        AuraValue input,
        long callbackId,
        CancellationToken cancellationToken)
    {
        if (input.Kind != AuraValueKind.Map)
        {
            return ValueTask.FromResult(AuraValue.FromError(AuraErrorCode.InvalidArgument));
        }

        if (string.Equals(operation, "hook.before-game-launch", StringComparison.Ordinal))
        {
            return ValueTask.FromResult(AuraValue.FromMap([
                new("contractVersion", AuraValue.FromInteger(1)),
                new("action", AuraValue.FromString("unchanged")),
            ]));
        }

        if (string.Equals(operation, "aura.patch.v1", StringComparison.Ordinal))
        {
            return ValueTask.FromResult(AuraValue.FromMap([
                new("schemaVersion", AuraValue.FromInteger(1)),
                new("action", AuraValue.FromString("unchanged")),
            ]));
        }

        return ValueTask.FromResult(AuraValue.FromError(AuraErrorCode.InvalidArgument));
    }

    public ValueTask DisableAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask UnloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
