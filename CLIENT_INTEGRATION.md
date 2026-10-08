# 客户端接入协议

可运行的客户端在 Unity UPM 包 `unity/com.dotnetgameserver.client`。第一次接入建议先跑通 `Demo/BasicServer` 的热更新示例，再用 Unity Sample 发 Ping。

## Unity UPM（推荐）

仓库已提供 `unity/com.dotnetgameserver.client`。Unity 2022.3 及以上版本可通过 Package Manager 的
**Add package from disk** 安装。包内不引用服务器 `Core`，职责如下：

- 从 `/.well-known/dotnet-game-server/services` 自动发现 TCP/TLS 服务；
- 负责 Envelope、长度帧、请求 ID、超时、错误响应与通知分发；
- 使用 `Google.Protobuf` 生成类完成业务协议互传；
- 通过 `INetworkTransport` 对接项目已有的 WebSocket、KCP、Mirror 或自研网络层；
- 在 Editor 提供 Proto Generator、Service Browser 与独立 Network Console；
- 网络日志单独保存在 `Library/DotNetGameServer/NetworkLogs`，不会混入普通业务日志。

第一次验证可直接导入 UPM 包中的 `Basic Client` Sample，启动 `Demo/BasicServer`，打开
`BasicClient` 场景并进入 Play Mode。Sample 已携带 Ping 的生成代码，不需要先安装 `protoc`。
`UnityGameNetworkClientFactory.CreateDefault()` 应在 Unity 主线程调用；通过该工厂创建的客户端会把
通知与断线事件派发回 Unity 主线程。

服务器通过 `GameServerDefinition.Responsibilities` 声明职责。Unity 可以按 `gameplay`、`login`、
`lobby` 等职责查找服务，不需要硬编码具体服务器角色。

TCP 流由连续帧组成：

```text
[4 字节 little-endian int32 长度][protobuf envelope]
```

长度仅包含 `envelope` 本体，必须大于 0 且不超过服务端 `server:maxMessageSizeBytes`。客户端应完整写入和完整读取；一次 socket 读操作不保证得到整帧。

`envelope` 关键字段：

- `type`：请求 protobuf 的消息名或完整名；
- `payload`：序列化后的业务 protobuf；
- `token`：可选签名令牌；
- `request_id`：客户端生成的请求关联 ID；
- `error_code` / `error_message`：响应错误信息。
- `channel`：客户端必须填写 `CLIENT`；`INTERNAL` 仅供服务器间调用。

`type` 必须使用 Protobuf 完整类型名。Demo 的请求类型为 `DotNetGameServer.Demo.PingRequest`，`payload` 为 `Demo/BasicServer/Protos/demo.proto` 中的消息。成功响应类型为 `DotNetGameServer.Demo.PingResponse`。连接应设置超时并处理服务端主动关闭；遇到非法长度、协议解析失败、空闲超时或限流时，服务端可能直接断开连接。

业务失败时信封会带 `error_code`，载荷仍是业务响应。客户端应使用 `RequestResultAsync`：有载荷时先读 `response.Success`，没有载荷再按信封错误处理。`GameProtocolException` 也携带原始载荷。

客户端心跳不是全局强制协议。服务目录的 `heartbeatIntervalSeconds` 大于 0 时，Unity 客户端会按该间隔自动发送 `DotNetGameServer.ClientHeartbeatRequest`。未启用时不要发送，否则会返回未知消息错误。服务器之间的服务发现心跳属于 `INTERNAL` 通道，客户端不得调用。

服务器主动推送不带 `request_id`。使用 `client.On(Foo.Parser, handler)` 按类型订阅；未订阅的消息仍会进入 `NotificationReceived`。

目录中的 `version` 与客户端 `ClientVersion` 比较主版本。主版本不同时连接失败，错误码为 `426`。

Demo 的 `GuestLoginRequest` / `RefreshSessionRequest` 即使开启客户端鉴权也可以匿名调用。拿到 `access_token` 后调用 `ApplyAccessToken`，并把 `TokenRefreshHandler` 指到刷新或再次游客登录。令牌接近过期或业务请求返回 `401` 时，Unity SDK 会自动刷新并重试一次。不要在心跳循环里自己实现第二套刷新。

生产环境建议启用 `server:tls`，为每个服务设置独立 `authentication:audience`，并使用至少 32 个随机字符的密钥。
开启 `authentication:requireClientAuthentication` 后，除匿名入口外的客户端消息都必须携带有效令牌。
