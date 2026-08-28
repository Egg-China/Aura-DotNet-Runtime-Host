using Aura.Runtime.DotNet.Host;
using Xunit;

namespace Aura.Runtime.DotNet.Tests;

public sealed class ProtocolCodecTests
{
    private static readonly byte[] HelloFrame =
    [
        0, 0, 0, 107, 146, 7, 221, 0, 0, 0, 4, 146, 219, 0, 0,
        0, 15, 112, 114, 111, 116, 111, 99, 111, 108, 86, 101, 114, 115, 105, 111, 110,
        146, 2, 211, 0, 0, 0, 0, 0, 0, 0, 1, 146, 219, 0, 0, 0,
        9, 114, 101, 113, 117, 101, 115, 116, 73, 100, 146, 2, 211, 0, 0, 0,
        0, 0, 0, 0, 7, 146, 219, 0, 0, 0, 4, 107, 105, 110, 100, 146,
        4, 219, 0, 0, 0, 5, 104, 101, 108, 108, 111, 146, 219, 0, 0,
        0, 7, 112, 97, 121, 108, 111, 97, 100, 146, 7, 221, 0, 0, 0, 0,
    ];

    [Fact]
    public async Task ReadsAuraHelloGoldenFrame()
    {
        await using var input = new MemoryStream(HelloFrame);

        var message = await ProtocolMessageCodec.ReadAsync(input, CancellationToken.None);

        Assert.NotNull(message);
        Assert.Equal(7, message.RequestId);
        Assert.Equal("hello", message.Kind);
        Assert.Empty(message.Payload.AsMap());
    }

    [Fact]
    public async Task WritesAuraHelloGoldenFrame()
    {
        await using var output = new MemoryStream();

        await ProtocolMessageCodec.WriteAsync(
            output,
            new ProtocolMessage(7, "hello", AuraValue.FromMap([])),
            CancellationToken.None);

        Assert.Equal(HelloFrame, output.ToArray());
    }

    [Fact]
    public async Task CleanEofReturnsNull()
    {
        await using var input = new MemoryStream();

        Assert.Null(await ProtocolMessageCodec.ReadAsync(input, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(InvalidFrames))]
    public async Task InvalidFrameIsRejected(byte[] frame)
    {
        await using var input = new MemoryStream(frame);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await ProtocolMessageCodec.ReadAsync(input, CancellationToken.None));
    }

    public static TheoryData<byte[]> InvalidFrames => new()
    {
        new byte[] { 0x00 },
        new byte[] { 0x00, 0x00, 0x00, 0x00 },
        new byte[] { 0x01, 0x00, 0x00, 0x01 },
    };
}
