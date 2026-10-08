# Basic Client 首次运行

这个 Sample 已携带生成好的 Ping、笔记和游客登录协议代码，不需要先安装或配置 `protoc`。

1. 在 DotNetGameServer 仓库根目录运行：

   ```powershell
   dotnet run --project Demo/BasicServer/BasicServer.csproj
   ```

2. 在 Unity 中打开本 Sample 的 `BasicClient` 场景。
3. 进入 Play Mode。
4. Game 视图显示“连接成功 ✓”和 `pong: hello from Unity` 即表示接入完成。Sample 会先调用 `GuestLogin`，再带令牌发送 Ping。

如果连接失败，请确认服务端终端仍在运行，并打开
**Window > DotNet Game Server > Network Console** 查看独立网络日志。

以后修改或新增 `.proto` 时，再使用
**Tools > DotNet Game Server > Proto Generator** 重新生成 C# 代码。
