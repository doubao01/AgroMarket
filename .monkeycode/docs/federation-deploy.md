# AgroMarket 多级节点部署手册

本文档说明如何把一个 AgroMarket 实例部署成**省 / 市 / 县任意层级的一个节点**，并把多个节点组成**扁平对等的联邦**，做到「多级部署不出错」。

> 设计原理（拓扑、令牌、幂等、口径、脱敏）见 [`federation-design.md`](./federation-design.md)。

---

## 1. 核心约定（先读这一节）

| 约定 | 说明 |
| --- | --- |
| **一个节点 = 一套独立实例** | 每个节点是独立的 `{ 数据库 + API 服务 }`，拥有各自的业务数据。**没有中心库**，不存在跨节点事务。 |
| **层级只是标签** | `Federation:Level` 取 `PROVINCE` / `CITY` / `COUNTY`，仅用于展示分组与筛选；节点之间**平级**，省级节点并不天然是县级节点的父级。 |
| **靠 baseUrl 寻址** | 节点间同步方向由「对端登记记录的 `baseUrl`」决定。谁能访问谁，谁就能同步谁。 |
| **库名可保持 `agro_market`** | 所有迁移 SQL 内已写死 `USE agro_market`。因此**同一 MySQL 实例上不能跑两个节点**；不同节点必须用不同 MySQL 实例（不同容器 / 端口）。用 `NodeCode` 区分节点身份，不必改库名。 |
| **共享密钥必须一致** | `Federation:SharedKey`（≥32 字节）在**所有节点必须完全相同**，否则节点令牌无法互相校验。 |
| **节点令牌 ≠ 用户令牌** | 节点间调用用 `Federation:SharedKey` 签发的节点令牌；管理接口用管理员用户令牌。两者互不通用。 |

> ⚠️ **同一台机器演示/部署多节点时，务必给每个节点的 DB 与 API 分配不同端口与不同数据卷**，否则端口/库名冲突。

---

## 2. 配置项速查

以下均可通过 `appsettings.json` 的 `Federation` 段，或环境变量 `Federation__<Key>` 覆盖（Docker/K8s 推荐环境变量）。

| 配置键 | 环境变量 | 默认 | 说明 |
| --- | --- | --- | --- |
| `Enabled` | `Federation__Enabled` | `false` | 联动总开关。**为 `false` 时联动接口返回 400，但业务功能不受影响**。 |
| `NodeCode` | `Federation__NodeCode` | 空 | 本节点全局唯一编码，如 `node-xixian`。 |
| `NodeName` | `Federation__NodeName` | 空 | 展示名称，如 `息县农贸通`。 |
| `Level` | `Federation__Level` | `COUNTY` | `PROVINCE` / `CITY` / `COUNTY`。 |
| `RegionCode` | `Federation__RegionCode` | 空 | 行政区划编码（对应 `sys_region.region_code`），用于按区划关联。 |
| `SharedKey` | `Federation__SharedKey` | 空 | 节点令牌签名密钥，**≥32 字节，全联邦一致**。 |
| `AutoSync` | `Federation__AutoSync` | `true` | 是否开启后台定时同步。 |
| `SyncIntervalMinutes` | `Federation__SyncIntervalMinutes` | `30` | 后台同步间隔（分钟）。 |
| `BackfillDays` | `Federation__BackfillDays` | `1` | 每次上报回补的历史天数（含当天）。 |
| `RecordWindowHours` | `Federation__RecordWindowHours` | `24` | 明细上报时间窗（小时）。 |
| `MaxRecordsPerPush` | `Federation__MaxRecordsPerPush` | `500` | 单批明细上限。 |
| `TokenDays` | `Federation__TokenDays` | `365` | 签发节点令牌的有效期（天）。 |

**启动强校验**：当 `Enabled=true` 时，`NodeCode` / `NodeName` 非空且 `SharedKey` ≥32 字节，否则 API **启动即失败**（避免带着半配置上线）。因此多级部署时，先把身份与密钥配置齐全再启动。

---

## 3. 数据库初始化（关键：7 步全执行）

所有迁移按序编号，必须**全部执行**，否则会缺表：

```
01_schema.sql          业务主表
02_seed.sql            种子数据（含管理员账号）
03_chat.sql            聊天
04_chat_wechat.sql     微信聊天扩展
05_ai_chat.sql         AI 会话
06_chat_cleanup.sql    聊天清理
07_federation.sql      联动 5 张表  ← 上一版 compose 曾漏挂此步
```

### 3.1 容器方式（推荐，自动串行）

`database/docker-init.sh` 会遍历 `/migrations/0*.sql` 依次执行，MySQL 容器**首次启动**（数据卷为空）时自动完成：

```yaml
volumes:
  - db_data:/var/lib/mysql
  - ./database:/migrations:ro
  - ./database/docker-init.sh:/docker-entrypoint-initdb.d/00-migrate.sh:ro
```

> 只会在数据卷为空时执行一次。**已存在的库不会重跑**；如需升级已上线节点，见第 7 节。

### 3.2 手工方式

```bash
DB_HOST=127.0.0.1 DB_PORT=3306 DB_USER=root DB_PASSWORD=xxx \
  bash database/init-db.sh
```

验证联动表是否存在：

```sql
SELECT table_name FROM information_schema.tables
WHERE table_schema='agro_market' AND table_name LIKE 'fed_%';
-- 期望 5 张: fed_node / fed_report / fed_daily_metric / fed_business_record / fed_sync_cursor
```

---

## 4. 单节点部署

用于「只跑一个节点，暂不联动」，或作为某个节点的样板。

```bash
cp .env.example .env         # 填 JWT_SECRET_KEY、MYSQL_* 等
docker compose up -d --build
docker compose logs -f api
```

单节点默认 `FEDERATION_ENABLED=false`。若要让它成为联邦一员，在 `.env` 补：

```dotenv
FEDERATION_ENABLED=true
FEDERATION_NODE_CODE=node-xixian
FEDERATION_NODE_NAME=息县农贸通
FEDERATION_LEVEL=COUNTY
FEDERATION_REGION_CODE=411528
FEDERATION_SHARED_KEY=<与其它节点一致的 ≥32 字节密钥>
```

---

## 5. 多级集群部署（省 + 市 + 县）

模板：`docker-compose.cluster.yml`（默认给了 1 省级 + 2 县级 三个节点，可增减）。

### 5.1 准备环境变量

```bash
cp .env.cluster.example .env.cluster
# 生成强密钥
openssl rand -base64 48     # 分别填入 JWT_SECRET_KEY 与 FEDERATION_SHARED_KEY
vim .env.cluster
```

`.env.cluster` 关键项：

```dotenv
MYSQL_ROOT_PASSWORD=<root 密码>
MYSQL_USER=agromarket
MYSQL_PASSWORD=<业务账号密码>
JWT_SECRET_KEY=<≥32 字节>
FEDERATION_SHARED_KEY=<≥32 字节，所有节点一致>
API_PORT_HENAN=8080
API_PORT_XIXIAN=8081
API_PORT_HUAIYANG=8082
CORS_ORIGIN=http://localhost:5173
```

### 5.2 启动

```bash
docker compose -f docker-compose.cluster.yml --env-file .env.cluster up -d --build
docker compose -f docker-compose.cluster.yml --env-file .env.cluster logs -f federation-bootstrap
```

编排会依次完成：

1. `db-*` 各自初始化数据库（执行 01–07 全部迁移）；
2. `api-*` 各自以待配置的身份启动（`node-henan` / `node-xixian` / `node-huaiyang`）；
3. `federation-bootstrap` 一次性容器：等待所有 API `/health` 就绪 → 用管理员账号登录每个节点 → **把「除自己以外」的节点登记为对端**（`baseUrl` 用 Docker 网络内服务名，如 `http://api-xixian:8080`）→ 触发首轮 `pull` + `push` → 打印各节点汇聚结果。

> `federation-bootstrap` 是 `restart: "no"` 的一次性任务，跑完即退出，属正常现象。

### 5.3 验证

各节点管理后台（用管理员登录后打开「联动」页）应能看到：

- 顶部本节点卡片：`NodeCode` / 名称 / 层级正确；
- 「对端节点」列表：看到其余 N-1 个节点，`status=ACTIVE`，且 `lastPushAt/lastPullAt` 有值；
- 「跨节点汇聚」：`nodeCount` 等于节点总数，且含有其它节点的贡献。

命令行抽查任一节点：

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/api/auth/login/password \
  -H 'Content-Type: application/json' \
  -d '{"phone":"13800000001","password":"Abc123456"}' | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')

curl -s http://localhost:8080/api/admin/federation/self      -H "Authorization: Bearer $TOKEN"
curl -s http://localhost:8080/api/admin/federation/nodes     -H "Authorization: Bearer $TOKEN"
curl -s "http://localhost:8080/api/admin/federation/aggregate?range=30d" -H "Authorization: Bearer $TOKEN"
```

### 5.4 新增一个节点

1. 复制 `db-xixian` + `api-xixian` 两段，改：服务名、`container_name`、数据卷名、`ConnectionStrings__Default` 的 `Server=`、`Federation__NodeCode/NodeName/Level/RegionCode`、对外端口；
2. 在 `federation-bootstrap` 的 `FED_ALL` 末尾追加 `node-xxx|http://api-xxx:8080|COUNTY|名称|区划码`；
3. 在 `volumes:` 段补上新数据卷；
4. `up -d` 后 bootstrap 会重新跑（幂等），新节点与既有节点自动互相登记。

---

## 6. 非 Docker / 跨机房部署

每个节点各自起一套 `.NET API + MySQL`，用环境变量注入身份：

```bash
export ASPNETCORE_URLS="http://0.0.0.0:8081"
export ConnectionStrings__Default="Server=10.0.0.11;Port=3306;Database=agro_market;User=agromarket;Password=***;"
export Federation__Enabled=true
export Federation__NodeCode=node-xixian
export Federation__NodeName=息县农贸通
export Federation__Level=COUNTY
export Federation__RegionCode=411528
export Federation__SharedKey="<与全联邦一致>"
export Federation__AutoSync=true
dotnet AgroMarket.Api.dll
```

对端登记有两种方式：

- **管理后台 UI**：「联动」页 → 对端节点 → 新增（填对端 `nodeCode/name/level/baseUrl`，方向默认 `BOTH`）。
- **API**：

  ```bash
  curl -X POST http://<本节点>/api/admin/federation/nodes \
    -H "Authorization: Bearer $ADMIN_TOKEN" -H 'Content-Type: application/json' \
    -d '{"nodeCode":"node-henan","nodeName":"河南省农贸通","level":"PROVINCE",
         "regionCode":"410000","baseUrl":"http://<对端地址>:8080",
         "nodeToken":null,"direction":"BOTH","status":"ACTIVE"}'
  ```

> `baseUrl` 必须填**本节点真正能访问到对端**的地址（内网 IP / 域名 / 服务名）。登记后建议点一次「立即同步」验证连通。

---

## 7. 升级已上线的节点

数据库迁移是**追加式**的，容器初始化只在空卷时执行。已上线的库需手工补跑新迁移：

```bash
# 只跑新增的迁移文件（示例：补 07）
mysql -h <host> -u root -p < database/07_federation.sql
```

代码升级：`docker compose ... up -d --build`（API 重启即可，无需停库）。若本次升级新增了 `Federation:*` 配置项，务必同步补进各节点 `.env`，**并保证 `SharedKey` 全联邦一致**，否则升级后节点间会出现 401。

---

## 8. 排障对照表

| 现象 | 可能原因 | 处理 |
| --- | --- | --- |
| API 启动失败，日志含 `Federation` 校验 | `Enabled=true` 但 `NodeCode/NodeName/SharedKey` 缺失或 `SharedKey<32 字节` | 补齐身份与密钥；或先置 `Enabled=false` |
| 登记对端成功但 `lastPushAt` 一直为空 | 对端 `baseUrl` 不可达 / 端口写错 / 防火墙 | 在本节点容器内 `curl <baseUrl>/health` 验证 |
| 上报返回 **401** | 节点令牌无效或过期 | 确认两端 `Federation__SharedKey` 完全一致；重新签发令牌或重启节点 |
| 上报返回 **403** | 载荷 `nodeCode` 与令牌身份不一致 | 正常防伪行为；检查是否用了别的节点的令牌/别名 |
| `aggregate` 里只有本节点 | 对端未登记、方向不含 `PULL`、或尚未同步 | 检查「对端节点」页方向与状态，点「立即同步」 |
| 全新库缺 `chat_*` / `fed_*` 表 | 迁移未全量执行 | 用 `database/docker-init.sh`（遍历 `0*.sql`）重建，或手工补跑缺失 SQL |
| 两个节点都连到同一个库 | 库名都是 `agro_market` 且指向同一 MySQL 实例 | 每个节点必须独立 MySQL 实例（不同容器/端口） |
| 中文节点名乱码 | 连接串缺字符集 / 库非 utf8mb4 | 连接串加 `CharSet=utf8mb4`，库用 `utf8mb4_unicode_ci` |

---

## 9. 相关文件

| 文件 | 说明 |
| --- | --- |
| `docker-compose.cluster.yml` | 多级节点集群编排（3 节点 + 引导任务） |
| `.env.cluster.example` | 集群环境变量模板 |
| `docker-compose.yml` | 单节点编排（联动默认关闭） |
| `database/docker-init.sh` | 容器内按序执行全部迁移 |
| `database/init-db.sh` | 手工/宿主机执行全部迁移 |
| `scripts/federation-bootstrap.sh` | 联邦引导：登记对端 + 首轮同步（幂等，可重复运行） |
| `.monkeycode/docs/federation-design.md` | 联动设计说明 |
