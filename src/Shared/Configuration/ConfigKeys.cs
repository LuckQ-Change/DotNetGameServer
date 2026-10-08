namespace Shared.Configuration;

public static class ConfigKeys
{
    public const string ServiceConfigKey = "service";
    public const string ServiceHealthCheckUrlKeyLower = "service:healthCheckUrl";
    public const string TelemetryMetricsEnabledKeyLower = "telemetry:metrics:enabled";
    public const string TelemetryMetricsOtlpEndpointKeyLower = "telemetry:metrics:otlp:endpoint";
}
