<template>
  <view class="page">
    <view class="tabs">
      <view class="tab" :class="{ active: tab === 'user' }" @tap="switchTab('user')">用户管理</view>
      <view class="tab" :class="{ active: tab === 'notice' }" @tap="switchTab('notice')">公告发布</view>
      <view class="tab" :class="{ active: tab === 'log' }" @tap="switchTab('log')">操作日志</view>
    </view>

    <!-- 用户管理 -->
    <view v-if="tab === 'user'" class="container">
      <view class="card search-bar">
        <input v-model="userKw" class="input flex1" placeholder="手机号/昵称搜索" confirm-type="search" @confirm="loadUsers" />
        <text class="mini" @tap="loadUsers">搜索</text>
      </view>
      <view v-if="!users.length" class="empty">暂无用户</view>
      <view v-for="u in users" :key="u.id" class="card">
        <view class="between">
          <text class="card-title">{{ u.nickname || '用户 ' + u.id }}</text>
          <text class="tag" :class="{ danger: u.status === 'FROZEN' }">{{ userStatusText(u.status) }}</text>
        </view>
        <view class="muted mt8">{{ maskPhone(u.phone) }} · {{ roleText(u.role) }} · 注册于 {{ fmt(u.createdAt) }}</view>
        <view class="actions mt16">
          <text v-if="u.status !== 'FROZEN'" class="mini danger" @tap="setFreeze(u, true)">冻结</text>
          <text v-else class="mini" @tap="setFreeze(u, false)">解冻</text>
        </view>
      </view>
    </view>

    <!-- 公告发布 -->
    <view v-else-if="tab === 'notice'" class="container">
      <view class="card">
        <view class="field">
          <text class="field-label">公告标题 *</text>
          <input v-model="noticeForm.title" class="input" placeholder="如：市场交易规则更新" />
        </view>
        <view class="field" style="align-items:flex-start;">
          <text class="field-label">公告内容 *</text>
          <textarea v-model="noticeForm.content" class="input" style="min-height:160rpx;" placeholder="公告正文" />
        </view>
        <button class="btn btn-block" :disabled="publishing" @tap="publishNotice">{{ publishing ? '发布中...' : '发布公告' }}</button>
      </view>
      <view class="section-title mt24">已发布公告</view>
      <view v-if="!notices.length" class="empty">暂无公告</view>
      <view v-for="n in notices" :key="n.id" class="card">
        <view class="between">
          <text class="card-title">{{ n.title }}</text>
          <text class="mini danger" @tap="unpublishNotice(n)">下线</text>
        </view>
        <view class="muted mt8">{{ n.content }}</view>
        <view class="muted mt8">{{ fmt(n.createdAt) }}</view>
      </view>
    </view>

    <!-- 操作日志 -->
    <view v-else class="container">
      <view class="card search-bar">
        <picker :range="logModules" :value="logModuleIndex" @change="onLogModule">
          <text class="flex1">{{ logModules[logModuleIndex] }}</text>
        </picker>
      </view>
      <view v-if="!logs.length" class="empty">暂无日志</view>
      <view v-for="(l, i) in logs" :key="l.id || i" class="card">
        <view class="between">
          <text class="card-title">{{ l.module || l.action || '日志' }}</text>
          <text class="muted">{{ fmt(l.createdAt) }}</text>
        </view>
        <view class="muted mt8">{{ logDetail(l) }}</view>
      </view>
      <view v-if="logs.length" class="load-more muted" @tap="loadLogs(true)">加载更多</view>
    </view>
  </view>
</template>

<script>
import { adminApi, noticeApi } from '../../api/index'
import { requireLogin, getUser } from '../../utils/request'

export default {
  data() {
    return {
      tab: 'user',
      users: [], userKw: '',
      notices: [], noticeForm: { title: '', content: '' }, publishing: false,
      logs: [], logPage: 1, logModules: ['全部模块', 'USER', 'SUPPLY', 'DEMAND', 'ORDER', 'AUTH', 'REPORT'], logModuleIndex: 0,
    }
  },
  onLoad() {
    if (!requireLogin()) return
    const me = getUser()
    if (!me || me.role !== 'ADMIN') {
      uni.showToast({ title: '需要管理员权限', icon: 'none' })
      return setTimeout(() => uni.navigateBack(), 800)
    }
    this.loadUsers()
  },
  methods: {
    switchTab(t) {
      this.tab = t
      if (t === 'user' && !this.users.length) this.loadUsers()
      if (t === 'notice' && !this.notices.length) this.loadNotices()
      if (t === 'log' && !this.logs.length) { this.logPage = 1; this.logs = []; this.loadLogs() }
    },
    async loadUsers() {
      try {
        const res = await adminApi.users({ keyword: this.userKw || undefined, page: 1, pageSize: 30 })
        this.users = (res && res.items) || []
      } catch (e) { this.users = [] }
    },
    async setFreeze(u, freeze) {
      const confirm = await new Promise(resolve => uni.showModal({
        title: freeze ? '冻结用户' : '解冻用户',
        content: freeze ? `确定冻结 ${u.nickname || u.phone}？冻结后无法登录。` : `确定解冻 ${u.nickname || u.phone}？`,
        success: r => resolve(r.confirm),
      }))
      if (!confirm) return
      try {
        await adminApi.freeze(u.id, freeze)
        uni.showToast({ title: freeze ? '已冻结' : '已解冻' })
        this.loadUsers()
      } catch (e) {}
    },
    async loadNotices() {
      try { this.notices = (await noticeApi.list()) || [] } catch (e) { this.notices = [] }
    },
    async publishNotice() {
      const f = this.noticeForm
      if (!f.title) return uni.showToast({ title: '请填写标题', icon: 'none' })
      if (!f.content) return uni.showToast({ title: '请填写内容', icon: 'none' })
      this.publishing = true
      try {
        await adminApi.createNotice({ title: f.title, content: f.content })
        uni.showToast({ title: '公告已发布' })
        f.title = ''; f.content = ''
        this.loadNotices()
      } catch (e) {} finally { this.publishing = false }
    },
    async unpublishNotice(n) {
      try {
        await adminApi.unpublishNotice(n.id)
        uni.showToast({ title: '公告已下线' })
        this.loadNotices()
      } catch (e) {}
    },
    onLogModule(e) {
      this.logModuleIndex = Number(e.detail.value)
      this.logPage = 1; this.logs = []
      this.loadLogs()
    },
    async loadLogs(more) {
      try {
        const module = this.logModuleIndex > 0 ? this.logModules[this.logModuleIndex] : undefined
        const res = await adminApi.logs({ module, page: this.logPage, pageSize: 20 })
        const items = (res && res.items) || []
        this.logs = more ? this.logs.concat(items) : items
        if (items.length >= 20) this.logPage += 1
      } catch (e) { if (!more) this.logs = [] }
    },
    userStatusText(s) { return { ACTIVE: '正常', FROZEN: '已冻结' }[s] || s || '' },
    roleText(r) { return { USER: '普通用户', ADMIN: '管理员', MERCHANT: '商户' }[r] || r || '' },
    maskPhone(p) { return p ? p.slice(0, 3) + '****' + p.slice(-4) : '' },
    fmt(t) { return t ? String(t).replace('T', ' ').slice(0, 16) : '' },
    logDetail(l) {
      const parts = [l.operatorName || l.operatorId, l.action, l.detail || l.summary]
      return parts.filter(Boolean).join(' · ')
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
.actions { display: flex; }
.mini { font-size: 26rpx; color: #2e8b57; border: 1rpx solid #2e8b57; border-radius: 999rpx; padding: 8rpx 28rpx; margin-left: 20rpx; }
.mini.danger { color: #dd524d; border-color: #dd524d; }
.search-bar { display: flex; align-items: center; }
.search-bar .mini { white-space: nowrap; }
.tag.danger { color: #dd524d; border-color: #dd524d; }
.section-title { font-size: 30rpx; font-weight: 600; }
.load-more { text-align: center; padding: 24rpx 0; }
</style>
