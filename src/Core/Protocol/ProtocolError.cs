namespace Core;

/// <summary>框架保留的传输与消息处理错误码。</summary>
public static class ProtocolError
{
    public const int InvalidRequest = Shared.ProtocolError.InvalidRequest;
    public const int AuthenticationRequired = Shared.ProtocolError.AuthenticationRequired;
    public const int ChannelForbidden = Shared.ProtocolError.ChannelForbidden;
    public const int UnknownMessageType = Shared.ProtocolError.UnknownMessageType;
    public const int RequestTimeout = Shared.ProtocolError.RequestTimeout;
    public const int VersionMismatch = Shared.ProtocolError.VersionMismatch;
    public const int ResponseTooLarge = Shared.ProtocolError.ResponseTooLarge;
    public const int InternalError = Shared.ProtocolError.InternalError;
}
