using Core;
using Core.Configuration;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hosting;

/// <summary>
/// 单个游戏服务的宿主定义。应用通过它声明如何创建实例、注册依赖与扩展 HTTP 端点。
/// </summary>
public sealed class GameServerDefinition
{
    /// <summary>服务名，用于日志与诊断。</summary>
    public required string Role { get; init; }

    /// <summary>未通过 --port 指定时的默认监听端口。</summary>
    public int DefaultPort { get; init; }

    /// <summary>生产环境是否强制要求非默认 TokenSecret。</summary>
    public bool RequiresProductionTokenSecret { get; init; } = true;

    /// <summary>对 Unity 等客户端公开的服务职责，例如 gameplay、login、lobby。</summary>
    public IReadOnlyList<string> Responsibilities { get; init; } = [];

    /// <summary>公开给客户端的协议版本。</summary>
    public string ProtocolVersion { get; init; } = "1.0.0";

    /// <summary>创建服务器实例并注册其消息处理器与组件。</summary>
    public required Func<HostServerContext, BaseGameServer> CreateServer { get; init; }

    /// <summary>可选：注册应用特有的 DI 服务。</summary>
    public Action<IServiceCollection, IConfiguration>? ConfigureServices { get; init; }

    /// <summary>可选：映射应用特有的 HTTP 端点。</summary>
    public Action<WebApplication>? ConfigureEndpoints { get; init; }
}

/// <summary>
/// 创建服务器实例时可用的上下文。
/// </summary>
public sealed class HostServerContext
{
    public required IServiceProvider Services { get; init; }
    public required IConfiguration Configuration { get; init; }
    public required ServerOptions ServerOptions { get; init; }
    public required AuthenticationOptions AuthenticationOptions { get; init; }
    public required ClientHeartbeatOptions ClientHeartbeatOptions { get; init; }
    public required int Port { get; init; }
}
