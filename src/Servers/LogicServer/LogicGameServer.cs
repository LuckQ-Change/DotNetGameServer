using Core;
using Core.Configuration;

using Microsoft.Extensions.Logging;

namespace LogicServer;

public sealed class LogicGameServer(ILogger<LogicGameServer> logger, ServerOptions options)
    : BaseGameServer(nameof(LogicGameServer), logger, options);
