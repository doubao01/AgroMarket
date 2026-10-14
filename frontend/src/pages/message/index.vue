<template>
  <view class="page">
    <view class="topbar between">
      <text class="muted">未读 {{ unread }}</text>
      <text class="primary" @tap="readAll">全部已读</text>
    </view>
    <view class="container">
      <view v-if="!loading && !items.length" class="empty">暂无消息</view>
      <view v-for="m in items" :key="m.id" class="card" :class="{ unread: !m.isRead, alert: m.msgType === 'ALERT' }" @tap="open(m)">
        <view class="between">
          <text class="msg-title" :class="{ 'alert-title': m.msgType === 'ALERT' }">{{ m.msgType === 'ALERT' ? '⚠ ' : '' }}{{ m.title }}</text>
          <text class="muted">{{ fmt(m.createdAt) }}</text>
        </view>
        <view class="muted mt8">{{ m.content }}</view>
      </view>
    </view>
  </view>
</template>

<script>
import { messageApi } from '../../api/index'
import { requireLogin } from '../../utils/request'
import { onChatEvent } from '../../utils/chat-store'

export default {
  data() { return { items: [], unread: 0, loading: false, _offNotify: null } },
  onLoad() { if (!requireLogin()) return },
  onShow() {
    if (!requireLogin()) return
    this.reload()
    // 收到 WS notify 帧时实时刷新列表与未读数
    this._offNotify = onChatEvent('notify', () => this.reload())
  },
  onHide() { if (this._offNotify) { this._offNotify(); this._offNotify = null } },
  onUnload() { if (this._offNotify) { this._offNotify(); this._offNotify = null } },
  onPullDownRefresh() { this.reload().finally(() => uni.stopPullDownRefresh()) },
  methods: {
    async reload() {
      this.loading = true
      try {
        const res = await messageApi.list({ page: 1, pageSize: 30 })
        this.items = res.items || []
        this.unread = this.items.filter(m => !m.isRead).length
      } catch (e) {} finally { this.loading = false }
    },
    async open(m) {
      if (!m.isRead) {
        try { await messageApi.read(m.id); m.isRead = true; this.unread = Math.max(0, this.unread - 1) } catch (e) {}
      }
      if (m.bizId && m.msgType === 'ORDER') uni.navigateTo({ url: '/pages/order/detail?id=' + m.bizId })
      else if (m.bizId && m.msgType === 'QUOTE') uni.navigateTo({ url: '/pages/quote/detail?id=' + m.bizId })
    },
    async readAll() {
      try { await messageApi.readAll(); this.items.forEach(m => (m.isRead = true)); this.unread = 0 } catch (e) {}
    },
    fmt(t) { return (t || '').replace('T', ' ').slice(5, 16) },
  },
}
</script>

<style>
.topbar { background: #fff; padding: 20rpx 32rpx; }
.container { padding: 24rpx; }
.msg-title { font-size: 30rpx; font-weight: 600; }
.card.unread { border-left: 8rpx solid #2e8b57; }
.card.alert { border-left: 8rpx solid #e6a23c; background: #fdf6ec; }
.alert-title { color: #e6a23c; }
</style>
