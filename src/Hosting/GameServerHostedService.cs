using Core;
using Core.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hosting;

/// <summary>
/// 通用托管服务：按角色定义创建并启动单个 <see cref="BaseGameServer"/> 实例。
/// </summary>
internal sealed class GameServerHostedService(
    ILogger<GameServerHostedService> logger,
    IConfiguration configuration,
    IServiceProvider serviceProvider,
    IHostEnvironment environment,
    CurrentServerAccessor accessor,
    IOptions<ServerOptions> serverOptions,
    IOptions<AuthenticationOptions> authenticationOptions,
    IOptions<ClientHeartbeatOptions> clientHeartbeatOptions,
    GameServerDefinition definition)
    : IHostedService
{
    private BaseGameServer? _server;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var port = configuration.GetValue<int?>("port") ?? definition.DefaultPort;

        if (environment.IsProduction() && definition.RequiresProductionTokenSecret)
            ValidateProductionConfiguration();

        var context = new HostServerContext
        {
            Services = serviceProvider,
            Configuration = configuration,
            ServerOptions = serverOptions.Value,
            AuthenticationOptions = authenticationOptions.Value,
            ClientHeartbeatOptions = clientHeartbeatOptions.Value,
            Port = port
        };

        _server = definition.CreateServer(context);
        await _server.StartAsync(port, cancellationToken);
        accessor.Set(_server);

        var listeningPort = _server.ListeningPort ?? port;
        try { Console.Title = $"{_server.ServerName} : {listeningPort}"; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Host] 设置控制台标题失败: {ex.Message}"); }

        logger.LogInformation("服务启动成功: {Role} 监听 {Port}", definition.Role, listeningPort);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server != null)
            await _server.StopAsync(cancellationToken);
        _server = null;
        try { accessor.Clear(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Host] 清除访问器失败: {ex.Message}"); }
    }

    private void ValidateProductionConfiguration()
    {
        var secret = authenticationOptions.Value.TokenSecret;
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32 ||
            secret.Equals("dev-secret", StringComparison.OrdinalIgnoreCase) ||
            secret.StartsWith("changeme", StringComparison.OrdinalIgnoreCase) ||
            secret.StartsWith("__", StringComparison.Ordinal))
            throw new InvalidOperationException("[Guard] 生产环境必须提供至少 32 字符且非占位值的 TokenSecret。");
    }
}
