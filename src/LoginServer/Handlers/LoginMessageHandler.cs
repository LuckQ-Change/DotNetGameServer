using Core;
using Core.Components;
using Core.Config;
using DotNetGameServer;
using LoginServer.Components;
using Microsoft.Extensions.Logging;
using Shared;

namespace LoginServer.Handlers;

public class LoginMessageHandler(IUserAuthComponent authComponent) : IStrongMessageHandler<LoginRequest, LoginResponse>
{
    public async Task<LoginResponse?> HandleAsync(LoginRequest request, IServerContext context)
    {
        var authResult = await authComponent.AuthenticateAsync(request.Username, request.Password);

        context.Log($"Handling login request for user: '{request.Username}', Result: {authResult.Success}");

        string? token = null;
        long expire = 0;

        if (authResult.Success)
        {
            try
            {
                var hb = context.GetComponent<HeartbeatClientComponent>();
                var serverId = hb?.TryGetCurrentServerId();
                var secretProvider = context.GetComponent<ITokenSecretProvider>();
                var secret = secretProvider?.GetSecret() ?? SimpleConfigLoader.LoadDirectoryTokenSecret(AppContext.BaseDirectory);
                if (string.IsNullOrWhiteSpace(secret))
                {
                    if (!IsDevelopment()) throw new InvalidOperationException("目录令牌密钥未配置，无法签发登录令牌。");
                    secret = "dev-secret";
                }

                if (!string.IsNullOrWhiteSpace(serverId))
                {
                    expire = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
                    var tokenId = Guid.NewGuid().ToString("N");
                    var sig = TokenSigner.ComputeSignature(tokenId, expire, request.Username, serverId, secret);
                    token = TokenSigner.ComposeSigned(tokenId, expire, sig);
                }
            }
            catch (Exception ex)
            {
                context.Log($"Error during token generation: {ex.Message}", LogLevel.Error);
            }
        } 

        return new LoginResponse
        {
            Success = authResult.Success,
            Message = authResult.Message,
            Token = token ?? string.Empty,
            ExpireAt = expire
        };
    }
    private static bool IsDevelopment()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? string.Empty;
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }
}
