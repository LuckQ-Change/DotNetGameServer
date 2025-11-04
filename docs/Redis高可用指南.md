# Redis 高可用指南（Sentinel/Cluster/云托管）

本文档提供在生产环境下部署与接入 Redis 高可用的建议与示例，适用于目录注册表（Directory.Registry=redis）。

- 推荐优先使用云托管 Redis（ElastiCache/Azure Cache for Redis/GCP Memorystore），简化维护并获得内置高可用。
- 本地自管优先选择 Sentinel 架构（主从 + 哨兵自动故障切换）；对水平扩展需求强的场景再考虑 Cluster。

## Sentinel 架构（示例骨架）

详见 k8s/redis-sentinel.yaml（示例骨架，需按你的集群与持久化策略调整）。

接入方式：Sentinel 提供主节点发现，客户端可通过哨兵获取当前主节点地址。StackExchange.Redis 支持 Sentinel（建议使用 ConfigurationOptions 指定 ServiceName 与 Sentinel endpoints，并设置 AbortOnConnectFail=false、ConnectRetry 等）。

## Cluster 架构（简述）

- 优势：分片扩展读写吞吐，适合超大键空间与高并发场景。
- 注意：应用需按 Redis Cluster 语义设计（跨槽操作受限，Lua 脚本涉及跨键需谨慎）。
- 可参考 bitnami/redis-cluster Helm 或 redis-operator，结合 PVC、节点反亲和部署。

## PDB 与反亲和性

- 为 Redis 与 Sentinel 配置 PodDisruptionBudget（例如 minAvailable: 2），减少节点维护时的服务中断风险。
- 配置 podAntiAffinity 使副本分散在不同节点上，提升容灾能力（示例见上文清单骨架及 k8s/redis-sentinel.yaml）。

## 安全与接入

- 生产必须启用密码或 TLS：
  - 密码：通过 Secret 注入，示例连接串 `redis:6379,password=strong-pass`。
  - TLS：通过证书挂载，采用 `ssl=True` 与证书校验（后续可通过 ConfigurationOptions 集成双向证书）。
- 连接串与密码切勿放在 ConfigMap 或镜像内，统一使用 Secret 管理。

## 应用配置（本项目）

- `DIRECTORY_REGISTRY=redis`（ConfigMap）
- `REDIS_CONNECTION`（Secret）
- 在 Sentinel/Cluster 场景下推荐使用 ConfigurationOptions：
  - 指定多个端点与合理的 `ConnectRetry`、`SyncTimeout`；同时开启 `AbortOnConnectFail=false`。

## 云托管接入示例

- AWS ElastiCache：使用私有子网与安全组限制访问；连接地址通过 Secret 注入至 `REDIS_CONNECTION`。
- Azure Cache for Redis：推荐强制 TLS 并使用证书校验；禁用公共网络或限制来源。

## 运维建议

- 建立备份与恢复演练（快照/AOF），监控内存与命中率指标。
- 对应用启用重试、超时与断路器策略，并对关键操作（令牌消费、心跳写入）采用 Lua 脚本保证原子性（本项目已集成）。