# 第一课：五分钟跑通服务器

这一课只做一件事：启动服务器，并从客户端收到一次 `pong`。

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

## 3. 运行客户端

打开第二个终端：

```powershell
dotnet run --project Demo/BasicClient/BasicClient.csproj -- 127.0.0.1 5500 hello
```

正确结果包含：

```text
服务端响应: pong: hello
```

参数依次是服务器地址、TCP 端口和要发送的文本。客户端完整代码在 `Demo/BasicClient/Program.cs`。

## 4. 刚才发生了什么

客户端把 `PingRequest` 序列化成 Protobuf，放进通用 `envelope`，并标记为 `CLIENT` 通道。服务器按 Protobuf 完整类型名找到 `PingHandler`，处理后把 `PingResponse` 放回响应信封。长度前缀、完整读写、请求 ID 和错误字段由框架统一约定。

现在只需记住三个位置：

- `Protos/demo.proto`：定义网络数据；
- `PingHandler.cs`：编写业务逻辑；
- `DemoGameServer.cs`：注册处理器和长期运行组件。

