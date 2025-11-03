using Core;
using Core.Components;
using Core.Config;
using DotNetGameServer;
using LoginServer.Components;
using Microsoft.Extensions.Logging;
using Shared;
using System;
using System.Threading.Tasks;

namespace LoginServer.Handlers; // Updated namespace

/// <summary>
/// Handles login requests.
/// </summary>
public class LoginMessageHandler : IStrongMessageHandler<LoginRequest, LoginResponse>
{
    private readonly IUserAuthComponent _authComponent;

    public LoginMessageHandler(IUserAuthComponent authComponent)
    {
        _authComponent = authComponent;
    }

    public async Task<LoginResponse?> HandleAsync(LoginRequest request, IServerContext context)
    {
        var authResult = await _authComponent.AuthenticateAsync(request.Username, request.Password);

        context.Log($"Handling login request for user: '{request.Username}', Result: {authResult.Success}");

        string? token = null;
        long expire = 0;

        if (authResult.Success)
        {
            // On successful login, issue a token
            try
            {
                var hb = context.GetComponent<HeartbeatClientComponent>();
                var serverId = hb?.TryGetCurrentServerId();
                var secretProvider = context.GetComponent<ITokenSecretProvider>();
                // Fallback to loading secret from config if not available in context
                var secret = secretProvider?.GetSecret() ?? SimpleConfigLoader.LoadDirectoryTokenSecret(AppContext.BaseDirectory) ?? "dev-secret";

                if (!string.IsNullOrWhiteSpace(serverId))
                {
                    expire = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600; // Token valid for 1 hour
                    var sig = TokenSigner.ComputeSignature(serverId, expire, request.Username, serverId, secret);
                    token = TokenSigner.ComposeSigned(serverId, expire, sig);
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
}