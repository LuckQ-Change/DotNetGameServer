# DotNet Game Server Client for Unity

面向 Unity 2022.3 LTS 及以上版本的客户端 UPM 包。它负责：

- 自动从服务器 HTTP 目录发现可用服务；
- TCP/TLS 长连接、长度帧、请求关联、超时和错误映射；
- Protobuf Envelope 与业务 Protobuf 的双向转换；
- 允许项目现有网络框架通过 `INetworkTransport` 接入；
- Editor 中生成 Proto C#、检查服务目录和查看独立 Network Console；
- 将网络日志独立写入 `DotNetGameServer/NetworkLogs`，不污染普通游戏日志。

## 安装

在 Unity Package Manager 中选择 **Add package from disk**，指向本目录的 `package.json`。也可以把本目录发布到 Git 后使用 Git URL 安装。

包内携带 `Google.Protobuf.dll` 3.33.0（BSD-3-Clause）。如果项目已经通过其他方式提供 Google.Protobuf，应只保留一份程序集，避免 Unity 报重复程序集错误。

## 第一次使用

1. 打开 **Edit > Project Settings > DotNet Game Server**，点击 `Create/Repair Runtime Settings`。
2. 默认 Bootstrap URL 为 `http://127.0.0.1:8080`，服务职责为 `gameplay`。
3. 打开 **Tools > DotNet Game Server > Proto Generator**，选择 `protoc` 并生成协议代码。
4. 导入 `Basic Client` Sample，启动仓库中的 `Demo/BasicServer` 后进入 Play Mode。
5. 网络日志在 **Window > DotNet Game Server > Network Console** 查看。

服务器发现地址：

```text
GET /.well-known/dotnet-game-server/services?responsibility=gameplay&region=
```

## 接入已有网络框架

实现 `INetworkTransport` 即可。传输层收发的每个 `byte[]` 必须是一条完整 Envelope；长度前缀、WebSocket message 或第三方框架 packet header 均由适配器自行处理。客户端的请求关联、协议错误、Proto 序列化、日志与服务发现无需修改。

详见 `Documentation~/third-party-transport.md`。

## 内置运行时依赖

包内包含 `Google.Protobuf.dll` 3.33.0，以及它所需的
`System.Runtime.CompilerServices.Unsafe.dll` 4.5.2。Unity 项目中每个程序集
只能保留一份，否则会产生重复程序集错误。
