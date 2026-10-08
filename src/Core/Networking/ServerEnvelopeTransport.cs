using System.Buffers;
using System.Buffers.Binary;

using DotNetGameServer;

using Google.Protobuf;

namespace Core.Networking;

/// <summary>
/// 实现服务端信封协议的精确读取与串行写入。
/// </summary>
internal static class ServerEnvelopeTransport
{
    /// <summary>
    /// 精确读取指定长度；对端提前关闭时返回实际读取量。
    /// 每次底层读取都重新计算超时，避免慢连接永久占用读取任务。
    /// </summary>
    public static async ValueTask<int> ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken,
        int timeoutMs)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeoutMs > 0) readCts.CancelAfter(timeoutMs);
            var read = await stream.ReadAsync(buffer[total..], readCts.Token);
            if (read == 0) return total;
            total += read;
        }

        return total;
    }

    /// <summary>
    /// 构造并写入响应信封。若完整响应超过帧上限，则自动降级为无载荷的结构化错误。
    /// </summary>
    public static async ValueTask WriteResponseAsync(
        Stream stream,
        string type,
        byte[] payload,
        string? requestId,
        int errorCode,
        string? errorMessage,
        CommunicationChannel channel,
        CancellationToken cancellationToken,
        int timeoutMs,
        int maximumFrameBytes)
    {
        var response = new envelope
        {
            Type = type,
            Payload = ByteString.CopyFrom(payload),
            Channel = channel,
            ErrorCode = errorCode
        };
        if (!string.IsNullOrWhiteSpace(requestId)) response.RequestId = requestId;
        if (!string.IsNullOrWhiteSpace(errorMessage)) response.ErrorMessage = errorMessage;

        // 服务端不回写协议版本，客户端只依赖请求对应的响应类型和请求 ID。
        var bytes = response.ToByteArray();
        if (bytes.Length > maximumFrameBytes)
        {
            response.Payload = ByteString.Empty;
            response.ErrorCode = ProtocolError.ResponseTooLarge;
            response.ErrorMessage = "response_too_large";
            bytes = response.ToByteArray();
        }

        var lengthBuffer = ArrayPool<byte>.Shared.Rent(sizeof(int));
        try
        {
            BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer.AsSpan(0, sizeof(int)), bytes.Length);
            using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeoutMs > 0) writeCts.CancelAfter(timeoutMs);
            await stream.WriteAsync(lengthBuffer.AsMemory(0, sizeof(int)), writeCts.Token);
            await stream.WriteAsync(bytes, writeCts.Token);
            await stream.FlushAsync(writeCts.Token);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lengthBuffer);
        }
    }
}
