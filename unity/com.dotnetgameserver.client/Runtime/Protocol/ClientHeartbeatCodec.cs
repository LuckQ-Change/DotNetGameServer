using System.IO;

namespace DotNetGameServer.UnityClient.Protocol
{
    /// <summary>不依赖服务器程序集的客户端心跳编解码。</summary>
    public static class ClientHeartbeatCodec
    {
        public const string RequestType = "DotNetGameServer.ClientHeartbeatRequest";
        public const string ResponseType = "DotNetGameServer.ClientHeartbeatResponse";

        public static byte[] EncodeRequest(long clientTimeUnix)
        {
            using (var stream = new MemoryStream())
            {
                WriteVarint(stream, (1 << 3) | 0);
                WriteVarint(stream, unchecked((ulong)clientTimeUnix));
                return stream.ToArray();
            }
        }

        public static int ReadRecommendedIntervalSeconds(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return 0;
            var offset = 0;
            while (offset < payload.Length)
            {
                var tag = ReadVarint(payload, ref offset);
                var field = (int)(tag >> 3);
                var wire = (int)(tag & 7);
                if (field == 5 && wire == 0)
                    return (int)ReadVarint(payload, ref offset);
                Skip(payload, ref offset, wire);
            }
            return 0;
        }

        private static void Skip(byte[] bytes, ref int offset, int wire)
        {
            if (wire == 0) ReadVarint(bytes, ref offset);
            else if (wire == 2)
            {
                var length = (int)ReadVarint(bytes, ref offset);
                offset += length;
            }
            else if (wire == 1) offset += 8;
            else if (wire == 5) offset += 4;
        }

        private static void WriteVarint(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            stream.WriteByte((byte)value);
        }

        private static ulong ReadVarint(byte[] bytes, ref int offset)
        {
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                if (offset >= bytes.Length) return value;
                var current = bytes[offset++];
                value |= ((ulong)(current & 0x7F)) << shift;
                if ((current & 0x80) == 0) return value;
            }
            return value;
        }
    }
}
