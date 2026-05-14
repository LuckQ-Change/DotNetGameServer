using System.Net.Sockets;
using DotNetGameServer;
using Google.Protobuf;

namespace ControlPlane;

public sealed class DirectoryClient
{
    public async Task<s2c_query_servers> QueryServersAsync(string host, int port, c2s_query_servers req, string? token,
        CancellationToken ct)
    {
        return await SendAsync<c2s_query_servers, s2c_query_servers>(host, port, req, token, ct);
    }

    private static async Task<TResp> SendAsync<TReq, TResp>(string host, int port, TReq req, string? token,
        CancellationToken ct)
        where TReq : class, IMessage<TReq>
        where TResp : class, IMessage<TResp>, new()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, ct);
        await using var stream = client.GetStream();

        var env = new envelope
        {
            Type = req.Descriptor.Name,
            Payload = ByteString.CopyFrom(req.ToByteArray()),
            RequestId = Guid.NewGuid().ToString("N")
        };
        if (!string.IsNullOrWhiteSpace(token)) env.Token = token;

        var envBytes = env.ToByteArray();
        var lenBuf = BitConverter.GetBytes(envBytes.Length);
        await stream.WriteAsync(lenBuf.AsMemory(0, 4), ct);
        await stream.WriteAsync(envBytes.AsMemory(), ct);
        await stream.FlushAsync(ct);

        var respLenBuf = new byte[4];
        await ReadExactAsync(stream, respLenBuf, 0, 4, ct);
        var respLen = BitConverter.ToInt32(respLenBuf, 0);
        if (respLen <= 0) throw new InvalidOperationException($"invalid respLen={respLen}");

        var respBuf = new byte[respLen];
        await ReadExactAsync(stream, respBuf, 0, respLen, ct);
        var respEnv = envelope.Parser.ParseFrom(respBuf);

        if (respEnv.ErrorCode != 0)
            throw new InvalidOperationException($"directory_error code={respEnv.ErrorCode} msg={respEnv.ErrorMessage}");

        var expectedType = new TResp().Descriptor.Name;
        if (!string.Equals(respEnv.Type, expectedType, StringComparison.Ordinal))
            throw new InvalidOperationException($"unexpected resp type={respEnv.Type} expected={expectedType}");

        var parser = new MessageParser<TResp>(() => new TResp());
        return parser.ParseFrom(respEnv.Payload);
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count,
        CancellationToken ct)
    {
        var total = 0;
        while (total < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(offset + total, count - total), ct);
            if (n == 0) throw new IOException("remote closed");
            total += n;
        }
    }
}
