using System;

namespace DotNetGameServer.UnityClient.Protocol
{
    public enum CommunicationChannel
    {
        Unspecified = 0,
        Client = 1,
        Internal = 2
    }

    public sealed class NetworkEnvelope
    {
        public string Type { get; set; } = string.Empty;
        public byte[] Payload { get; set; } = Array.Empty<byte>();
        public string Token { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public int ErrorCode { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public CommunicationChannel Channel { get; set; } = CommunicationChannel.Client;
    }

    public sealed class GameProtocolException : Exception
    {
        public GameProtocolException(int errorCode, string errorMessage, string requestId)
            : base("Server rejected request " + requestId + ": " + errorCode + " " + errorMessage)
        {
            ErrorCode = errorCode;
            ErrorMessage = errorMessage ?? string.Empty;
            RequestId = requestId ?? string.Empty;
        }

        public int ErrorCode { get; private set; }
        public string ErrorMessage { get; private set; }
        public string RequestId { get; private set; }
    }
}
