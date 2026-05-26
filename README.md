## DotNetGameServer 运行与配置指南

本项目由多种微服务组成（Directory/Login/Logic/Battle），使用统一的 TCP + Protobuf Envelope 协议与 Unity 客户端通信。每个角色是 `src/Servers/` 下的**独立可执行项目**（`DirectoryServer`/`LoginServer`/`LogicServer`/`BattleServer`），均复用 `src/Hosting` 共享宿主库启动。`src/Core` 为框架内核，`src/Shared` 为协议与原语。

### 1. 配置方式

配置按优先级覆盖：**CLI 参数** > **appsettings.json** > **默认值**。

#### 1.1 CLI 参数

| 参数 | 说明 | 示例 |
|---|---|---|
| `--port` | TCP 监听端口 | `--port 5501` |
| `--healthCheckUrl` | 健康检查 / 管理 / 路由 API 的 HTTP 地址 | `--healthCheckUrl http://localhost:5102` |

> 角色不再由 `--service` 决定，而是由所启动的项目（`DirectoryServer`/`LoginServer`/`LogicServer`/`BattleServer`）决定。每个项目有独立的 `appsettings.json`。

#### 1.2 appsettings.json 核心配置段

| 段 | 绑定的 Options 类 | 用途 |
|---|---|---|
| `server` | `ServerOptions` | 网络限制、超时、限流 |
| `logging` | `LoggingOptions` | 日志级别、开关、缓存容量 |
| `directory` | `DirectoryOptions` | 注册表模式（memory/redis）、TokenSecret、Redis 连接 |
| `heartbeat` | `HeartbeatOptions` | 心跳间隔、目录地址、地域标识 |

详细子键见 `src/Core/Configuration/` 中各 Options 类的属性。

#### 1.3 无环境变量

所有配置仅通过 CLI 参数和 `appsettings.json` 提供。不再读取任何环境变量。

### 2. 本地开发（Windows）

#### 2.1 一键多窗启动（推荐）

```
scripts\start-all.ps1
```

脚本自动完成：
1. 读取 `scripts/servers.json` 中的服务定义
2. 执行 `dotnet build`
3. 为每个服务打开独立窗口启动

添加/调整服务实例只需编辑 `scripts/servers.json`（指向各服务器项目）：
```json
{
    "name": "Login",
    "project": "src/Servers/LoginServer/LoginServer.csproj",
    "dll": "LoginServer.dll",
    "port": 5501,
    "healthPort": 5102
}
```

#### 2.2 单窗全服务

```
scripts\start-all.ps1 -Single
```

四个服务器作为子进程运行在同一控制台窗口（日志交织输出；如需交互式命令 REPL，建议用默认多窗模式）。

#### 2.3 单服务手动启动

```powershell
dotnet build src\Servers\DirectoryServer\DirectoryServer.csproj -c Debug
dotnet exec src\Servers\DirectoryServer\bin\Debug\net8.0\DirectoryServer.dll -- --port 5500 --healthCheckUrl http://localhost:5101
```

其余角色同理：`LoginServer.dll`(5501) / `LogicServer.dll`(5502) / `BattleServer.dll`(5503)。

#### 2.4 批处理兼容

`start_all_servers.bat` / `start_all_single_window.bat` 保持一致，内部调用 PowerShell 脚本。

### 3. Docker 运行

`docker-compose.yml` 提供示例服务（directory/login/logic/battle/redis/postgres）。部署前配置：

```
DIRECTORY_TOKEN_SECRET=please-change-to-strong-secret
REDIS_PASSWORD=please-change
```

详细配置项见 `appsettings.Production.json` 的 `directory:registry:redis` 段。

### 4. Kubernetes 部署

`k8s/secret.yaml` 和 `k8s/configmap.yaml` 提供基础样例。通过 `kubectl apply` 配置后注入 Deployment。

### 5. 安全注意事项

- **TokenSecret**：`directory:tokenSecret`（或 `TOKEN_SECRET` 环境变量）必须为强随机字符串，生产不可为空或默认值
- **注册表模式**：`directory:registry:mode` 可选 `memory`（开发）或 `redis`（生产）
- **时间偏差容忍**：`TOKEN_TIME_SKEW_SEC` 控制令牌/心跳的过期校验误差窗口
- **心跳签名**：目录心跳和请求令牌的校验已严格化，签名缺失将拒绝
- **传输安全**：建议在公网入口启用 TLS/反向代理，服务间暂未内建 mTLS
- **时钟同步**：令牌/心跳依赖 UTC 时间，请确保各节点同步 NTP

### 6. TLS/反向代理实践

核心服务对外暴露为原始 TCP（Envelope 协议），以及 HTTP 健康端点。建议在公网入口使用反向代理或边车提供 TLS：

- **HTTP 健康端点（Kestrel HTTPS）**：配置 `ASPNETCORE_Kestrel__Certificates__Default__Path` 等 Kestrel 证书变量
- **TCP 服务加密**：推荐 Nginx stream / Envoy TCP proxy / stunnel 三种方案
- **mTLS（可选）**：在网关开启客户端证书校验，Redis 亦支持 `directory:registry:redis:ssl`

### 7. 故障排查

- 启动即失败：检查 `directory:tokenSecret` 是否为空
- 心跳被拒绝：确认各服务加载同一密钥，并排查服务器时间是否同步
- 客户端请求未认证：检查是否正确附带登录令牌以及服务端能否解析 `userId` 与 `serverId`
