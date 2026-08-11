# DotNetGameServer

一个与游戏类型无关的 .NET 8 服务器基础框架。仓库不再内置登录服、逻辑服、战斗服、目录服、GM、运维后台或部署方案；应用层示例统一放在 `Demo/`，框架源码仅位于 `src/`。

## 工程结构

```text
src/
  Shared/   通用信封协议、服务发现协议、令牌与时间转换工具
  Core/     TCP 服务器、消息管线、组件、鉴权、数据库和分流接口
  Hosting/  .NET Host 集成、配置、健康检查与 OpenTelemetry
Demo/
  BasicServer/  最小可运行示例
  BasicClient/  可直接调用 Ping 的 .NET 客户端
unity/
  com.dotnetgameserver.client/  可直接安装的 Unity UPM 客户端、Editor 工具与示例
tests/
  Tests/        核心单元测试与 TCP 集成测试
```

`src` 不包含具体游戏的房间、账号、角色、战斗规则或运营功能。业务工程通过以下接口组合能力：

- `BaseGameServer`：有界请求队列、连接上限、超时、限流、TLS、完整读写和优雅关闭；
- `IMessageHandler<TRequest,TResponse>`：强类型 protobuf 消息处理；
- `IServerComponent` / `IServerComponentHealth`：资源生命周期与就绪检查；
- `IGameDatabase` / `IGameDatabaseSession`：数据库厂商无关的查询、命令和事务；
- `IServerRegistry` / `IServerSelectionStrategy`：服务注册与负载、轮询、加权随机、一致性哈希分流；
- `IServerRpcClient`：类型安全跨服调用、长连接复用、请求关联、超时和幂等重试；
- `GameServerDefinition`：统一启动、依赖注入、HTTP 扩展点和健康检查。

客户端与服务器内部通讯虽然共用 Protobuf 信封，但使用不同通信通道和处理器路由。客户端服务器默认只允许 `CLIENT`，服务发现服务器只允许 `INTERNAL`；内部通道默认强制校验服务令牌。客户端心跳默认关闭，仅需要维持客户端会话的服务器按需启用。

## 快速开始

```powershell
dotnet restore DotNetGameServer.sln
dotnet run --project Demo/BasicServer/BasicServer.csproj
```

另开一个终端调用服务端：

```powershell
dotnet run --project Demo/BasicClient/BasicClient.csproj -- 127.0.0.1 5500 hello
```

默认 TCP 地址为 `127.0.0.1:5500`，健康检查为 `http://127.0.0.1:8080/health` 和 `/ready`。新游戏可复制 `Demo/BasicServer`，替换 protobuf、处理器和组件，无需修改框架源码。第一次使用请从[五分钟入门](docs/tutorials/01-FIRST-RUN.md)开始。

Unity 项目在 Package Manager 选择 **Add package from disk**，指向
`unity/com.dotnetgameserver.client/package.json`。UPM 包提供自动服务发现、TCP/TLS、可插拔第三方
Transport、Proto Generator、独立网络日志和 Network Console；详见包内 `README.md`。

严格验证：

```powershell
dotnet build DotNetGameServer.sln -c Release -p:TreatWarningsAsErrors=true
dotnet test DotNetGameServer.sln -c Release
```

## 文档导航

- [五分钟跑通服务器](docs/tutorials/01-FIRST-RUN.md)
- [增加一条业务消息](docs/tutorials/02-ADD-MESSAGE.md)
- [从 Demo 到生产服务](docs/tutorials/03-PRODUCTION.md)
- [常见问题排查](docs/TROUBLESHOOTING.md)
- [框架扩展指南](DEVELOPER.md)
- [客户端接入协议](CLIENT_INTEGRATION.md)
- [贡献与本地质量门禁](CONTRIBUTING.md)
- [质量完成定义](docs/QUALITY_GATE.md)
