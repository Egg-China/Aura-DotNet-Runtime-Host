using System.Text.Json;

namespace Aura.Runtime.DotNet.Host;

internal sealed record PayloadDescriptor(string AssemblyPath, string TypeName)
{
    private static readonly HashSet<string> ExactFields =
        new(["schemaVersion", "assembly", "type"], StringComparer.Ordinal);

    public static PayloadDescriptor Load(string packageRoot, string entrypoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        if (!string.Equals(entrypoint, "aura-dotnet.json", StringComparison.Ordinal))
        {
            throw Invalid(".NET payload entrypoint must be exactly aura-dotnet.json.");
        }

        var root = Path.GetFullPath(packageRoot);
        var descriptorPath = Path.Combine(root, entrypoint);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllBytes(descriptorPath), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw Invalid("Unable to read aura-dotnet.json.", exception);
        }

        using (document)
        {
            var json = document.RootElement;
            if (json.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("aura-dotnet.json must be an object.");
            }

            var properties = json.EnumerateObject().ToArray();
            if (properties.Length != ExactFields.Count ||
                properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != ExactFields.Count ||
                properties.Any(property => !ExactFields.Contains(property.Name)))
            {
                throw Invalid("aura-dotnet.json has unknown, missing, or duplicate fields.");
            }

            if (!json.TryGetProperty("schemaVersion", out var schema) ||
                schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out var schemaVersion) ||
                schemaVersion != 1)
            {
                throw Invalid("aura-dotnet.json schemaVersion must be 1.");
            }

            var assembly = RequiredString(json, "assembly");
            var typeName = RequiredString(json, "type");
            ValidateRelativeAssembly(assembly);
            ValidateTypeName(typeName);

            var assemblyPath = Path.GetFullPath(Path.Combine(root, assembly.Replace('/', Path.DirectorySeparatorChar)));
            var rootedPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (!assemblyPath.StartsWith(rootedPrefix, PathComparison()) || !File.Exists(assemblyPath))
            {
                throw Invalid("Payload assembly is missing or escapes the package root.");
            }

            return new PayloadDescriptor(assemblyPath, typeName);
        }
    }

    private static string RequiredString(JsonElement json, string name)
    {
        var element = json.GetProperty(name);
        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"aura-dotnet.json {name} must be a string.");
        }

        var value = element.GetString()!;
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw Invalid($"aura-dotnet.json {name} must be nonblank canonical text.");
        }

        return value;
    }

    private static void ValidateRelativeAssembly(string assembly)
    {
        if (Path.IsPathFullyQualified(assembly) || assembly.Contains('\\', StringComparison.Ordinal) ||
            !assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("Payload assembly must be a safe relative DLL path.");
        }

        var segments = assembly.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." ||
            segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw Invalid("Payload assembly must be a normalized relative path.");
        }
    }

    private static void ValidateTypeName(string typeName)
    {
        if (!typeName.Contains('.', StringComparison.Ordinal) || typeName.Contains(',', StringComparison.Ordinal) ||
            typeName.Any(char.IsWhiteSpace) || typeName.Split('.', '+').Any(segment =>
                segment.Length == 0 || !(char.IsLetter(segment[0]) || segment[0] == '_') ||
                segment.Skip(1).Any(character => !(char.IsLetterOrDigit(character) || character is '_' or '`'))))
        {
            throw Invalid("Payload type must be a complete non-assembly-qualified type name.");
        }
    }

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static InvalidDataException Invalid(string message, Exception? inner = null) => new(message, inner);
}
