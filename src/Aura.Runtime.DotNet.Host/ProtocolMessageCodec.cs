using System.Buffers.Binary;
using Aura.Runtime.DotNet;

namespace Aura.Runtime.DotNet.Host;

internal sealed record ProtocolMessage
{
    private static readonly HashSet<string> KnownKinds = new(StringComparer.Ordinal)
    {
        "hello", "load", "enable", "invoke", "disable", "shutdown", "ok", "result", "error",
        "bridge-invoke", "retain-handle", "release-handle", "callback-result", "callback-error",
    };

    private static readonly HashSet<string> EvenKinds = new(StringComparer.Ordinal)
    {
        "bridge-invoke", "retain-handle", "release-handle", "callback-result", "callback-error",
    };

    public ProtocolMessage(long requestId, string kind, AuraValue payload)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(payload);
        if (!KnownKinds.Contains(kind) || payload.Kind != AuraValueKind.Map ||
            (requestId % 2 == 0) != EvenKinds.Contains(kind))
        {
            throw new ArgumentException("Protocol message kind, direction, or payload is invalid.");
        }

        RequestId = requestId;
        Kind = kind;
        Payload = payload;
    }

    public long RequestId { get; }

    public string Kind { get; }

    public AuraValue Payload { get; }
}

internal static class ProtocolMessageCodec
{
    public const int MaxFrameBytes = 16 * 1024 * 1024;

    public static async ValueTask<ProtocolMessage?> ReadAsync(Stream input, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        var first = await input.ReadAsync(header.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (first == 0)
        {
            return null;
        }

        await ReadExactlyAsync(input, header.AsMemory(1), "Protocol frame header is truncated.", cancellationToken)
            .ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length is 0 or > MaxFrameBytes)
        {
            throw Invalid("Protocol frame length is outside bounds.");
        }

        var body = new byte[length];
        await ReadExactlyAsync(input, body, "Protocol frame body is truncated.", cancellationToken)
            .ConfigureAwait(false);
        return Decode(body);
    }

    public static async ValueTask WriteAsync(
        Stream output,
        ProtocolMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var envelope = AuraValue.FromMap([
            new("protocolVersion", AuraValue.FromInteger(1)),
            new("requestId", AuraValue.FromInteger(message.RequestId)),
            new("kind", AuraValue.FromString(message.Kind)),
            new("payload", message.Payload),
        ]);
        var body = AuraValueCodec.Encode(envelope);
        if (body.Length is 0 or > MaxFrameBytes)
        {
            throw Invalid("Protocol frame length is outside bounds.");
        }

        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)body.Length);
        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ProtocolMessage Decode(byte[] body)
    {
        AuraValue envelope;
        try
        {
            envelope = AuraValueCodec.Decode(body);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            throw Invalid("Protocol body is not Bridge Value v1.", exception);
        }

        var fields = ExactMap(envelope, "protocolVersion", "requestId", "kind", "payload");
        var version = Field(fields, "protocolVersion");
        var requestId = Field(fields, "requestId");
        var kind = Field(fields, "kind");
        var payload = Field(fields, "payload");
        if (version.Kind != AuraValueKind.Integer || version.AsInteger() != 1 ||
            requestId.Kind != AuraValueKind.Integer || requestId.AsInteger() <= 0 ||
            kind.Kind != AuraValueKind.String || payload.Kind != AuraValueKind.Map)
        {
            throw Invalid("Protocol envelope contains an invalid field type or value.");
        }

        try
        {
            return new ProtocolMessage(requestId.AsInteger(), kind.AsString(), payload);
        }
        catch (ArgumentException exception)
        {
            throw Invalid("Protocol envelope direction or kind is invalid.", exception);
        }
    }

    internal static IReadOnlyList<KeyValuePair<string, AuraValue>> ExactMap(
        AuraValue value,
        params string[] expectedFields)
    {
        if (value.Kind != AuraValueKind.Map)
        {
            throw Invalid("Protocol payload must be a map.");
        }

        var fields = value.AsMap();
        if (fields.Count != expectedFields.Length ||
            expectedFields.Any(expected => fields.All(field => !string.Equals(field.Key, expected, StringComparison.Ordinal))))
        {
            throw Invalid("Protocol payload has unknown or missing fields.");
        }

        return fields;
    }

    internal static AuraValue Field(IReadOnlyList<KeyValuePair<string, AuraValue>> fields, string name) =>
        fields.Single(field => string.Equals(field.Key, name, StringComparison.Ordinal)).Value;

    private static async ValueTask ReadExactlyAsync(
        Stream input,
        Memory<byte> buffer,
        string truncatedMessage,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await input.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw Invalid(truncatedMessage);
            }

            offset += read;
        }
    }

    internal static InvalidDataException Invalid(string message, Exception? inner = null) => new(message, inner);
}
