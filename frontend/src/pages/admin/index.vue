<template>
  <view class="page">
    <view class="stat-grid">
      <view class="stat"><text class="stat-num">{{ stats.users }}</text><text class="muted">注册用户</text></view>
      <view class="stat"><text class="stat-num">{{ stats.supplies }}</text><text class="muted">在售供应</text></view>
      <view class="stat"><text class="stat-num">{{ stats.demands }}</text><text class="muted">采购需求</text></view>
      <view class="stat"><text class="stat-num">{{ stats.orders }}</text><text class="muted">真实成交</text></view>
    </view>
    <view class="card" style="margin:0 24rpx;">
      <view class="between"><text>真实成交额</text><text class="price-big">{{ stats.dealAmount }} 元</text></view>
      <view class="between mt8"><text class="muted">演示订单成交额（不计入真实）</text><text class="muted">{{ stats.demoDealAmount }} 元</text></view>
    </view>

    <view class="card dash-entry" style="margin:16rpx 24rpx;" @tap="goDashboard">
      <text class="dash-entry-title">进入数据大屏</text>
      <text class="dash-entry-arrow">›</text>
    </view>

    <view class="card dash-entry" style="margin:16rpx 24rpx;" @tap="goManage">
      <text class="dash-entry-title">用户 / 公告 / 日志管理</text>
      <text class="dash-entry-arrow">›</text>
    </view>

    <view class="card dash-entry" style="margin:16rpx 24rpx;" @tap="goFederation">
      <text class="dash-entry-title">节点联动 / 数据上报同步</text>
      <text class="dash-entry-arrow">›</text>
    </view>

    <view class="tabs mt16">
      <view class="tab" :class="{ active: tab === 'supply' }" @tap="switchTab('supply')">供审</view>
      <view class="tab" :class="{ active: tab === 'demand' }" @tap="switchTab('demand')">采审</view>
      <view class="tab" :class="{ active: tab === 'auth' }" @tap="switchTab('auth')">认证</view>
      <view class="tab" :class="{ active: tab === 'report' }" @tap="switchTab('report')">举报</view>
    </view>

    <view class="container">
      <view v-if="!loading && !items.length" class="empty">暂无待处理</view>
      <view v-for="it in items" :key="it.id" class="card">
        <view class="between">
          <text class="card-title">{{ itemTitle(it) }}</text>
          <text class="tag">{{ itemStatus(it) }}</text>
        </view>
        <view class="muted mt8">{{ itemSub(it) }}</view>
        <view class="actions mt16">
          <text class="mini" @tap="approve(it)">通过</text>
          <text class="mini danger" @tap="reject(it)">驳回</text>
        </view>
      </view>
    </view>
  </view>
</template>

<script>
import { adminApi } from '../../api/index'
import { requireLogin, getUser } from '../../utils/request'

export default {
  data() {
    return {
      stats: { users: 0, supplies: 0, demands: 0, orders: 0, dealAmount: 0, demoDealAmount: 0 },
      tab: 'supply', items: [], loading: false,
    }
  },
  onLoad() {
    if (!requireLogin()) return
    const me = getUser()
    if (!me || me.role !== 'ADMIN') { uni.showToast({ title: '需要管理员权限', icon: 'none' }); return setTimeout(() => uni.navigateBack(), 800) }
    this.loadStats()
    this.reload()
  },
  methods: {
    async loadStats() { try { const s = await adminApi.stats(); if (s) this.stats = s } catch (e) {} },
    goDashboard() { uni.navigateTo({ url: '/pages/dashboard/index' }) },
    goManage() { uni.navigateTo({ url: '/pages/admin/manage' }) },
    goFederation() { uni.navigateTo({ url: '/pages/admin/federation' }) },
    switchTab(t) { this.tab = t; this.reload() },
    async reload() {
      this.loading = true
      try {
        let res
        if (this.tab === 'supply') res = await adminApi.supplies({ status: 'PENDING', page: 1, pageSize: 30 })
        else if (this.tab === 'demand') res = await adminApi.demands({ status: 'PENDING', page: 1, pageSize: 30 })
        else if (this.tab === 'auth') res = await adminApi.auths({ status: 'PENDING', page: 1, pageSize: 30 })
        else res = await adminApi.reports({ status: 'PENDING', page: 1, pageSize: 30 })
        this.items = (res && res.items) || res || []
        if (!Array.isArray(this.items)) this.items = []
      } catch (e) { this.items = [] } finally { this.loading = false }
    },
    itemTitle(it) {
      if (this.tab === 'auth') return it.subjectName || ('认证 #' + it.id)
      if (this.tab === 'report') return (it.targetType || '') + ' 举报 #' + it.targetId
      return it.title
    },
    itemStatus(it) { return it.status || it.auditStatus || '' },
    itemSub(it) {
      if (this.tab === 'auth') return (it.subjectType || '') + (it.idNumber ? ' · ' + it.idNumber : '')
      if (this.tab === 'report') return it.reason || ''
      return (it.quantity || '') + (it.unit || '') + ' · ' + (it.townName || '')
    },
    async approve(it) {
      try {
        if (this.tab === 'supply') await adminApi.auditSupply(it.id, true)
        else if (this.tab === 'demand') await adminApi.auditDemand(it.id, true)
        else if (this.tab === 'auth') await adminApi.auditAuth(it.id, true)
        else await adminApi.handleReport(it.id, true)
        uni.showToast({ title: '已通过' })
        this.reload(); this.loadStats()
      } catch (e) {}
    },
    async reject(it) {
      try {
        if (this.tab === 'supply') await adminApi.auditSupply(it.id, false)
        else if (this.tab === 'demand') await adminApi.auditDemand(it.id, false)
        else if (this.tab === 'auth') await adminApi.auditAuth(it.id, false)
        else await adminApi.handleReport(it.id, false)
        uni.showToast({ title: '已驳回' })
        this.reload(); this.loadStats()
      } catch (e) {}
    },
  },
}
</script>

<style>
.stat-grid { display: flex; padding: 24rpx; }
.stat { flex: 1; background: #fff; border-radius: 20rpx; margin: 0 8rpx; padding: 28rpx 0; text-align: center; }
.stat-num { display: block; font-size: 40rpx; font-weight: 700; color: #2e8b57; margin-bottom: 8rpx; }
.tabs { display: flex; background: #fff; }
.tab { flex: 1; text-align: center; padding: 24rpx 0; font-size: 28rpx; color: #7a8a80; }
.tab.active { color: #2e8b57; font-weight: 600; }
.card-title { font-size: 30rpx; font-weight: 600; flex: 1; margin-right: 16rpx; }
.container { padding: 24rpx; }
.actions { display: flex; }
.mini { font-size: 26rpx; color: #2e8b57; border: 1rpx solid #2e8b57; border-radius: 999rpx; padding: 8rpx 32rpx; margin-right: 20rpx; }
.mini.danger { color: #dd524d; border-color: #dd524d; }
.dash-entry { display: flex; align-items: center; justify-content: space-between; }
.dash-entry-title { font-size: 30rpx; font-weight: 600; color: #2e8b57; }
.dash-entry-arrow { font-size: 40rpx; color: #a7b3ac; }
</style>
