# 第三方网络系统适配

`GameNetworkClient` 不依赖 Socket，只依赖 `INetworkTransport`：

```csharp
Task ConnectAsync(ServiceEndpoint endpoint, CancellationToken token);
Task SendAsync(byte[] completeEnvelope, CancellationToken token);
Task<byte[]> ReceiveAsync(CancellationToken token);
Task DisconnectAsync(CancellationToken token);
```

适配 Mirror、LiteNetLib、WebSocket 或自研框架时：

1. 一次 `ReceiveAsync` 必须返回一条完整 Envelope；
2. 不得把多个 Envelope 合并返回；
3. Transport 不解析业务 Proto；
4. Transport 应保证同一连接上的发送顺序；
5. 断线时让 `ReceiveAsync` 抛出异常，客户端会终止并释放全部 pending request；
6. 第三方框架如果只允许主线程调用，应在适配器中把操作 marshal 到主线程。

服务发现同样可替换：用 `DelegateServiceDiscoveryProvider` 包装项目已有的登录服、配置中心或平台 SDK 即可。
