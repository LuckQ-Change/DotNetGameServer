# DotNetGameServer 命令参考

## 概述
- 输入通道：
  - 控制台 REPL（开发环境启用）：直接在控制台输入命令文本
  - HTTP 管理端点：`POST /admin/command`，请求体形如 `{ "cmd": "<命令>" }`
- 生产环境安全：需设置 `ADMIN_HTTP_ENABLED=true`、`ADMIN_TOKEN`，并按需限制 `ADMIN_HTTP_ALLOWED_IPS`
- 返回结果：统一为 JSON，对应每个命令的结构化输出

## 快速示例（HTTP）
- 查询状态：`{"cmd":"status"}`
- 查看限制：`{"cmd":"limits"}`
- 查看处理器：`{"cmd":"handlers"}` 或 `{"cmd":"handlers list"}`
- 黑名单：
  - 列出：`{"cmd":"blacklist list"}`
  - 添加：`{"cmd":"blacklist add 127.0.0.1:12345 60"}`
  - 移除：`{"cmd":"blacklist remove 127.0.0.1:12345"}`
- 日志：
  - 查询：`{"cmd":"log status"}`
  - 全局开关：`{"cmd":"log on"}` / `{"cmd":"log off"}`
  - 级别设置：`{"cmd":"log level Information"}`
  - 心跳分类开关：`{"cmd":"log heartbeat on"}` / `{"cmd":"log heartbeat off"}`
  - 指定分类开关：`{"cmd":"log category DirectoryGameServer on"}`

## 命令列表

### 1. status
- 功能：查看当前服务器运行时快照
- 输出字段：`serverName`、`listeningPort`、`currentConnections`、`blacklistCount`

### 2. limits
- 功能：查看服务器限制与网络阈值
- 输出字段：
  - `maxMessageSizeBytes`
  - `maxConcurrentConnections`
  - `receiveTimeoutMs`
  - `acceptBackoffMs`
  - `maxBytesPerSecond`
  - `maxMessagesPerSecond`
  - `idleTimeoutMs`
  - `ipMaxBytesPerSecond`
  - `ipMaxMessagesPerSecond`

### 3. handlers / handlers list
- 功能：查看已注册的强类型消息处理器
- 输出字段：`count`、`names[]`

### 4. blacklist
- 用法：`blacklist list|add <remote> [seconds]|remove <remote>`
- 说明：
  - `remote` 为 `IP:port` 字符串，按原样匹配
  - `seconds` 可选，默认 `120`
- 输出：
  - `list`：`{ count, items: [{ remote, until }] }`
  - `add`：`{ ok, remote, seconds }`
  - `remove`：`{ ok, remote }`

### 5. log
- 用法：`log on|off|level <Trace|Debug|Information|Warning|Error|Critical|None>|heartbeat <on|off>|category <name> <on|off>|status`
- 说明：
  - 全局日志默认关闭（`global=false`，`level=None`）
  - 分类开启优先：即使全局关闭，只要某分类开启，就会输出该分类日志
  - 心跳日志分类名为 `Heartbeat`
  - 一般服务器日志的分类名为服务器名，例如 `DirectoryGameServer`、`LoginGameServer`、`BattleGameServer`、`LogicGameServer`
- 输出：
  - `status`：`{ global, level }`
  - `on|off`：`{ ok, global }`
  - `level <...>`：`{ ok, level }`
  - `heartbeat <on|off>`：`{ ok, category: "Heartbeat", enabled }`
  - `category <name> <on|off>`：`{ ok, category, enabled }`

## 使用方式
- 控制台 REPL（开发环境）：直接输入命令文本（例：`status`、`log heartbeat on`）
- HTTP 管理端点：
  - URL：`/admin/command`
  - 方法：`POST`
  - Content-Type：`application/json`
  - 请求体：`{"cmd":"<命令>"}`
  - 生产环境需：`Authorization: Bearer <ADMIN_TOKEN>` 并限制来源 IP

## 注意事项
- 黑名单条目的匹配基于 `remote` 字符串（通常为 `IP:port`），与连接端点一致
- 心跳日志开关仅影响通过心跳组件输出的日志；其他分类可通过 `log category` 独立控制
- 命令执行是并发的，不存在全局锁
