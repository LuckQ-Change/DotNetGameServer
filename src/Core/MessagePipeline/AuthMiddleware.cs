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
            var reqContext = new RequestServerContext(
                ctx.Server,
                token,
                ctx.RemoteAddress,
                ctx.RequestId,
                ctx.Token);
            ctx.RequestContext = reqContext;
            var authenticated = false;

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
                                if (TokenSigner.TryVerify(token, ctx.Server.TokenAudience, s, out var tid, out var uid,
                                        out var exp, ctx.Server.TokenClockSkewSeconds))
                                {
                                    var revocation = ctx.Server.GetComponent<ITokenRevocationStore>();
                                    if (revocation == null || !await revocation.IsRevokedAsync(tid, ctx.Token))
                                    {
                                        reqContext.SetAuth(new AuthInfo { UserId = uid, TokenId = tid, ExpireUnixSeconds = exp });
                                        authenticated = true;
                                    }
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ctx.Server.Log($"认证异常: {ex}", Microsoft.Extensions.Logging.LogLevel.Warning);
                    }
                }
            }

            var authenticationRequired = ctx.EffectiveChannel switch
            {
                DotNetGameServer.CommunicationChannel.Internal => ctx.Server.RequireInternalAuthentication,
                DotNetGameServer.CommunicationChannel.Client => ctx.Server.RequireClientAuthentication,
                _ => true
            };
            var validIdentity = ctx.EffectiveChannel != DotNetGameServer.CommunicationChannel.Internal
                || TokenSigner.TryGetServiceIdentity(reqContext.Auth?.UserId, out _);
            if (authenticationRequired && (!authenticated || !validIdentity))
            {
                ctx.ResponseType = string.IsNullOrWhiteSpace(ctx.Envelope.Type)
                    ? "unknown"
                    : ctx.Envelope.Type;
                ctx.ResponsePayload = [];
                ctx.ResponseErrorCode = ProtocolError.AuthenticationRequired;
                ctx.ResponseErrorMessage = "authentication_required";
                return;
            }

            await next(ctx);
        });
    }
}
