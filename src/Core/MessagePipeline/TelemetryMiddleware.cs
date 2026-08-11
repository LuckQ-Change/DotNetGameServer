using System.Diagnostics;

using Core.Telemetry;

namespace Core.MessagePipeline;

public static class TelemetryMiddleware
{
    public static MessagePipelineBuilder UseTelemetry(this MessagePipelineBuilder pipeline)
    {
        return pipeline.Use(next => async ctx =>
        {
            var reqType = ctx.Server.TryGetHandler(ctx.EffectiveChannel, ctx.Envelope.Type, out _)
                ? ctx.Envelope.Type
                : "unknown";
            var sw = Stopwatch.StartNew();
            var activity = CoreTelemetry.ActivitySource.StartActivity("game_server.request", ActivityKind.Server);
            activity?.AddTag("server_name", ctx.Server.ServerName);
            activity?.AddTag("message_type", reqType);
            activity?.AddTag("client_ip", ctx.RemoteAddress);

            string status = "ok";
            try
            {
                await next(ctx);
                if (!ctx.HasResponse)
                    status = "no_response";
                else if (ctx.ResponseErrorCode != 0)
                    status = "rejected";
            }
            catch (Exception)
            {
                status = "error";
                activity?.SetStatus(ActivityStatusCode.Error, "request_processing_failed");
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
