# 框架扩展指南

如果尚未跑通过 Demo，请先完成 `docs/tutorials/01-FIRST-RUN.md` 和 `docs/tutorials/02-ADD-MESSAGE.md`。本文面向已经理解 Handler 基本流程、准备使用数据库或多服能力的开发者。

## 新建游戏服务

以 `Demo/BasicServer` 为模板，在应用工程中定义 protobuf、实现 `IMessageHandler<TRequest,TResponse>`，并从 `BaseGameServer` 派生服务器。处理器通过 `RegisterMessageHandler` 注册，应用由 `GameServerHost.RunAsync` 托管。

框架只处理传输、鉴权、分发、错误隔离和生命周期；游戏状态、房间模型、匹配、帧同步或回合制规则均属于应用层。

## 组件

长期资源实现 `IServerComponent`。初始化失败时框架会逆序回滚已经启动的组件；关闭时会逐个释放，即使某个组件抛出异常也不会阻塞其余资源。影响服务可用性的组件同时实现 `IServerComponentHealth`，结果会进入 `/ready`。

## 不停服更新

`ReplaceClientMessageHandler` 可以在监听不中断的情况下替换处理器。已经建立的连接保持不变，正在执行的请求把旧处理器跑完，替换之后到达的请求使用新处理器。

入口程序集不能卸载。需要更换的逻辑放到单独的插件程序集，实现 `IServerPlugin`，再调用 `LoadPluginAsync(dllPath)`。插件里的 `MapClient` 会替换同名消息的处理器。上一份插件要等它自己的请求结束后才卸载；超时仍有请求时会保留到下一次装载再试，避免把正在执行的代码卸掉。

组件和配置快照不会跟着插件一起换。数据库、房间容器这类长期状态留在宿主里，由新处理器继续使用。

## 数据库

Core 不绑定 ORM 和数据库厂商。业务只依赖 `IGameDatabase`，使用 `DatabaseCommand` 和参数化查询；事务通过 `InTransactionAsync` 保证同一连接和事务上下文。

启用 `database` 配置前，应用工程应引用对应 ADO.NET 驱动，并注册其 `DbProviderFactory`。也可直接在 `ConfigureServices` 中注册自定义 `IGameDatabase`，用于 ORM、分库分表或测试替身。

## 服务发现与分流

`IServerRegistry` 定义注册、注销、查询和过期清理。框架提供进程内 `MemoryServerRegistry`，生产应用可在自己的工程实现 Redis、etcd、Consul 或数据库版本，不把基础设施 SDK 强制绑定到 Core。

`ServerSelectionStrategyFactory` 提供：

- `load` / `least_connections`：优先低负载；
- `round_robin`：轮询；
- `weighted_random`：按剩余容量加权；
- `consistent_hash`：同一玩家、队伍或房间键稳定落到同一节点；
- `random`：随机候选。

服务若需要向发现节点上报，可调用 `server.AddServiceRegistration(context, serverType, configure)`。不需要集群能力的单体游戏无需启用它。

服务发现心跳仅用于服务器注册、负载上报和失联判断，不承担客户端连接保活。

## 跨服调用

业务通过 `IServerRpcClient` 发起类型安全调用：

```csharp
var response = await rpc.CallAsync<GetPlayerRequest, GetPlayerResponse>(
    "player",
    request,
    new ServerCallOptions
    {
        RoutingKey = playerId,
        Strategy = "consistent_hash",
        Idempotent = true
    },
    cancellationToken);
```

底层自动完成服务发现、分流、服务令牌、长连接复用、请求 ID 关联、超时和响应解析。只有显式声明为幂等的请求才允许自动重试，避免重复扣费、重复发奖等副作用。

内部处理器使用 `RegisterInternalMessageHandler` 注册，客户端处理器使用 `RegisterClientMessageHandler` 注册。服务器构造时必须显式允许对应通道；同时服务客户端和内部调用时可传入 `[CommunicationChannel.Client, CommunicationChannel.Internal]`，内部消息仍会强制验证服务身份。安全边界要求更高时，应使用两个独立服务器实例或端口。

服务令牌携带经过签名的来源服务器 ID。处理器可以调用 `context.TryGetCallingServerId(out var serverId)` 获取调用方，服务发现心跳会强制要求该 ID 与上报的 `server_id` 一致。

默认解析器通过服务发现协议获取节点；单体部署和测试可以使用 `FixedServiceResolver`，内嵌注册表可以使用 `RegistryServiceResolver`。业务也可实现 `IServiceResolver` 对接 Consul、etcd 或其他注册中心。

最小配置示例：

```json
{
  "authentication": {
    "tokenSecret": "由外部配置注入的至少 32 字符随机密钥",
    "audience": "game-1",
    "requireInternalAuthentication": true
  },
  "serviceRegistration": {
    "serverId": "game-1",
    "serverType": "game",
    "host": "10.0.0.12",
    "port": 5500,
    "tokenAudience": "game-1",
    "discoveryHost": "10.0.0.10",
    "discoveryPort": 5400,
    "discoveryAudience": "ServiceDiscoveryServer"
  },
  "serverRpc": {
    "requestTimeoutMs": 5000,
    "maxAttempts": 2,
    "failureThreshold": 3,
    "failureCooldownSeconds": 10
  }
}
```

## 客户端心跳

`clientHeartbeat:enabled` 默认为 `false`。开启后服务器注册 `ClientHeartbeatRequest` 处理器，客户端可以定期发送心跳并获得服务器时间和建议间隔。未开启的服务器不会注册该协议。

TCP 空闲清理由 `server:idleTimeoutMs` 控制：设置为 `0` 表示不因空闲主动断开；需要检测客户端断线的长连接服务可以启用客户端心跳并设置合理空闲超时，短连接或持续有业务消息的服务通常无需心跳。

## Unity 自动服务目录

所有使用 `GameServerHost` 的服务自动提供：

```text
GET /.well-known/dotnet-game-server
GET /.well-known/dotnet-game-server/services?responsibility=gameplay&region=cn
```

应用在 `GameServerDefinition` 中声明职责与协议版本：

```csharp
Responsibilities = ["gameplay", "room"],
ProtocolVersion = "1.0.0"
```

如果当前服务包含 `IServerRegistry`，HTTP 目录返回注册表中的在线节点并按负载排序；否则返回当前服务。
目录只暴露连接所需的公开元数据，不返回 TokenSecret、连接字符串或服务令牌。

## 配置边界

- `server`：TCP、队列、连接、超时、限流与 TLS；
- `authentication`：令牌密钥、受众与单实例时钟偏差；
- `clientHeartbeat`：可选客户端应用层心跳，默认关闭；
- `serverRpc`：跨服调用来源标识、连接和请求超时、幂等重试；
- `database`：可选数据库工厂配置；
- `serviceRegistration` / `discovery`：可选服务发现；
- `gameLogging`：基础日志事件；
- `telemetry`：可选 OpenTelemetry 导出。

密钥和连接字符串必须由环境变量或外部配置注入，不应提交到仓库。

`gameLogging:fileEnabled=true` 时，服务器在 `fileDirectory` 下写入按日期和大小滚动的 JSONL 文件。
成功的连接、收发帧记录为 Debug；Information 默认只保留生命周期和有操作价值的事件。

数据库启用后自动加入 `/ready`。连接探针受 `database:healthCheckTimeoutSeconds` 限制，事务回滚受
`database:rollbackTimeoutSeconds` 限制，避免数据库故障拖住请求或停服。
