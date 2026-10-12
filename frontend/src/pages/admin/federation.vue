<template>
  <view class="page">
    <!-- 本节点 -->
    <view class="card" style="margin:24rpx 24rpx 0;">
      <view class="between">
        <text class="card-title">{{ self.nodeName || '本节点' }}</text>
        <text class="tag" :class="{ danger: !self.enabled }">{{ self.enabled ? '已启用' : '未启用' }}</text>
      </view>
      <view class="muted mt8">{{ self.nodeCode || '未配置' }} · {{ levelText(self.level) }}<text v-if="self.regionCode"> · {{ self.regionCode }}</text></view>
      <view class="muted mt8">自动同步：{{ self.autoSync ? ('每 ' + self.syncIntervalMinutes + ' 分钟') : '关闭' }}</view>
      <view v-if="!self.hasIdentity" class="error mt8">未完成节点身份配置（Federation:NodeCode / SharedKey），节点接口不可用。</view>
      <view class="actions mt16">
        <text class="mini" @tap="doPush">立即推送</text>
        <text class="mini" @tap="doPull">立即拉取</text>
        <text class="mini" @tap="loadSelf">刷新</text>
      </view>
    </view>

    <view class="tabs mt16">
      <view class="tab" :class="{ active: tab === 'node' }" @tap="switchTab('node')">对端节点</view>
      <view class="tab" :class="{ active: tab === 'audit' }" @tap="switchTab('audit')">上报审计</view>
      <view class="tab" :class="{ active: tab === 'agg' }" @tap="switchTab('agg')">跨节点汇聚</view>
    </view>

    <!-- 对端节点 -->
    <view v-if="tab === 'node'" class="container">
      <view v-if="!nodes.length" class="empty">暂无对端节点</view>
      <view v-for="n in nodes" :key="n.id" class="card">
        <view class="between">
          <text class="card-title">{{ n.nodeName }}</text>
          <text class="tag" :class="{ danger: n.status !== 'ACTIVE' }">{{ n.status === 'ACTIVE' ? '启用' : '停用' }}</text>
        </view>
        <view class="muted mt8">{{ n.nodeCode }} · {{ levelText(n.level) }}<text v-if="n.regionCode"> · {{ n.regionCode }}</text> · {{ dirText(n.direction) }}</view>
        <view class="muted mt8">{{ n.baseUrl }}</view>
        <view class="muted mt8">令牌：{{ n.hasToken ? '已配置' : '使用本节点令牌(自动)' }}</view>
        <view class="muted mt8">推送 {{ fmt(n.lastPushAt) || '—' }} · 拉取 {{ fmt(n.lastPullAt) || '—' }}</view>
        <view v-if="n.lastError" class="error mt8">{{ n.lastError }}</view>
        <view class="actions mt16">
          <text class="mini" @tap="pushOne(n)">推送</text>
          <text class="mini" @tap="pullOne(n)">拉取</text>
          <text class="mini" @tap="toggleNode(n)">{{ n.status === 'ACTIVE' ? '停用' : '启用' }}</text>
          <text class="mini" @tap="editNode(n)">编辑</text>
          <text class="mini danger" @tap="removeNode(n)">删除</text>
        </view>
      </view>

      <view class="section-title mt24">{{ editingId ? '编辑节点' : '新增对端节点' }}</view>
      <view class="card">
        <view class="field"><text class="field-label">节点编码 *</text><input v-model="form.nodeCode" class="input" :disabled="!!editingId" placeholder="如 node-hn-prov" /></view>
        <view class="field"><text class="field-label">节点名称 *</text><input v-model="form.nodeName" class="input" placeholder="如 河南省农贸通" /></view>
        <view class="field">
          <text class="field-label">层级 *</text>
          <picker :range="levels" :value="levelIndex" @change="onLevel"><text class="flex1">{{ levels[levelIndex] }}</text></picker>
        </view>
        <view class="field"><text class="field-label">行政区划码</text><input v-model="form.regionCode" class="input" placeholder="可空" /></view>
        <view class="field"><text class="field-label">对端地址 *</text><input v-model="form.baseUrl" class="input" placeholder="https://peer.example.com" /></view>
        <view class="field">
          <text class="field-label">同步方向</text>
          <picker :range="directionLabels" :value="dirIndex" @change="onDir"><text class="flex1">{{ directionLabels[dirIndex] }}</text></picker>
        </view>
        <view class="field"><text class="field-label">节点令牌</text><input v-model="form.nodeToken" class="input" placeholder="留空则用本节点令牌" /></view>
        <view class="actions mt8">
          <button class="btn flex1" @tap="saveNode">{{ editingId ? '保存修改' : '添加节点' }}</button>
          <text v-if="editingId" class="mini" style="margin-left:20rpx;" @tap="resetForm">取消</text>
        </view>
      </view>

      <view class="section-title mt24">签发节点令牌</view>
      <view class="card">
        <view class="muted">为其它节点（或本节点）签发令牌，对方即可凭此令牌访问本节点接口。</view>
        <view class="field mt8"><text class="field-label">节点编码</text><input v-model="tokenForm.nodeCode" class="input" placeholder="留空=本节点" /></view>
        <view class="field"><text class="field-label">有效期(天)</text><input v-model="tokenForm.days" class="input" type="number" placeholder="365" /></view>
        <button class="btn btn-block mt8" @tap="issueToken">生成令牌</button>
        <view v-if="issuedToken" class="mt8">
          <text class="muted">令牌（点击下方复制）</text>
          <textarea class="input token-text mt8" :value="issuedToken" disabled />
          <text class="mini mt8" @tap="copyToken">复制令牌</text>
        </view>
      </view>
    </view>

    <!-- 上报审计 -->
    <view v-else-if="tab === 'audit'" class="container">
      <view class="section-title">上报批次</view>
      <view v-if="!reports.length" class="empty">暂无上报</view>
      <view v-for="r in reports" :key="r.id" class="card">
        <view class="between">
          <text class="card-title">{{ r.sourceNodeCode }}</text>
          <text class="muted">{{ r.periodDate }}</text>
        </view>
        <view class="muted mt8">批次 {{ r.batchNo }}</view>
        <view class="muted mt8">指标 {{ r.metricCount }} · 明细 {{ r.recordCount }} · {{ fmt(r.receivedAt) }}</view>
      </view>
      <view v-if="reports.length" class="load-more muted" @tap="loadReports(true)">加载更多</view>

      <view class="section-title mt24">最近业务明细</view>
      <view class="card search-bar">
        <picker :range="recordTypeLabels" :value="typeIndex" @change="onType"><text class="flex1">{{ recordTypeLabels[typeIndex] }}</text></picker>
        <text class="mini" @tap="loadRecords">刷新</text>
      </view>
      <view v-if="!records.length" class="empty">暂无明细</view>
      <view v-for="rd in records" :key="rd.id" class="card">
        <view class="between">
          <text class="card-title">{{ rd.type }}</text>
          <text class="muted">{{ fmt(rd.occurredAt) }}</text>
        </view>
        <view class="muted mt8">{{ rd.sourceNodeCode }} · {{ rd.externalId }}<text v-if="rd.regionCode"> · {{ rd.regionCode }}</text></view>
        <view class="muted mt8">{{ recordSummary(rd) }}</view>
      </view>
    </view>

    <!-- 跨节点汇聚 -->
    <view v-else class="container">
      <view class="card search-bar">
        <picker :range="ranges" :value="rangeIndex" @change="onRange"><text class="flex1">统计区间：{{ ranges[rangeIndex] }}</text></picker>
        <text class="mini" @tap="loadAggregate">刷新</text>
      </view>
      <view class="stat-grid" style="padding:0;">
        <view class="stat"><text class="stat-num">{{ agg.nodeCount }}</text><text class="muted">参与节点</text></view>
        <view class="stat"><text class="stat-num">{{ agg.totalOrders }}</text><text class="muted">成交订单</text></view>
      </view>
      <view class="card">
        <view class="between"><text>跨节点成交额</text><text class="price-big">{{ agg.totalTradeAmount }} 元</text></view>
      </view>
      <view class="section-title mt16">按来源节点</view>
      <view v-if="!agg.byNode.length" class="empty">暂无数据</view>
      <view v-for="bn in agg.byNode" :key="bn.sourceNodeCode" class="card between">
        <text>{{ bn.sourceNodeCode }}</text>
        <text class="muted">{{ bn.orderCount }} 单 / {{ bn.tradeAmount }} 元</text>
      </view>
      <view class="section-title mt16">按日趋势</view>
      <view v-if="!agg.points.length" class="empty">暂无数据</view>
      <view v-for="p in agg.points" :key="p.statDate" class="card between">
        <text>{{ p.statDate }}</text>
        <text class="muted">{{ p.orderCount }} 单 / {{ p.tradeAmount }} 元</text>
      </view>
    </view>
  </view>
</template>

<script>
import { federationApi } from '../../api/index'
import { requireLogin, getUser } from '../../utils/request'

const LEVELS = ['COUNTY', 'CITY', 'PROVINCE']
const DIRECTIONS = ['BOTH', 'PUSH', 'PULL']
const DIRECTION_LABELS = ['双向同步', '仅推送', '仅拉取']

export default {
  data() {
    return {
      tab: 'node',
      self: { enabled: false, hasIdentity: false, nodeCode: '', nodeName: '', level: '', regionCode: '', autoSync: false, syncIntervalMinutes: 0 },
      nodes: [], editingId: 0,
      levels: LEVELS, levelIndex: 0,
      directions: DIRECTIONS, directionLabels: DIRECTION_LABELS, dirIndex: 0,
      form: { nodeCode: '', nodeName: '', level: 'COUNTY', regionCode: '', baseUrl: '', nodeToken: '' },
      tokenForm: { nodeCode: '', days: '365' }, issuedToken: '',
      reports: [], reportPage: 1,
      records: [], recordTypeLabels: ['全部类型', 'ORDER', 'SUPPLY', 'DEMAND'], typeIndex: 0,
      ranges: ['7d', '30d', '90d'], rangeIndex: 1,
      agg: { nodeCount: 0, totalOrders: 0, totalTradeAmount: 0, byNode: [], points: [] },
    }
  },
  onLoad() {
    if (!requireLogin()) return
    const me = getUser()
    if (!me || me.role !== 'ADMIN') {
      uni.showToast({ title: '需要管理员权限', icon: 'none' })
      return setTimeout(() => uni.navigateBack(), 800)
    }
    this.loadSelf()
    this.loadNodes()
  },
  methods: {
    switchTab(t) {
      this.tab = t
      if (t === 'audit') { this.loadReports(); this.loadRecords() }
      if (t === 'agg') this.loadAggregate()
    },
    async loadSelf() {
      try { this.self = await federationApi.self() } catch (e) {}
    },
    async loadNodes() {
      try { this.nodes = (await federationApi.nodes()) || [] } catch (e) { this.nodes = [] }
    },
    async doPush() {
      try {
        const r = await federationApi.syncPush()
        uni.showToast({ title: `推送 ${r.succeeded}/${r.targets} 成功`, icon: 'none' })
        this.loadNodes()
      } catch (e) {}
    },
    async doPull() {
      try {
        const r = await federationApi.syncPull()
        uni.showToast({ title: `拉取 ${r.succeeded}/${r.targets} 成功`, icon: 'none' })
        this.loadNodes()
      } catch (e) {}
    },
    async pushOne(n) {
      try {
        const r = await federationApi.syncPush(n.nodeCode)
        uni.showToast({ title: (r.succeeded ? '推送成功' : '推送失败') + (r.details[0] && r.details[0].error ? '：' + r.details[0].error : ''), icon: 'none' })
        this.loadNodes()
      } catch (e) {}
    },
    async pullOne(n) {
      try {
        const r = await federationApi.syncPull(n.nodeCode)
        uni.showToast({ title: r.succeeded ? '拉取成功' : '拉取失败', icon: 'none' })
        this.loadNodes()
      } catch (e) {}
    },
    async toggleNode(n) {
      try {
        await federationApi.setNodeStatus(n.id, n.status === 'ACTIVE' ? 'DISABLED' : 'ACTIVE')
        uni.showToast({ title: '已更新' })
        this.loadNodes()
      } catch (e) {}
    },
    async removeNode(n) {
      const confirm = await new Promise(resolve => uni.showModal({
        title: '删除节点', content: `确定删除对端节点「${n.nodeName}」？`, success: r => resolve(r.confirm),
      }))
      if (!confirm) return
      try { await federationApi.removeNode(n.id); uni.showToast({ title: '已删除' }); this.loadNodes() } catch (e) {}
    },
    editNode(n) {
      this.editingId = n.id
      this.form = { nodeCode: n.nodeCode, nodeName: n.nodeName, level: n.level, regionCode: n.regionCode || '', baseUrl: n.baseUrl, nodeToken: '' }
      this.levelIndex = Math.max(0, LEVELS.indexOf(n.level))
      this.dirIndex = Math.max(0, DIRECTIONS.indexOf(n.direction))
      uni.pageScrollTo({ scrollTop: 0, duration: 200 })
    },
    resetForm() {
      this.editingId = 0
      this.form = { nodeCode: '', nodeName: '', level: 'COUNTY', regionCode: '', baseUrl: '', nodeToken: '' }
      this.levelIndex = 0; this.dirIndex = 0
    },
    async saveNode() {
      const f = this.form
      if (!f.nodeCode) return uni.showToast({ title: '请填写节点编码', icon: 'none' })
      if (!f.nodeName) return uni.showToast({ title: '请填写节点名称', icon: 'none' })
      if (!/^https?:\/\//.test(f.baseUrl)) return uni.showToast({ title: '地址需以 http(s):// 开头', icon: 'none' })
      const payload = {
        nodeCode: f.nodeCode, nodeName: f.nodeName, level: f.level,
        regionCode: f.regionCode || null, baseUrl: f.baseUrl,
        nodeToken: f.nodeToken || null, direction: DIRECTIONS[this.dirIndex], status: 'ACTIVE',
      }
      try {
        if (this.editingId) await federationApi.updateNode(this.editingId, payload)
        else await federationApi.saveNode(payload)
        uni.showToast({ title: this.editingId ? '已保存' : '已添加' })
        this.resetForm(); this.loadNodes()
      } catch (e) {}
    },
    onLevel(e) { this.levelIndex = Number(e.detail.value); this.form.level = LEVELS[this.levelIndex] },
    onDir(e) { this.dirIndex = Number(e.detail.value) },
    async issueToken() {
      const days = parseInt(this.tokenForm.days, 10)
      try {
        const r = await federationApi.issueToken({ nodeCode: this.tokenForm.nodeCode || null, days: isNaN(days) ? 365 : days })
        this.issuedToken = r.token
        uni.showToast({ title: '已生成令牌', icon: 'none' })
      } catch (e) {}
    },
    copyToken() {
      if (!this.issuedToken) return
      uni.setClipboardData({ data: this.issuedToken, success: () => uni.showToast({ title: '已复制' }) })
    },
    async loadReports(more) {
      try {
        const page = more ? this.reportPage : 1
        const res = await federationApi.reports({ page, size: 20 })
        const items = (res && res.items) || []
        this.reports = more ? this.reports.concat(items) : items
        this.reportPage = items.length >= 20 ? page + 1 : page
      } catch (e) { if (!more) this.reports = [] }
    },
    onType(e) { this.typeIndex = Number(e.detail.value); this.loadRecords() },
    async loadRecords() {
      const type = this.typeIndex > 0 ? this.recordTypeLabels[this.typeIndex] : undefined
      try { this.records = (await federationApi.records({ type, days: 7, limit: 50 })) || [] } catch (e) { this.records = [] }
    },
    onRange(e) { this.rangeIndex = Number(e.detail.value); this.loadAggregate() },
    async loadAggregate() {
      try {
        const r = await federationApi.aggregate(this.ranges[this.rangeIndex])
        this.agg = r || { nodeCount: 0, totalOrders: 0, totalTradeAmount: 0, byNode: [], points: [] }
      } catch (e) {}
    },
    levelText(l) { return { PROVINCE: '省级', CITY: '市级', COUNTY: '县级' }[l] || l || '' },
    dirText(d) { return { BOTH: '双向', PUSH: '推送', PULL: '拉取' }[d] || d || '' },
    fmt(t) { return t ? String(t).replace('T', ' ').slice(0, 16) : '' },
    recordSummary(rd) {
      const d = rd.data || {}
      const parts = []
      if (d.title) parts.push(d.title)
      if (d.productName) parts.push(d.productName)
      if (d.category) parts.push(d.category)
      if (d.amount != null) parts.push(d.amount + ' 元')
      if (d.quantity != null && d.unit) parts.push(d.quantity + d.unit)
      if (d.townName) parts.push(d.townName)
      return parts.filter(Boolean).join(' · ') || '—'
    },
  },
}
</script>

<style>
.tabs { display: flex; background: #fff; }
.tab { flex: 1; text-align: center; padding: 24rpx 0; font-size: 28rpx; color: #7a8a80; }
.tab.active { color: #2e8b57; font-weight: 600; }
.container { padding: 24rpx; }
.card-title { font-size: 30rpx; font-weight: 600; flex: 1; margin-right: 16rpx; }
.actions { display: flex; flex-wrap: wrap; align-items: center; }
.mini { font-size: 26rpx; color: #2e8b57; border: 1rpx solid #2e8b57; border-radius: 999rpx; padding: 8rpx 28rpx; margin-right: 20rpx; margin-bottom: 12rpx; }
.mini.danger { color: #dd524d; border-color: #dd524d; }
.search-bar { display: flex; align-items: center; justify-content: space-between; }
.tag.danger { color: #dd524d; border-color: #dd524d; }
.section-title { font-size: 30rpx; font-weight: 600; }
.load-more { text-align: center; padding: 24rpx 0; }
.error { color: #dd524d; font-size: 24rpx; }
.token-text { width: 100%; min-height: 120rpx; font-size: 22rpx; word-break: break-all; }
</style>
