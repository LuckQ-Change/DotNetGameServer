using Core;
using Core.Configuration;

using Microsoft.Extensions.Logging;

namespace LoginServer;

public sealed class LoginGameServer(ILogger<LoginGameServer> logger, ServerOptions options)
    : BaseGameServer(nameof(LoginGameServer), logger, options);
