using Core;
using Core.Configuration;

using Microsoft.Extensions.Logging;

namespace ServerHost.ServerTypes;

public class LoginGameServer(ILogger<LoginGameServer> logger, ServerOptions options)
    : BaseGameServer(nameof(LoginGameServer), logger, options);
