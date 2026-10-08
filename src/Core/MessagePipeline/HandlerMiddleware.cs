using Core.Telemetry;

namespace Core.MessagePipeline;

public static class HandlerMiddleware
{
    public static MessagePipelineBuilder UseHandler(this MessagePipelineBuilder pipeline)
    {
        return pipeline.Use(next => async ctx =>
        {
            var env = ctx.Envelope;
            if (ctx.Server.TryLeaseHandler(ctx.EffectiveChannel, env.Type, out var lease))
            {
                using (lease)
                {
                    var context = ctx.RequestContext as IServerContext ?? ctx.Server;
                    var result = await lease.Invoker.Invoke(env.Payload, context);
                    if (result.HasValue)
                    {
                        ctx.ResponseType = result.Value.responseType;
                        ctx.ResponsePayload = result.Value.payload;
                        ctx.ResponseErrorCode = result.Value.errorCode;
                        ctx.ResponseErrorMessage = result.Value.errorMessage;
                    }
                }
            }
            else
            {
                ctx.Server.Log($"未知消息类型: {env.Type}", Microsoft.Extensions.Logging.LogLevel.Warning);
                CoreTelemetry.RecordUnknown(ctx.Server.ServerName);
                ctx.ResponseType = string.IsNullOrWhiteSpace(env.Type) ? "unknown" : env.Type;
                ctx.ResponsePayload = [];
                ctx.ResponseErrorCode = ProtocolError.UnknownMessageType;
                ctx.ResponseErrorMessage = "unknown_message_type";
            }

            await next(ctx);
        });
    }
}
