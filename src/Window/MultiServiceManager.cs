using System.Collections.Concurrent;

namespace Window;

public class MultiServiceManager
{
    public record ServiceInfo(ServiceType Type, string Name, int Port, GameServer Server, bool IsRunning);

    private readonly ConcurrentDictionary<ServiceType, ServiceInfo> _services = new();
    private readonly Action<string> _log;

    public MultiServiceManager(Action<string> log)
    {
        _log = log;
    }

    public ServiceInfo AddOrUpdate(ServiceType type, int port)
    {
        var name = type.ToString();
        var server = _services.TryGetValue(type, out var existing) ? existing.Server : new GameServer(name, _log);
        var info = new ServiceInfo(type, name, port, server, existing?.IsRunning ?? false);
        _services[type] = info;
        _log($"配置服务: {name} @ {port}");
        return info;
    }

    public bool Start(ServiceType type)
    {
        if (!_services.TryGetValue(type, out var info)) return false;
        if (info.IsRunning) return true;
        info.Server.Start(info.Port);
        _services[type] = info with { IsRunning = true };
        return true;
    }

    public bool Stop(ServiceType type)
    {
        if (!_services.TryGetValue(type, out var info)) return false;
        if (!info.IsRunning) return true;
        info.Server.Stop();
        _services[type] = info with { IsRunning = false };
        return true;
    }

    public ServiceInfo? Get(ServiceType type) => _services.TryGetValue(type, out var info) ? info : null;
    public IEnumerable<ServiceInfo> List() => _services.Values;
}