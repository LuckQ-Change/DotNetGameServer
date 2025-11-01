using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using DotNetGameServer;

namespace Window;

public class GameServer
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly Action<string> _log;

    public string Name { get; }

    public GameServer(string name, Action<string> log)
    {
        Name = name;
        _log = log;
    }

    public void Start(int port)
    {
        if (_listener != null) return;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _log($"[{Name}] 服务器已启动，端口 {port}");
        Task.Run(() => AcceptLoop(_cts!.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _log($"[{Name}] 服务器已停止");
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClient(client, token));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"接受连接异常: {ex.Message}");
                break;
            }
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken token)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "未知";
        _log($"[{Name}] 客户端连接: {remote}");
        using (client)
        {
            var stream = client.GetStream();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var lenBuf = new byte[4];
                    var read = await ReadExactAsync(stream, lenBuf, 0, 4, token);
                    if (read == 0) break;
                    var length = BitConverter.ToInt32(lenBuf, 0);
                    var buf = new byte[length];
                    await ReadExactAsync(stream, buf, 0, length, token);

                    var envelope = Envelope.Parser.ParseFrom(buf);
                    switch (envelope.Type)
                    {
                        case "LoginRequest":
                            var req = LoginRequest.Parser.ParseFrom(envelope.Payload);
                            var ok = req.Username == "admin" && req.Password == "123456";
                            var resp = new LoginResponse { Success = ok, Message = ok ? "登录成功" : "登录失败" };
                            await WriteEnvelopeAsync(stream, "LoginResponse", resp.ToByteArray(), token);
                            _log($"[{Name}] 处理登录请求: {req.Username}, 结果={ok}");
                            break;
                        default:
                            _log($"[{Name}] 未知消息类型: {envelope.Type}");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"[{Name}] 客户端异常: {ex.Message}");
            }
            _log($"[{Name}] 客户端断开: {remote}");
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

    private static async Task WriteEnvelopeAsync(NetworkStream stream, string type, byte[] payload, CancellationToken token)
    {
        var env = new Envelope { Type = type, Payload = ByteString.CopyFrom(payload) };
        var bytes = env.ToByteArray();
        var lenBuf = BitConverter.GetBytes(bytes.Length);
        await stream.WriteAsync(lenBuf.AsMemory(0, 4), token);
        await stream.WriteAsync(bytes.AsMemory(), token);
        await stream.FlushAsync(token);
    }
}
