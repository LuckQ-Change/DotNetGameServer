using DotNetGameServer;

namespace Core.MessagePipeline;

public class MessageContext
{
    public required envelope Envelope { get; init; }
    public required BaseGameServer Server { get; init; }
    public required string RemoteAddress { get; init; }
    public required string RequestId { get; init; }
    public required string ConnectionId { get; init; }
    public CancellationToken Token { get; init; }
    public CommunicationChannel EffectiveChannel { get; set; }

    public string? ResponseType { get; set; }
    public byte[]? ResponsePayload { get; set; }
    public int ResponseErrorCode { get; set; }
    public string ResponseErrorMessage { get; set; } = string.Empty;
    public bool HasResponse => !string.IsNullOrEmpty(ResponseType);

    internal RequestServerContext? RequestContext { get; set; }
}
