# 省级节点部署（AGGREGATOR 只读汇聚）

省级节点作为全网汇聚点：**禁止业务写操作**（仅放行登录与联动管理接口），通过联动接收市/县上报指标与交易明细。

## 一、起服

```bash
cp .env.example .env
# 编辑 .env: 至少填写 MYSQL_ROOT_PASSWORD / MYSQL_PASSWORD / JWT_SECRET_KEY / FEDERATION_SHARED_KEY / 节点标识
docker compose up -d --build
docker compose logs -f api
```

首启会自动执行 `database/0*.sql` 全部迁移（含联动相关 `07/08/09`）。健康检查：

```bash
curl -fsS http://localhost:8080/health    # 站点存活(公开)；节点探测 /api/federation/node/ping 需携带节点令牌
```

## 二、登记下级对端

以管理员登录省级管理端 → 「联动」页新增对端，或调用接口：

```bash
# 1) 管理员登录拿 token
TOKEN=$(curl -s http://localhost:8080/api/auth/login/password \
  -H 'Content-Type: application/json' \
  -d '{"phone":"13800000001","password":"Abc123456"}' | jq -r .data.token)

# 2) 登记一个市级对端（baseUrl 为市级节点对省级可达的地址）
curl -s http://localhost:8080/api/admin/federation/nodes \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"nodeCode":"node-zhoukou","nodeName":"周口市农贸通","level":"CITY","baseUrl":"http://10.0.0.11:8080","regionCode":"411600"}'
```

> 批量登记可参考仓库根目录 `scripts/federation-bootstrap.sh`。

## 三、令牌模式

- **SYMMETRIC（默认）**：`FEDERATION_SHARED_KEY` 与全网一致即可，无需额外操作。
- **ASYMMETRIC**：在「联动 → 设置 → 密钥管理」点击「生成密钥对」；随后对每个对端点击「拉取公钥」。未登记公钥的对端令牌将被拒绝（401）。

## 四、查看汇聚结果

- 「联动 → 总览 / 指标 / 明细」可按对端与 `regionPrefix` 过滤查看汇聚数据。
- 「联动 → 设置」查看同步健康（各对端 `healthy / stale / lastError`）。

## 五、说明

- 省级不参与时间窗内的自身业务上报（无业务写）；`IncludeSelfInAggregate` 影响的是"全景"是否并入本节点自身指标。
- 建议前置 TLS 终止，并将下级登记省级时的 `baseUrl` 配为 `https://`。
