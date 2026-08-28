using Aura.Runtime.DotNet.Host;
using Xunit;

namespace Aura.Runtime.DotNet.Tests;

public sealed class PayloadAssemblyTests
{
    private static readonly string FixtureAssembly = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "Fixtures", "Aura.Runtime.DotNet.Fixture", "bin", "Debug", "net10.0",
        "Aura.Runtime.DotNet.Fixture.dll"));

    [Fact]
    public void LoadCreatesPluginInCollectibleContext()
    {
        using var loaded = PayloadAssembly.Load(new PayloadDescriptor(
            FixtureAssembly,
            "Aura.Runtime.DotNet.Fixture.FixturePlugin"));

        Assert.IsAssignableFrom<IAuraPluginV1>(loaded.Plugin);
        Assert.True(loaded.IsCollectible);
    }

    [Fact]
    public void LoadRejectsTypeThatDoesNotImplementPluginContract()
    {
        Assert.Throws<InvalidDataException>(() => PayloadAssembly.Load(new PayloadDescriptor(
            FixtureAssembly,
            "Aura.Runtime.DotNet.Fixture.NotPlugin")));
    }
}
