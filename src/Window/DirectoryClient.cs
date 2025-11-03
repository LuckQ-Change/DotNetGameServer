using System.Net.Sockets;
using Google.Protobuf;
using DotNetGameServer;

namespace Window;

public class DirectoryClient
{
    private const int MaxMessageSize = 1 * 1024 * 1024; // 1 MB 上限
    private readonly string _host;
    private readonly int _port;
    private readonly Action<string> _log;

    public DirectoryClient(string host, int port, Action<string> log)
    {
        _host = host;
        _port = port;
        _log = log;
    }

    public async Task<bool> SendHeartbeatAsync(HeartbeatRequest req, CancellationToken token)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_host, _port, token);
            using var stream = client.GetStream();
            var payload = req.ToByteArray();
            var env = new Envelope { Type = "HeartbeatRequest", Payload = ByteString.CopyFrom(payload) };
            var bytes = env.ToByteArray();
            var lenBuf = BitConverter.GetBytes(bytes.Length);
            await stream.WriteAsync(lenBuf.AsMemory(0, 4), token);
            await stream.WriteAsync(bytes.AsMemory(), token);
            await stream.FlushAsync(token);
            // 可选：读取响应
            var respLenBuf = new byte[4];
            var n = await ReadExactAsync(stream, respLenBuf, 0, 4, token);
            if (n == 0) return false;
            var respLen = BitConverter.ToInt32(respLenBuf, 0);
            if (respLen <= 0 || respLen > MaxMessageSize)
            {
                _log($"[Directory] 响应长度异常: {respLen}");
                return false;
            }
            var respBuf = new byte[respLen];
            await ReadExactAsync(stream, respBuf, 0, respLen, token);
            var respEnv = Envelope.Parser.ParseFrom(respBuf);
            if (respEnv.Type != "HeartbeatResponse") return false;
            var resp = HeartbeatResponse.Parser.ParseFrom(respEnv.Payload);
            return resp.Accepted;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _log($"[Directory] 心跳发送失败: {ex.Message}");
            return false;
        }
    }

    private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken token)
    {
        var total = 0;
        while (total < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(offset + total, count - total), token);
            if (n == 0) return total;
            total += n;
        }
        return total;
    }
}