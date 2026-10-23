# 市级节点部署（中间汇聚：接收县级上报并向上转发）

市级节点既是**业务节点**（本地正常经营），又是**中间汇聚点**：接收所辖县级的转发数据，再向省级转发，实现多跳 `县→市→省`。

## 一、起服

```bash
cp .env.example .env
# 编辑 .env: 填写 MYSQL_* / JWT_SECRET_KEY / FEDERATION_SHARED_KEY / 节点标识
docker compose up -d --build
curl -fsS http://localhost:8080/health    # 站点存活(公开)；节点探测 /api/federation/node/ping 需携带节点令牌
```

## 二、登记上级与下级

管理员登录后，在「联动」页（或接口）完成登记：

- **上级（省级）**：`{nodeCode, level: PROVINCE, baseUrl: <省级可达地址>, regionCode}`。
- **下级（各县级）**：`{nodeCode, level: COUNTY, baseUrl: <县级可达地址>, regionCode}`。

县级节点同样需要把市级登记为对端（对端间 baseUrl 互相可达）。

```bash
# 账号来自部署时设置的 BOOTSTRAP_ADMIN_PHONE / BOOTSTRAP_ADMIN_PASSWORD 与 .env 中的 ADMIN_PASSWORD
TOKEN=$(curl -s http://localhost:8080/api/auth/login/password \
  -H 'Content-Type: application/json' \
  -d "{\"phone\":\"$BOOTSTRAP_ADMIN_PHONE\",\"password\":\"$ADMIN_PASSWORD\"}" | jq -r .data.token)

curl -s http://localhost:8080/api/admin/federation/nodes \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"nodeCode":"node-henan","nodeName":"河南省农贸通","level":"PROVINCE","baseUrl":"http://10.0.0.10:8080","regionCode":"410000"}'
```

## 三、转发行为（本节点已开启）

- `Federation__ForwardReceived=true`：本节点向省级推送时，会把**接收到的县级数据（保留原始来源）**一并转发。
- `Federation__AcceptForwarded=true`：接受县级转发来的第三方来源数据并落库。
- 落库按**原始来源**去重（幂等键 `来源+日期`），因此县级即使同时直报省级、又经市级转发，省级也不会双计。

> 若所在网络允许县级直连省级，可关闭市级转发（`FEDERATION_...`：把 compose 中 `Federation__ForwardReceived` 改为 `false`），退化为星型直连。

## 四、令牌模式

- **SYMMETRIC（默认）**：`FEDERATION_SHARED_KEY` 全网一致即可。
- **ASYMMETRIC**：本节点「联动 → 设置 → 密钥管理」生成密钥对，并在每个对端条目上「拉取公钥」。

## 五、查看

「联动 → 设置」查看各对端同步健康；「联动 → 指标/明细」按来源过滤核对县级上收数据。
