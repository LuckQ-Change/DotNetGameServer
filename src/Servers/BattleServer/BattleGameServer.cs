using Core;
using Core.Configuration;

using Microsoft.Extensions.Logging;

namespace BattleServer;

public sealed class BattleGameServer(ILogger<BattleGameServer> logger, ServerOptions options)
    : BaseGameServer(nameof(BattleGameServer), logger, options);
