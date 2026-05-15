using Core;
using Core.Configuration;

using Microsoft.Extensions.Logging;

namespace ServerHost.ServerTypes;

public class BattleGameServer(ILogger<BattleGameServer> logger, ServerOptions options)
    : BaseGameServer(nameof(BattleGameServer), logger, options);
