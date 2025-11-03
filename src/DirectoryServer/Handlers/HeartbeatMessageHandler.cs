using Core;
using DotNetGameServer;
using Google.Protobuf;
using DirectoryServer.Components;
using Shared;
using Core.Config;

namespace DirectoryServer.Handlers;

public class HeartbeatMessageHandler : IStrongMessageHandler<HeartbeatRequest, HeartbeatResponse>
{
    public Task<HeartbeatResponse?> HandleAsync(HeartbeatRequest req, IServerContext context)
    {
        var metrics = context.GetComponent<DirectoryServer.Components.DirectoryMetricsComponent>();
        try
        {
            // 校验心跳签名令牌
            var token = context.AccessToken ?? string.Empty;
            var secret = SimpleConfigLoader.LoadDirectoryTokenSecret(AppContext.BaseDirectory) ?? "dev-secret";
            var ok = !string.IsNullOrWhiteSpace(token) && TokenSigner.TryVerifyHeartbeat(token, req.Id, secret, out var _, out var _);
            if (!ok)
            {
                metrics?.RecordHeartbeat(false);
                context.Log("[目录] 心跳被拒绝：签名无效或缺失");
                return Task.FromResult<HeartbeatResponse?>(new HeartbeatResponse { Accepted = false });
            }

            var reg = context.GetComponent<IServerRegistryComponent>();
            var resp = new HeartbeatResponse { Accepted = reg != null };
            if (reg == null)
            {
                metrics?.RecordHeartbeat(false);
                context.Log("[目录] 心跳被忽略：组件不可用");
                return Task.FromResult<HeartbeatResponse?>(resp);
            }

            reg.UpsertHeartbeat(req.Id, req.Type, req.Region, req.Host, req.Port, req.PlayerCount, req.Capacity, req.Status, req.AvgRttMs);
            metrics?.RecordHeartbeat(true);
            context.Log($"[目录] 心跳: id={req.Id} type={req.Type} host={req.Host}:{req.Port} online={req.Status} players={req.PlayerCount}/{req.Capacity}");
            return Task.FromResult<HeartbeatResponse?>(resp);
        }
        catch (Exception ex)
        {
            metrics?.RecordHeartbeatError();
            context.Log($"[目录] 心跳处理异常: {ex.Message}");
            var resp = new HeartbeatResponse { Accepted = false };
            return Task.FromResult<HeartbeatResponse?>(resp);
        }
    }
}