using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using DotNetGameServer;

namespace Core;

/// <summary>
/// 基础游戏服务器类，采用ECS架构
/// </summary>
public abstract class BaseGameServer : IServerContext
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<string, IMessageHandler> _messageHandlers = new();
    private readonly ConcurrentDictionary<Type, IServerComponent> _components = new();

    public string ServerName { get; }

    protected BaseGameServer(string serverName, Action<string> log)
    {
        ServerName = serverName;
        _log = log;
    }

    /// <summary>
    /// 添加组件
    /// </summary>
    public void AddComponent<T>(T component) where T : class, IServerComponent
    {
        _components[typeof(T)] = component;
    }

    /// <summary>
    /// 获取组件
    /// </summary>
    public T? GetComponent<T>() where T : class, IServerComponent
    {
        return _components.TryGetValue(typeof(T), out var component) ? component as T : null;
    }

    /// <summary>
    /// 注册消息处理器
    /// </summary>
    public void RegisterMessageHandler(IMessageHandler handler)
    {
        _messageHandlers[handler.MessageType] = handler;
    }

    /// <summary>
    /// 启动服务器
    /// </summary>
    public async Task StartAsync(int port)
    {
        if (_listener != null) return;

        // 初始化所有组件
        foreach (var component in _components.Values)
        {
            await component.InitializeAsync();
        }

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Log($"服务器已启动，端口 {port}");
        
        _ = Task.Run(() => AcceptLoop(_cts.Token));
    }

    /// <summary>
    /// 停止服务器
    /// </summary>
    public async Task StopAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;

        // 销毁所有组件
        foreach (var component in _components.Values)
        {
            await component.DestroyAsync();
        }

        Log("服务器已停止");
    }

    /// <summary>
    /// 日志记录
    /// </summary>
    public void Log(string message)
    {
        _log($"[{ServerName}] {message}");
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
                Log($"接受连接异常: {ex.Message}");
                break;
            }
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken token)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "未知";
        Log($"客户端连接: {remote}");
        
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
                    
                    // 查找消息处理器
                    if (_messageHandlers.TryGetValue(envelope.Type, out var handler))
                    {
                        var responsePayload = await handler.HandleAsync(envelope.Payload.ToByteArray(), this);
                        if (responsePayload != null)
                        {
                            var responseType = GetResponseType(envelope.Type);
                            await WriteEnvelopeAsync(stream, responseType, responsePayload, token);
                        }
                    }
                    else
                    {
                        Log($"未知消息类型: {envelope.Type}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"客户端异常: {ex.Message}");
            }
            
            Log($"客户端断开: {remote}");
        }
    }

    /// <summary>
    /// 获取响应消息类型，子类可重写
    /// </summary>
    protected virtual string GetResponseType(string requestType)
    {
        return requestType.Replace("Request", "Response");
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