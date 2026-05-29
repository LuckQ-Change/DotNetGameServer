# DotNetGameServer 开发手册

> 适用版本：.NET 8 / 当前 main 分支

---

## 目录

1. [架构概览](#1-架构概览)
2. [新增服务器类型](#2-新增服务器类型)
3. [消息处理器开发](#3-消息处理器开发)
4. [业务开发模式](#4-业务开发模式)
5. [内置工具使用](#5-内置工具使用)
6. [配置参考](#6-配置参考)
7. [安全配置](#7-安全配置)
8. [部署指南](#8-部署指南)

---

## 1. 架构概览

### 1.1 分层结构

```
src/
├── Shared/          # 无依赖基础库：Protobuf 生成代码、TokenSigner、TimeUtil
├── Core/            # 服务器核心：BaseGameServer、消息管道、组件体系、诊断
├── Hosting/         # ASP.NET Core 宿主：GameServerHost、健康检查、Admin HTTP
├── Servers/
│   ├── DirectoryServer/   # 目录服务：心跳注册、服务发现、Redis 注册表
│   ├── LoginServer/       # 登录服务（业务扩展点）
│   ├── LogicServer/       # 逻辑服务（业务扩展点）
│   └── BattleServer/      # 战斗服务（业务扩展点）
├── ControlPlane/    # 运维管控面：HTTP BFF + Vue 前端（生产仅限内网访问）
web/
└── control-plane-ui/  # 管控 UI 前端（Vite + Lit）
tests/
└── Tests/           # xUnit 单元测试
```

### 1.2 服务器拓扑

```
客户端
  │  TCP（可选 TLS）
  ▼
LoginServer ──────────────► DirectoryServer（TCP 5500 / HTTP 5101）
LogicServer ──心跳────────►        │
BattleServer ─────────────►        │ Redis（生产）/ 内存（开发）
                                   │
ControlPlane（HTTP 5200）◄─────────┘
  │ 代理 /api/routing/query、/api/gm/*
  └► 各服务 /health /admin/command
```

### 1.3 请求生命周期

```
TCP 帧（4字节长度 + Protobuf Envelope）
  │
AcceptLoop
  │  黑名单 / 并发上限检查
  ▼
HandleClient（Channel 异步解耦读写）
  │  TLS 握手（可选）、字节/消息限速
  ▼
MessagePipeline
  ├─ TelemetryMiddleware   记录指标、Activity
  ├─ AuthMiddleware        解析 token → AuthInfo（UserId, TokenId, Exp）
  └─ HandlerMiddleware     按 envelope.type 路由到 IMessageHandler<TReq, TResp>
```

### 1.4 组件体系

`BaseGameServer` 维护一个 `IServerComponent` 字典，组件通过 `AddComponent<T>` 注册、`GetComponent<T>` 获取。内置组件：

| 组件接口 | 默认实现 | 说明 |
|---|---|---|
| `ITokenSecretProvider` | `TokenSecretProviderComponent` | 提供签名/验签密钥 |
| `ITokenRevocationStore` | `InMemoryTokenRevocationStore` | Token 吊销（生产用 Redis 实现） |
| `IServerRegistry` | `MemoryServerRegistry` | 服务注册表（DirectoryServer 专用） |

---

## 2. 新增服务器类型

以新增 `ChatServer` 为例，完整流程如下。

### 2.1 创建项目

```
src/Servers/ChatServer/
  ChatServer.csproj
  ChatGameServer.cs
  Program.cs
  appsettings.json
  appsettings.Production.json
```

**ChatServer.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Hosting\Hosting.csproj" />
  </ItemGroup>
</Project>
```

### 2.2 实现服务器类

```csharp
// ChatGameServer.cs
using Core;
using Core.Configuration;
using Microsoft.Extensions.Logging;

namespace ChatServer;

public sealed class ChatGameServer(ILogger<ChatGameServer> logger, ServerOptions options)
    : BaseGameServer(nameof(ChatGameServer), logger, options)
{
    protected override void OnConfigureHandlers()
    {
        RegisterMessageHandler<SendMessageRequest, SendMessageResponse>(
            new SendMessageHandler());
        // 继续注册其他 Handler...
    }
}
```

> 若服务器需要心跳向 DirectoryServer 注册，在 `Program.cs` 里添加 `HeartbeatSenderService` 组件（见 BattleServer/Program.cs 示例）。

### 2.3 编写 Program.cs

```csharp
// Program.cs
using ChatServer;
using Core.Configuration;
using Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

await GameServerHost.RunAsync(args, new GameServerDefinition
{
    Role = "chat",
    DefaultPort = 5504,
    RequiresProductionTokenSecret = true,
    CreateServer = ctx =>
    {
        var logger = ctx.Services.GetRequiredService<ILogger<ChatGameServer>>();
        return new ChatGameServer(logger, ctx.ServerOptions);
    }
});
```

### 2.4 配置文件

```jsonc
// appsettings.json
{
  "server": {
    "maxConcurrentConnections": 500,
    "maxMessageSizeBytes": 65536
  },
  "directory": { "tokenSecret": "dev-secret" },
  "heartbeat": {
    "serverType": "chat",
    "region": "default",
    "host": "127.0.0.1",
    "port": 5504,
    "capacity": 500,
    "intervalSeconds": 5,
    "directoryHost": "127.0.0.1",
    "directoryPort": 5500
  },
  "logging": { "enabled": true, "minimumLevel": "Information" }
}
```

### 2.5 将新服务加入 ControlPlane

在 `src/ControlPlane/appsettings.json` 的 `ControlPlane.Servers` 数组追加：

```jsonc
{ "Id": "chat", "Type": "chat", "Region": "default",
  "Host": "127.0.0.1", "TcpPort": 5504, "HealthPort": 5105 }
```

---

## 3. 消息处理器开发

### 3.1 定义 Protobuf 消息

在 `src/Shared/` 的 `.proto` 文件中添加消息（示例）：

```protobuf
message SendMessageRequest {
  string room_id = 1;
  string content = 2;
}

message SendMessageResponse {
  bool success = 1;
  int32 code    = 2;
  string message = 3;
}
```

运行 `dotnet build` 后 Grpc.Tools 自动生成 C# 类。

### 3.2 实现 IMessageHandler

```csharp
// Handlers/SendMessageHandler.cs
using Core;
using Microsoft.Extensions.Logging;

namespace ChatServer;

public sealed class SendMessageHandler : IMessageHandler<SendMessageRequest, SendMessageResponse>
{
    public async Task<SendMessageResponse?> HandleAsync(
        SendMessageRequest request, IServerContext context)
    {
        // 未登录用户拒绝
        if (context.Auth == null)
            return new SendMessageResponse { Success = false, Code = 401, Message = "unauthorized" };

        var userId = context.Auth.UserId;
        context.Log($"用户 {userId} 发言至房间 {request.RoomId}");

        // 业务逻辑...
        await Task.CompletedTask;

        return new SendMessageResponse { Success = true };
    }
}
```

**关键规则**：
- 返回 `null` 表示无需响应（服务器不回包）。
- 响应的 `Success=false` 会触发框架将 `Code` / `Message` 写入 `envelope.error_code` / `envelope.error_message`。
- Handler 内抛出的异常会被 `HandlerInvoker` 捕获，响应为 `{errorCode:500, errorMessage: ex.Message}`。

### 3.3 注册 Handler

在服务器类中调用：

```csharp
RegisterMessageHandler<SendMessageRequest, SendMessageResponse>(new SendMessageHandler());
```

框架同时以 **短名**（`SendMessageRequest`）和 **全名**（含命名空间）注册，客户端发送任一形式均可路由。

### 3.4 用户身份隔离

如果请求需要携带用户 ID（用于鉴权以外的业务逻辑），实现 `IUserIdentifiedRequest`：

```csharp
public partial class SendMessageRequest : IUserIdentifiedRequest
{
    public string? GetUserIdOrNull() => string.IsNullOrWhiteSpace(UserId) ? null : UserId;
}
```

框架会在 Handler 调用前将 `context.Auth.UserId` 与请求中的 `GetUserIdOrNull()` 对比，防止用户篡改他人 ID。

---

## 4. 业务开发模式

### 4.1 Token 签发

登录成功后，由 LoginServer 签发 token 给客户端：

```csharp
var tokenId  = Guid.NewGuid().ToString("N");
var expiry   = DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds();
var userId   = "player_123";
var serverId = "server";     // 与 AuthMiddleware TryVerify 中 serverId 保持一致
var secret   = secretProvider.GetSecret();

var sig   = TokenSigner.ComputeSignature(tokenId, expiry, userId, serverId, secret);
var token = TokenSigner.ComposeSigned(tokenId, expiry, userId, sig);
// token 形如：{tokenId}:{expiry}:{userId}:{sigHex}
```

### 4.2 Token 吊销

在登出或封号时吊销 token：

```csharp
var revocation = server.GetComponent<ITokenRevocationStore>();
revocation?.Revoke(tokenId, DateTimeOffset.FromUnixTimeSeconds(expireUnixSec));
```

生产环境（Redis 模式）下吊销立即在所有服务实例生效。

### 4.3 在 Handler 中读取身份

```csharp
public Task<MyResponse?> HandleAsync(MyRequest req, IServerContext context)
{
    if (context.Auth is not { } auth)
        return Task.FromResult<MyResponse?>(new MyResponse { Success = false, Code = 401 });

    var userId  = auth.UserId;   // 从 token payload 解出的真实用户 ID
    var tokenId = auth.TokenId;
    var expiry  = auth.ExpireUnixSeconds;
    // ...
}
```

### 4.4 自定义组件

实现 `IServerComponent` 接口，在 `Program.cs` 的 `CreateServer` 回调中挂载：

```csharp
public interface IPlayerStore : IServerComponent { /* ... */ }

public class InMemoryPlayerStore : IPlayerStore
{
    public string Name => "PlayerStore";
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DestroyAsync()    => Task.CompletedTask;
    // ...
}

// Program.cs CreateServer 回调内：
server.AddComponent<IPlayerStore>(new InMemoryPlayerStore());

// Handler 内使用：
var store = context.GetComponent<IPlayerStore>();
```

### 4.5 自定义诊断命令

实现 `ICommand` 并在启动时注册，即可在 Admin REPL 和 HTTP 端点中使用：

```csharp
public sealed class RoomCommand : ICommand
{
    public string Name => "room";

    public Task<object?> ExecuteAsync(string[] args, ICommandContext context)
    {
        if (args is ["list"])
            return Task.FromResult<object?>(new { rooms = GetRoomList() });

        return Task.FromResult<object?>(new { error = "usage: room list" });
    }
}

// 启动时（Program.cs 或 CreateServer 回调后）：
CommandRegistry.Register(new RoomCommand());
```

---

## 5. 内置工具使用

### 5.1 Admin HTTP 端点

所有服务暴露 `POST /admin/command`，接受 JSON 体 `{ "cmd": "<命令>" }`。

**触发条件**：
- 开发环境（`ASPNETCORE_ENVIRONMENT=Development`）：无需鉴权
- 生产环境：需设置环境变量 `ADMIN_HTTP_ENABLED=true`、`ADMIN_TOKEN=<secret>`，并在 `ADMIN_HTTP_ALLOWED_IPS` 中列出允许的 IP

```bash
# 开发环境示例
curl -X POST http://127.0.0.1:5101/admin/command \
  -H "Content-Type: application/json" \
  -d '{"cmd":"status"}'

# 生产环境示例
curl -X POST http://127.0.0.1:5101/admin/command \
  -H "Authorization: Bearer <ADMIN_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"cmd":"status"}'
```

### 5.2 本地 Admin REPL

开发环境启动服务后，直接在控制台输入命令（`AdminConsoleHostedService` 驱动）：

```
> status
> log recent 50
> blacklist list
```

### 5.3 内置命令速查

#### 服务器状态

| 命令 | 说明 |
|---|---|
| `status` | 当前连接数、黑名单数、监听端口 |
| `limits` | 所有限速 / 超时配置值 |
| `handlers` | 已注册的所有消息类型名 |
| `commands` | 所有可用命令及别名列表 |

#### 黑名单管理

```
blacklist list
blacklist add <IP> [seconds]   # 默认 120s
blacklist remove <IP>
```

#### 日志控制（`log` 命令）

```
log status                         # 查看当前日志状态
log on / off                       # 全局开关
log level <Trace|Debug|Information|Warning|Error|Critical>
log heartbeat on/off               # 单独开关心跳日志类别
log category <name> on/off         # 任意类别开关
log recent [count]                 # 查看最近 N 条（默认 100）
log cache status / clear / on / off / size <N>
log export [filename] [count]      # 导出到 exports/ 目录（JSONL 格式）
```

#### 时间控制（`time` 命令，测试利器）

```
time status                        # 查看当前逻辑时间与偏移量
time set <ISO8601 | UnixSeconds>   # 设置绝对时间
time offset <seconds>              # 设置时间偏移（正/负秒数）
time advance <seconds>             # 在当前时间上向前推进
time freeze <ISO8601 | UnixSeconds># 冻结时间（进入手动模式）
time reset                         # 恢复系统时钟
```

示例——模拟 token 过期测试：

```bash
# 先将时间推进 25 小时，使 24h token 过期
{"cmd": "time advance 90000"}
# 验证系统时间
{"cmd": "time status"}
# 恢复
{"cmd": "time reset"}
```

### 5.4 ControlPlane 管控 UI

默认地址：`http://127.0.0.1:5200`（仅允许回环访问）

| 面板 | 说明 |
|---|---|
| 服务健康 | 探测所有服务 `/health`，展示延迟与状态 |
| 路由查询 | 调用 DirectoryServer 的分流接口，支持 load / random / round_robin 策略 |
| GM 指令 | 列出并执行所有已注册的 Admin 命令 |

生产扩展访问 IP（服务器环境变量）：

```
CONTROL_PLANE_ALLOWED_IPS=10.0.1.0,10.0.1.1
```

### 5.5 健康检查端点

每个服务提供两个端点（由 `GameServerHost` 统一注册）：

| 路径 | 说明 |
|---|---|
| `/health` | 综合健康（liveness + readiness） |
| `/ready` | 仅 readiness（服务器 TCP 已启动） |

响应格式：
```json
{
  "status": "Healthy",
  "checks": [
    { "name": "liveness", "status": "Healthy", "duration": 0.12 },
    { "name": "readiness", "status": "Healthy", "duration": 0.08 }
  ]
}
```

---

## 6. 配置参考

### 6.1 server（ServerOptions）

```jsonc
"server": {
  "maxMessageSizeBytes": 1048576,      // 单消息最大字节（默认 1MB）
  "maxConcurrentConnections": 200,      // 最大并发连接数
  "receiveTimeoutMs": 10000,            // 读取超时（ms）
  "acceptBackoffMs": 200,               // accept 失败后退避基准（ms，指数退避上限 10s）
  "maxBytesPerSecond": 1000000,         // 单连接字节限速（B/s）
  "maxMessagesPerSecond": 200,          // 单连接消息限速（条/s）
  "idleTimeoutMs": 120000,              // 空闲超时（ms），超时后断开
  "ipMaxBytesPerSecond": 0,             // 同 IP 汇总字节限速（0=禁用）
  "ipMaxMessagesPerSecond": 0,          // 同 IP 汇总消息限速（0=禁用）
  "tls": {
    "enabled": false,                   // 是否启用 TCP TLS
    "certificatePath": "cert.pfx",      // PFX 证书路径
    "certificatePassword": "",          // 证书密码
    "requireClientCertificate": false   // 是否强制 mTLS
  }
}
```

### 6.2 directory（DirectoryOptions，各服务通用）

```jsonc
"directory": {
  "tokenSecret": "dev-secret",          // 心跳 / 用户 token 签名密钥
  "registry": {
    "mode": "memory",                   // "memory" | "redis"
    "offlineTtlSeconds": 120,           // 心跳超时后标记 offline
    "removeTtlSeconds": 600,            // 超时后从注册表移除
    "cleanupIntervalSeconds": 10,       // 清理周期
    "redis": {
      "connectionString": "localhost:6379",
      "ssl": false,
      "sslHost": "",
      "validateServerCertificate": true,
      "clientCertificate": { "path": "", "password": "" }
    }
  }
}
```

### 6.3 heartbeat（HeartbeatOptions，非目录服务）

```jsonc
"heartbeat": {
  "serverId": "",                       // 留空时自动生成 "{serverType}:{port}"
  "serverType": "battle",
  "host": "127.0.0.1",                 // 对外暴露给客户端的地址
  "port": 5503,
  "region": "default",
  "capacity": 100,
  "intervalSeconds": 5,
  "directoryHost": "127.0.0.1",        // DirectoryServer TCP 地址
  "directoryPort": 5500
}
```

### 6.4 logging（LoggingOptions）

```jsonc
"logging": {
  "enabled": true,
  "minimumLevel": "Information",        // Trace|Debug|Information|Warning|Error|Critical
  "cacheCapacity": 2000,               // LogCenter 内存日志条数上限
  "heartbeatEnabled": false,           // 默认关闭心跳日志（非常高频）
  "forwardToILogger": false            // 是否同时输出到 ILogger（控制台）
}
```

### 6.5 telemetry（遥测，可选）

```jsonc
"telemetry": {
  "metrics": {
    "enabled": false,
    "otlp": { "endpoint": "http://otel-collector:4318" }
  }
}
```

### 6.6 ControlPlane（ControlPlaneOptions）

```jsonc
"ControlPlane": {
  "Directory": {
    "BaseUrl": "http://127.0.0.1:5101",  // DirectoryServer 的 HTTP 端口
    "Token": ""                           // 访问 /api/routing/query 的 Bearer token
  },
  "Admin": {
    "BaseUrl": "http://127.0.0.1:5101",   // 转发 GM 命令的目标（通常指向 DirectoryServer）
    "Token": ""                            // ADMIN_TOKEN 的值
  },
  "Servers": [/* ServerEndpoint 数组 */]
}
```

---

## 7. 安全配置

### 7.1 生产环境必须配置项

| 配置 | 说明 |
|---|---|
| `directory.tokenSecret` | 不得为 `dev-secret` / `changeme`，生产启动时自动检测并拒绝 |
| `ADMIN_HTTP_ENABLED=true` + `ADMIN_TOKEN` | 开放 Admin HTTP 时必须同时配置 token |
| `DIRECTORY_API_TOKEN` | DirectoryServer 的路由 API（`/api/routing/query`）访问令牌 |
| `CONTROL_PLANE_ALLOWED_IPS` | ControlPlane 允许的 IP，默认仅回环，生产须显式配置运维网段 |

### 7.2 TCP TLS

在 `appsettings.Production.json` 中启用：

```jsonc
"server": {
  "tls": {
    "enabled": true,
    "certificatePath": "/run/secrets/server.pfx",
    "certificatePassword": ""
  }
}
```

生成自签名证书（开发测试用）：

```bash
openssl req -x509 -newkey rsa:4096 -keyout key.pem -out cert.pem -days 365 -nodes
openssl pkcs12 -export -out server.pfx -inkey key.pem -in cert.pem -passout pass:
```

### 7.3 多实例 Token 吊销

DirectoryServer 在 `directory.registry.mode=redis` 时自动启用 `RedisTokenRevocationStore`，吊销状态跨实例实时共享。其他服务（LoginServer / LogicServer / BattleServer）如需分布式吊销，参照 DirectoryServer 的 `Program.cs` 挂载 `RedisTokenRevocationStore`。

### 7.4 Token 时钟偏差

默认容忍 30 秒时钟偏差，可在启动后通过代码调整：

```csharp
TokenSigner.SetAllowedSkewSeconds(15); // 更严格
```

---

## 8. 部署指南

### 8.1 端口规划（默认值）

| 服务 | TCP 游戏端口 | HTTP 健康 / Admin 端口 |
|---|---|---|
| DirectoryServer | 5500 | 5101 |
| LoginServer | 5501 | 5102 |
| LogicServer | 5502 | 5103 |
| BattleServer | 5503 | 5104 |
| ControlPlane | — | 5200 |

### 8.2 环境变量参考

```bash
ASPNETCORE_ENVIRONMENT=Production   # 触发生产检查：tokenSecret、Admin auth
ADMIN_HTTP_ENABLED=true             # 在生产环境启用 Admin HTTP
ADMIN_TOKEN=<强随机串>               # Admin 命令鉴权 token
ADMIN_HTTP_ALLOWED_IPS=10.0.0.5    # 允许调用 Admin 的 IP（逗号分隔）
DIRECTORY_API_TOKEN=<强随机串>       # DirectoryServer 路由 API token
CONTROL_PLANE_ALLOWED_IPS=10.0.0.1 # ControlPlane 允许访问的 IP
```

### 8.3 启动顺序

1. **DirectoryServer** — 其他服务的心跳目标，必须最先启动
2. **LoginServer / LogicServer / BattleServer** — 依赖 Directory
3. **ControlPlane** — 依赖各服务 /health 端点，最后启动

### 8.4 生产配置模板（appsettings.Production.json）

```jsonc
// DirectoryServer appsettings.Production.json
{
  "directory": {
    "tokenSecret": "__FROM_SECRET__",
    "registry": {
      "mode": "redis",
      "redis": { "connectionString": "redis:6379" }
    }
  },
  "server": {
    "maxConcurrentConnections": 1000,
    "tls": {
      "enabled": true,
      "certificatePath": "/run/secrets/server.pfx"
    }
  }
}
```

占位符 `__FROM_SECRET__` 在 CI/CD 中通过 Secret 替换，或直接通过环境变量覆盖：

```bash
directory__tokenSecret=<真实密钥>
```

### 8.5 健康检查集成（Kubernetes / Docker）

```yaml
livenessProbe:
  httpGet:
    path: /health
    port: 5101
  initialDelaySeconds: 5
readinessProbe:
  httpGet:
    path: /ready
    port: 5101
  initialDelaySeconds: 3
```

### 8.6 OpenTelemetry 指标

生产环境启用后，指标通过 OTLP 上报到收集器，关键指标：

| 指标 | 说明 |
|---|---|
| `gameserver.connections` | 当前活跃连接数（按 server 标签区分） |
| `gameserver.messages` | 消息吞吐（通过 TelemetryMiddleware 记录） |
| `gameserver.unknown_types` | 路由失败的未知消息类型数 |
