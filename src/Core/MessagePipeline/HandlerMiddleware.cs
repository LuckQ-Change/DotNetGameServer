using Core.Telemetry;

namespace Core.MessagePipeline;

public static class HandlerMiddleware
{
    public static MessagePipelineBuilder UseHandler(this MessagePipelineBuilder pipeline)
    {
        return pipeline.Use(next => async ctx =>
        {
            var env = ctx.Envelope;
            var typedHandlers = ctx.Server.ListHandlerInvokers();
            if (typedHandlers.TryGetValue(env.Type, out var invoker))
            {
                var context = ctx.RequestContext as IServerContext ?? ctx.Server;
                var result = await invoker.Invoke(env.Payload, context);
                if (result.HasValue)
                {
                    ctx.ResponseType = result.Value.responseType;
                    ctx.ResponsePayload = result.Value.payload;
                    ctx.ResponseErrorCode = result.Value.errorCode;
                    ctx.ResponseErrorMessage = result.Value.errorMessage;
                }
            }
            else
            {
                ctx.Server.Log($"未知消息类型: {env.Type}");
                CoreTelemetry.RecordUnknown(ctx.Server.ServerName, env.Type);
            }

            await next(ctx);
        });
    }
}
