using Aura.Runtime.DotNet;

namespace Aura.Runtime.DotNet.Host;

internal sealed class ProcessServer(Stream input, Stream output)
{
    private readonly Stream input = input ?? throw new ArgumentNullException(nameof(input));
    private readonly Stream output = output ?? throw new ArgumentNullException(nameof(output));
    private PayloadAssembly? payload;
    private ServerState state = ServerState.AwaitHello;

    public async Task ServeAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var request = await ProtocolMessageCodec.ReadAsync(input, cancellationToken).ConfigureAwait(false);
                if (request is null)
                {
                    await ClosePayloadAsync(CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                var (response, close) = await HandleAsync(request, cancellationToken).ConfigureAwait(false);
                await ProtocolMessageCodec.WriteAsync(output, response, cancellationToken).ConfigureAwait(false);
                if (close)
                {
                    return;
                }
            }
        }
        finally
        {
            payload?.Dispose();
            payload = null;
            state = ServerState.Closed;
        }
    }

    private async ValueTask<(ProtocolMessage Response, bool Close)> HandleAsync(
        ProtocolMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return request.Kind switch
            {
                "hello" => (Hello(request), false),
                "load" => (await LoadAsync(request, cancellationToken).ConfigureAwait(false), false),
                "enable" => (await EnableAsync(request, cancellationToken).ConfigureAwait(false), false),
                "invoke" => (await InvokeAsync(request, cancellationToken).ConfigureAwait(false), false),
                "disable" => (await DisableAsync(request, cancellationToken).ConfigureAwait(false), false),
                "shutdown" => (await ShutdownAsync(request, cancellationToken).ConfigureAwait(false), true),
                _ => throw Invalid("Child received a non-command message kind."),
            };
        }
        catch (Exception exception) when (exception is not ProtocolViolationException &&
            exception is InvalidDataException or InvalidOperationException or IOException or ArgumentException or TypeLoadException)
        {
            return (Error(request.RequestId, exception), false);
        }
    }

    private ProtocolMessage Hello(ProtocolMessage request)
    {
        RequireState(ServerState.AwaitHello);
        ProtocolMessageCodec.ExactMap(request.Payload);
        state = ServerState.AwaitLoad;
        return Ok(request.RequestId);
    }

    private async ValueTask<ProtocolMessage> LoadAsync(
        ProtocolMessage request,
        CancellationToken cancellationToken)
    {
        RequireState(ServerState.AwaitLoad);
        var fields = ProtocolMessageCodec.ExactMap(
            request.Payload, "packageRoot", "entrypoint", "pluginId", "session");
        var packageRoot = RequireString(fields, "packageRoot");
        var entrypoint = RequireString(fields, "entrypoint");
        var pluginId = RequirePositiveInteger(fields, "pluginId");
        _ = RequirePositiveInteger(fields, "session");
        var loaded = PayloadAssembly.Load(PayloadDescriptor.Load(packageRoot, entrypoint));
        try
        {
            await loaded.Plugin.LoadAsync(
                new PluginContext(
                    pluginId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new ProcessBridge(input, output)),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            loaded.Dispose();
            throw;
        }

        payload = loaded;
        state = ServerState.Loaded;
        return Ok(request.RequestId);
    }

    private async ValueTask<ProtocolMessage> EnableAsync(
        ProtocolMessage request,
        CancellationToken cancellationToken)
    {
        RequireState(ServerState.Loaded);
        ProtocolMessageCodec.ExactMap(request.Payload);
        await RequirePayload().Plugin.EnableAsync(cancellationToken).ConfigureAwait(false);
        state = ServerState.Enabled;
        return Ok(request.RequestId);
    }

    private async ValueTask<ProtocolMessage> InvokeAsync(
        ProtocolMessage request,
        CancellationToken cancellationToken)
    {
        RequireState(ServerState.Enabled);
        var fields = ProtocolMessageCodec.ExactMap(request.Payload, "operation", "input", "callbackId");
        var operation = RequireString(fields, "operation");
        var encodedInput = RequireBytes(fields, "input");
        var callbackId = RequireNonnegativeInteger(fields, "callbackId");
        var inputValue = AuraValueCodec.Decode(encodedInput);
        var result = await RequirePayload().Plugin.InvokeAsync(
            operation, inputValue, callbackId, cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(result);
        return new ProtocolMessage(request.RequestId, "result", AuraValue.FromMap([
            new("output", AuraValue.FromBytes(AuraValueCodec.Encode(result))),
        ]));
    }

    private async ValueTask<ProtocolMessage> DisableAsync(
        ProtocolMessage request,
        CancellationToken cancellationToken)
    {
        RequireState(ServerState.Enabled);
        ProtocolMessageCodec.ExactMap(request.Payload);
        await RequirePayload().Plugin.DisableAsync(cancellationToken).ConfigureAwait(false);
        state = ServerState.Disabled;
        return Ok(request.RequestId);
    }

    private async ValueTask<ProtocolMessage> ShutdownAsync(
        ProtocolMessage request,
        CancellationToken cancellationToken)
    {
        if (state is not (ServerState.Loaded or ServerState.Disabled))
        {
            throw InvalidState();
        }

        ProtocolMessageCodec.ExactMap(request.Payload);
        await ClosePayloadAsync(cancellationToken).ConfigureAwait(false);
        state = ServerState.Closed;
        return Ok(request.RequestId);
    }

    private async ValueTask ClosePayloadAsync(CancellationToken cancellationToken)
    {
        var loaded = payload;
        if (loaded is null)
        {
            return;
        }

        payload = null;
        try
        {
            await loaded.Plugin.UnloadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            loaded.Dispose();
        }
    }

    private PayloadAssembly RequirePayload() =>
        payload ?? throw new InvalidOperationException("Payload is not loaded.");

    private void RequireState(ServerState expected)
    {
        if (state != expected)
        {
            throw InvalidState();
        }
    }

    private InvalidOperationException InvalidState() =>
        new($"Operation is unavailable in state {state}.");

    private static ProtocolMessage Ok(long requestId) =>
        new(requestId, "ok", AuraValue.FromMap([]));

    private static ProtocolMessage Error(long requestId, Exception exception)
    {
        var message = exception.Message;
        if (System.Text.Encoding.UTF8.GetByteCount(message) > 4096)
        {
            message = TruncateUtf8(message, 4096);
        }

        var code = exception is InvalidOperationException ? "invalid-state" : "payload-error";
        return new ProtocolMessage(requestId, "error", AuraValue.FromMap([
            new("code", AuraValue.FromString(code)),
            new("message", AuraValue.FromString(string.IsNullOrWhiteSpace(message) ? "Payload operation failed." : message)),
        ]));
    }

    private static string RequireString(IReadOnlyList<KeyValuePair<string, AuraValue>> fields, string name)
    {
        var value = ProtocolMessageCodec.Field(fields, name);
        if (value.Kind != AuraValueKind.String || string.IsNullOrWhiteSpace(value.AsString()))
        {
            throw Invalid($"Protocol field {name} must be nonblank text.");
        }

        return value.AsString();
    }

    private static byte[] RequireBytes(IReadOnlyList<KeyValuePair<string, AuraValue>> fields, string name)
    {
        var value = ProtocolMessageCodec.Field(fields, name);
        if (value.Kind != AuraValueKind.Bytes)
        {
            throw Invalid($"Protocol field {name} must be bytes.");
        }

        return value.AsBytes();
    }

    private static long RequirePositiveInteger(
        IReadOnlyList<KeyValuePair<string, AuraValue>> fields,
        string name)
    {
        var value = RequireNonnegativeInteger(fields, name);
        return value > 0 ? value : throw Invalid($"Protocol field {name} must be positive.");
    }

    private static long RequireNonnegativeInteger(
        IReadOnlyList<KeyValuePair<string, AuraValue>> fields,
        string name)
    {
        var value = ProtocolMessageCodec.Field(fields, name);
        if (value.Kind != AuraValueKind.Integer || value.AsInteger() < 0)
        {
            throw Invalid($"Protocol field {name} must be a nonnegative integer.");
        }

        return value.AsInteger();
    }

    private static string TruncateUtf8(string value, int maximumBytes)
    {
        while (System.Text.Encoding.UTF8.GetByteCount(value) > maximumBytes)
        {
            value = value[..^1];
        }

        return value;
    }

    private static InvalidDataException Invalid(string message) => new(message);

    private sealed class PluginContext(string pluginId, IAuraBridgeV1 bridge) : IAuraPluginContext
    {
        public string PluginId { get; } = pluginId;

        public IAuraBridgeV1 Bridge { get; } = bridge;
    }

    private sealed class ProcessBridge(Stream input, Stream output) : IAuraBridgeV1
    {
        private readonly Stream input = input;
        private readonly Stream output = output;
        private long nextRequestId = 2;

        public async ValueTask<AuraValue> InvokeAsync(
            string operation,
            AuraValue input,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            ArgumentNullException.ThrowIfNull(input);
            var response = await ExchangeAsync(
                "bridge-invoke",
                AuraValue.FromMap([
                    new("operation", AuraValue.FromString(operation)),
                    new("input", AuraValue.FromBytes(AuraValueCodec.Encode(input))),
                ]),
                cancellationToken).ConfigureAwait(false);
            return AuraValueCodec.Decode(RequireCallbackOutput(response));
        }

        public async ValueTask RetainAsync(AuraHandle handle, CancellationToken cancellationToken = default)
        {
            await ExchangeHandleAsync("retain-handle", handle, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask ReleaseAsync(AuraHandle handle, CancellationToken cancellationToken = default)
        {
            await ExchangeHandleAsync("release-handle", handle, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask ExchangeHandleAsync(
            string kind,
            AuraHandle handle,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(handle);
            var response = await ExchangeAsync(
                kind,
                AuraValue.FromMap([
                    new("objectId", AuraValue.FromInteger(checked((long)handle.ObjectId))),
                    new("generation", AuraValue.FromInteger(checked((long)handle.Generation))),
                ]),
                cancellationToken).ConfigureAwait(false);
            if (RequireCallbackOutput(response).Length != 0)
            {
                throw Invalid("Handle callback result must be empty bytes.");
            }
        }

        private async ValueTask<ProtocolMessage> ExchangeAsync(
            string kind,
            AuraValue payload,
            CancellationToken cancellationToken)
        {
            var requestId = AllocateRequestId();
            await ProtocolMessageCodec.WriteAsync(
                output,
                new ProtocolMessage(requestId, kind, payload),
                cancellationToken).ConfigureAwait(false);
            var response = await ProtocolMessageCodec.ReadAsync(input, cancellationToken).ConfigureAwait(false) ??
                throw new IOException("Launcher closed the protocol during a Bridge callback.");
            if (response.RequestId != requestId)
            {
                throw new ProtocolViolationException("Bridge callback response has a mismatched request ID.");
            }

            return response;
        }

        private long AllocateRequestId()
        {
            var requestId = nextRequestId;
            if (requestId <= 0)
            {
                throw new IOException("Bridge callback request ID space is exhausted.");
            }

            nextRequestId = requestId == long.MaxValue - 1 ? 0 : requestId + 2;
            return requestId;
        }

        private static byte[] RequireCallbackOutput(ProtocolMessage response)
        {
            if (response.Kind == "callback-error")
            {
                var fields = ProtocolMessageCodec.ExactMap(response.Payload, "code");
                throw new IOException($"Launcher Bridge callback failed [{RequireString(fields, "code")}].");
            }

            if (response.Kind != "callback-result")
            {
                throw new ProtocolViolationException("Launcher returned an invalid Bridge callback response kind.");
            }

            var result = ProtocolMessageCodec.ExactMap(response.Payload, "output");
            return RequireBytes(result, "output");
        }
    }

    private enum ServerState
    {
        AwaitHello,
        AwaitLoad,
        Loaded,
        Enabled,
        Disabled,
        Closed,
    }
}

internal sealed class ProtocolViolationException(string message) : IOException(message);
