using Core;
using Core.Configuration;

using Microsoft.Extensions.Logging;

namespace ServerHost.ServerTypes;

public class LogicGameServer(ILogger<LogicGameServer> logger, ServerOptions options)
    : BaseGameServer(nameof(LogicGameServer), logger, options);
