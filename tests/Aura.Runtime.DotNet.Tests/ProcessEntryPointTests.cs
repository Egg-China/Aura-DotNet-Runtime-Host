using System.Diagnostics;
using Aura.Runtime.DotNet.Host;
using Xunit;

namespace Aura.Runtime.DotNet.Tests;

public sealed class ProcessEntryPointTests
{
    [Fact]
    public async Task StdioModeKeepsPayloadLogsOutOfProtocolStream()
    {
        var host = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Aura.Runtime.DotNet.Host",
            "bin", "Debug", "net10.0", "aura-dotnet-runtime-host.dll"));
        using var process = Process.Start(new ProcessStartInfo("dotnet", $"\"{host}\" --stdio")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var packageRoot = CreateFixturePackage();
        await WriteAsync(process.StandardInput.BaseStream, 1, "hello", []);
        await WriteAsync(process.StandardInput.BaseStream, 3, "load",
        [
            new("packageRoot", AuraValue.FromString(packageRoot)),
            new("entrypoint", AuraValue.FromString("aura-dotnet.json")),
            new("pluginId", AuraValue.FromInteger(41)),
            new("session", AuraValue.FromInteger(43)),
        ]);
        await WriteAsync(process.StandardInput.BaseStream, 5, "enable", []);
        await WriteAsync(process.StandardInput.BaseStream, 7, "invoke",
        [
            new("operation", AuraValue.FromString("log")),
            new("input", AuraValue.FromBytes(AuraValueCodec.Encode(AuraValue.Null))),
            new("callbackId", AuraValue.FromInteger(0)),
        ]);
        await WriteAsync(process.StandardInput.BaseStream, 9, "disable", []);
        await WriteAsync(process.StandardInput.BaseStream, 11, "shutdown", []);
        process.StandardInput.Close();

        for (var index = 0; index < 6; index++)
        {
            Assert.NotNull(await ProtocolMessageCodec.ReadAsync(process.StandardOutput.BaseStream, timeout.Token));
        }

        Assert.Null(await ProtocolMessageCodec.ReadAsync(process.StandardOutput.BaseStream, timeout.Token));
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, process.ExitCode);
        Assert.Contains("fixture stdout log", await process.StandardError.ReadToEndAsync(timeout.Token));
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

    private static string CreateFixturePackage()
    {
        var source = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "Fixtures", "Aura.Runtime.DotNet.Fixture", "bin", "Debug", "net10.0",
            "Aura.Runtime.DotNet.Fixture.dll"));
        var directory = Path.Combine(Path.GetTempPath(), "aura-dotnet-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.Copy(source, Path.Combine(directory, "fixture.dll"));
        File.Copy(Path.ChangeExtension(source, ".deps.json"), Path.Combine(directory, "fixture.deps.json"));
        File.WriteAllText(
            Path.Combine(directory, "aura-dotnet.json"),
            """{"schemaVersion":1,"assembly":"fixture.dll","type":"Aura.Runtime.DotNet.Fixture.FixturePlugin"}""");
        return directory;
    }
}
