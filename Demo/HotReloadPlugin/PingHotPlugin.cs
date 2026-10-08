using BasicServer.Protocol;

using Core;
using Core.Plugins;

namespace HotReloadPlugin;

/// <summary>
/// 改 Version 后重新生成到 BasicServer 的 plugins 目录，运行中的服务器会装入这份程序集。
/// </summary>
public sealed class PingHotPlugin : IServerPlugin
{
    public const string Version = "1";

    public string Name => "hot-reload-" + Version;

    public void Configure(IPluginCatalog catalog) =>
        catalog.MapClient<PingRequest, PingResponse>(new HotPingHandler());

    private sealed class HotPingHandler : IMessageHandler<PingRequest, PingResponse>
    {
        public Task<PingResponse?> HandleAsync(PingRequest request, IServerContext context) =>
            Task.FromResult<PingResponse?>(new PingResponse
            {
                Success = true,
                Code = 0,
                Message = $"hot:{Version}:{request.Message}",
                ServerTimeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
    }
}
