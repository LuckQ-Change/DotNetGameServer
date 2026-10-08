# 第一课：五分钟跑通服务器

这一课只做一件事：启动服务器，并在不重启的情况下装入一份新的 Ping 逻辑。

## 1. 准备环境

安装 .NET 8 SDK，在仓库根目录确认：

```powershell
dotnet --version
dotnet restore DotNetGameServer.sln --locked-mode --configfile NuGet.Config
```

## 2. 启动服务端

打开第一个终端：

```powershell
dotnet run --project Demo/BasicServer/BasicServer.csproj
```

看到“服务器已启动，端口 5500”后不要关闭终端。TCP 业务端口是 `127.0.0.1:5500`，HTTP 健康检查地址是：

- `http://127.0.0.1:8080/health`：进程和 TCP 监听是否存活；
- `http://127.0.0.1:8080/ready`：依赖组件是否已经就绪。

## 3. 不停服装入插件

服务器启动后会监视 `Demo/BasicServer/plugins`。另开一个终端，在仓库根目录执行：

```powershell
dotnet build Demo/HotReloadPlugin/HotReloadPlugin.csproj -o Demo/BasicServer/plugins
```

第一个终端出现 `热更新已生效: hot-reload-1` 即表示插件已经替换 `Ping`，进程和端口都没有重启。

把 `Demo/HotReloadPlugin/PingHotPlugin.cs` 里的 `Version` 改成 `2`，再执行同一条 `dotnet build`。服务器仍在运行，日志变为 `热更新已生效: hot-reload-2`。

Unity 示例连上之后，Ping 响应会变成 `hot:2:...`。客户端示例在 `unity/com.dotnetgameserver.client`。

## 4. 刚才发生了什么

插件实现 `IServerPlugin`，用同一条 Protobuf 消息替换 `PingHandler`。服务器把新的 dll 复制到临时目录再调用 `LoadPluginAsync`，所以正在监听的连接不会断开，源文件也不会被锁死。

现在只需记住三个位置：

- `Protos/demo.proto`：定义网络数据；
- `PingHandler.cs`：插件还没装入时的默认逻辑；
- `Demo/HotReloadPlugin/PingHotPlugin.cs`：运行中可以替换的逻辑。

