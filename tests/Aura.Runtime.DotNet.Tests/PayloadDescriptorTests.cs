using Aura.Runtime.DotNet.Host;
using Xunit;

namespace Aura.Runtime.DotNet.Tests;

public sealed class PayloadDescriptorTests : IDisposable
{
    private readonly string packageRoot = Path.Combine(Path.GetTempPath(), $"aura-dotnet-descriptor-{Guid.NewGuid():N}");

    public PayloadDescriptorTests()
    {
        Directory.CreateDirectory(Path.Combine(packageRoot, "lib"));
        File.WriteAllBytes(Path.Combine(packageRoot, "lib", "Plugin.dll"), [0x00]);
    }

    [Fact]
    public void LoadAcceptsExactSafeDescriptor()
    {
        WriteDescriptor("""
            {"schemaVersion":1,"assembly":"lib/Plugin.dll","type":"Example.Plugin"}
            """);

        var descriptor = PayloadDescriptor.Load(packageRoot, "aura-dotnet.json");

        Assert.Equal(Path.Combine(packageRoot, "lib", "Plugin.dll"), descriptor.AssemblyPath);
        Assert.Equal("Example.Plugin", descriptor.TypeName);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"assembly\":\"lib/Plugin.dll\",\"type\":\"Example.Plugin\"}")]
    [InlineData("{\"schemaVersion\":1,\"assembly\":\"../Plugin.dll\",\"type\":\"Example.Plugin\"}")]
    [InlineData("{\"schemaVersion\":1,\"assembly\":\"/Plugin.dll\",\"type\":\"Example.Plugin\"}")]
    [InlineData("{\"schemaVersion\":1,\"assembly\":\"lib/Plugin.exe\",\"type\":\"Example.Plugin\"}")]
    [InlineData("{\"schemaVersion\":1,\"assembly\":\"lib/Plugin.dll\",\"type\":\"Plugin\"}")]
    [InlineData("{\"schemaVersion\":1,\"assembly\":\"lib/Plugin.dll\",\"type\":\"Example.Plugin\",\"extra\":true}")]
    public void LoadRejectsUnsafeOrNonExactDescriptor(string json)
    {
        WriteDescriptor(json);

        Assert.Throws<InvalidDataException>(() => PayloadDescriptor.Load(packageRoot, "aura-dotnet.json"));
    }

    [Fact]
    public void LoadRejectsEntrypointOtherThanAuraDotNetJson()
    {
        WriteDescriptor("{\"schemaVersion\":1,\"assembly\":\"lib/Plugin.dll\",\"type\":\"Example.Plugin\"}");

        Assert.Throws<InvalidDataException>(() => PayloadDescriptor.Load(packageRoot, "other.json"));
    }

    public void Dispose()
    {
        Directory.Delete(packageRoot, true);
        GC.SuppressFinalize(this);
    }

    private void WriteDescriptor(string json)
    {
        File.WriteAllText(Path.Combine(packageRoot, "aura-dotnet.json"), json);
    }
}
