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
        public GameProtocolException(
            int errorCode,
            string errorMessage,
            string requestId,
            byte[] payload = null,
            string responseType = null)
            : base("Server rejected request " + requestId + ": " + errorCode + " " + errorMessage)
        {
            ErrorCode = errorCode;
            ErrorMessage = errorMessage ?? string.Empty;
            RequestId = requestId ?? string.Empty;
            Payload = payload ?? Array.Empty<byte>();
            ResponseType = responseType ?? string.Empty;
        }

        public int ErrorCode { get; private set; }
        public string ErrorMessage { get; private set; }
        public string RequestId { get; private set; }
        public byte[] Payload { get; private set; }
        public string ResponseType { get; private set; }

        public bool TryGetResponse<T>(Google.Protobuf.MessageParser<T> parser, out T response)
            where T : Google.Protobuf.IMessage<T>
        {
            response = default(T);
            if (parser == null || Payload == null || Payload.Length == 0) return false;
            try
            {
                response = parser.ParseFrom(Payload);
                return true;
            }
            catch (Google.Protobuf.InvalidProtocolBufferException)
            {
                return false;
            }
        }
    }

    public sealed class GameRequestResult<T> where T : class
    {
        public bool Success;
        public int ErrorCode;
        public string ErrorMessage = string.Empty;
        public T Response;
        public bool HasResponse { get { return Response != null; } }
    }
}
