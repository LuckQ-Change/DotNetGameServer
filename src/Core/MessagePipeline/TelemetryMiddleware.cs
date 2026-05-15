using System.Diagnostics;
using Core.Telemetry;

namespace Core.MessagePipeline;

public static class TelemetryMiddleware
{
    public static MessagePipeline UseTelemetry(this MessagePipeline pipeline)
    {
        return pipeline.Use(next => async ctx =>
        {
            var reqType = ctx.Envelope.Type;
            var sw = Stopwatch.StartNew();
            var activity = CoreTelemetry.ActivitySource.StartActivity($"Handle {reqType}", ActivityKind.Server);
            activity?.AddTag("server_name", ctx.Server.ServerName);
            activity?.AddTag("client_ip", ctx.RemoteAddress);

            string status = "ok";
            try
            {
                await next(ctx);
                if (!ctx.HasResponse) status = "no_response";
            }
            catch (Exception ex)
            {
                status = "error";
                activity?.AddTag("exception", ex.Message);
                throw;
            }
            finally
            {
                sw.Stop();
                CoreTelemetry.RecordRequest(ctx.Server.ServerName, reqType, status, sw.Elapsed.TotalSeconds);
                activity?.AddTag("status", status);
                activity?.AddTag("latency_ms", (int)sw.Elapsed.TotalMilliseconds);
                activity?.Stop();
            }
        });
    }
}
