using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Core.Telemetry;

/// <summary>
/// 全局遥测（Metrics/Tracing）工具。
/// </summary>
public static class CoreTelemetry
{
    public static readonly Meter Meter = new("DotNetGameServer", "1.0.0");
    public static readonly ActivitySource ActivitySource = new("DotNetGameServer");

    // 常量化单位与标签键，避免魔法字符串
    private const string UnitCount = "count";
    private const string UnitSeconds = "s";
    private const string TagServerName = "server_name";
    private const string TagMessageType = "message_type";
    private const string TagStatus = "status";
    private const string TagTargetServer = "target_server";

    // 请求指标
    private static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>(
        name: "requests_total",
        unit: UnitCount,
        description: "已处理请求总数");

    private static readonly Histogram<double> HandlerLatencySeconds = Meter.CreateHistogram<double>(
        name: "handler_latency_seconds",
        unit: UnitSeconds,
        description: "消息处理耗时（秒）");

    // 未知类型计数
    private static readonly Counter<long> UnknownMessagesTotal = Meter.CreateCounter<long>(
        name: "unknown_messages_total",
        unit: UnitCount,
        description: "收到但无法路由的未知类型消息总数");

    private static readonly Counter<long> RpcCallsTotal = Meter.CreateCounter<long>(
        name: "server_rpc_calls_total",
        unit: UnitCount,
        description: "服务器内部 RPC 调用总数");

    private static readonly Histogram<double> RpcLatencySeconds = Meter.CreateHistogram<double>(
        name: "server_rpc_latency_seconds",
        unit: UnitSeconds,
        description: "服务器内部 RPC 调用耗时（秒）");

    private static readonly UpDownCounter<long> ActiveConnections = Meter.CreateUpDownCounter<long>(
        name: "active_connections",
        unit: UnitCount,
        description: "当前活动 TCP 连接数");

    /// <summary>
    /// 为指定服务器创建并注册当前连接数的观测指标。
    /// </summary>
    public static void RecordConnectionDelta(string serverName, int delta)
    {
        ActiveConnections.Add(delta, new KeyValuePair<string, object?>(TagServerName, serverName));
    }

    /// <summary>
    /// 记录一次请求的计数与耗时。
    /// </summary>
    public static void RecordRequest(string serverName, string messageType, string status, double elapsedSeconds)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new(TagServerName, serverName),
            new(TagMessageType, messageType),
            new(TagStatus, status)
        };
        RequestsTotal.Add(1, tags);
        HandlerLatencySeconds.Record(elapsedSeconds, tags);
    }

    /// <summary>
    /// 记录未知类型消息事件。
    /// </summary>
    public static void RecordUnknown(string serverName)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new(TagServerName, serverName)
        };
        UnknownMessagesTotal.Add(1, tags);
    }

    /// <summary>记录一次服务器内部 RPC 调用。</summary>
    public static void RecordServerRpc(string targetServer, string status, double elapsedSeconds)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new(TagTargetServer, targetServer),
            new(TagStatus, status)
        };
        RpcCallsTotal.Add(1, tags);
        RpcLatencySeconds.Record(elapsedSeconds, tags);
    }
}
