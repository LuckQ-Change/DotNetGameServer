namespace Core;

/// <summary>框架保留的传输与消息处理错误码。</summary>
public static class ProtocolError
{
    public const int InvalidRequest = 400;
    public const int AuthenticationRequired = 401;
    public const int ChannelForbidden = 403;
    public const int UnknownMessageType = 404;
    public const int RequestTimeout = 408;
    public const int ResponseTooLarge = 500;
    public const int InternalError = 500;
}
