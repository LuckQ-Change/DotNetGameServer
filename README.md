## DotNetGameServer 运行与配置指南（关键安全项）

本项目由多种服务组成（Directory/Login/Logic/Battle/Resource），使用统一的 TCP + Protobuf Envelope 协议与 Unity 客户端通信。生产环境下，必须正确设置鉴权密钥与注册表等关键参数。

### 1. 关键环境变量
- DIRECTORY_TOKEN_SECRET：用于签发/校验令牌与心跳签名的 HMAC 密钥（必须是强随机字符串，生产必填且不可为默认值）
- DIRECTORY_REGISTRY：目录注册表模式（`memory` 或 `redis`），生产建议使用 `redis`
- REDIS_CONNECTION：当启用 `redis` 时的连接串（示例：`redis:6379,password=StrongPassword`；生产请避免 `localhost` 与默认口令）
- SERVICE / PORT：服务类型与端口（由 ServerHost 读取）

> 生产环境未提供有效的 `DIRECTORY_TOKEN_SECRET` 会导致服务启动失败；如果启用 `redis` 模式但未提供有效 `REDIS_CONNECTION` 也会启动失败。

### 2. 本地开发（Windows）
1) 一键多窗启动：
```
start_all_servers.bat
```
脚本会自动设置开发环境，并为 `DIRECTORY_TOKEN_SECRET` 生成临时值（仅用于本地调试）。

2) 单服务启动（示例：目录服务）：
```
dotnet build .\src\ServerHost\ServerHost.csproj -c Debug
dotnet exec .\src\ServerHost\bin\Debug\net8.0\ServerHost.dll -- --service=directory --port=5500 --healthCheckUrl=http://localhost:5101
```

### 3. Docker 运行
`docker-compose.yml` 已提供示例服务（directory/login/logic/battle/resource/redis/postgres）。请在部署前通过环境变量或 `.env` 配置强密钥与连接串：
```
DIRECTORY_TOKEN_SECRET=please-change-to-strong-secret
REDIS_PASSWORD=please-change
REDIS_CONNECTION=redis:6379,password=please-change
```
注意：
- 生产不要使用 `changeme`/`dev-secret`/`localhost` 等默认或弱配置
- 建议将密钥与密码改为通过 Docker Secret 或外部秘密管理注入

### 4. Kubernetes 部署
`k8s/secret.yaml` 和 `k8s/configmap.yaml` 提供了基础样例。请修改为实际生产值并通过 `kubectl apply` 配置：
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: game-server-secret
type: Opaque
stringData:
  DIRECTORY_TOKEN_SECRET: "<强随机密钥>"
  REDIS_PASSWORD: "<强口令>"
  REDIS_CONNECTION: "redis:6379,password=<强口令>"
```
并在 Deployment 中以环境变量注入上述 Secret。

### 5. 安全注意事项
- 生产环境强制校验：项目在启动阶段会检查 `DIRECTORY_TOKEN_SECRET`、`REDIS_CONNECTION`（当使用 `redis`）与数据库连接，非法或缺失将阻止启动
- Token 使用：服务端统一通过 `TokenSecretProvider` 加载密钥，开发环境允许 `dev-secret`，生产不允许回退
- 心跳与消息：目录心跳和请求令牌的校验已严格化，签名/密钥缺失将拒绝
- 传输安全：建议在公网入口启用 TLS/反向代理（后续可考虑服务间 mTLS）
- 时钟一致性：令牌/心跳依赖 UTC 时间，请确保宿主同步 NTP，避免因时间漂移导致误判

#### 时间偏差容忍（Time Skew）
- 可以通过环境变量 `TOKEN_TIME_SKEW_SEC` 配置令牌/心跳的过期校验容忍窗口（默认 60 秒，上限 600 秒）
- 该窗口用于容忍客户端或不同服务节点间的时钟微小漂移，避免误判过期

### 5.1 TLS/反向代理实践
本项目的核心服务对外暴露为原始 TCP（Envelope 协议），以及内置的健康端点（HTTP）。建议在公网入口统一使用反向代理或边车提供 TLS：

- HTTP 健康端点（Kestrel HTTPS）：
  - 将健康检查 URL 设为 `https://0.0.0.0:5101`，并为 Kestrel 配置证书（以下二选一）
  - 环境变量：
    - `ASPNETCORE_Kestrel__Certificates__Default__Path=/certs/server.pfx`
    - `ASPNETCORE_Kestrel__Certificates__Default__Password=<pfx密码>`
  - 或在 `ServerHost/appsettings.Production.json` 中配置 `Kestrel:Certificates:Default`（推荐通过 Secret/挂载）

- TCP 服务加密（三种方案）：
  1) Nginx stream：
     ```nginx
     stream {
       upstream logic_tcp { server logic:5502; }
       server {
         listen 0.0.0.0:6502 ssl;
         proxy_pass logic_tcp;
         ssl_certificate     /etc/nginx/tls/fullchain.pem;
         ssl_certificate_key /etc/nginx/tls/privkey.pem;
       }
     }
     ```
  2) Envoy/TCP proxy：在 `listener` 上配置 `downstream_tls_context`，将 `cluster` 指向内网明文端口。
  3) stunnel：在边车容器提供 TLS 封装，将外部 65xx 端口映射到内部 55xx。

- mTLS（可选）：
  - 在 Nginx/Envoy 开启客户端证书校验（仅服务间或可信客户端使用）
  - Redis 亦可在 `DIRECTORY_REDIS_SSL=true` 下配置证书或校验策略

注意：目前游戏服务的 TCP 层未内建 TLS 握手，采用边车/网关代理是更简洁稳妥的方式；如需在服务内直接支持 `SslStream`，需要扩展握手与证书管理，这会影响协议层与性能，建议后续单独评估。


### 6. 故障排查
- 启动即失败：检查 `DIRECTORY_TOKEN_SECRET` 与（如启用）`REDIS_CONNECTION` 的值是否为空、是否包含默认口令或 `localhost`
- 心跳被拒绝：确认所有游戏服务已正确加载同一密钥（环境变量/Secret 一致），并排查服务器时间是否同步
- 客户端请求未认证：检查是否正确附带登录令牌以及服务端是否能解析出 `userId` 与 `serverId`

以上为关键安全与配置指引。如需进一步的限流、TLS/mTLS、分布式追踪与灰度发布实践，可在当前基础上按环境与合规要求扩展。


