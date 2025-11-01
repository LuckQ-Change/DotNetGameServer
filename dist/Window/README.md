# DotNetGameServer 项目说明

本项目是一个基于 .NET 的简易游戏服务器示例，采用 ECS（实体-组件-系统）思想对服务器进行模块化拆分，并提供一个 Windows 窗口应用作为纯 UI 层用于启动与观察日志。

## 项目概览
- UI 层：`Window` 项目，负责提供启动/停止服务器、查看日志的界面。
- 核心层：`Core` 项目，提供抽象的 `BaseGameServer`、组件系统和消息处理器接口。
- 登录服务器：`LoginServer` 项目，处理登录请求。
- 战斗服务器：`BattleServer` 项目，处理开始战斗与攻击请求。
- 共享协议：`Shared` 项目，管理 Protobuf 协议文件并生成消息类型。

## 架构设计（ECS）
- 服务器以 `BaseGameServer` 为抽象基类，统一：
  - 网络收发（TCP + 长度前缀 + Protobuf `Envelope`）。
  - 组件注册与生命周期（`InitializeAsync`/`DestroyAsync`）。
  - 消息处理器注册与派发（请求到响应映射）。
- 组件（`IServerComponent`）：可插拔，按接口获取（例如 `IUserAuthComponent`、`IBattleManagerComponent`）。
- 消息处理器（`IMessageHandler`）：按消息类型处理请求并返回响应负载。

## 项目结构
```
├─ Window/           # 纯 UI 层（WinForms）
│  ├─ Form1.cs       # 启动/停止 Login、Battle 服务器，显示日志
│  └─ ...
├─ Core/             # 核心 ECS 框架
│  ├─ BaseGameServer.cs
│  ├─ IServerComponent.cs
│  └─ ...
├─ LoginServer/      # 登录服务器实现
│  ├─ LoginGameServer.cs
│  ├─ LoginMessageHandler.cs
│  └─ ...
├─ BattleServer/     # 战斗服务器实现
│  ├─ BattleGameServer.cs
│  ├─ BattleMessageHandlers.cs
│  ├─ BattleComponents.cs
│  └─ ...
├─ Shared/           # 协议与公共依赖
│  ├─ Protos/messages.proto
│  └─ Shared.csproj（带 Grpc.Tools 生成设置）
└─ DotNetGameServer.sln
```

## 协议与消息
- 基础封包：`Envelope { string type; bytes payload; }`
- 登录：
  - `LoginRequest { string username; string password; }`
  - `LoginResponse { bool success; string message; }`
- 战斗：
  - `StartBattleRequest { string player_id; string battle_type; }`
  - `StartBattleResponse { bool success; string message; string battle_id; int32 player_hp; int32 enemy_hp; }`
  - `AttackRequest { string battle_id; string player_id; int32 damage; }`
  - `AttackResponse { bool success; string message; int32 player_hp; int32 enemy_hp; bool battle_ended; bool player_won; }`

说明：
- 所有请求与响应均通过 `Envelope` 包裹，使用长度前缀（4 字节，`int32` 小端）发送与接收。
- 服务器自动将 `*Request` 映射为 `*Response`（可在 `BaseGameServer.GetResponseType` 中覆写）。

## 构建与运行
1. 在仓库根目录执行构建：
   - `dotnet build`
2. 运行 UI（Window 项目）：
   - 进入 `Window\bin\Debug\net9.0-windows`，启动 `Window.exe`。
3. 在 UI 中：
   - 输入端口，点击“启动登录服务”或“启动战斗服务”。
   - 使用日志筛选（All/Login/Battle/System）观察实时日志。

## 日志与界面
- 已将日志显示区域明显加大，同时缩小上方 Tab 区域以便更好地查看。
- 日志前缀统一：`[Login]`、`[Battle]`、`[System]` 等，用于筛选与分类显示。

## 交互示例（客户端侧思路）
- 封包格式：`[4字节长度][Envelope序列化字节]`
- 发送登录请求：
  - `Envelope.Type = "LoginRequest"`
  - `Envelope.Payload = LoginRequest(username, password).ToByteArray()`
- 收到响应：
  - 根据服务器返回的 `Envelope.Type`（例如 `LoginResponse`），解析负载。
- 战斗流程：
  - 发送 `StartBattleRequest` 获得 `battle_id` 与初始 HP。
  - 循环发送 `AttackRequest`，直到 `AttackResponse.battle_ended = true`。

## 扩展与开发建议
- 新增消息：在 `Shared/Protos/messages.proto` 中添加对应消息，编译后在相关服务器注册处理器。
- 新增组件：实现 `IServerComponent`（提供 `Name`、`InitializeAsync`、`DestroyAsync`），在具体服务器中通过接口类型注册并在处理器中获取。
- 错误处理：统一在 `BaseGameServer` 中进行日志记录，必要时为关键路径增加 try-catch 与更多上下文。

## 后续计划
- 在 UI 中加入简单的“模拟请求”面板，便于直接从界面触发 `StartBattleRequest` 和 `AttackRequest`（用于联调演示）。
- 增加认证组件的配置化存储（例如文件或数据库）。
- 引入更多战斗机制（技能、状态、随机事件等），组件化扩展。

## PB 协议生成工具
- 位置：`tools/gen-proto.ps1`
- 功能：从指定的 `.proto` 目录生成 C# 消息代码，支持可配置输出路径；可选生成 gRPC 服务 stub（`-IncludeGrpc`）。

使用示例：
- 默认从 `Shared/Protos` 读取，输出到 `artifacts/proto-gen`：
  - `powershell -NoProfile -ExecutionPolicy Bypass -File tools/gen-proto.ps1`
- 指定输出到自定义路径：
  - `powershell -NoProfile -ExecutionPolicy Bypass -File tools/gen-proto.ps1 -OutputDir "D:\proto-out"`
- 指定 Proto 目录 + 生成 gRPC stub：
  - `powershell -NoProfile -ExecutionPolicy Bypass -File tools/gen-proto.ps1 -ProtoDir "C:\path\to\protos" -OutputDir "C:\out" -IncludeGrpc`

注意事项：
- 运行前请先 `dotnet restore`，以便在 NuGet 全局缓存中找到 `Grpc.Tools` 内置的 `protoc.exe` 与 `grpc_csharp_plugin.exe`。
- 如果项目中已通过 `Shared.csproj` 的 `<Protobuf Include=... />` 使用 MSBuild 自动生成，请避免将脚本生成的 C# 文件放入项目目录内，以免出现类型重复编译冲突。推荐输出到 `artifacts` 或其他外部目录。
- 修改 `.proto` 后如使用 MSBuild 自动生成，重新 `dotnet build` 即可；如使用脚本生成，请重新执行脚本。