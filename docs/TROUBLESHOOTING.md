# 常见问题排查

## 客户端连接被拒绝

确认服务端日志显示 TCP 已启动，客户端连接的是 TCP 端口 `5500`，不是健康检查端口 `8080`。跨机器连接时还要确认 `server:listenAddress` 不是 `127.0.0.1`。

## 返回 unknown_message_type

`envelope.type` 必须是 Protobuf 完整名，例如 `DotNetGameServer.Demo.PingRequest`，不能使用 C# 类名或文件名代替。

## 返回 communication_channel_forbidden

普通客户端必须填写 `CLIENT`。`INTERNAL` 只供服务器间调用，并且默认强制验证服务令牌。

## 返回 authentication_required

检查目标通道是否要求鉴权、Token 的 audience 是否与目标服务器一致、服务器时间是否同步，以及密钥是否来自同一套秘密配置。

## /health 正常但 /ready 失败

这表示进程仍在运行，但某个关键组件尚未就绪。查看 `/ready` JSON 中失败检查的 description；服务发现心跳失败时重点检查发现节点地址、TLS 和令牌 audience。

## 修改依赖后 locked-mode restore 失败

更新 `Directory.Packages.props` 后执行：

```powershell
dotnet restore DotNetGameServer.sln --force-evaluate --configfile NuGet.Config
```

把对应的 `packages.lock.json` 一并提交。
