<template>
  <view class="page">
    <view class="tabs">
      <view class="tab" :class="{ active: tab === 'supply' }" @tap="switchTab('supply')">我的供应</view>
      <view class="tab" :class="{ active: tab === 'demand' }" @tap="switchTab('demand')">我的采购</view>
    </view>
    <view class="container">
      <view v-if="!loading && !items.length" class="empty">
        暂无发布，<text class="primary" @tap="goPublish">立即发布</text>
      </view>
      <view v-for="it in items" :key="it.id" class="card">
        <view class="between">
          <text class="card-title">{{ it.title }}</text>
          <text class="tag">{{ statusText(it.status) }}</text>
        </view>
        <view class="muted mt8">{{ it.quantity }}{{ it.unit }} · {{ it.townName || '息县' }}</view>
        <view class="actions mt16">
          <text class="mini" @tap="goDetail(it.id)">查看</text>
          <text v-if="canOff(it.status)" class="mini" @tap="changeStatus(it.id, 'off_shelf')">下架</text>
          <text v-if="canOn(it.status)" class="mini" @tap="changeStatus(it.id, 're_activate')">上架</text>
          <text v-if="it.status === 'ACTIVE'" class="mini" @tap="changeStatus(it.id, 'sold_out')">售罄</text>
        </view>
      </view>
    </view>
  </view>
</template>

<script>
import { supplyApi, demandApi } from '../../api/index'
import { requireLogin } from '../../utils/request'

export default {
  data() { return { tab: 'supply', items: [], loading: false } },
  onLoad(query) { if (!requireLogin()) return; if (query.tab === 'demand') this.tab = 'demand'; this.reload() },
  onShow() { if (requireLogin()) this.reload() },
  onPullDownRefresh() { this.reload().finally(() => uni.stopPullDownRefresh()) },
  methods: {
    switchTab(t) { this.tab = t; this.reload() },
    async reload() {
      this.loading = true
      try {
        const api = this.tab === 'supply' ? supplyApi : demandApi
        const res = await api.list({ status: 'MINE', page: 1, pageSize: 50 })
        this.items = res.items || []
      } catch (e) {} finally { this.loading = false }
    },
    async changeStatus(id, op) {
      const api = this.tab === 'supply' ? supplyApi : demandApi
      try { await api.changeStatus(id, op); uni.showToast({ title: '操作成功' }); this.reload() } catch (e) {}
    },
    goDetail(id) {
      uni.navigateTo({ url: (this.tab === 'supply' ? '/pages/supply/detail?id=' : '/pages/demand/detail?id=') + id })
    },
    goPublish() { uni.navigateTo({ url: this.tab === 'supply' ? '/pages/supply/publish' : '/pages/demand/publish' }) },
    canOff(s) { return s === 'ACTIVE' },
    canOn(s) { return ['OFF_SHELF', 'SOLD_OUT'].includes(s) },
    statusText(s) {
      return { ACTIVE: '在售', PENDING: '审核中', OFF_SHELF: '已下架', SOLD_OUT: '已售罄', REJECTED: '已驳回', CLOSED: '已关闭' }[s] || s
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
.actions { display: flex; }
.mini { font-size: 26rpx; color: #2e8b57; border: 1rpx solid #2e8b57; border-radius: 999rpx; padding: 8rpx 28rpx; margin-right: 20rpx; }
</style>
