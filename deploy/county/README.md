# 县级节点部署（业务节点：本地经营并向市/省上报）

县级节点是主要的业务节点：承接本地农商业务，并按周期把**聚合指标**与**交易明细**上报到上级（市级或省级）。默认不接收、不转发第三方数据。

## 一、起服

```bash
cp .env.example .env
# 编辑 .env: 填写 MYSQL_* / JWT_SECRET_KEY / FEDERATION_SHARED_KEY / 节点标识
docker compose up -d --build
curl -fsS http://localhost:8080/health    # 站点存活(公开)；节点探测 /api/federation/node/ping 需携带节点令牌
```

## 二、登记上级对端

管理员登录后在「联动」页登记上级（市或省）：

```bash
TOKEN=$(curl -s http://localhost:8080/api/auth/login/password \
  -H 'Content-Type: application/json' \
  -d '{"phone":"13800000001","password":"Abc123456"}' | jq -r .data.token)

# 推荐: 登记市级(由市级统一向上转发); 若县级可直连省级, 也可直接登记省级
curl -s http://localhost:8080/api/admin/federation/nodes \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"nodeCode":"node-zhoukou","nodeName":"周口市农贸通","level":"CITY","baseUrl":"http://10.0.0.11:8080","regionCode":"411600"}'
```

> 批量登记可参考仓库根目录 `scripts/federation-bootstrap.sh`。

## 三、上报内容与节奏

- **上报范围**：本地聚合指标（口径复用 `DashboardService.RealOrderWhere`）+ 交易明细（买卖双方以假名 `ref_xxx` 传输，自由文本经 `PiiMasker` 脱敏）。
- **节奏**：`FEDERATION_SYNC_INTERVAL_MINUTES`（默认 30）+ 启动错峰（10~40s）+ 周期抖动（0~20%）。
- **回补**：`FEDERATION_BACKFILL_DAYS=7`，每次同步回补近 7 天，防止中断 >1 天导致指标永久缺失。

## 四、令牌模式

- **SYMMETRIC（默认）**：`FEDERATION_SHARED_KEY` 与全网一致即可。
- **ASYMMETRIC**：本节点「联动 → 设置 → 密钥管理」生成密钥对；上级在其对端条目上「拉取公钥」。

## 五、手动触发同步（排障用）

管理端「联动」页可对某个对端执行「推送」/「拉取」；或：

```bash
curl -s -X POST "http://localhost:8080/api/admin/federation/sync/push?node=<对端nodeCode>" \
  -H "Authorization: Bearer $TOKEN"
```

「联动 → 设置」查看同步健康（`healthy / stale / lastError`）。
