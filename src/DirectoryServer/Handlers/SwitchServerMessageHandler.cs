using Core;
using DotNetGameServer;
using Google.Protobuf;
using DirectoryServer.Components;

namespace DirectoryServer.Handlers;

public class SwitchServerMessageHandler : IStrongMessageHandler<SwitchServerRequest, SwitchServerResponse>
{
    public Task<SwitchServerResponse?> HandleAsync(SwitchServerRequest req, IServerContext context)
    {
        var metrics = context.GetComponent<DirectoryServer.Components.DirectoryMetricsComponent>();
        try
        {
            var reg = context.GetComponent<IServerRegistryComponent>();
            var resp = new SwitchServerResponse();
            if (reg == null)
            {
                metrics?.RecordSwitch(false);
                resp.Success = false;
                resp.Message = "目录组件不可用";
                return Task.FromResult<SwitchServerResponse?>(resp);
            }

            var s = reg.GetServer(req.ServerId);
            if (s == null)
            {
                metrics?.RecordSwitch(false);
                resp.Success = false;
                resp.Message = "目标服务器不存在";
                return Task.FromResult<SwitchServerResponse?>(resp);
            }
            if (!string.Equals(s.Status, "online", StringComparison.OrdinalIgnoreCase))
            {
                metrics?.RecordSwitch(false);
                resp.Success = false;
                resp.Message = "目标服务器不在线";
                return Task.FromResult<SwitchServerResponse?>(resp);
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

            return Task.FromResult<SwitchServerResponse?>(resp);
        }
        catch (Exception ex)
        {
            metrics?.RecordSwitchError();
            context.Log($"[目录] 切服处理异常: {ex.Message}");
            var resp = new SwitchServerResponse { Success = false, Message = "内部错误" };
            return Task.FromResult<SwitchServerResponse?>(resp);
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