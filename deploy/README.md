# AgroMarket 多级部署交付物（省 / 市 / 县）

本目录提供**三套可独立部署的单节点交付物**，对应省、市、县三级。每套包含：

```
deploy/<level>/
  docker-compose.yml   单节点编排（MySQL + API，首启自动执行全部迁移）
  .env.example         环境变量模板（复制为 .env 后填写）
  README.md            该级的部署与联动接入步骤
```

> 目录内的 `docker-compose.yml` 通过相对路径 `../../backend`、`../../database` 复用仓库根目录的构建上下文与迁移脚本。请**在 `deploy/<level>/` 目录内**执行 `docker compose`（或使用 `--project-directory`）。

## 拓扑与角色

| 级别 | 目录 | `Federation__Role` | 接收转发数据 | 向上转发 | 说明 |
| --- | --- | --- | --- | --- | --- |
| 省 | `deploy/province` | `AGGREGATOR` | 是 | 否 | 只读汇聚节点：禁业务写，仅登录 + 联动管理 + 接收上报 |
| 市 | `deploy/city` | `BUSINESS` | 是 | 是 | 中间汇聚：接收县级上报，再向省级转发（多跳 县→市→省） |
| 县 | `deploy/county` | `BUSINESS` | 否 | 否 | 业务节点：承接本地业务并向市/省上报 |

联动拓扑默认为**扁平对等（星型扇入）**：每个业务节点可直接向省级推送。若县与市之间网络更优，可开启市级的 `ForwardReceived` + 接收端 `AcceptForwarded`，由市级中转向省级转发；由于落库按**原始来源**去重（幂等键 `来源+日期`），重复上报不会造成双计。

## 上线顺序

1. **省级先行**：`deploy/province` 起服 → 拿到省级 `http://<host>:8080` 地址与联动共享密钥。
2. **市级接入**：`deploy/city` 起服 → 登录该市管理端，在「联动」页登记省级为对端（或调用 `POST /api/admin/federation/nodes`）。
3. **县级接入**：`deploy/county` 起服 → 登记市级（推荐）或省级为对端。
4. **互信**：所有节点 `FEDERATION_SHARED_KEY` 必须一致（`SYMMETRIC` 模式）；若用 `ASYMMETRIC` 模式，各节点在「联动 → 设置 → 密钥管理」生成密钥对，并在对端条目上「拉取公钥」。

> 批量引导可参考仓库根目录 `scripts/federation-bootstrap.sh`；本地演示见 `scripts/federation-demo.sh`。
> 三级一体编排（省+市+2县）见仓库根目录 `docker-compose.cluster.yml`。

## 关键默认值（已按生产建议预置）

- `FEDERATION_BACKFILL_DAYS=7`：每次同步回补近 7 天，避免节点中断 >1 天导致指标永久缺失。
- `FEDERATION_SYNC_INTERVAL_MINUTES=30`：同步间隔；worker 内置启动错峰（10~40s）与周期抖动（0~20%），多节点不会同时批量推拉。
- `FEDERATION_HEALTH_STALE_MINUTES=0`：自动取 `max(2×同步间隔, 60)` 作为对端「过期」告警阈值。

## 网络与安全

- 节点间使用 **HTTP**（内网/专线/VPN）。如需公网直连，请前置 TLS 终止（Nginx/网关）并将对端 `baseUrl` 配为 `https://`。
- 出站自由文本经 `PiiMasker` 脱敏；订单买卖双方以假名 `ref_xxx` 传输。
- 令牌双模式：`SYMMETRIC`(HS256 共享密钥，默认) / `ASYMMETRIC`(RS256 每节点密钥对，单点泄露不可伪造全网令牌)。生产强烈建议 `ASYMMETRIC`。
