using Core;
using Core.Components;
using Core.Configuration;
using Core.Heartbeat;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hosting;

/// <summary>
/// 为业务服务器（login/logic/battle）附加令牌密钥与心跳上报组件的通用助手。
/// </summary>
public static class HeartbeatRole
{
    public static void Attach(BaseGameServer server, string serverType, HostServerContext ctx)
    {
        var directoryOptions = ctx.Services.GetRequiredService<IOptions<DirectoryOptions>>().Value;
        var heartbeatOptions = ctx.Services.GetRequiredService<IOptions<HeartbeatOptions>>().Value;
        var logger = ctx.Services.GetRequiredService<ILoggerFactory>().CreateLogger($"Heartbeat.{serverType}");

        var tokenSecret = string.IsNullOrWhiteSpace(directoryOptions.TokenSecret)
            ? "dev-secret"
            : directoryOptions.TokenSecret;

        server.AddComponent<ITokenSecretProvider>(new TokenSecretProviderComponent(tokenSecret));

        heartbeatOptions.ServerType = serverType;
        heartbeatOptions.Port = ctx.Port;
        heartbeatOptions.DirectoryPort = ctx.Configuration.GetValue<int?>("directory:registry:port")
            ?? ctx.Configuration.GetValue<int?>("heartbeat:directoryPort")
            ?? heartbeatOptions.DirectoryPort;

        server.AddComponent(new HeartbeatSenderService(heartbeatOptions, tokenSecret, logger));
    }
}
