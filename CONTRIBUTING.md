# 贡献指南

感谢参与 AgroMarket。本文档说明如何搭建开发环境、跑测试与提交改动。

## 开发环境

| 依赖 | 版本 | 说明 |
|------|------|------|
| .NET SDK | 10.0+ | 后端 |
| MySQL / MariaDB | MySQL 8.0+ 或 MariaDB 10.11+ | 迁移脚本兼容两者 |
| Node.js | 20+ | 前端（uni-app） |

## 初始化数据库

```bash
./database/init-db.sh
```

脚本会按文件名顺序执行 `database/[0-9][0-9]_*.sql` 全部迁移，可重复执行。
默认连本机 root 无密码，可用 `DB_HOST` / `DB_PORT` / `DB_USER` / `DB_PASS` 覆盖。

演示账号（`13800000001`~`13800000004`，口令 `Abc123456`）位于 `99_demo_seed.sql`，
**默认不执行**，本地演示时显式开：

```bash
SEED_DEMO=1 ./database/init-db.sh
```

> 该文件含公开口令的账号，**严禁在生产环境执行**。

### 新增迁移

- 文件名用两位数字前缀递增（当前最大为 `23`，下一个是 `24`）。
- 每个文件**必须自带 `USE agro_market;`**：`init-db.sh` 与 `docker-init.sh` 都是「一个文件一次
  `mysql` 调用」，上一次的会话不会带到下一次。
- **必须幂等**：用 `information_schema` 判断 + `PREPARE`/`EXECUTE` 包裹 DDL，
  参考 `14_payment_idempotency.sql`。原因见下方「已验证的踩坑」。

## 后端

```bash
# 需要先准备 appsettings.Development.json（见 *.example 模板）
cp backend/AgroMarket.Api/appsettings.Development.json.example \
   backend/AgroMarket.Api/appsettings.Development.json
# 填入本机 DB 口令、Jwt:SecretKey(≥32 字节)等

dotnet build backend/AgroMarket.slnx
dotnet run --project backend/AgroMarket.Api --urls http://127.0.0.1:8080
```

接口文档：开发环境下访问 `/swagger`。

## 测试

测试是**黑盒集成测试**：直接打真实 HTTP + 真实数据库，不是 mock。

```bash
# 另开一个终端先启动 API，然后：
dotnet run --project backend/AgroMarket.Tests -- http://127.0.0.1:8080
```

- 失败会返回非零退出码，可直接用于 CI。
- **必须先按上文生成 `appsettings.Development.json`**：测试覆盖联动模块，
  依赖其中的 `Federation:Enabled` / `NodeCode` / `ForwardReceived` / `AcceptForwarded` 等项。
  缺这个文件时会回落到 `appsettings.json`（`Federation:Enabled=false`），
  联动相关的用例会失败——这不是代码缺陷，是配置缺失。
- 其中 AI E2E 段会自行拉起独立 API 实例 + 内置 Mock LLM。
  该段依赖 `AgroMarket.Api/bin/Debug/net10.0/AgroMarket.Api.dll`，
  因此**请用 Debug 构建**，否则这一整段会被 SKIP（测试仍会「通过」，但少测一大块）。

## 前端

```bash
cd frontend
npm ci
npm run dev:h5        # 本地开发
npm run build:h5      # 构建 H5
npm run build:mp-weixin
```

## 提交规范

使用 [Conventional Commits](https://www.conventionalcommits.org/)：

```
fix(trade): 报价接受时校验预留量, 防止同一挂牌被超量接受
feat(dashboard): 新增乡镇成交分布
docs(readme): 补充集群部署说明
```

- `type(scope): 描述`，描述用中文，聚焦「改了什么、为什么」。
- 一次提交只做一件事；重构与功能改动不要混在一起。
- 提交前请确保 `dotnet build backend/AgroMarket.slnx` 无警告，且集成测试全绿。

## 已验证的踩坑

这两类问题曾真实存在于仓库中，且**只有在全新数据库上才会暴露**（长期增量演进的本地库会掩盖它们）。
改动迁移脚本时请对照检查：

1. **迁移文件缺 `USE agro_market;`**
   `init-db.sh` / `docker-init.sh` 对每个文件单独起一次 `mysql`，不带 `-D`。
   缺 `USE` 的文件会以 `ERROR 1046 (3D000) No database selected` 失败，
   导致全新安装（README 快速开始、Docker 首启）在迁移中途断掉。

2. **DDL 非幂等**
   例如对已存在的列执行 `ADD COLUMN` → `ERROR 1060 Duplicate column name`。
   仓库里 `01_schema.sql` 等基础脚本已包含较新的列，后续 `ALTER` 必须自行判存。

验证方式（推荐在本地用一次性实例跑，不要动开发库）：

```bash
mysql -uroot -e "DROP DATABASE IF EXISTS agro_market;"
SEED_DEMO=1 ./database/init-db.sh    # 全新安装能跑通
SEED_DEMO=1 ./database/init-db.sh    # 再跑一次仍然成功 = 幂等
```

## 安全

请勿提交任何真实密钥（数据库口令、`Jwt:SecretKey`、AI ApiKey、微信/支付凭据）。
本地配置放 `appsettings.Development.json`，该文件已在 `.gitignore` 中。
漏洞报告流程见 [SECURITY.md](SECURITY.md)。

## 许可

贡献即表示同意以 [Apache License 2.0](LICENSE) 授权你的改动。
