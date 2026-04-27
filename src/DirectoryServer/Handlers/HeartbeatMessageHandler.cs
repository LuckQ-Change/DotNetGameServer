using Core;
using DotNetGameServer;
using DirectoryServer.Components;
using Shared;
namespace DirectoryServer.Handlers;

public class HeartbeatMessageHandler : IMessageHandler<c2s_heartbeat, s2c_heartbeat>
{
    public Task<s2c_heartbeat?> HandleAsync(c2s_heartbeat req, IServerContext context)
    {
        var metrics = context.GetComponent<DirectoryMetricsComponent>();
        try
        {
            // 校验心跳签名令牌（统一通过 TokenSecretProvider 获取密钥）
            var token = context.AccessToken ?? string.Empty;
            var secretProvider = context.GetComponent<Core.Components.ITokenSecretProvider>();
            var primary = secretProvider?.GetSecret();
            if (string.IsNullOrWhiteSpace(primary))
            {
                // 生产环境下 Provider 初始化已阻止服务启动；此处作为双保险直接拒绝
                metrics?.RecordHeartbeat(false);
                context.Log("[目录] 心跳被拒绝：目录令牌密钥不可用");
                return Task.FromResult<s2c_heartbeat?>(new s2c_heartbeat { Accepted = false });
            }
            var ok = false;
            if (!string.IsNullOrWhiteSpace(token))
            {
                var candidates = secretProvider!.GetVerifySecrets();
                foreach (var s in candidates)
                {
                    if (!string.IsNullOrWhiteSpace(s) && TokenSigner.TryVerifyHeartbeat(token, req.Id, s, out var _, out var _))
                    {
                        ok = true;
                        break;
                    }
                }
            }
            if (!ok)
            {
                metrics?.RecordHeartbeat(false);
                context.Log("[目录] 心跳被拒绝：签名无效或缺失");
                return Task.FromResult<s2c_heartbeat?>(new s2c_heartbeat { Accepted = false });
            }

            var reg = context.GetComponent<IServerRegistryComponent>();
            var resp = new s2c_heartbeat { Accepted = reg != null };
            if (reg == null)
            {
                metrics?.RecordHeartbeat(false);
                context.Log("[目录] 心跳被忽略：组件不可用");
                return Task.FromResult<s2c_heartbeat?>(resp);
            }

            reg.UpsertHeartbeat(req.Id, req.Type, req.Region, req.Host, req.Port, req.PlayerCount, req.Capacity, req.Status, req.AvgRttMs);
            metrics?.RecordHeartbeat(true);
            context.Log($"[目录] 心跳: id={req.Id} type={req.Type} host={req.Host}:{req.Port} online={req.Status} players={req.PlayerCount}/{req.Capacity}");
            return Task.FromResult<s2c_heartbeat?>(resp);
        }
        catch (Exception ex)
        {
            metrics?.RecordHeartbeatError();
            context.Log($"[目录] 心跳处理异常: {ex.Message}");
            var resp = new s2c_heartbeat { Accepted = false };
            return Task.FromResult<s2c_heartbeat?>(resp);
        }
    }

}
