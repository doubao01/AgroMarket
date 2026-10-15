<template>
  <view class="page">
    <!-- 规则配置 -->
    <view class="tabs">
      <view class="tab" :class="{ active: tab === 'rules' }" @tap="switchTab('rules')">报警规则</view>
      <view class="tab" :class="{ active: tab === 'records' }" @tap="switchTab('records')">报警记录</view>
    </view>

    <view v-if="tab === 'rules'" class="container">
      <view class="toolbar">
        <button class="btn" :loading="scanning" @tap="doScan">{{ scanning ? '扫描中…' : '立即扫描' }}</button>
        <text v-if="scanSummary" class="muted">执行 {{ scanSummary.executed }} 条 · 新报警 {{ scanSummary.hit }} · 抑制 {{ scanSummary.suppressed }}</text>
      </view>
      <view v-for="r in rules" :key="r.code" class="card">
        <view class="between">
          <text class="card-title">{{ r.name }}</text>
          <switch :checked="r.enabled" color="#2e8b57" style="transform:scale(0.8)" @change="(e) => toggleRule(r, e)" />
        </view>
        <view class="muted mt8">{{ r.desc }}</view>
        <view class="muted mt8">接收范围：{{ scopeText(r.recipientScope) }} · 抑制窗口：{{ r.suppressHours }} 小时</view>
        <view v-if="r.thresholdKey" class="field mt8">
          <text class="field-label">{{ r.thresholdLabel }}</text>
          <input v-model="r.thresholdInput" class="input" type="number" :placeholder="String(r.thresholdValue)" />
          <text class="mini" @tap="saveThreshold(r)">保存</text>
        </view>
      </view>
    </view>

    <view v-if="tab === 'records'" class="container">
      <view class="toolbar">
        <picker :range="ruleFilterLabels" :value="ruleFilterIndex" @change="onRuleFilter">
          <text class="mini">规则筛选：{{ ruleFilterLabels[ruleFilterIndex] }}</text>
        </picker>
        <text class="mini" @tap="loadRecords">刷新</text>
      </view>
      <view v-if="!records.length" class="empty">暂无报警记录</view>
      <view v-for="rec in records" :key="rec.id" class="card" :class="{ open: rec.status === 'OPEN' }">
        <view class="between">
          <text class="card-title">{{ ruleName(rec.ruleCode) }}</text>
          <text class="tag" :class="{ danger: rec.status === 'OPEN' }">{{ rec.status === 'OPEN' ? '未处理' : '已解决' }}</text>
        </view>
        <view class="mt8">{{ rec.detail }}</view>
        <view class="muted mt8">触发 {{ rec.hitCount }} 次 · 首次 {{ fmt(rec.createdAt) }}<text v-if="rec.resolvedAt"> · 解决于 {{ fmt(rec.resolvedAt) }}</text></view>
      </view>
      <view v-if="hasMore" class="toolbar"><text class="mini" @tap="loadMore">加载更多</text></view>
    </view>
  </view>
</template>

<script>
import { alertApi } from '../../api/index'
import { requireLogin, getUser } from '../../utils/request'

const RULE_META = {
  ORDER_PAY_TIMEOUT: { name: '订单超时未支付', desc: '买家下单后超过阈值时长仍未支付，提醒买方与平台。', thresholdKey: 'hours', thresholdLabel: '超时时长（小时）' },
  QUOTE_NO_RESPONSE: { name: '报价长期未响应', desc: '报价发出后超过阈值时长卖方未处理，提醒卖方与平台。', thresholdKey: 'hours', thresholdLabel: '响应时长（小时）' },
  LISTING_EXPIRED: { name: '挂牌过期', desc: '供应/采购挂牌过了有效期仍为上架状态，提醒发布者下架或续期。' },
  AUDIT_BACKLOG: { name: '审核积压', desc: '待审核的供应/采购总数超过阈值，提醒平台管理员及时处理。', thresholdKey: 'count', thresholdLabel: '积压阈值（条）' },
  TRADE_ANOMALY: { name: '成交额异常波动', desc: '今日真实成交额较前 7 日日均偏离超过阈值（每日至多提醒一次）。', thresholdKey: 'percent', thresholdLabel: '波动阈值（%）' },
  FED_SYNC_STALE: { name: '联动同步滞后', desc: '对端节点长时间未推送/拉取数据，提醒平台管理员检查联动链路。' },
}
const RULE_ORDER = ['ORDER_PAY_TIMEOUT', 'QUOTE_NO_RESPONSE', 'LISTING_EXPIRED', 'AUDIT_BACKLOG', 'TRADE_ANOMALY', 'FED_SYNC_STALE']

export default {
  data() {
    return {
      tab: 'rules',
      rules: [],
      records: [],
      page: 1,
      hasMore: false,
      ruleFilterIndex: 0,
      scanning: false,
      scanSummary: null,
    }
  },
  computed: {
    ruleFilterLabels() { return ['全部规则', ...RULE_ORDER.map((c) => RULE_META[c].name)] },
  },
  onLoad() {
    if (!requireLogin()) return
    const me = getUser()
    if (!me || me.role !== 'ADMIN') {
      uni.showToast({ title: '需要管理员权限', icon: 'none' })
      return setTimeout(() => uni.navigateBack(), 800)
    }
  },
  onShow() { if (requireLogin()) this.loadAll() },
  onPullDownRefresh() { this.loadAll().finally(() => uni.stopPullDownRefresh()) },
  methods: {
    async loadAll() {
      await Promise.all([this.loadRules(), this.loadRecords()])
    },
    async loadRules() {
      try {
        const list = await alertApi.rules()
        this.rules = RULE_ORDER.map((code) => {
          const raw = list.find((r) => r.code === code) || {}
          const meta = RULE_META[code]
          let thresholdValue = null
          try { thresholdValue = JSON.parse(raw.thresholdJson || '{}')[meta.thresholdKey] ?? null } catch (e) {}
          return {
            code, ...meta,
            enabled: !!raw.enabled,
            suppressHours: raw.suppressHours || 24,
            recipientScope: raw.recipientScope || 'ADMIN_ONLY',
            thresholdValue, thresholdInput: '',
          }
        })
      } catch (e) {}
    },
    async toggleRule(r, e) {
      try {
        await alertApi.updateRule(r.code, { enabled: e.detail.value })
        r.enabled = e.detail.value
      } catch (err) { e.detail && (e.detail.value = r.enabled) }
    },
    async saveThreshold(r) {
      const v = Number(r.thresholdInput)
      if (!v || v <= 0) { uni.showToast({ title: '请输入有效阈值', icon: 'none' }); return }
      try {
        await alertApi.updateRule(r.code, { thresholdValue: v })
        r.thresholdValue = v
        r.thresholdInput = ''
        uni.showToast({ title: '已保存', icon: 'success' })
      } catch (err) {}
    },
    async loadRecords() {
      this.page = 1
      try {
        const code = this.ruleFilterIndex > 0 ? RULE_ORDER[this.ruleFilterIndex - 1] : undefined
        const res = await alertApi.records({ page: 1, pageSize: 20, ruleCode: code })
        this.records = res.items || []
        this.hasMore = this.records.length < Number(res.total || 0)
      } catch (e) {}
    },
    async loadMore() {
      try {
        const code = this.ruleFilterIndex > 0 ? RULE_ORDER[this.ruleFilterIndex - 1] : undefined
        const res = await alertApi.records({ page: this.page + 1, pageSize: 20, ruleCode: code })
        this.records = this.records.concat(res.items || [])
        this.page += 1
        this.hasMore = this.records.length < Number(res.total || 0)
      } catch (e) {}
    },
    onRuleFilter(e) {
      this.ruleFilterIndex = Number(e.detail.value)
      this.loadRecords()
    },
    async doScan() {
      this.scanning = true
      try {
        this.scanSummary = await alertApi.scan()
        if (this.tab === 'records') this.loadRecords()
        uni.showToast({ title: `新报警 ${this.scanSummary.hit} 条`, icon: 'none' })
      } catch (e) {} finally { this.scanning = false }
    },
    switchTab(t) {
      this.tab = t
      if (t === 'records') this.loadRecords()
    },
    ruleName(code) { return (RULE_META[code] || {}).name || code },
    scopeText(s) { return { PARTY: '业务双方', PARTY_AND_ADMIN: '双方与管理员', ADMIN_ONLY: '仅管理员', PUBLISHER: '发布者' }[s] || s },
    fmt(t) { return (t || '').replace('T', ' ').slice(0, 16) },
  },
}
</script>

<style>
.toolbar { display: flex; align-items: center; gap: 20rpx; padding: 20rpx 0; }
.card { background: #fff; border-radius: 12rpx; padding: 24rpx; margin-bottom: 20rpx; }
.card.open { border-left: 8rpx solid #e6a23c; }
.card-title { font-size: 30rpx; font-weight: 600; }
.field { display: flex; align-items: center; gap: 16rpx; }
.field-label { font-size: 26rpx; color: #666; flex-shrink: 0; }
</style>
