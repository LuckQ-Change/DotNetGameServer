# BasicServer 示例

`BasicServer` 是独立应用工程，不属于框架源码。`BasicClient` 是与它配套的可执行 .NET 客户端。它们演示：

- 从 `BaseGameServer` 派生与游戏类型无关的 TCP 服务器；
- 注册强类型 Protobuf 客户端消息处理器；
- 通过 `IServerResponse` 返回统一错误；
- 通过 `IServerComponent` 管理资源生命周期；
- 使用公共宿主提供 `/health` 和 `/ready` 健康检查；
- 游客登录签发访问令牌，客户端 SDK 在 `401` 时自动刷新。

客户端心跳在示例配置中已开启，目录会下发 `heartbeatIntervalSeconds`，由 SDK 自动发送。

在仓库根目录运行：

```powershell
dotnet run --project Demo/BasicServer/BasicServer.csproj -- --port 5500 --healthCheckUrl http://127.0.0.1:8080
```

另开终端：

```powershell
dotnet run --project Demo/BasicClient/BasicClient.csproj -- 127.0.0.1 5500 hello
```

创建新游戏时可以复制该目录。游戏协议、处理器、领域模型和数据库驱动包应放在应用工程中，不应放入 `src`。
