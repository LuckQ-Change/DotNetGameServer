# 客户端接入指南

> 本指南说明如何从任意语言/平台接入 DotNetGameServer，覆盖协议规范、服务发现、鉴权、收发消息、错误处理与重连策略。

---

## 目录

1. [协议规范](#1-协议规范)
2. [服务发现](#2-服务发现)
3. [鉴权流程](#3-鉴权流程)
4. [收发消息](#4-收发消息)
5. [错误处理](#5-错误处理)
6. [重连策略](#6-重连策略)
7. [C# 完整示例](#7-c-完整示例unity--控制台通用)
8. [Python 示例（测试 / 工具）](#8-python-示例测试--工具)
9. [协议速查](#9-协议速查)

---

## 1. 协议规范

### 1.1 帧格式

所有数据通过 **TCP 长连接** 传输，帧格式为：

```
┌─────────────────────────────┬──────────────────────────────────┐
│  Length（4 字节，小端序）     │  Envelope（Protobuf 序列化字节）  │
└─────────────────────────────┴──────────────────────────────────┘
```

- **Length**：`int32`，小端序，表示后续 Envelope 字节长度
- **Envelope**：`envelope` 消息的 Protobuf 序列化结果

### 1.2 Envelope 定义

```protobuf
// src/Shared/Protos/envelope.proto
message envelope {
  string type          = 1;  // 消息类型名，如 "SendMessageRequest"
  bytes  payload       = 2;  // 业务消息的 Protobuf 序列化字节
  string token         = 3;  // 登录后每包携带，未登录可留空
  string request_id    = 4;  // 客户端生成的唯一请求 ID（用于去重和日志关联）
  string version       = 5;  // 客户端版本（可选，如 "1.0.0"）
  int32  error_code    = 6;  // 服务端响应填写，0 = 成功，非零 = 失败
  string error_message = 7;  // 错误描述（成功时为空）
}
```

### 1.3 通信模型

```
客户端                              服务端
  │                                   │
  │── envelope(type="LoginRequest") ──►│
  │◄── envelope(type="LoginResponse") ─│
  │                                   │
  │── envelope(type="ChatRequest") ───►│
  │◄── envelope(type="ChatResponse") ──│
```

- 请求与响应通过 `request_id` 对应（服务端回包中 `request_id` 与请求一致）
- 服务端可在任意时刻主动推送（`request_id` 为空）
- `type` 字段使用 Protobuf 消息的**短名**（不含命名空间），如 `"HeartbeatRequest"`

---

## 2. 服务发现

客户端在连接前，先通过 **DirectoryServer HTTP API** 获取可用服务器列表。

### 2.1 接口

```
GET http://<directory-host>:<health-port>/api/routing/query
```

**查询参数**

| 参数 | 说明 | 示例 |
|---|---|---|
| `type` | 服务器类型 | `login` / `battle` / `logic` |
| `region` | 区域（可选） | `default` / `asia` |
| `strategy` | 选择策略（可选） | `load`（默认）/ `random` / `round_robin` |
| `limit` | 返回数量（可选，默认 10） | `3` |

**示例请求**

```bash
# 获取负载最低的登录服务器
curl "http://127.0.0.1:5101/api/routing/query?type=login&strategy=load&limit=1"
```

**响应**

```json
{
  "count": 1,
  "strategy": "load",
  "servers": [
    {
      "id": "login:5501",
      "type": "login",
      "region": "default",
      "host": "127.0.0.1",
      "port": 5501,
      "playerCount": 42,
      "capacity": 200,
      "load": 0.21,
      "status": "online",
      "lastHeartbeat": 1717000000,
      "version": "1.0.0"
    }
  ]
}
```

> **生产环境**：请求需携带 Bearer token，见[安全配置](DEVELOPER.md#71-生产环境必须配置项)。

### 2.2 推荐流程

```
启动
 │
 ├─ 1. GET /api/routing/query?type=login&limit=1
 │        拿到 host:port
 │
 ├─ 2. TCP 连接 LoginServer，完成登录，拿到 token
 │
 ├─ 3. GET /api/routing/query?type=logic&limit=1
 │        （可选：传 region 选最近区域）
 │
 └─ 4. TCP 连接 LogicServer，带 token 发送业务消息
```

---

## 3. 鉴权流程

### 3.1 Token 格式

服务端签发的 token 是一个用冒号分隔的字符串：

```
{tokenId}:{expireUnixSec}:{userId}:{hmacSha256Hex}
```

示例：

```
a3f9b2c1d4e5:1717090000:player_123:8e3f2a...（64位十六进制）
```

客户端**直接存储**这个字符串，无需解析，随每个请求的 `envelope.token` 字段原样携带即可。

### 3.2 登录流程

```
1. 构造 LoginRequest { username, password_hash, ... }
2. 发送 envelope { type="LoginRequest", payload=<序列化>, token="", request_id=<uuid> }
3. 收到 envelope { type="LoginResponse", error_code=0 }
4. 从 LoginResponse.token 字段取出 token，本地保存
5. 后续所有请求：envelope.token = <保存的token>
```

> LoginRequest / LoginResponse 由业务侧自定义并注册 Handler，token 字段由 LoginServer Handler 调用 `TokenSigner.ComposeSigned` 生成后写入响应。

### 3.3 Token 失效处理

服务端收到请求后：
- `error_code = 401`：token 无效或已过期，需重新登录
- `error_code = 403`：已登录但权限不足

客户端逻辑：

```
收到 error_code == 401
  └─ 清除本地 token
  └─ 重走登录流程（可配合自动重试）
```

---

## 4. 收发消息

### 4.1 发送流程

```
1. 将业务消息序列化为字节：  payload = Protobuf.Serialize(request)
2. 构造 Envelope：
      type       = "YourRequest"（Protobuf 消息短名）
      payload    = payload
      token      = <当前token，未登录留空>
      request_id = UUID（建议每次新建）
3. 序列化 Envelope：  envBytes = Protobuf.Serialize(envelope)
4. 构造帧头：          lenBytes = int32ToLittleEndian(len(envBytes))
5. 写入 TCP：          write(lenBytes + envBytes)
```

### 4.2 接收流程

```
1. 读取 4 字节：      length = int32FromLittleEndian(read(4))
2. 读取 length 字节： envBytes = readExact(length)
3. 反序列化：         envelope = Protobuf.Parse(envBytes)
4. 检查错误：         if envelope.error_code != 0 → 处理错误
5. 按 type 分发：     dispatch(envelope.type, envelope.payload)
6. 按 request_id 匹配等待方的 Future / Callback
```

### 4.3 并发请求

客户端可以**不等待上一个响应就发送下一个请求**，服务端并发处理、并发回包。通过 `request_id` 将响应路由回对应的等待者（Promise / Future / Callback）。

---

## 5. 错误处理

### 5.1 标准错误码

| error_code | 含义 | 客户端动作 |
|---|---|---|
| `0` | 成功 | 正常处理响应 |
| `400` | 请求格式错误 | 检查请求字段 |
| `401` | 未鉴权 / token 失效 | 重新登录 |
| `403` | 权限不足 | 提示用户 |
| `404` | 资源不存在 | 提示用户 |
| `429` | 限速 | 指数退避后重试 |
| `500` | 服务器内部错误 | 日志上报，重试 1 次 |
| `503` | 服务不可用 | 重新服务发现，换服务器连接 |

### 5.2 未知 type

若服务端回包 `type` 不在客户端已注册的处理器中，**忽略**该包（记录警告日志即可），不要断开连接。这是服务端协议扩展的正常现象。

---

## 6. 重连策略

### 6.1 断线检测

- 设置 TCP KeepAlive（建议服务端已开启，客户端同步配置）
- 维护心跳定时器：每 30 秒向服务端发送应用层心跳（如空的 `PingRequest`）
- 超过 60 秒未收到任何数据 → 判定断线

### 6.2 重连流程

```
断线
 │
 ├─ 1. 等待指数退避时间（初始 1s，最大 30s）
 │
 ├─ 2. 重新服务发现（GET /api/routing/query），
 │      避免因服务器下线导致重连到同一个死节点
 │
 ├─ 3. 建立新 TCP 连接
 │
 ├─ 4. 若本地 token 尚未过期，直接携带 token 发送业务消息
 │      （服务端无状态鉴权，无需重新登录）
 │
 └─ 5. 若 token 过期（error_code=401），重新登录获取新 token
```

### 6.3 指数退避参考实现

```
retries = 0
max_delay = 30s

while not connected:
    delay = min(2^retries, max_delay)
    wait(delay + random(0, 1))  # 加随机抖动避免惊群
    try_connect()
    if success:
        retries = 0
        break
    retries += 1
```

---

## 7. C# 完整示例（Unity / 控制台通用）

以下示例演示完整的**连接 → 登录 → 发送业务消息 → 断线重连**流程，不依赖任何第三方库（仅 Google.Protobuf）。

```csharp
using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DotNetGameServer;          // Protobuf 生成的命名空间
using Google.Protobuf;

/// <summary>
/// 最小化游戏服务器客户端，支持并发请求与自动重连。
/// </summary>
public sealed class GameServerClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private string _token = string.Empty;

    // 并发等待表：requestId → TaskCompletionSource<envelope>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<envelope>>
        _pending = new();

    private CancellationTokenSource _cts = new();

    public GameServerClient(string host, int port)
    {
        _host = host;
        _port = port;
    }

    // ─── 连接 ────────────────────────────────────────────────────────────────

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(_host, _port, ct);
        _stream = _tcp.GetStream();
        _cts = new CancellationTokenSource();
        // 后台持续读取响应
        _ = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
    }

    public void Disconnect()
    {
        _cts.Cancel();
        _stream?.Close();
        _tcp?.Close();
    }

    // ─── 发送 / 接收 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 发送请求并等待对应响应（通过 request_id 匹配）。
    /// </summary>
    public async Task<envelope> SendAsync<TReq>(
        string type, TReq request, TimeSpan timeout = default)
        where TReq : IMessage<TReq>
    {
        if (timeout == default) timeout = TimeSpan.FromSeconds(10);

        var reqId = Guid.NewGuid().ToString("N");
        var env = new envelope
        {
            Type      = type,
            Payload   = ByteString.CopyFrom(request.ToByteArray()),
            Token     = _token,
            RequestId = reqId,
            Version   = "1.0.0"
        };

        var tcs = new TaskCompletionSource<envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[reqId] = tcs;

        await WriteEnvelopeAsync(env);

        using var timeoutCts = new CancellationTokenSource(timeout);
        timeoutCts.Token.Register(() => tcs.TrySetCanceled());

        return await tcs.Task;
    }

    private async Task WriteEnvelopeAsync(envelope env)
    {
        var payload = env.ToByteArray();
        var header  = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

        var stream = _stream ?? throw new InvalidOperationException("未连接");
        await stream.WriteAsync(header);
        await stream.WriteAsync(payload);
    }

    // ─── 接收循环 ─────────────────────────────────────────────────────────────

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var lenBuf = new byte[4];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 读 4 字节帧头
                await ReadExactAsync(lenBuf, 0, 4, ct);
                var length = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);

                if (length <= 0 || length > 4 * 1024 * 1024)
                    throw new InvalidOperationException($"非法帧长度: {length}");

                // 读 Envelope 字节
                var buf = new byte[length];
                await ReadExactAsync(buf, 0, length, ct);

                var env = envelope.Parser.ParseFrom(buf);
                Dispatch(env);
            }
        }
        catch (OperationCanceledException) { /* 正常断开 */ }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Client] 读取异常: {ex.Message}");
            // 取消所有等待中的请求
            foreach (var kv in _pending)
                kv.Value.TrySetException(new Exception("连接断开"));
            _pending.Clear();
        }
    }

    private void Dispatch(envelope env)
    {
        // 1. 路由到等待的请求（有 request_id）
        if (!string.IsNullOrWhiteSpace(env.RequestId)
            && _pending.TryRemove(env.RequestId, out var tcs))
        {
            tcs.TrySetResult(env);
            return;
        }
        // 2. 服务端主动推送（无 request_id）
        OnServerPush?.Invoke(env);
    }

    private async Task ReadExactAsync(byte[] buf, int offset, int count, CancellationToken ct)
    {
        var stream = _stream ?? throw new InvalidOperationException("未连接");
        var total = 0;
        while (total < count)
        {
            var n = await stream.ReadAsync(buf.AsMemory(offset + total, count - total), ct);
            if (n == 0) throw new Exception("连接已关闭");
            total += n;
        }
    }

    // ─── 推送回调 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 服务端主动推送的处理回调。
    /// </summary>
    public event Action<envelope>? OnServerPush;

    // ─── 便捷方法 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 设置登录后获得的 token（后续请求自动携带）。
    /// </summary>
    public void SetToken(string token) => _token = token;

    public void Dispose() => Disconnect();
}
```

### 7.1 使用示例

```csharp
// 1. 服务发现（调用 DirectoryServer HTTP API）
var directoryUrl = "http://127.0.0.1:5101/api/routing/query?type=login&limit=1";
using var http = new HttpClient();
var json = await http.GetStringAsync(directoryUrl);
// 解析 json 拿到 host / port（略）
var loginHost = "127.0.0.1";
var loginPort = 5501;

// 2. 连接 LoginServer
using var client = new GameServerClient(loginHost, loginPort);
await client.ConnectAsync();

// 3. 登录
var loginResp = await client.SendAsync("LoginRequest", new LoginRequest
{
    Username = "player01",
    PasswordHash = "abc123"
});

if (loginResp.ErrorCode != 0)
{
    Console.Error.WriteLine($"登录失败: {loginResp.ErrorMessage}");
    return;
}

var loginResult = LoginResponse.Parser.ParseFrom(loginResp.Payload);
client.SetToken(loginResult.Token);   // 保存 token
Console.WriteLine($"登录成功，userId={loginResult.UserId}");

// 4. 切换到 LogicServer
// （重新服务发现，建立新连接，复用同一个 token）
using var logicClient = new GameServerClient("127.0.0.1", 5502);
await logicClient.ConnectAsync();
logicClient.SetToken(loginResult.Token);

// 5. 发送业务消息
var chatResp = await logicClient.SendAsync("SendMessageRequest", new SendMessageRequest
{
    RoomId  = "room_001",
    Content = "Hello, World!"
});

if (chatResp.ErrorCode == 0)
    Console.WriteLine("消息发送成功");
else
    Console.Error.WriteLine($"错误 {chatResp.ErrorCode}: {chatResp.ErrorMessage}");
```

### 7.2 自动重连包装

```csharp
public static async Task<GameServerClient> ConnectWithRetryAsync(
    Func<Task<(string host, int port)>> discover,
    int maxRetries = 10)
{
    var delay = TimeSpan.FromSeconds(1);
    for (var i = 0; i < maxRetries; i++)
    {
        try
        {
            var (host, port) = await discover();
            var client = new GameServerClient(host, port);
            await client.ConnectAsync();
            return client;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Retry {i+1}] 连接失败: {ex.Message}，{delay.TotalSeconds}s 后重试");
            await Task.Delay(delay);
            // 指数退避，上限 30s
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }
    throw new Exception("超过最大重试次数，连接失败");
}
```

---

## 8. Python 示例（测试 / 工具）

适合用于压测脚本、自动化测试和调试工具。依赖：`pip install protobuf`

> **注意**：需要先用 `protoc` 将 `.proto` 文件生成 Python 代码：
> ```bash
> protoc --python_out=. src/Shared/Protos/envelope.proto
> protoc --python_out=. src/Shared/Protos/directory.proto
> ```

```python
import socket
import struct
import uuid
import threading
from typing import Callable, Optional

# 生成的 Protobuf 模块（由 protoc 产生）
import envelope_pb2   # envelope 消息
# import your_pb2     # 业务消息


class GameServerClient:
    """最小化游戏服务器客户端（同步阻塞版，适合测试脚本）。"""

    def __init__(self, host: str, port: int, timeout: float = 10.0):
        self.host = host
        self.port = port
        self.timeout = timeout
        self.token = ""
        self._sock: Optional[socket.socket] = None

    # ── 连接 ──────────────────────────────────────────────────────────────

    def connect(self):
        self._sock = socket.create_connection((self.host, self.port), timeout=self.timeout)

    def close(self):
        if self._sock:
            self._sock.close()
            self._sock = None

    def __enter__(self):
        self.connect()
        return self

    def __exit__(self, *_):
        self.close()

    # ── 发送 ──────────────────────────────────────────────────────────────

    def send(self, msg_type: str, payload_bytes: bytes) -> envelope_pb2.envelope:
        """发送请求并同步等待响应。"""
        req_id = uuid.uuid4().hex

        env = envelope_pb2.envelope()
        env.type       = msg_type
        env.payload    = payload_bytes
        env.token      = self.token
        env.request_id = req_id
        env.version    = "1.0.0"

        env_bytes = env.SerializeToString()
        # 小端序 int32 帧头
        header = struct.pack("<i", len(env_bytes))
        self._sock.sendall(header + env_bytes)

        return self._recv()

    def _recv(self) -> envelope_pb2.envelope:
        # 读 4 字节帧头
        header = self._recv_exact(4)
        (length,) = struct.unpack("<i", header)

        if length <= 0 or length > 4 * 1024 * 1024:
            raise ValueError(f"非法帧长度: {length}")

        body = self._recv_exact(length)
        resp = envelope_pb2.envelope()
        resp.ParseFromString(body)
        return resp

    def _recv_exact(self, n: int) -> bytes:
        buf = bytearray()
        while len(buf) < n:
            chunk = self._sock.recv(n - len(buf))
            if not chunk:
                raise ConnectionError("连接已关闭")
            buf.extend(chunk)
        return bytes(buf)
```

### 8.1 使用示例

```python
import requests
import json

# 1. 服务发现
resp = requests.get("http://127.0.0.1:5101/api/routing/query",
                    params={"type": "login", "limit": 1})
server = resp.json()["servers"][0]
host, port = server["host"], server["port"]

# 2. 连接并登录
import login_pb2   # 业务 proto 生成的模块

with GameServerClient(host, port) as client:
    req = login_pb2.LoginRequest()
    req.username      = "player01"
    req.password_hash = "abc123"

    resp_env = client.send("LoginRequest", req.SerializeToString())
    if resp_env.error_code != 0:
        raise Exception(f"登录失败: {resp_env.error_message}")

    login_resp = login_pb2.LoginResponse()
    login_resp.ParseFromString(resp_env.payload)
    client.token = login_resp.token
    print(f"登录成功，userId={login_resp.user_id}")

    # 3. 发送业务消息
    import chat_pb2
    chat_req = chat_pb2.SendMessageRequest()
    chat_req.room_id = "room_001"
    chat_req.content = "Hello from Python!"

    chat_resp_env = client.send("SendMessageRequest", chat_req.SerializeToString())
    if chat_resp_env.error_code == 0:
        print("消息发送成功")
    else:
        print(f"错误 {chat_resp_env.error_code}: {chat_resp_env.error_message}")
```

### 8.2 压测辅助脚本

```python
import concurrent.futures
import time

def single_session(user_id: str) -> float:
    start = time.monotonic()
    with GameServerClient("127.0.0.1", 5502) as c:
        c.token = "<预生成的token>"
        req = chat_pb2.SendMessageRequest()
        req.room_id = "bench_room"
        req.content = f"msg from {user_id}"
        resp = c.send("SendMessageRequest", req.SerializeToString())
        assert resp.error_code == 0
    return time.monotonic() - start

# 并发 100 个连接
with concurrent.futures.ThreadPoolExecutor(max_workers=100) as pool:
    latencies = list(pool.map(single_session, [f"u{i}" for i in range(100)]))

print(f"P50={sorted(latencies)[50]*1000:.1f}ms  "
      f"P99={sorted(latencies)[99]*1000:.1f}ms")
```

---

## 9. 协议速查

### 帧结构

```
[Length: 4B LE int32][Envelope: N 字节 Protobuf]
```

### Envelope 字段

| 字段 | 方向 | 说明 |
|---|---|---|
| `type` | 请求 / 响应 | Protobuf 消息短名（不含命名空间） |
| `payload` | 请求 / 响应 | 业务消息 Protobuf 序列化字节 |
| `token` | 请求 | 登录后获得的鉴权 token，未登录留空 |
| `request_id` | 请求 | 客户端生成 UUID，用于匹配响应 |
| `version` | 请求 | 客户端版本字符串（可选） |
| `error_code` | 响应 | 0 成功，非零失败（见错误码表） |
| `error_message` | 响应 | 错误描述，成功时为空 |

### 错误码速查

| 码 | 含义 | 建议处理 |
|---|---|---|
| 0 | 成功 | 解析 payload |
| 400 | 请求格式错误 | 检查字段 |
| 401 | token 无效 / 过期 | 重新登录 |
| 403 | 权限不足 | 提示用户 |
| 429 | 限速 | 指数退避重试 |
| 500 | 服务器内部错误 | 上报日志，重试 1 次 |
| 503 | 服务不可用 | 重新服务发现，换节点 |

### 连接注意事项

| 项目 | 说明 |
|---|---|
| 字节序 | 帧头为**小端序** int32 |
| 读取方式 | 必须按帧头长度**精确读取**，不可依赖 recv 返回完整帧 |
| TLS（可选） | 服务端开启 TLS 时，在 TCP 连接后先完成 TLS 握手，再发送帧 |
| KeepAlive | 建议开启 TCP KeepAlive，防止 NAT 超时静默断线 |
| 并发 | 单连接内可并发发包，通过 `request_id` 匹配响应 |
| token 续期 | token 过期（error_code=401）后重新登录，无需重建 TCP 连接 |
