using Core;
using DotNetGameServer;
using DirectoryServer.Components;

namespace DirectoryServer.Handlers;

public class VerifySwitchTokenMessageHandler : IStrongMessageHandler<VerifySwitchTokenRequest, VerifySwitchTokenResponse>
{
    public Task<VerifySwitchTokenResponse?> HandleAsync(VerifySwitchTokenRequest req, IServerContext context)
    {
        var metrics = context.GetComponent<DirectoryServer.Components.DirectoryMetricsComponent>();
        try
        {
            var reg = context.GetComponent<IServerRegistryComponent>();
            if (reg == null)
            {
                return Task.FromResult<VerifySwitchTokenResponse?>(new VerifySwitchTokenResponse { Valid = false, Message = "目录组件不可用" });
            }

            // 校验签名与过期
            var secretProvider = context.GetComponent<Core.Components.ITokenSecretProvider>();
            var primary = secretProvider?.GetSecret();
            if (string.IsNullOrWhiteSpace(primary))
            {
                return Task.FromResult<VerifySwitchTokenResponse?>(new VerifySwitchTokenResponse { Valid = false, Message = "目录令牌密钥不可用" });
            }
            string? tokenId = null;
            long expSec = 0;
            bool verified = false;
            foreach (var s in secretProvider!.GetVerifySecrets())
            {
                if (!string.IsNullOrWhiteSpace(s) && Shared.TokenSigner.TryVerify(req.Token, req.UserId, req.ServerId, s, out var tid, out var e))
                {
                    verified = true;
                    tokenId = tid;
                    expSec = e;
                    break;
                }
            }
            if (!verified || tokenId == null)
            {
                metrics?.RecordSwitch(false);
                context.Log($"[目录] 令牌签名或过期校验失败 user={req.UserId}, server={req.ServerId}");
                return Task.FromResult<VerifySwitchTokenResponse?>(new VerifySwitchTokenResponse { Valid = false, Message = "令牌签名或过期无效" });
            }

            // 检查注册表中令牌是否存在/匹配，并可选地消费
            var ok = reg.CheckSwitchToken(req.UserId, req.ServerId, tokenId, req.Consume);
            if (!ok)
            {
                metrics?.RecordSwitch(false);
                return Task.FromResult<VerifySwitchTokenResponse?>(new VerifySwitchTokenResponse { Valid = false, Message = "令牌不存在或不匹配" });
            }

            metrics?.RecordSwitch(true);
            var resp = new VerifySwitchTokenResponse
            {
                Valid = true,
                Message = "OK",
                ExpireAt = expSec * 1000
            };
            context.Log($"[目录] 令牌验证通过 user={req.UserId}, server={req.ServerId}, consume={(req.Consume ? 1 : 0)}");
            return Task.FromResult<VerifySwitchTokenResponse?>(resp);
        }
        catch (Exception ex)
        {
            metrics?.RecordSwitchError();
            context.Log($"[目录] 令牌验证异常: {ex.Message}");
            return Task.FromResult<VerifySwitchTokenResponse?>(new VerifySwitchTokenResponse { Valid = false, Message = "内部错误" });
        }
    }

}
