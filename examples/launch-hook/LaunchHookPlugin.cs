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
        if (!string.Equals(operation, "hook.before-game-launch", StringComparison.Ordinal) ||
            input.Kind != AuraValueKind.Map)
        {
            return ValueTask.FromResult(AuraValue.FromError(AuraErrorCode.InvalidArgument));
        }

        return ValueTask.FromResult(AuraValue.FromMap([
            new("contractVersion", AuraValue.FromInteger(1)),
            new("action", AuraValue.FromString("unchanged")),
        ]));
    }

    public ValueTask DisableAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask UnloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
