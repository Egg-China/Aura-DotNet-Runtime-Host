using System.Reflection;
using System.Runtime.Loader;
using Aura.Runtime.DotNet;

namespace Aura.Runtime.DotNet.Host;

internal sealed class PayloadAssembly : IDisposable
{
    private readonly RestrictedLoadContext loadContext;
    private IAuraPluginV1? plugin;

    private PayloadAssembly(RestrictedLoadContext loadContext, IAuraPluginV1 plugin)
    {
        this.loadContext = loadContext;
        this.plugin = plugin;
    }

    public IAuraPluginV1 Plugin => plugin ?? throw new ObjectDisposedException(nameof(PayloadAssembly));

    public bool IsCollectible => loadContext.IsCollectible;

    public static PayloadAssembly Load(PayloadDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var context = new RestrictedLoadContext(descriptor.AssemblyPath);
        try
        {
            var assembly = context.LoadFromAssemblyPath(descriptor.AssemblyPath);
            var targetFramework = assembly.GetCustomAttributesData()
                .SingleOrDefault(attribute => attribute.AttributeType.FullName ==
                    "System.Runtime.Versioning.TargetFrameworkAttribute")?
                .ConstructorArguments.SingleOrDefault().Value as string;
            if (!string.Equals(targetFramework, ".NETCoreApp,Version=v10.0", StringComparison.Ordinal))
            {
                throw Invalid("Payload entry assembly must target net10.0.");
            }

            var type = assembly.GetType(descriptor.TypeName, throwOnError: false, ignoreCase: false);
            if (type is null || !type.IsClass || type.IsAbstract || !type.IsPublic ||
                !typeof(IAuraPluginV1).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
            {
                throw Invalid("Payload type must be a public concrete IAuraPluginV1 with a public parameterless constructor.");
            }

            IAuraPluginV1 instance;
            try
            {
                instance = (IAuraPluginV1)Activator.CreateInstance(type)!;
            }
            catch (Exception exception)
            {
                throw Invalid("Payload type construction failed.", exception);
            }

            return new PayloadAssembly(context, instance);
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    public void Dispose()
    {
        plugin = null;
        loadContext.Unload();
    }

    private static InvalidDataException Invalid(string message, Exception? inner = null) => new(message, inner);

    private sealed class RestrictedLoadContext : AssemblyLoadContext
    {
        private static readonly string ContractName = typeof(IAuraPluginV1).Assembly.GetName().Name!;
        private static readonly HashSet<string> PlatformAssemblies = CreatePlatformAssemblyNames();
        private readonly AssemblyDependencyResolver resolver;
        private readonly string allowedRoot;

        public RestrictedLoadContext(string entryAssemblyPath)
            : base($"Aura payload {Guid.NewGuid():N}", isCollectible: true)
        {
            resolver = new AssemblyDependencyResolver(entryAssemblyPath);
            allowedRoot = Path.GetDirectoryName(entryAssemblyPath)! + Path.DirectorySeparatorChar;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name ?? throw Invalid("Payload requested an unnamed assembly.");
            if (string.Equals(name, ContractName, StringComparison.Ordinal))
            {
                return typeof(IAuraPluginV1).Assembly;
            }

            if (PlatformAssemblies.Contains(name))
            {
                return null;
            }

            var path = resolver.ResolveAssemblyToPath(assemblyName);
            if (path is null)
            {
                throw new FileNotFoundException($"Payload dependency is not declared: {assemblyName.FullName}");
            }

            RequireContained(path);
            return LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (path is null)
            {
                throw new DllNotFoundException($"Payload native dependency is not declared: {unmanagedDllName}");
            }

            RequireContained(path);
            return LoadUnmanagedDllFromPath(path);
        }

        private void RequireContained(string candidate)
        {
            var fullPath = Path.GetFullPath(candidate);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!fullPath.StartsWith(allowedRoot, comparison))
            {
                throw Invalid("Payload dependency escapes the entry assembly directory.");
            }
        }

        private static HashSet<string> CreatePlatformAssemblyNames()
        {
            var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
            return new HashSet<string>(
                paths.Select(Path.GetFileNameWithoutExtension).OfType<string>(),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
