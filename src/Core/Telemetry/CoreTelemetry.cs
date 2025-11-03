using System;
using System.Collections.Generic;
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

    // 请求指标
    private static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>(
        name: "requests_total",
        unit: "count",
        description: "已处理请求总数");

    private static readonly Histogram<double> HandlerLatencySeconds = Meter.CreateHistogram<double>(
        name: "handler_latency_seconds",
        unit: "s",
        description: "消息处理耗时（秒）");

    // 未知类型计数
    private static readonly Counter<long> UnknownMessagesTotal = Meter.CreateCounter<long>(
        name: "unknown_messages_total",
        unit: "count",
        description: "收到但无法路由的未知类型消息总数");

    /// <summary>
    /// 为指定服务器创建并注册当前连接数的观测指标。
    /// </summary>
    public static Instrument CreateConnectionsGauge(Func<int> getter, string serverName)
    {
        return Meter.CreateObservableGauge(
            name: "current_connections",
            observeValue: () => new Measurement<int>(getter(), new KeyValuePair<string, object?>("server_name", serverName)),
            unit: "count",
            description: "当前并发连接数量");
    }

    /// <summary>
    /// 记录一次请求的计数与耗时。
    /// </summary>
    public static void RecordRequest(string serverName, string messageType, string status, double elapsedSeconds)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("server_name", serverName),
            new("message_type", messageType),
            new("status", status)
        };
        RequestsTotal.Add(1, tags);
        HandlerLatencySeconds.Record(elapsedSeconds, tags);
    }

    /// <summary>
    /// 记录未知类型消息事件。
    /// </summary>
    public static void RecordUnknown(string serverName, string messageType)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("server_name", serverName),
            new("message_type", messageType)
        };
        UnknownMessagesTotal.Add(1, tags);
    }
}