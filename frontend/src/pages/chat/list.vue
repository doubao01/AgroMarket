<template>
  <view class="page">
    <view class="tabs">
      <view
        v-for="t in tabs"
        :key="t.key"
        class="tab"
        :class="{ active: filter === t.key }"
        @tap="switchTab(t.key)"
      >
        <text>{{ t.label }}</text>
        <text v-if="t.key === 'UNREAD' && unread > 0" class="tab-badge">{{ unread > 99 ? '99+' : unread }}</text>
      </view>
      <view class="tab-notice" @tap="openNotice">通知</view>
    </view>

    <view class="container">
      <view v-if="!loading && !items.length" class="empty">暂无会话</view>

      <view
        v-for="c in items"
        :key="c.id"
        class="card conv"
        :class="{ pinned: c.isPinned }"
        @tap="open(c)"
        @longpress="manage(c)"
      >
        <image v-if="c.peer.avatarUrl" class="avatar" :src="c.peer.avatarUrl" mode="aspectFill" />
        <view v-else class="avatar avatar-text">{{ firstChar(c.peer.nickname) }}</view>

        <view class="flex1 conv-body">
          <view class="between">
            <view class="row">
              <text class="name">{{ c.peer.nickname }}</text>
              <text v-if="c.peer.verified" class="tag">已认证</text>
            </view>
            <text class="muted">{{ fmtTime(c.lastMessageAt) }}</text>
          </view>
          <view class="between mt8">
            <text class="muted preview">{{ preview(c) }}</text>
            <view class="row">
              <text v-if="c.isPinned" class="muted pin-flag">置顶</text>
              <text v-if="c.unreadCount" class="badge">{{ c.unreadCount > 99 ? '99+' : c.unreadCount }}</text>
            </view>
          </view>
        </view>
      </view>
    </view>
  </view>
</template>

<script>
import { chatApi } from '../../api/index'
import { requireLogin } from '../../utils/request'

export default {
  data() {
    return {
      tabs: [
        { key: 'ALL', label: '全部' },
        { key: 'UNREAD', label: '未读' },
      ],
      filter: 'ALL',
      items: [],
      unread: 0,
      loading: false,
    }
  },
  onLoad() { if (!requireLogin()) return },
  onShow() { if (requireLogin()) this.reload() },
  onPullDownRefresh() { this.reload().finally(() => uni.stopPullDownRefresh()) },
  methods: {
    async reload() {
      this.loading = true
      try {
        const res = await chatApi.conversations({ filter: this.filter, page: 1, pageSize: 50 })
        this.items = res.items || []
        await this.loadUnread()
      } catch (e) {} finally { this.loading = false }
    },
    async loadUnread() {
      try { this.unread = await chatApi.unread() } catch (e) {}
    },
    switchTab(key) {
      if (this.filter === key) return
      this.filter = key
      this.reload()
    },
    open(c) {
      uni.navigateTo({ url: '/pages/chat/chat?id=' + c.id })
    },
    openNotice() {
      uni.navigateTo({ url: '/pages/message/index' })
    },
    manage(c) {
      uni.showActionSheet({
        itemList: [c.isPinned ? '取消置顶' : '置顶会话', '删除会话'],
        success: async (r) => {
          try {
            if (r.tapIndex === 0) {
              await chatApi.updateSettings(c.id, { isPinned: !c.isPinned })
            } else {
              await chatApi.removeConversation(c.id)
            }
            this.reload()
          } catch (e) {}
        },
      })
    },
    preview(c) {
      const map = { IMAGE: '[图片]', FILE: '[文件]', PRODUCT_CARD: '[商品]', INQUIRY: '[询价]', QUOTE: '[报价]', ORDER_CARD: '[订单]', SYSTEM: '[系统]' }
      if (c.lastMessageType && c.lastMessageType !== 'TEXT') return map[c.lastMessageType] || '[消息]'
      return c.lastContent || ''
    },
    firstChar(name) { return (name || '?').slice(0, 1) },
    fmtTime(t) {
      if (!t) return ''
      const d = String(t).replace('T', ' ')
      const today = new Date()
      const ymd = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
      if (d.slice(0, 10) === ymd) return d.slice(11, 16)
      return d.slice(5, 16)
    },
  },
}
</script>

<style>
.tabs { display: flex; background: #fff; padding: 0 32rpx; border-bottom: 1rpx solid #e3e9e4; }
.tab { position: relative; padding: 24rpx 8rpx; margin-right: 48rpx; font-size: 30rpx; color: #7a8a80; }
.tab.active { color: #2e8b57; font-weight: 600; }
.tab.active::after {
  content: ''; position: absolute; left: 0; right: 0; bottom: 0;
  height: 6rpx; background: #2e8b57; border-radius: 6rpx;
}
.tab-badge {
  margin-left: 8rpx; padding: 0 10rpx; font-size: 20rpx; line-height: 28rpx;
  color: #fff; background: #dd524d; border-radius: 999rpx;
}
.tab-notice { margin-left: auto; padding: 24rpx 0; color: #7a8a80; font-size: 28rpx; }
.conv { display: flex; align-items: center; }
.conv.pinned { border-left: 8rpx solid #2e8b57; }
.conv-body { margin-left: 20rpx; min-width: 0; }
.avatar { width: 88rpx; height: 88rpx; border-radius: 20rpx; background: #e8f5ee; }
.avatar-text {
  display: flex; align-items: center; justify-content: center;
  color: #2e8b57; font-size: 36rpx; font-weight: 600;
}
.name { font-size: 30rpx; font-weight: 600; }
.preview { flex: 1; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
.pin-flag { margin-right: 12rpx; }
.badge {
  min-width: 36rpx; padding: 0 10rpx; text-align: center; font-size: 22rpx;
  line-height: 36rpx; color: #fff; background: #dd524d; border-radius: 999rpx;
}
</style>
