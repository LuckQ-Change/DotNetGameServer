# Changelog

## 0.1.2 - 2026-08-13

- Basic Client 增加可直接打开的场景、已生成的 Ping 协议和 Game 视图状态反馈。
- 首次运行不再要求安装 `protoc` 或手工挂载 MonoBehaviour。
- Unity 默认工厂将通知与断线事件派发到创建客户端时捕获的主线程上下文。
- 增加 Sample 协议兼容性与事件派发契约测试。

## 0.1.0 - 2026-08-07

- 首个 UPM 版本。
- 增加 TCP/TLS、可插拔 Transport、服务发现和 Protobuf Envelope。
- 增加 Proto Generator、Project Settings、Service Browser 与独立 Network Console。
- 增加独立滚动网络日志。
