# 更新日志

本文件记录本项目的显著变更。
格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

> 说明：本文件自开源治理整理时开始维护，不追溯重构此前的全部提交历史。

## [Unreleased]

### 修复

- **全新安装无法完成数据库初始化**（严重）：`database/16`~`22` 共 7 个迁移文件缺少
  `USE agro_market;`，而 `init-db.sh` 与 `docker-init.sh` 都是「每个文件独立一次 `mysql`
  调用、不带 `-D`」，因此全新库在第 16 个迁移处以 `ERROR 1046 No database selected` 中断。
  影响 README 快速开始与 Docker 首次启动；长期增量演进的开发库不会暴露此问题。
- **迁移非幂等**：`18_alert_notified.sql`、`19_upload_private.sql`、`20_token_version.sql`、
  `21_retention_and_deactivate.sql` 直接 `ADD COLUMN` / `CREATE INDEX`，
  遇到基础脚本已含该列（或重复执行）时报 `ERROR 1060 Duplicate column name`。
  已统一改为 `information_schema` 判存 + `PREPARE`/`EXECUTE` 的幂等写法。
- **报价可超量接受**：库存在下单时才扣减，同一挂牌可被多张报价同时「接受」满量，
  其后只有第一张能下单、其余永久悬空。现接受时校验
  `可售量 − 已接受未成交量 ≥ 本单量`，并写入 `valid_until`（默认 24h，可配 `Quote:AcceptValidHours`）；
  过期未下单的报价由后台任务置 `CLOSED` 释放占用。
- **过期挂牌仍可交易**：`expire_at` 此前只用于发报警，没有任何任务改状态。
  现交易校验拦截过期挂牌，并由维护任务将其置为 `EXPIRED`。
- **库存回补可能重复累加**：`ReleaseStockAsync` 原用 `JOIN` 关联订单明细，
  多行匹配时会重复累加。改为子查询 `SUM` 聚合。
- **计数与主数据可能不一致**：注册（用户 + 角色）、发评论（评论 + `comment_count`）、
  点赞（点赞表 + `like_count`）改为同事务写入，消除部分失败导致的永久偏移。
- **联邦拉取游标跳数据**：分页触顶/无法推进时，游标会被推到对端的墙钟 `GeneratedAt`，
  使两次拉取之间的明细被永久跳过。现只在确实取完时推进，触顶时保留已消费水位。
- **状态流转未做 CAS**：发布者可用 `re_activate` 或编辑操作覆盖管理员的违规处置。
  现显式限定允许的来源状态，并对编辑加 `status <> 'VIOLATION'` 守卫。

### 新增

- `LICENSE`：Apache License 2.0。
- CI（`.github/workflows/ci.yml`）：起 MariaDB 服务、跑全部迁移、启动 API、执行集成测试；
  另含前端 H5 构建任务。配置来源改为复用 `appsettings.Development.json.example`
  （与 `CONTRIBUTING.md` 的开发者流程一致），只由环境变量覆盖连接串等环境相关项，
  避免 CI 逐项列举配置而与模板漂移。
- `CONTRIBUTING.md`、`SECURITY.md`、`.editorconfig`、`backend/AgroMarket.slnx`。
- `docs/`：迁入原 `.monkeycode/docs/` 下的设计文档（聊天 ADR、AI 大屏、联邦设计与部署）。

### 安全

- **重写 git 历史，清除此前进入版本控制的开发凭据与内部文档。**
  `backend/AgroMarket.Api/appsettings.Development.json` 自首个提交起就被跟踪，
  含真实 AI ApiKey、数据库口令与 `Jwt:SecretKey`；内部工作区 `.monkeycode/`
  含资损/越权/隐私的完整审计清单。现用 `git filter-repo` 将该文件与 `.monkeycode/`
  从**全部提交**中移除，并把旧版 `README.md`、`scripts/federation-demo.sh` 中
  出现过的开发口令替换为占位符。
  - `HEAD` 的目录树对象不变（`35bc96c`），即当前代码与文档内容未受影响，仅历史变更。
  - 联动 `.monkeycode` 中只含文档的 10 个提交因变空而被剪除，无代码提交丢失。
  - 历史重写不能让已公开的内容重新保密：已被克隆或 fork 的旧副本不受影响。

### 移除

- `.monkeycode/`（AI 协作工具私有工作区）不再纳入版本控制，已加入 `.gitignore`，
  并已从 git 历史中移除。该目录含内部审计报告与资产风险清单，不适合随开源仓库分发。

### 已知待办

- **吊销并更换历史遗留的 AI ApiKey**：该 key 曾进入 git 历史，应视为已泄露。
  历史重写已完成，但重写不能让已暴露的密钥重新保密——吊销与轮换仍需在服务商侧完成。
- 集成测试目前是控制台黑盒测试，尚无单元测试与覆盖率统计。
