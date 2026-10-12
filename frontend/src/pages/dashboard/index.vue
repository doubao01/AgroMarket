<template>
  <view class="dash" :class="{ dark }">
    <!-- 顶部标题栏 -->
    <view class="hero">
      <view class="hero-main">
        <view class="hero-title">息县农贸通 · 数据大屏</view>
        <view class="hero-sub">统计口径：真实成交（不含演示与模拟支付） · 每 30 秒自动刷新</view>
      </view>
      <text class="theme-toggle" @tap="toggleTheme">{{ dark ? '亮色' : '投屏' }}</text>
    </view>

    <!-- 核心指标 -->
    <view class="metrics">
      <view class="metric" v-for="m in metrics" :key="m.label">
        <view class="metric-val">{{ m.value }}</view>
        <view class="metric-label">{{ m.label }}</view>
      </view>
    </view>

    <!-- 成交趋势 -->
    <view class="panel">
      <view class="panel-head">
        <text class="panel-title">成交趋势</text>
        <view class="range-tabs">
          <text v-for="r in ranges" :key="r.key" class="range-tab"
            :class="{ active: trendRange === r.key }" @tap="switchTrendRange(r.key)">{{ r.label }}</text>
        </view>
      </view>
      <u-chart
        v-if="trend.length"
        chart-type="mix" :categories="trendCats" :series="trendSeries" :dark="dark" :height="260"
      />
      <view v-if="!trend.length" class="empty">暂无数据</view>
      <view class="trend-legend">
        <text>订单量合计 {{ trendTotal }} 单</text>
        <text>成交额 {{ trendAmount }} 元</text>
      </view>
    </view>

    <!-- 品类分布 -->
    <view class="panel">
      <view class="panel-head"><text class="panel-title">品类供需</text></view>
      <u-chart
        v-if="category.length"
        chart-type="column" :categories="categoryCats" :series="categorySeries" :dark="dark" :height="240"
      />
      <view v-if="!category.length" class="empty">暂无数据</view>
    </view>

    <!-- 乡镇分布 -->
    <view class="panel">
      <view class="panel-head"><text class="panel-title">乡镇供需 TOP</text></view>
      <view class="row-list">
        <view class="row" v-for="r in regionTop" :key="r.townId">
          <text class="row-name">{{ r.townName }}</text>
          <view class="row-bar-wrap">
            <view class="row-bar orange" :style="{ width: pct(r.supplyCount + r.demandCount, regionMax) }"></view>
          </view>
          <text class="row-val">{{ r.supplyCount + r.demandCount }}</text>
        </view>
        <view v-if="!regionTop.length" class="empty">暂无数据</view>
      </view>
    </view>

    <!-- 用户结构 -->
    <view class="panel">
      <view class="panel-head">
        <text class="panel-title">用户结构</text>
        <view class="range-tabs">
          <text v-for="r in ranges" :key="r.key" class="range-tab"
            :class="{ active: userTrendRange === r.key }" @tap="switchUserTrendRange(r.key)">{{ r.label }}</text>
        </view>
      </view>
      <view class="chips">
        <view class="chip" v-for="u in users" :key="u.role">
          <text class="chip-role">{{ roleLabel(u.role) }}</text>
          <text class="chip-val">{{ u.count }}</text>
        </view>
      </view>
      <u-chart
        v-if="userTrend.length"
        chart-type="mix" :categories="userTrendCats" :series="userTrendSeries" :dark="dark" :height="220"
      />
    </view>

    <!-- 消息活跃 -->
    <view class="panel">
      <view class="panel-head"><text class="panel-title">消息活跃</text></view>
      <u-chart
        v-if="chatActivity.length"
        chart-type="column" :categories="chatCats" :series="chatSeries" :dark="dark" :height="200" :legend="false"
      />
      <view v-if="!chatActivity.length" class="empty">暂无数据</view>
    </view>

    <!-- AI 解读 -->
    <view class="panel ai-panel">
      <view class="panel-head">
        <text class="panel-title">AI 数据解读</text>
        <text class="ai-btn" @tap="loadInsight">{{ insight ? '重新解读' : '生成解读' }}</text>
      </view>
      <view v-if="insightLoading" class="empty">分析中...</view>
      <view v-else-if="insight">
        <view class="ai-summary">{{ insight.summary }}</view>
        <view class="ai-anomaly" v-for="(a, i) in insight.anomalies || []" :key="'a' + i">! {{ a }}</view>
        <view class="ai-hl" v-for="(h, i) in insight.highlights" :key="i">· {{ h }}</view>
      </view>
      <view v-else class="empty">点击「生成解读」由 AI 分析当前数据</view>

      <view class="ask-box">
        <input class="ask-input" v-model="askText" placeholder="对数据提问，如：本周成交额如何" />
        <button class="ask-btn" size="mini" @tap="ask">提问</button>
      </view>
      <view v-if="askAnswer" class="ai-answer">{{ askAnswer }}</view>
    </view>

    <view class="foot" v-if="loading">加载中...</view>
  </view>
</template>

<script>
import { dashboardApi, aiApi } from '../../api/index.js'
import UChart from '../../components/u-chart.vue'

export default {
  components: { UChart },
  data() {
    return {
      metrics: [],
      trend: [],
      trendRange: '30d',
      ranges: [
        { key: '7d', label: '7天' },
        { key: '30d', label: '30天' },
        { key: '90d', label: '90天' },
      ],
      category: [],
      regionTop: [],
      users: [],
      userTrend: [],
      userTrendRange: '30d',
      chatActivity: [],
      insight: null,
      insightLoading: false,
      askText: '',
      askAnswer: '',
      loading: false,
      dark: false,
      autoTimer: null,
    }
  },
  computed: {
    trendCats() { return this.trend.map(p => this.shortDate(p.date)) },
    trendSeries() {
      return [
        { name: '成交额(元)', type: 'line', data: this.trend.map(p => Number(p.dealAmount || 0)) },
        { name: '订单量', type: 'column', data: this.trend.map(p => p.orderCount || 0) },
      ]
    },
    categoryCats() { return this.category.map(c => c.categoryName) },
    categorySeries() {
      return [
        { name: '供应', type: 'column', data: this.category.map(c => c.supplyCount || 0) },
        { name: '采购', type: 'column', data: this.category.map(c => c.demandCount || 0) },
      ]
    },
    chatCats() { return this.chatActivity.map(p => this.shortDate(p.date)) },
    chatSeries() {
      return [{ name: '消息数', type: 'column', data: this.chatActivity.map(p => p.messageCount || 0) }]
    },
    userTrendCats() { return this.userTrend.map(p => this.shortDate(p.date)) },
    userTrendSeries() {
      return [
        { name: '新增用户', type: 'line', data: this.userTrend.map(p => p.newUsers || 0) },
        { name: '活跃用户', type: 'line', data: this.userTrend.map(p => p.activeUsers || 0) },
      ]
    },
    regionMax() { return Math.max(1, ...this.regionTop.map(r => r.supplyCount + r.demandCount)) },
    trendTotal() { return this.trend.reduce((s, p) => s + (p.orderCount || 0), 0) },
    trendAmount() { return this.trend.reduce((s, p) => s + Number(p.dealAmount || 0), 0).toFixed(2) },
  },
  onLoad() {
    try { this.dark = uni.getStorageSync('dash_dark') === 1 } catch (e) {}
    this.loadAll()
    this.autoTimer = setInterval(() => { this.loadAll(true) }, 30000)
  },
  onHide() { this.stopAuto() },
  onUnload() { this.stopAuto() },
  onPullDownRefresh() {
    this.loadAll().then(() => uni.stopPullDownRefresh())
  },
  methods: {
    stopAuto() { if (this.autoTimer) { clearInterval(this.autoTimer); this.autoTimer = null } },
    async loadAll(silent) {
      if (!silent) this.loading = true
      try {
        const [ov, cat, reg, us] = await Promise.all([
          dashboardApi.overview(),
          dashboardApi.category(),
          dashboardApi.region(),
          dashboardApi.users(),
        ])
        this.buildMetrics(ov)
        this.category = cat || []
        this.regionTop = (reg || [])
          .filter(r => (r.supplyCount + r.demandCount) > 0)
          .sort((a, b) => (b.supplyCount + b.demandCount) - (a.supplyCount + a.demandCount))
          .slice(0, 8)
        this.users = us || []
        await Promise.all([this.loadTrend(), this.loadChat(), this.loadUserTrend()])
      } catch (e) {
        if (!silent) uni.showToast({ title: (e && e.msg) || '加载失败', icon: 'none' })
      } finally {
        this.loading = false
      }
    },
    buildMetrics(ov) {
      this.metrics = [
        { label: '注册用户', value: ov.users },
        { label: '活跃用户(30天)', value: ov.activeUsers },
        { label: '在售供应', value: ov.supplies },
        { label: '在售需求', value: ov.demands },
        { label: '真实成交单', value: ov.orders },
        { label: '成交额(元)', value: Number(ov.dealAmount || 0).toFixed(2) },
        { label: '今日成交单', value: ov.todayOrders },
        { label: '今日成交额', value: Number(ov.todayDealAmount || 0).toFixed(2) },
      ]
    },
    async loadTrend() {
      this.trend = (await dashboardApi.trend(this.trendRange)) || []
    },
    async loadChat() {
      this.chatActivity = (await dashboardApi.chat(this.trendRange)) || []
    },
    async loadUserTrend() {
      this.userTrend = (await dashboardApi.userTrend(this.userTrendRange)) || []
    },
    switchUserTrendRange(key) {
      this.userTrendRange = key
      this.loadUserTrend()
    },
    switchTrendRange(key) {
      this.trendRange = key
      Promise.all([this.loadTrend(), this.loadChat()])
    },
    pct(v, max) {
      const p = max > 0 ? (v / max) * 100 : 0
      return Math.max(v > 0 ? 6 : 0, p) + '%'
    },
    shortDate(d) { return d ? d.slice(5) : '' },
    toggleTheme() {
      this.dark = !this.dark
      try { uni.setStorageSync('dash_dark', this.dark ? 1 : 0) } catch (e) {}
    },
    roleLabel(r) {
      const map = { ADMIN: '管理员', FARMER: '农户', BUYER: '采购商', COOP: '合作社' }
      return map[r] || r
    },
    async loadInsight() {
      this.insightLoading = true
      try {
        this.insight = await aiApi.insight()
      } catch (e) {
        uni.showToast({ title: (e && e.msg) || 'AI 未配置', icon: 'none' })
      } finally {
        this.insightLoading = false
      }
    },
    async ask() {
      const q = (this.askText || '').trim()
      if (!q) return
      uni.showLoading({ title: '思考中' })
      try {
        const res = await aiApi.ask(q)
        this.askAnswer = res.answer
      } catch (e) {
        uni.showToast({ title: (e && e.msg) || 'AI 未配置', icon: 'none' })
      } finally {
        uni.hideLoading()
      }
    },
  },
}
</script>

<style scoped>
.dash { min-height: 100vh; background: #f4f6f4; padding: 24rpx 24rpx 60rpx; box-sizing: border-box; }
.hero { display: flex; align-items: center; justify-content: space-between; background: linear-gradient(135deg, #2e8b57, #3aa76d); border-radius: 20rpx; padding: 32rpx; color: #fff; }
.hero-title { font-size: 38rpx; font-weight: 700; }
.hero-sub { font-size: 22rpx; opacity: 0.85; margin-top: 10rpx; }
.theme-toggle {
  flex: none; margin-left: 20rpx; font-size: 24rpx; color: #fff;
  border: 1rpx solid rgba(255,255,255,0.7); border-radius: 24rpx; padding: 8rpx 24rpx;
}

.metrics { display: flex; flex-wrap: wrap; margin-top: 24rpx; }
.metric { width: 50%; box-sizing: border-box; padding: 8rpx; }
.metric-val { background: #fff; border-radius: 16rpx 16rpx 0 0; font-size: 44rpx; font-weight: 700; color: #2e8b57; padding: 24rpx 24rpx 4rpx; text-align: center; }
.metric-label { background: #fff; border-radius: 0 0 16rpx 16rpx; font-size: 22rpx; color: #7a8a80; padding: 0 12rpx 24rpx; text-align: center; }

.panel { background: #fff; border-radius: 20rpx; padding: 28rpx; margin-top: 24rpx; }
.panel-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 24rpx; }
.panel-title { font-size: 30rpx; font-weight: 600; color: #22322a; }
.range-tabs { display: flex; }
.range-tab { font-size: 22rpx; color: #7a8a80; padding: 6rpx 16rpx; border-radius: 20rpx; margin-left: 8rpx; background: #f0f3f1; }
.range-tab.active { background: #2e8b57; color: #fff; }

.trend-legend { display: flex; justify-content: space-between; margin-top: 16rpx; font-size: 22rpx; color: #7a8a80; }
.row-list { display: flex; flex-direction: column; }
.row { display: flex; align-items: center; margin-bottom: 18rpx; }
.row-name { width: 150rpx; font-size: 24rpx; color: #22322a; }
.row-bar-wrap { flex: 1; height: 20rpx; background: #eef2ef; border-radius: 10rpx; overflow: hidden; }
.row-bar { height: 100%; background: #2e8b57; border-radius: 10rpx; }
.row-bar.orange { background: #f2952b; }
.row-val { width: 150rpx; text-align: right; font-size: 22rpx; color: #7a8a80; }

.chips { display: flex; flex-wrap: wrap; }
.chip { background: #f0f6f2; border-radius: 14rpx; padding: 20rpx 28rpx; margin: 0 16rpx 16rpx 0; display: flex; flex-direction: column; align-items: center; }
.chip-role { font-size: 22rpx; color: #7a8a80; }
.chip-val { font-size: 34rpx; font-weight: 700; color: #2e8b57; }

.empty { font-size: 24rpx; color: #a7b3ac; padding: 20rpx 0; }
.ai-panel { }
.ai-btn { font-size: 24rpx; color: #2e8b57; border: 1rpx solid #2e8b57; border-radius: 20rpx; padding: 6rpx 20rpx; }
.ai-summary { font-size: 28rpx; color: #22322a; font-weight: 600; margin-bottom: 14rpx; }
.ai-anomaly { font-size: 25rpx; color: #d47b1f; line-height: 1.6; font-weight: 600; }
.ai-hl { font-size: 25rpx; color: #4a5a50; line-height: 1.6; }
.ask-box { display: flex; align-items: center; margin-top: 20rpx; }
.ask-input { flex: 1; background: #f0f3f1; border-radius: 12rpx; padding: 16rpx 20rpx; font-size: 25rpx; }
.ask-btn { margin-left: 16rpx; background: #2e8b57; color: #fff; font-size: 24rpx; }
.ai-answer { margin-top: 18rpx; background: #f0f6f2; border-radius: 12rpx; padding: 20rpx; font-size: 25rpx; color: #22322a; line-height: 1.6; }
.foot { text-align: center; font-size: 24rpx; color: #a7b3ac; padding: 30rpx; }

/* ---------- 暗色投屏主题 ---------- */
.dash.dark { background: #0d1b14; }
.dash.dark .hero { background: linear-gradient(135deg, #103a28, #1d5c3f); }
.dash.dark .metric-val { background: #14251c; color: #5fd694; }
.dash.dark .metric-label { background: #14251c; color: #8fa79a; }
.dash.dark .panel { background: #14251c; }
.dash.dark .panel-title { color: #e6f2ea; }
.dash.dark .range-tab { color: #8fa79a; background: #1e332697; }
.dash.dark .range-tab.active { background: #2e8b57; color: #fff; }
.dash.dark .bar { background: #3aa76d; }
.dash.dark .bar.orange { background: #f2952b; }
.dash.dark .bar.green { background: #5fd694; }
.dash.dark .bar-x { color: #6f8578; }
.dash.dark .trend-legend { color: #8fa79a; }
.dash.dark .row-name { color: #d5e5db; }
.dash.dark .row-bar-wrap { background: #1e3326; }
.dash.dark .row-val { color: #8fa79a; }
.dash.dark .chip { background: #1e3326; }
.dash.dark .chip-role { color: #8fa79a; }
.dash.dark .chip-val { color: #5fd694; }
.dash.dark .empty { color: #6f8578; }
.dash.dark .ai-summary { color: #e6f2ea; }
.dash.dark .ai-anomaly { color: #f2a45c; }
.dash.dark .ai-hl { color: #b6cabf; }
.dash.dark .ask-input { background: #1e3326; color: #e6f2ea; }
.dash.dark .ai-answer { background: #1e3326; color: #d5e5db; }
.dash.dark .foot { color: #6f8578; }

/* ---------- 投屏大字号（远距离可读） ---------- */
.dash.dark .hero-title { font-size: 52rpx; }
.dash.dark .hero-sub { font-size: 26rpx; }
.dash.dark .theme-toggle { font-size: 26rpx; }
.dash.dark .metric-val { font-size: 56rpx; }
.dash.dark .metric-label { font-size: 26rpx; }
.dash.dark .panel-title { font-size: 38rpx; }
.dash.dark .range-tab { font-size: 26rpx; }
.dash.dark .bar-x { font-size: 22rpx; transform: scale(1); }
.dash.dark .trend-legend { font-size: 28rpx; }
.dash.dark .row-name { font-size: 30rpx; width: 180rpx; }
.dash.dark .row-val { font-size: 28rpx; width: 180rpx; }
.dash.dark .row-bar-wrap { height: 24rpx; }
.dash.dark .chip { padding: 24rpx 34rpx; }
.dash.dark .chip-role { font-size: 26rpx; }
.dash.dark .chip-val { font-size: 48rpx; }
.dash.dark .empty { font-size: 28rpx; }
.dash.dark .ai-summary { font-size: 34rpx; }
.dash.dark .ai-hl { font-size: 30rpx; }
.dash.dark .ask-input { font-size: 30rpx; }
.dash.dark .ai-answer { font-size: 30rpx; }
</style>
