# 贡献指南

## 开发环境

- 使用 `global.json` 指定的 .NET 8 SDK；
- 从仓库根目录执行命令；
- 不提交密钥、连接字符串、`bin/`、`obj/` 或测试结果。

## 本地质量门禁

提交前必须依次通过：

```powershell
dotnet restore DotNetGameServer.sln --locked-mode --configfile NuGet.Config
dotnet format DotNetGameServer.sln --verify-no-changes --no-restore --severity warn
dotnet build DotNetGameServer.sln -c Release --no-restore
dotnet test DotNetGameServer.sln -c Release --no-build
```

修改依赖版本时，应先更新 `Directory.Packages.props`，再执行：

```powershell
dotnet restore DotNetGameServer.sln --force-evaluate --configfile NuGet.Config
```

新的框架行为必须配套测试。协议字段只允许追加，不得复用或修改已经发布的 Protobuf 字段编号。破坏性 API 变更应在合并说明中标出迁移方式。

## 代码边界

- `src/Shared`：跨进程协议和无业务含义的通用原语；
- `src/Core`：传输、管线、鉴权、组件、服务发现、RPC 和持久化抽象；
- `src/Hosting`：配置、依赖注入、健康检查和遥测；
- `Demo`：可以复制的应用示例，不向框架内核反向引入具体游戏业务。
