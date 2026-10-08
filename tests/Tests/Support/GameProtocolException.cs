using Google.Protobuf;

namespace Client;

public sealed class GameProtocolException : Exception
{
    public GameProtocolException(
        int errorCode,
        string? errorMessage,
        string? requestId,
        byte[]? payload = null,
        string? responseType = null)
        : base($"服务器拒绝请求 {requestId}: {errorCode} {errorMessage}")
    {
        ErrorCode = errorCode;
        ErrorMessage = errorMessage ?? string.Empty;
        RequestId = requestId ?? string.Empty;
        Payload = payload ?? [];
        ResponseType = responseType ?? string.Empty;
    }

    public int ErrorCode { get; }
    public string ErrorMessage { get; }
    public string RequestId { get; }
    public byte[] Payload { get; }
    public string ResponseType { get; }

    public bool TryGetResponse<T>(MessageParser<T> parser, out T? response) where T : class, IMessage<T>
    {
        response = default;
        if (parser is null || Payload.Length == 0) return false;
        try
        {
            response = parser.ParseFrom(Payload);
            return true;
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }
    }
}

public sealed class GameRequestResult<T> where T : class
{
    public required bool Success { get; init; }
    public required int ErrorCode { get; init; }
    public required string ErrorMessage { get; init; }
    public T? Response { get; init; }
    public bool HasResponse => Response is not null;
}
