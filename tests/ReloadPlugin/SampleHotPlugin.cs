using Core;
using Core.Plugins;

using DotNetGameServer;

namespace ReloadPlugin;

public sealed class SampleHotPlugin : IServerPlugin
{
    public string Name => "sample-hot-plugin";

    public void Configure(IPluginCatalog catalog) =>
        catalog.MapClient<ClientHeartbeatRequest, ClientHeartbeatResponse>(new PluginHeartbeatHandler());

    private sealed class PluginHeartbeatHandler : IMessageHandler<ClientHeartbeatRequest, ClientHeartbeatResponse>
    {
        public Task<ClientHeartbeatResponse?> HandleAsync(
            ClientHeartbeatRequest request,
            IServerContext context) =>
            Task.FromResult<ClientHeartbeatResponse?>(new ClientHeartbeatResponse
            {
                Success = true,
                Code = 0,
                Message = "plugin"
            });
    }
}
