using BasicServer.Protocol;

using Core;

namespace BasicServer;

public sealed class PingHandler : IMessageHandler<PingRequest, PingResponse>
{
    public Task<PingResponse?> HandleAsync(PingRequest request, IServerContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Task.FromResult<PingResponse?>(new PingResponse
            {
                Success = false,
                Code = 400,
                Message = "message_required"
            });
        }

        context.Log($"Ping received: {request.Message}");
        return Task.FromResult<PingResponse?>(new PingResponse
        {
            Success = true,
            Code = 0,
            Message = $"pong: {request.Message}",
            ServerTimeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
    }
}
