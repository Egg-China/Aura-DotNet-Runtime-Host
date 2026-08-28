using Aura.Runtime.DotNet.Host;
using Xunit;

namespace Aura.Runtime.DotNet.Tests;

public sealed class ProcessServerTests
{
    [Fact]
    public async Task ServesCompletePayloadLifecycle()
    {
        var packageRoot = FixturePackage.Create();
        await using var input = new MemoryStream();
        await WriteAsync(input, 1, "hello", []);
        await WriteAsync(input, 3, "load",
        [
            new("packageRoot", AuraValue.FromString(packageRoot)),
            new("entrypoint", AuraValue.FromString("aura-dotnet.json")),
            new("pluginId", AuraValue.FromInteger(41)),
            new("session", AuraValue.FromInteger(43)),
        ]);
        await WriteAsync(input, 5, "enable", []);
        await WriteAsync(input, 7, "invoke",
        [
            new("operation", AuraValue.FromString("echo")),
            new("input", AuraValue.FromBytes(AuraValueCodec.Encode(AuraValue.FromString("round-trip")))),
            new("callbackId", AuraValue.FromInteger(11)),
        ]);
        await WriteAsync(input, 9, "disable", []);
        await WriteAsync(input, 11, "shutdown", []);
        input.Position = 0;
        await using var output = new MemoryStream();

        await new ProcessServer(input, output).ServeAsync(CancellationToken.None);

        output.Position = 0;
        Assert.Equal("ok", (await ReadAsync(output)).Kind);
        Assert.Equal("ok", (await ReadAsync(output)).Kind);
        Assert.Equal("ok", (await ReadAsync(output)).Kind);
        var result = await ReadAsync(output);
        Assert.Equal("result", result.Kind);
        var encoded = ProtocolMessageCodec.Field(result.Payload.AsMap(), "output").AsBytes();
        Assert.Equal("round-trip", AuraValueCodec.Decode(encoded).AsString());
        Assert.Equal("ok", (await ReadAsync(output)).Kind);
        Assert.Equal("ok", (await ReadAsync(output)).Kind);
        Assert.Null(await ProtocolMessageCodec.ReadAsync(output, CancellationToken.None));
    }

    [Fact]
    public async Task InvalidLifecycleReturnsStableErrorAndContinues()
    {
        await using var input = new MemoryStream();
        await WriteAsync(input, 1, "enable", []);
        input.Position = 0;
        await using var output = new MemoryStream();

        await new ProcessServer(input, output).ServeAsync(CancellationToken.None);

        output.Position = 0;
        var response = await ReadAsync(output);
        Assert.Equal("error", response.Kind);
        Assert.Equal("invalid-state", ProtocolMessageCodec.Field(response.Payload.AsMap(), "code").AsString());
    }

    [Fact]
    public async Task ServicesBridgeCallbackDuringInvoke()
    {
        var packageRoot = FixturePackage.Create();
        var payloadInput = AuraValueCodec.Encode(AuraValue.FromString("to-launcher"));
        var launcherOutput = AuraValueCodec.Encode(AuraValue.FromString("from-launcher"));
        await using var input = new MemoryStream();
        await WriteAsync(input, 1, "hello", []);
        await WriteAsync(input, 3, "load",
        [
            new("packageRoot", AuraValue.FromString(packageRoot)),
            new("entrypoint", AuraValue.FromString("aura-dotnet.json")),
            new("pluginId", AuraValue.FromInteger(41)),
            new("session", AuraValue.FromInteger(43)),
        ]);
        await WriteAsync(input, 5, "enable", []);
        await WriteAsync(input, 7, "invoke",
        [
            new("operation", AuraValue.FromString("bridge")),
            new("input", AuraValue.FromBytes(payloadInput)),
            new("callbackId", AuraValue.FromInteger(0)),
        ]);
        await WriteAsync(input, 2, "callback-result",
        [
            new("output", AuraValue.FromBytes(launcherOutput)),
        ]);
        await WriteAsync(input, 9, "disable", []);
        await WriteAsync(input, 11, "shutdown", []);
        input.Position = 0;
        await using var output = new MemoryStream();

        await new ProcessServer(input, output).ServeAsync(CancellationToken.None);

        output.Position = 0;
        _ = await ReadAsync(output);
        _ = await ReadAsync(output);
        _ = await ReadAsync(output);
        var callback = await ReadAsync(output);
        Assert.Equal(2, callback.RequestId);
        Assert.Equal("bridge-invoke", callback.Kind);
        Assert.Equal("fixture.bridge", ProtocolMessageCodec.Field(callback.Payload.AsMap(), "operation").AsString());
        Assert.Equal(payloadInput, ProtocolMessageCodec.Field(callback.Payload.AsMap(), "input").AsBytes());
        var result = await ReadAsync(output);
        Assert.Equal("from-launcher", AuraValueCodec.Decode(
            ProtocolMessageCodec.Field(result.Payload.AsMap(), "output").AsBytes()).AsString());
    }

    [Fact]
    public async Task MismatchedBridgeCallbackIdTerminatesProtocol()
    {
        var packageRoot = FixturePackage.Create();
        await using var input = new MemoryStream();
        await WriteAsync(input, 1, "hello", []);
        await WriteAsync(input, 3, "load",
        [
            new("packageRoot", AuraValue.FromString(packageRoot)),
            new("entrypoint", AuraValue.FromString("aura-dotnet.json")),
            new("pluginId", AuraValue.FromInteger(41)),
            new("session", AuraValue.FromInteger(43)),
        ]);
        await WriteAsync(input, 5, "enable", []);
        await WriteAsync(input, 7, "invoke",
        [
            new("operation", AuraValue.FromString("bridge")),
            new("input", AuraValue.FromBytes(AuraValueCodec.Encode(AuraValue.Null))),
            new("callbackId", AuraValue.FromInteger(0)),
        ]);
        await WriteAsync(input, 4, "callback-result",
        [
            new("output", AuraValue.FromBytes(AuraValueCodec.Encode(AuraValue.Null))),
        ]);
        input.Position = 0;
        await using var output = new MemoryStream();

        await Assert.ThrowsAsync<ProtocolViolationException>(async () =>
            await new ProcessServer(input, output).ServeAsync(CancellationToken.None));
    }

    private static async Task WriteAsync(
        Stream stream,
        long requestId,
        string kind,
        IReadOnlyList<KeyValuePair<string, AuraValue>> fields) =>
        await ProtocolMessageCodec.WriteAsync(
            stream,
            new ProtocolMessage(requestId, kind, AuraValue.FromMap(fields)),
            CancellationToken.None);

    private static async Task<ProtocolMessage> ReadAsync(Stream stream) =>
        Assert.IsType<ProtocolMessage>(await ProtocolMessageCodec.ReadAsync(stream, CancellationToken.None));

    private static class FixturePackage
    {
        public static string Create()
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var source = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "Fixtures", "Aura.Runtime.DotNet.Fixture", "bin", configuration, "net10.0",
                "Aura.Runtime.DotNet.Fixture.dll"));
            var directory = Path.Combine(Path.GetTempPath(), "aura-dotnet-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            File.Copy(source, Path.Combine(directory, "fixture.dll"));
            File.Copy(
                Path.ChangeExtension(source, ".deps.json"),
                Path.Combine(directory, "fixture.deps.json"));
            File.WriteAllText(
                Path.Combine(directory, "aura-dotnet.json"),
                """{"schemaVersion":1,"assembly":"fixture.dll","type":"Aura.Runtime.DotNet.Fixture.FixturePlugin"}""");
            return directory;
        }
    }
}
