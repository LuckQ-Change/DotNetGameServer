# 第三课：从本地 Demo 到生产服务

## 配置优先级

.NET 配置会按后加入者覆盖先加入者。应用使用 `appsettings.json`、环境对应文件、开发环境 User Secrets、环境变量和命令行参数；环境变量覆盖 JSON，命令行覆盖环境变量。密钥与数据库连接字符串只通过外部配置注入。

环境变量使用双下划线表示层级，例如：

```powershell
$env:AUTHENTICATION__TOKENSECRET = "至少32字符的随机生产密钥"
$env:DATABASE__CONNECTIONSTRING = "由秘密管理系统提供"
```

## 对外监听

默认 `server:listenAddress` 是 `127.0.0.1`，不会暴露到其他机器。部署到容器或需要监听网卡时显式配置 `0.0.0.0` 或 `::`，同时配置防火墙、TLS 和访问控制。

## 上线检查

1. `authentication:tokenSecret` 使用至少 32 字符随机值；
2. 不可信网络启用 `server:tls`，内部跨公网调用优先使用 mTLS；
3. 按实际消息大小设置 `maxMessageSizeBytes`，不要盲目调大；
4. Handler 的数据库和 HTTP 调用传递请求取消令牌；
5. 编排器将 `/health` 配为 liveness，将 `/ready` 配为 readiness；
6. 开启 OpenTelemetry 并采集请求耗时、错误率、连接数和跨服 RPC；
7. 使用外部实现替换单机内存注册表和令牌撤销表；
8. 发布前执行 `CONTRIBUTING.md` 中的全部质量门禁。
9. 检查 `/.well-known/dotnet-game-server/services` 返回的是客户端可访问地址，而不是容器内部地址；
10. 为服务器 JSONL 日志与 Unity 网络日志设置磁盘采集和保留策略。

## 服务发现和跨服 RPC

单体游戏不需要服务发现。拆分多个服务后，再为应用服务器配置 `serviceRegistration`，通过 `AddServiceRegistration` 上报负载。跨服调用只对明确声明为幂等的操作开启自动重试，扣费、发奖等写操作必须自行设计幂等键与事务边界。
