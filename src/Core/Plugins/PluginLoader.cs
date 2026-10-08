using System.Reflection;
using System.Runtime.Loader;

using DotNetGameServer;

using Google.Protobuf;

namespace Core.Plugins;

internal static class PluginLoader
{
    public static PluginSession Load(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("找不到插件程序集。", fullPath);

        var context = new CollectiblePluginLoadContext(fullPath);
        var assembly = context.LoadFromAssemblyPath(fullPath);
        var pluginType = FindPluginType(assembly);
        if (Activator.CreateInstance(pluginType) is not IServerPlugin plugin)
            throw new InvalidOperationException($"无法创建插件 {pluginType.FullName}。");

        return new PluginSession(plugin.Name, context, plugin);
    }

    private static Type FindPluginType(Assembly assembly)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types;
        }

        var matches = types
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                && typeof(IServerPlugin).IsAssignableFrom(type))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"插件程序集必须且只能包含一个 {nameof(IServerPlugin)}。");
        return matches[0]!;
    }
}

internal sealed class PluginSession
{
    private readonly CollectiblePluginLoadContext _context;
    private readonly IServerPlugin _plugin;
    private IHandlerInvoker[] _installed = [];

    public PluginSession(string name, CollectiblePluginLoadContext context, IServerPlugin plugin)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "plugin" : name;
        _context = context;
        _plugin = plugin;
    }

    public string Name { get; }

    public void Apply(BaseGameServer server)
    {
        var catalog = new PluginCatalog();
        _plugin.Configure(catalog);
        _installed = catalog.Apply(server);
    }

    public async Task<bool> TryReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await Task.WhenAll(_installed.Select(invoker => invoker.WaitForIdleAsync(timeoutSource.Token)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        _context.Unload();
        return true;
    }
}

internal sealed class PluginCatalog : IPluginCatalog
{
    private readonly List<Func<BaseGameServer, IHandlerInvoker>> _maps = [];

    public void MapClient<TRequest, TResponse>(IMessageHandler<TRequest, TResponse> handler)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        ArgumentNullException.ThrowIfNull(handler);
        _maps.Add(server => server.ReplaceHandler(handler, CommunicationChannel.Client).Current);
    }

    public void MapInternal<TRequest, TResponse>(IMessageHandler<TRequest, TResponse> handler)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        ArgumentNullException.ThrowIfNull(handler);
        _maps.Add(server => server.ReplaceHandler(handler, CommunicationChannel.Internal).Current);
    }

    public IHandlerInvoker[] Apply(BaseGameServer server) =>
        _maps.Select(map => map(server)).ToArray();
}

internal sealed class CollectiblePluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public CollectiblePluginLoadContext(string assemblyPath)
        : base(Path.GetFileNameWithoutExtension(assemblyPath) + "-" + Guid.NewGuid().ToString("N"), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (Default.Assemblies.Any(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.Ordinal)))
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
