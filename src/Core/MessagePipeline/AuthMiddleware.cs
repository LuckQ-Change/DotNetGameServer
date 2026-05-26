using Core.Components;
using Shared;

namespace Core.MessagePipeline;

public static class AuthMiddleware
{
    public static MessagePipelineBuilder UseAuth(this MessagePipelineBuilder pipeline)
    {
        return pipeline.Use(next => async ctx =>
        {
            var token = ctx.Envelope.Token ?? string.Empty;
            var reqContext = new RequestServerContext(ctx.Server, token, ctx.RemoteAddress, ctx.RequestId);
            ctx.RequestContext = reqContext;

            if (!string.IsNullOrWhiteSpace(token))
            {
                var secretProvider = ctx.Server.GetComponent<ITokenSecretProvider>();
                if (secretProvider != null)
                {
                    try
                    {
                        var secret = secretProvider.GetSecret();
                        if (!string.IsNullOrWhiteSpace(secret))
                        {
                            var verifySecrets = secretProvider.GetVerifySecrets().ToArray();
                            foreach (var s in verifySecrets)
                            {
                                if (string.IsNullOrWhiteSpace(s)) continue;
                                if (TokenSigner.TryVerify(token, "user", "server", s, out var tid, out var exp))
                                {
                                    var revocation = ctx.Server.GetComponent<ITokenRevocationStore>();
                                    if (revocation == null || !revocation.IsRevoked(tid))
                                    {
                                        reqContext.SetAuth(new AuthInfo { UserId = "user", TokenId = tid, ExpireUnixSeconds = exp });
                                    }
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ctx.Server.Log($"认证异常: {ex.Message}");
                    }
                }
            }

            await next(ctx);
        });
    }
}
