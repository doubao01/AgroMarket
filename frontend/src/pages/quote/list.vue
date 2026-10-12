<template>
  <view class="page">
    <view class="tabs">
      <view class="tab" :class="{ active: scope === 'ALL' }" @tap="setScope('ALL')">全部</view>
      <view class="tab" :class="{ active: scope === 'BUY' }" @tap="setScope('BUY')">我发起</view>
      <view class="tab" :class="{ active: scope === 'SELL' }" @tap="setScope('SELL')">我收到</view>
    </view>
    <view class="container">
      <view v-if="!loading && !items.length" class="empty">暂无报价记录</view>
      <view v-for="q in items" :key="q.id" class="card" @tap="goDetail(q.id)">
        <view class="between">
          <text class="card-title">{{ q.supplyTitle || q.demandTitle || ('报价 #' + q.id) }}</text>
          <text class="price">{{ q.price }} {{ q.priceUnit }}</text>
        </view>
        <view class="mt8 muted">{{ q.quantity }} · {{ q.supplyId ? '供应' : '采购' }} · {{ fmtTime(q.createdAt) }}</view>
        <view class="between mt8">
          <text class="tag">{{ statusText(q.status) }}</text>
          <text class="muted">{{ q.initiatorId === me.id ? '我发起' : '对方发起' }}</text>
        </view>
      </view>
    </view>
  </view>
</template>

<script>
import { quoteApi } from '../../api/index'
import { requireLogin, getUser } from '../../utils/request'

export default {
  data() { return { scope: 'ALL', items: [], loading: false, me: { id: 0 } } },
  onLoad() { if (!requireLogin()) return; this.me = getUser() || { id: 0 }; this.reload() },
  onShow() { if (this.me && this.me.id) this.reload() },
  onPullDownRefresh() { this.reload().finally(() => uni.stopPullDownRefresh()) },
  methods: {
    setScope(s) { this.scope = s; this.reload() },
    async reload() {
      this.loading = true
      try {
        const res = await quoteApi.list({ scope: this.scope, page: 1, pageSize: 30 })
        this.items = res.items || []
      } catch (e) {} finally { this.loading = false }
    },
    goDetail(id) { uni.navigateTo({ url: '/pages/quote/detail?id=' + id }) },
    statusText(s) {
      return { PENDING: '待回复', NEGOTIATED: '已还价', ACCEPTED: '已接受', REJECTED: '已拒绝', DEALED: '已成交' }[s] || s
    },
    fmtTime(t) { return (t || '').replace('T', ' ').slice(0, 16) },
  },
}
</script>

<style>
.tabs { display: flex; background: #fff; }
.tab { flex: 1; text-align: center; padding: 24rpx 0; font-size: 28rpx; color: #7a8a80; }
.tab.active { color: #2e8b57; font-weight: 600; }
.card-title { font-size: 30rpx; font-weight: 600; flex: 1; margin-right: 16rpx; }
.container { padding: 24rpx; }
</style>
