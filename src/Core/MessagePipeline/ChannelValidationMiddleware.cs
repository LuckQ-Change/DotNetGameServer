namespace Core.MessagePipeline;

public static class ChannelValidationMiddleware
{
    /// <summary>校验通信通道，并将兼容模式下的未声明通道归一化。</summary>
    public static MessagePipelineBuilder UseChannelValidation(this MessagePipelineBuilder pipeline)
    {
        return pipeline.Use(next => async context =>
        {
            if (!context.Server.TryResolveChannel(context.Envelope.Channel, out var channel))
            {
                context.ResponseType = string.IsNullOrWhiteSpace(context.Envelope.Type)
                    ? "unknown"
                    : context.Envelope.Type;
                context.ResponsePayload = [];
                context.ResponseErrorCode = ProtocolError.ChannelForbidden;
                context.ResponseErrorMessage = "communication_channel_forbidden";
                return;
            }

            context.EffectiveChannel = channel;
            await next(context);
        });
    }
}
