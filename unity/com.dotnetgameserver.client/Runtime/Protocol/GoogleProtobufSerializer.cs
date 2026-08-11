using System;

using Google.Protobuf;

namespace DotNetGameServer.UnityClient.Protocol
{
    public static class GoogleProtobufSerializer
    {
        public static byte[] Serialize(IMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            return message.ToByteArray();
        }

        public static T Deserialize<T>(MessageParser<T> parser, byte[] payload) where T : IMessage<T>
        {
            if (parser == null) throw new ArgumentNullException(nameof(parser));
            return parser.ParseFrom(payload ?? Array.Empty<byte>());
        }

        public static string GetMessageType(IMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            return message.Descriptor.FullName;
        }
    }
}
