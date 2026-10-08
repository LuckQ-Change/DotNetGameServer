namespace DotNetGameServer.UnityClient.Protocol
{
    /// <summary>与服务器 Shared.ProtocolError 保持同一组数字。</summary>
    public static class ProtocolError
    {
        public const int InvalidRequest = 400;
        public const int AuthenticationRequired = 401;
        public const int ChannelForbidden = 403;
        public const int UnknownMessageType = 404;
        public const int RequestTimeout = 408;
        public const int VersionMismatch = 426;
        public const int ResponseTooLarge = 500;
        public const int InternalError = 500;
    }
}
