using System;
using System.IO;
using System.Text;

namespace DotNetGameServer.UnityClient.Protocol
{
    /// <summary>
    /// 只编码公共 Envelope 的轻量 protobuf codec。业务消息仍由 Google.Protobuf 生成类负责，
    /// 因而客户端无需引用任何服务器程序集。
    /// </summary>
    public sealed class ProtobufEnvelopeCodec : IEnvelopeCodec
    {
        private readonly int _maxEnvelopeBytes;

        public ProtobufEnvelopeCodec(int maxEnvelopeBytes = 1024 * 1024)
        {
            if (maxEnvelopeBytes < 1024) throw new ArgumentOutOfRangeException(nameof(maxEnvelopeBytes));
            _maxEnvelopeBytes = maxEnvelopeBytes;
        }

        public byte[] Encode(NetworkEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            using (var stream = new MemoryStream())
            {
                WriteString(stream, 1, envelope.Type);
                WriteBytes(stream, 2, envelope.Payload);
                WriteString(stream, 3, envelope.Token);
                WriteString(stream, 4, envelope.RequestId);
                WriteString(stream, 5, envelope.Version);
                if (envelope.ErrorCode != 0)
                {
                    WriteTag(stream, 6, 0);
                    WriteVarint(stream, unchecked((ulong)(long)envelope.ErrorCode));
                }
                WriteString(stream, 7, envelope.ErrorMessage);
                if (envelope.Channel != CommunicationChannel.Unspecified)
                {
                    WriteTag(stream, 8, 0);
                    WriteVarint(stream, (ulong)envelope.Channel);
                }
                if (stream.Length > _maxEnvelopeBytes)
                    throw new InvalidDataException("Envelope exceeds configured maximum size.");
                return stream.ToArray();
            }
        }

        public NetworkEnvelope Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length == 0 || bytes.Length > _maxEnvelopeBytes)
                throw new InvalidDataException("Envelope size is invalid.");

            var envelope = new NetworkEnvelope();
            var offset = 0;
            while (offset < bytes.Length)
            {
                var tag = ReadVarint(bytes, ref offset);
                var field = (int)(tag >> 3);
                var wire = (int)(tag & 7);
                switch (field)
                {
                    case 1: envelope.Type = ReadString(bytes, ref offset, wire); break;
                    case 2: envelope.Payload = ReadBytes(bytes, ref offset, wire); break;
                    case 3: envelope.Token = ReadString(bytes, ref offset, wire); break;
                    case 4: envelope.RequestId = ReadString(bytes, ref offset, wire); break;
                    case 5: envelope.Version = ReadString(bytes, ref offset, wire); break;
                    case 6: RequireWire(wire, 0); envelope.ErrorCode = unchecked((int)ReadVarint(bytes, ref offset)); break;
                    case 7: envelope.ErrorMessage = ReadString(bytes, ref offset, wire); break;
                    case 8: RequireWire(wire, 0); envelope.Channel = (CommunicationChannel)ReadVarint(bytes, ref offset); break;
                    default: SkipUnknown(bytes, ref offset, wire); break;
                }
            }
            return envelope;
        }

        private static void WriteString(Stream stream, int field, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            WriteBytes(stream, field, Encoding.UTF8.GetBytes(value));
        }

        private static void WriteBytes(Stream stream, int field, byte[] value)
        {
            if (value == null || value.Length == 0) return;
            WriteTag(stream, field, 2);
            WriteVarint(stream, (ulong)value.Length);
            stream.Write(value, 0, value.Length);
        }

        private static void WriteTag(Stream stream, int field, int wire)
        {
            WriteVarint(stream, (ulong)((field << 3) | wire));
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
                if (offset >= bytes.Length) throw new EndOfStreamException("Unexpected end of protobuf varint.");
                var current = bytes[offset++];
                value |= ((ulong)(current & 0x7F)) << shift;
                if ((current & 0x80) == 0) return value;
            }
            throw new InvalidDataException("Invalid protobuf varint.");
        }

        private static byte[] ReadBytes(byte[] bytes, ref int offset, int wire)
        {
            RequireWire(wire, 2);
            var length64 = ReadVarint(bytes, ref offset);
            if (length64 > int.MaxValue) throw new InvalidDataException("Length-delimited field is too large.");
            var length = (int)length64;
            if (length < 0 || offset > bytes.Length - length) throw new EndOfStreamException("Truncated protobuf field.");
            var result = new byte[length];
            Buffer.BlockCopy(bytes, offset, result, 0, length);
            offset += length;
            return result;
        }

        private static string ReadString(byte[] bytes, ref int offset, int wire)
        {
            return Encoding.UTF8.GetString(ReadBytes(bytes, ref offset, wire));
        }

        private static void SkipUnknown(byte[] bytes, ref int offset, int wire)
        {
            switch (wire)
            {
                case 0: ReadVarint(bytes, ref offset); return;
                case 1: Advance(bytes, ref offset, 8); return;
                case 2:
                    var length = ReadVarint(bytes, ref offset);
                    if (length > int.MaxValue) throw new InvalidDataException("Unknown field is too large.");
                    Advance(bytes, ref offset, (int)length);
                    return;
                case 5: Advance(bytes, ref offset, 4); return;
                default: throw new InvalidDataException("Unsupported protobuf wire type: " + wire);
            }
        }

        private static void Advance(byte[] bytes, ref int offset, int count)
        {
            if (count < 0 || offset > bytes.Length - count) throw new EndOfStreamException("Truncated protobuf field.");
            offset += count;
        }

        private static void RequireWire(int actual, int expected)
        {
            if (actual != expected) throw new InvalidDataException("Unexpected protobuf wire type.");
        }
    }
}
