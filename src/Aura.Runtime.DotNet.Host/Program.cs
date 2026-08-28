namespace Aura.Runtime.DotNet.Host;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || !string.Equals(args[0], "--stdio", StringComparison.Ordinal))
        {
            await Console.Error.WriteLineAsync("Usage: aura-dotnet-runtime-host --stdio").ConfigureAwait(false);
            return 2;
        }

        var protocolOutput = Console.OpenStandardOutput();
        Console.SetOut(Console.Error);
        try
        {
            await new ProcessServer(Console.OpenStandardInput(), protocolOutput)
                .ServeAsync(CancellationToken.None).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            await Console.Error.WriteLineAsync($"Aura .NET Runtime Host protocol failure: {exception.Message}")
                .ConfigureAwait(false);
            return 1;
        }
    }
}
