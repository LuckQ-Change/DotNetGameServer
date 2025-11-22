using Core;
using DotNetGameServer;
using Google.Protobuf;
using DirectoryServer.Components;

namespace DirectoryServer.Handlers;

public class SwitchServerMessageHandler : IMessageHandler<c2s_switch_server, s2c_switch_server>
{
    public Task<s2c_switch_server?> HandleAsync(c2s_switch_server req, IServerContext context)
    {
        var metrics = context.GetComponent<DirectoryMetricsComponent>();
        try
        {
            var reg = context.GetComponent<IServerRegistryComponent>();
            var resp = new s2c_switch_server();
            if (reg == null)
            {
                metrics?.RecordSwitch(false);
                resp.Success = false;
                resp.Message = "目录组件不可用";
                return Task.FromResult<s2c_switch_server?>(resp);
            }

            var s = reg.GetServer(req.ServerId);
            if (s == null)
            {
                metrics?.RecordSwitch(false);
                resp.Success = false;
                resp.Message = "目标服务器不存在";
                return Task.FromResult<s2c_switch_server?>(resp);
            }
            if (!string.Equals(s.Status, "online", StringComparison.OrdinalIgnoreCase))
            {
                metrics?.RecordSwitch(false);
                resp.Success = false;
                resp.Message = "目标服务器不在线";
                return Task.FromResult<s2c_switch_server?>(resp);
            }

            if (!string.Equals(s.Type, "logic", StringComparison.OrdinalIgnoreCase))
            {
                metrics?.RecordSwitch(false);
                resp.Success = false;
                resp.Message = "目标服务器类型不允许";
                return Task.FromResult<s2c_switch_server?>(resp);
            }

            var token = reg.IssueSwitchToken(req.UserId, s, TimeSpan.FromMinutes(5));
            var expireSec = new DateTimeOffset(token.ExpireAtUtc).ToUnixTimeSeconds();
            var secretProvider = context.GetComponent<Core.Components.ITokenSecretProvider>();
            var secret = secretProvider?.GetSecret() ?? Core.Config.SimpleConfigLoader.LoadDirectoryTokenSecret(AppContext.BaseDirectory);
            if (string.IsNullOrWhiteSpace(secret))
            {
                if (IsDevelopment()) secret = "dev-secret"; else throw new InvalidOperationException("目录令牌密钥未配置，无法下发切服令牌。");
            }
            var sig = Shared.TokenSigner.ComputeSignature(token.Token, expireSec, req.UserId, s.Id, secret);
            var signed = Shared.TokenSigner.ComposeSigned(token.Token, expireSec, sig);

            resp.Success = true;
            resp.Message = "OK";
            resp.Host = s.Host;
            resp.Port = s.Port;
            resp.Token = signed; // 附带签名，不改协议字段
            resp.ExpireAt = new DateTimeOffset(token.ExpireAtUtc).ToUnixTimeMilliseconds();
            metrics?.RecordSwitch(true);
            context.Log($"[目录] 下发切服令牌 user={req.UserId}, server={s.Id}, ttl=5m, signed=1");

            return Task.FromResult<s2c_switch_server?>(resp);
        }
        catch (Exception ex)
        {
            metrics?.RecordSwitchError();
            context.Log($"[目录] 切服处理异常: {ex.Message}");
            var resp = new s2c_switch_server { Success = false, Message = "内部错误" };
            return Task.FromResult<s2c_switch_server?>(resp);
        }
    }

    private static bool IsDevelopment()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? string.Empty;
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }
}
