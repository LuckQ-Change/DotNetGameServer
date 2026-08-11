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

        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async ValueTask<envelope?> ReadEnvelopeAsync(
        Stream stream,
        int maximumFrameBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var lengthBuffer = new byte[sizeof(int)];
        var lengthBytes = await ReadExactlyOrEndAsync(stream, lengthBuffer, cancellationToken);
        if (lengthBytes == 0) return null;
        if (lengthBytes != lengthBuffer.Length)
            throw new EndOfStreamException("连接在长度前缀读取完成前关闭。");

        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        if (length <= 0 || length > maximumFrameBytes)
            throw new InvalidDataException($"信封长度超出范围: {length}");

        var payload = new byte[length];
        if (await ReadExactlyOrEndAsync(stream, payload, cancellationToken) != length)
            throw new EndOfStreamException("连接在信封读取完成前关闭。");
        return envelope.Parser.ParseFrom(payload);
    }

    private static async ValueTask<int> ReadExactlyOrEndAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0) return total;
            total += read;
        }
        return total;
    }
}
