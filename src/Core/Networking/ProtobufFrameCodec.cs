using System.Buffers;
using System.Buffers.Binary;

using DotNetGameServer;

using Google.Protobuf;

namespace Core.Networking;

/// <summary>统一读写带四字节小端长度前缀的 Protobuf 信封。</summary>
public static class ProtobufFrameCodec
{
    public static async ValueTask WriteEnvelopeAsync(
        Stream stream,
        envelope message,
        int maximumFrameBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);
        var payload = message.ToByteArray();
        if (payload.Length <= 0 || payload.Length > maximumFrameBytes)
            throw new InvalidDataException($"信封长度超出范围: {payload.Length}");

        var length = ArrayPool<byte>.Shared.Rent(sizeof(int));
        try
        {
            BinaryPrimitives.WriteInt32LittleEndian(length.AsSpan(0, sizeof(int)), payload.Length);
            await stream.WriteAsync(length.AsMemory(0, sizeof(int)), cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(length);
        }
    }

    public static async ValueTask<envelope?> ReadEnvelopeAsync(
        Stream stream,
        int maximumFrameBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var lengthBuffer = ArrayPool<byte>.Shared.Rent(sizeof(int));
        byte[]? payload = null;
        try
        {
            var lengthMemory = lengthBuffer.AsMemory(0, sizeof(int));
            var lengthBytes = await ReadExactlyOrEndAsync(stream, lengthMemory, cancellationToken);
            if (lengthBytes == 0) return null;
            if (lengthBytes != sizeof(int))
                throw new EndOfStreamException("连接在长度前缀读取完成前关闭。");

            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthMemory.Span);
            if (length <= 0 || length > maximumFrameBytes)
                throw new InvalidDataException($"信封长度超出范围: {length}");

            payload = ArrayPool<byte>.Shared.Rent(length);
            if (await ReadExactlyOrEndAsync(stream, payload.AsMemory(0, length), cancellationToken) != length)
                throw new EndOfStreamException("连接在信封读取完成前关闭。");
            return envelope.Parser.ParseFrom(payload, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lengthBuffer);
            if (payload is not null) ArrayPool<byte>.Shared.Return(payload);
        }
    }

    private static async ValueTask<int> ReadExactlyOrEndAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken);
            if (read == 0) return total;
            total += read;
        }
        return total;
    }
}
