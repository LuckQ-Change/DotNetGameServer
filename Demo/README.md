# BasicServer 示例

`BasicServer` 是独立应用工程，不属于框架源码。`HotReloadPlugin` 是可以在服务器运行期间反复装入的逻辑插件。它们演示：

- 从 `BaseGameServer` 派生与游戏类型无关的 TCP 服务器；
- 注册强类型 Protobuf 客户端消息处理器；
- 通过 `IServerResponse` 返回统一错误；
- 通过 `IServerComponent` 管理资源生命周期；
- 使用公共宿主提供 `/health` 和 `/ready` 健康检查；
- 监视 `plugins/HotReloadPlugin.dll`，不重启进程也不断开已有连接。

在仓库根目录运行：

```powershell
dotnet run --project Demo/BasicServer/BasicServer.csproj -- --port 5500 --healthCheckUrl http://127.0.0.1:8080
```

另开终端，生成并装入插件：

```powershell
dotnet build Demo/HotReloadPlugin/HotReloadPlugin.csproj -o Demo/BasicServer/plugins
```

服务器日志出现 `热更新已生效: hot-reload-1`。修改 `Demo/HotReloadPlugin/PingHotPlugin.cs` 的 `Version` 后再执行同一条 build，日志会变成新的版本号。

创建新游戏时可以复制这两个目录。游戏协议、处理器、领域模型和数据库驱动包应放在应用工程中，不应放入 `src`。
