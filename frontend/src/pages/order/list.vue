<template>
  <view class="page">
    <view class="tabs">
      <view class="tab" :class="{ active: role === 'ALL' }" @tap="setRole('ALL')">全部</view>
      <view class="tab" :class="{ active: role === 'BUY' }" @tap="setRole('BUY')">我买</view>
      <view class="tab" :class="{ active: role === 'SELL' }" @tap="setRole('SELL')">我卖</view>
    </view>
    <view class="container">
      <view v-if="!loading && !items.length" class="empty">暂无订单</view>
      <view v-for="o in items" :key="o.id" class="card" @tap="go(o.id)">
        <view class="between">
          <text class="card-title">{{ o.items && o.items[0] ? o.items[0].productName : ('订单 ' + o.orderNo.slice(0, 8)) }}</text>
          <text class="tag">{{ statusText(o.status) }}</text>
        </view>
        <view class="between mt8">
          <text class="muted">{{ o.buyerId === me.id ? '卖家：' + o.sellerName : '买家：' + o.buyerName }}</text>
          <text class="price">{{ o.totalAmount }} 元</text>
        </view>
        <view class="muted mt8">{{ o.orderNo }}</view>
      </view>
    </view>
  </view>
</template>

<script>
import { orderApi } from '../../api/index'
import { requireLogin, getUser } from '../../utils/request'

export default {
  data() { return { role: 'ALL', status: '', items: [], loading: false, me: { id: 0 } } },
  onLoad() { if (!requireLogin()) return; this.me = getUser() || { id: 0 }; this.reload() },
  onShow() { if (this.me && this.me.id) this.reload() },
  onPullDownRefresh() { this.reload().finally(() => uni.stopPullDownRefresh()) },
  methods: {
    setRole(r) { this.role = r; this.reload() },
    async reload() {
      this.loading = true
      try {
        const res = await orderApi.list({ role: this.role, status: this.status || undefined, page: 1, pageSize: 30 })
        this.items = res.items || []
      } catch (e) {} finally { this.loading = false }
    },
    go(id) { uni.navigateTo({ url: '/pages/order/detail?id=' + id }) },
    statusText(s) {
      return {
        PENDING_CONFIRM: '待卖方确认', TO_PAY: '待付款', TO_SHIP: '待发货', SHIPPING: '配送中',
        TO_RECEIVE: '待收货', COMPLETED: '已完成', CANCELLED: '已取消', AFTER_SALE: '售后中',
      }[s] || s
    },
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
