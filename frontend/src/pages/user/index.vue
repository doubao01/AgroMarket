<template>
  <view class="page">
    <view class="user-header">
      <view v-if="me" class="between">
        <view class="row">
          <view class="avatar">{{ (me.nickname || '农')[0] }}</view>
          <view style="margin-left:20rpx;">
            <view class="uname">{{ me.nickname }}</view>
            <view class="uphone">{{ maskPhone(me.phone) }}</view>
          </view>
        </view>
        <text class="tag" style="background:rgba(255,255,255,0.25);color:#fff;">{{ roleText(me.role) }}</text>
      </view>
      <view v-else class="between" @tap="go('/pages/user/login')">
        <view class="row">
          <view class="avatar">?</view>
          <view style="margin-left:20rpx;">
            <view class="uname">点击登录</view>
            <view class="uphone">登录后管理您的交易</view>
          </view>
        </view>
        <text style="color:#fff;">›</text>
      </view>
    </view>

    <view class="quick container">
      <view class="q-item" @tap="go('/pages/user/listings')"><text class="q-num">{{ counts.supplies }}</text><text class="muted">我的供应</text></view>
      <view class="q-item" @tap="go('/pages/user/listings?tab=demand')"><text class="q-num">{{ counts.demands }}</text><text class="muted">我的采购</text></view>
      <view class="q-item" @tap="go('/pages/quote/list')"><text class="q-num">›</text><text class="muted">报价</text></view>
      <view class="q-item" @tap="go('/pages/order/list')"><text class="q-num">›</text><text class="muted">订单</text></view>
    </view>

    <view class="container">
      <view class="card menu">
        <view class="menu-item" @tap="go('/pages/user/listings')"><text>我的发布</text><text class="muted">›</text></view>
        <view class="menu-item" @tap="go('/pages/ai/assistant')"><text>AI 助手</text><text class="muted">›</text></view>
        <view class="menu-item" @tap="go('/pages/order/list')"><text>我的订单</text><text class="muted">›</text></view>
        <view class="menu-item" @tap="go('/pages/quote/list')"><text>我的报价</text><text class="muted">›</text></view>
        <view class="menu-item" @tap="go('/pages/user/favorites')"><text>我的收藏</text><text class="muted">›</text></view>
        <view class="menu-item" @tap="go('/pages/user/blocks')"><text>黑名单</text><text class="muted">›</text></view>
        <view class="menu-item" @tap="go('/pages/address/list')"><text>收货地址</text><text class="muted">›</text></view>
        <view class="menu-item" @tap="go('/pages/user/auth')"><text>主体认证</text><text class="accent">{{ authStatus }}</text></view>
        <view class="menu-item" @tap="go('/pages/user/profile')"><text>个人资料</text><text class="muted">›</text></view>
        <view v-if="isAdmin" class="menu-item" @tap="go('/pages/admin/index')"><text>管理后台</text><text class="muted">›</text></view>
      </view>

      <button v-if="me" class="btn btn-plain btn-block mt24" @tap="logout">退出登录</button>
    </view>
  </view>
</template>

<script>
import { supplyApi, demandApi, userApi } from '../../api/index'
import { getUser, clearAuth, ensureLogin, getToken } from '../../utils/request'
import { teardownChat } from '../../utils/chat-store'

export default {
  data() { return { me: null, counts: { supplies: 0, demands: 0 }, authStatus: '' } },
  onShow() { this.refresh() },
  computed: { isAdmin() { return this.me && this.me.role === 'ADMIN' } },
  methods: {
    async refresh() {
      if (!getToken()) { this.me = null; return }
      this.me = getUser()
      const me = await ensureLogin(true)
      if (me) this.me = me
      if (!this.me) return
      try {
        const s = await supplyApi.list({ status: 'MINE', page: 1, pageSize: 1 })
        this.counts.supplies = s.total || 0
      } catch (e) {}
      try {
        const d = await demandApi.list({ status: 'MINE', page: 1, pageSize: 1 })
        this.counts.demands = d.total || 0
      } catch (e) {}
      try {
        const a = await userApi.getMerchantAuth()
        this.authStatus = a ? auditText(a.auditStatus) : '未提交'
      } catch (e) { this.authStatus = '未提交' }
    },
    go(url) { uni.navigateTo({ url }) },
    logout() {
      uni.showModal({ title: '退出登录', content: '确定退出当前账号？', success: (r) => {
        if (r.confirm) { clearAuth(); teardownChat(); this.me = null; uni.showToast({ title: '已退出' }) }
      } })
    },
    maskPhone(p) { return p ? p.slice(0, 3) + '****' + p.slice(7) : '' },
    roleText(r) { return { ADMIN: '平台管理员', FARMER: '农户', COOP: '合作社', BUYER: '采购商', ENTERPRISE: '企业', MERCHANT: '商户' }[r] || r },
    auditText(s) { return { PENDING: '审核中', APPROVED: '已认证', REJECTED: '已驳回' }[s] || s },
  },
}
</script>

<style>
.user-header { background: linear-gradient(135deg, #2e8b57, #3aa86a); padding: 60rpx 32rpx 90rpx; color: #fff; }
.avatar { width: 110rpx; height: 110rpx; border-radius: 50%; background: rgba(255, 255, 255, 0.25); display: flex; align-items: center; justify-content: center; font-size: 44rpx; }
.uname { font-size: 36rpx; font-weight: 700; }
.uphone { font-size: 24rpx; opacity: 0.9; margin-top: 6rpx; }
.quick { display: flex; margin-top: -60rpx; }
.q-item { flex: 1; background: #fff; border-radius: 20rpx; margin: 0 8rpx; padding: 28rpx 0; text-align: center; box-shadow: 0 4rpx 16rpx rgba(46, 139, 87, 0.08); }
.q-num { display: block; font-size: 34rpx; font-weight: 700; margin-bottom: 8rpx; }
.menu { padding: 0 24rpx; }
.menu-item { display: flex; justify-content: space-between; align-items: center; padding: 30rpx 0; border-bottom: 1rpx solid #e3e9e4; font-size: 30rpx; }
.menu-item:last-child { border-bottom: none; }
</style>
