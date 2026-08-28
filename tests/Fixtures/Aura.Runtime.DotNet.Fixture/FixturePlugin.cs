using Aura.Runtime.DotNet;

namespace Aura.Runtime.DotNet.Fixture;

public sealed class FixturePlugin : IAuraPluginV1
{
    private IAuraPluginContext? context;

    public ValueTask LoadAsync(IAuraPluginContext context, CancellationToken cancellationToken)
    {
        this.context = context;
        return ValueTask.CompletedTask;
    }

    public ValueTask EnableAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask<AuraValue> InvokeAsync(
        string operation,
        AuraValue input,
        long callbackId,
        CancellationToken cancellationToken)
    {
        if (operation == "bridge")
        {
            return context!.Bridge.InvokeAsync("fixture.bridge", input, cancellationToken);
        }

        if (operation == "log")
        {
            Console.WriteLine("fixture stdout log");
        }

        return ValueTask.FromResult(input);
    }

    public ValueTask DisableAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask UnloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed class NotPlugin;
