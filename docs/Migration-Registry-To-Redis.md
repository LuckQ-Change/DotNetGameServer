# 从内存注册迁移到 Redis 的操作指南

本指南说明如何将目录服务（Directory）使用的服务注册实现从内存切换到 Redis，包含配置变更、部署操作、验证方法与回滚方案。

## 适用范围
- DotNetGameServer 目录服务（DirectoryServer）与其下游心跳/查询调用方。
- 环境：Docker Compose 或 Kubernetes。

## 迁移前置条件
- 已构建包含 `StackExchange.Redis` 依赖的目录服务镜像或二进制。
- 可用的 Redis 实例：
  - Docker Compose：通过 `docker-compose.yml` 中的 `redis` 服务提供。
  - Kubernetes：通过 `k8s/redis.yaml` 部署提供，或对接现有 Redis 服务。

## 配置变更

### 环境变量/配置项
为目录服务容器设置以下环境变量（或在 `appsettings` 中配置等效项）：
- `DIRECTORY_REGISTRY=redis`
- `REDIS_CONNECTION=<host:port[,password=...]>`，例如：`redis:6379,password=changeme`

在 Docker Compose 中：
- 已更新 `docker-compose.yml`：
  - `directory` 服务注入 `DIRECTORY_REGISTRY=redis`、`REDIS_PASSWORD` 与 `REDIS_CONNECTION=redis:6379,password=${REDIS_PASSWORD}`
  - `redis` 服务启用密码（`--requirepass`）、AOF 持久化（`--appendonly yes`）、健康检查与数据卷（挂载宿主机 `./data/redis:/data`）
  - `depends_on: [redis]`

在 Kubernetes 中：
- 已更新 `k8s/configmap.yaml`：仅保留非敏感配置，如 `DIRECTORY_REGISTRY: "redis"`。
- 已更新 `k8s/secret.yaml`：新增 `REDIS_PASSWORD` 与 `REDIS_CONNECTION`（示例：`redis:6379,password=changeme-prod`）。
- 已更新 `k8s/deployment.yaml`：`REDIS_CONNECTION` 改为从 Secret 注入，避免在 ConfigMap 中暴露密码。
- 已更新 `k8s/redis.yaml`：为 Redis 启用密码（从 Secret 注入）、AOF 持久化、就绪/存活探针与 PVC 挂载。
- 新增 `k8s/redis-pvc.yaml`：定义 Redis 的持久化卷声明（PVC）。

## 部署步骤

### Docker Compose
1. 确认 `docker-compose.yml` 已包含 `redis` 服务，且 `directory` 服务 `environment` 已设置 `DIRECTORY_REGISTRY` 与 `REDIS_CONNECTION`。
2. 执行：`docker compose up -d`（首次）或 `docker compose up -d --force-recreate directory redis`（更新）。
3. 观察 `directory` 与 `redis` 两个容器日志，确认启动成功且无错误。

### Kubernetes
1. 创建 PVC：`kubectl apply -f k8s/redis-pvc.yaml`
2. 应用 Redis：`kubectl apply -f k8s/redis.yaml`
3. 更新 Secret 与 ConfigMap：`kubectl apply -f k8s/secret.yaml && kubectl apply -f k8s/configmap.yaml`
4. 滚动更新目录服务 Deployment：`kubectl apply -f k8s/deployment.yaml`
4. 观察 Pod：`kubectl get pods` 与 `kubectl logs <directory-pod>`，确认启动成功。

## 验证方法
1. 心跳：下游服务上报心跳后，检查目录服务日志或通过相应查询接口确认在线状态。
2. 查询：调用 QueryServerStatus 接口，确认服务器列表与状态来自 Redis 存储。
3. 切服令牌：触发 SwitchServer，确认令牌写入 Redis 并带有 TTL。
4. 过期清理：停止某服务器心跳后，等待过期时间，确认目录服务能标记离线并清理过期记录。

## 回滚方案
如需从 Redis 回退到内存实现：
1. 将环境变量 `DIRECTORY_REGISTRY` 改为 `memory`（或移除，默认使用内存）。
2. 保留 `REDIS_CONNECTION` 不影响运行，但可移除以简化配置。
3. 重新部署目录服务：
   - Docker Compose：`docker compose up -d --force-recreate directory`
   - Kubernetes：`kubectl apply -f k8s/deployment.yaml`
4. 可选择停止/移除 Redis 服务：
   - Docker Compose：`docker compose rm -f -s redis`
   - Kubernetes：`kubectl delete -f k8s/redis.yaml`

## 运维注意事项
- 令牌安全：`DIRECTORY_TOKEN_SECRET` 为必配，务必通过 Secret 管理，不要硬编码。
- Redis 安全与持久化：
  - Compose 与 K8s 已启用密码认证。生产环境请确保密码通过 Secret 管理，并限制网络访问来源。
  - K8s 使用 PVC 挂载数据目录，Compose 使用宿主机目录 `./data/redis`。按需调整容量与路径。
  - 启用 AOF 持久化（`--appendonly yes`），可结合 RDB 快照(`--save 20 1`)根据业务容忍度调整。
- Probe 与资源：已为 Redis 添加就绪/存活探针与基础资源配额，建议结合实际负载调整。
- 可选增强：
  - 加密传输（TLS）：可采用启用 TLS 的 Redis 镜像或 Sidecar（stunnel/Envoy）。若启用 TLS，需在 `REDIS_CONNECTION` 中加上 `ssl=true` 并配置证书。
  - 高可用：建议部署 Redis Sentinel（三节点）或 Redis Cluster，根据业务读写模式选择；本仓库可后续提供示例清单。
- 监控与告警：建议对 Redis 可用性与延迟进行监控（连接失败、心跳处理延迟等）。
- 容量规划：根据服务器数量与心跳频率评估 Redis 的内存与性能需求。

## 常见问题
- 目录服务启动报 Redis 连接错误：检查 `REDIS_CONNECTION` 地址、网络连通性与 Redis 服务是否正常。
- 查询不到在线服务器：确认心跳已上报；检查心跳 TTL 与清理逻辑配置；查看目录服务日志。
- 切服令牌验证失败：确认令牌 TTL 未过期，检查签名所用 `DIRECTORY_TOKEN_SECRET` 在目录服务与消费方一致。

如需进一步支持，请提供现有部署环境与 Redis 地址信息，以便协助排查与优化。