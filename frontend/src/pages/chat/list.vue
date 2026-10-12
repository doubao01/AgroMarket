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

    <view class="searchbar">
      <input
        v-model="keyword"
        class="search-input"
        type="text"
        placeholder="搜索聊天记录"
        confirm-type="search"
        @confirm="doSearch"
      />
      <text v-if="keyword" class="search-clear" @tap="clearSearch">×</text>
      <view class="search-btn" @tap="doSearch">搜索</view>
    </view>

    <view v-if="searching" class="container">
      <view v-if="!searchLoading && !results.length" class="empty">未找到相关聊天记录</view>
      <view
        v-for="r in results"
        :key="r.message.id"
        class="card conv"
        @tap="openResult(r)"
      >
        <image v-if="r.conversation.peer.avatarUrl" class="avatar" :src="r.conversation.peer.avatarUrl" mode="aspectFill" />
        <view v-else class="avatar avatar-text">{{ firstChar(displayName(r.conversation)) }}</view>
        <view class="flex1 conv-body">
          <view class="between">
            <text class="name">{{ displayName(r.conversation) }}</text>
            <text class="muted">{{ fmtTime(r.message.createdAt) }}</text>
          </view>
          <view class="mt8">
            <text class="muted preview">{{ r.message.content || '[消息]' }}</text>
          </view>
        </view>
      </view>
    </view>

    <view v-else class="container">
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
        <view v-else class="avatar avatar-text">{{ firstChar(displayName(c)) }}</view>

        <view class="flex1 conv-body">
          <view class="between">
            <view class="row">
              <text class="name">{{ displayName(c) }}</text>
              <text v-if="c.peer.verified" class="tag">已认证</text>
            </view>
            <text class="muted">{{ fmtTime(c.lastMessageAt) }}</text>
          </view>
          <view class="between mt8">
            <text class="muted preview">{{ preview(c) }}</text>
            <view class="row">
              <text v-if="c.isMuted" class="muted pin-flag">免打扰</text>
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
import { setConversations, onChatEvent, patchConversation } from '../../utils/chat-store'

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
      keyword: '',
      searching: false,
      searchLoading: false,
      results: [],
    }
  },
  onLoad() {
    if (!requireLogin()) return
    this._unsubs = [
      onChatEvent('message', () => this.scheduleReload()),
      onChatEvent('recall', () => this.scheduleReload()),
    ]
  },
  onShow() { if (requireLogin()) this.reload() },
  onUnload() {
    (this._unsubs || []).forEach((fn) => { try { fn() } catch (e) {} })
    this._unsubs = null
    if (this._reloadTimer) { clearTimeout(this._reloadTimer); this._reloadTimer = null }
  },
  onPullDownRefresh() { this.reload().finally(() => uni.stopPullDownRefresh()) },
  methods: {
    scheduleReload() {
      if (this._reloadTimer) return
      this._reloadTimer = setTimeout(() => { this._reloadTimer = null; this.reload() }, 400)
    },
    async reload() {
      this.loading = true
      try {
        const res = await chatApi.conversations({ filter: this.filter, page: 1, pageSize: 50 })
        this.items = res.items || []
        setConversations(this.items)
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
    openResult(r) {
      uni.navigateTo({ url: '/pages/chat/chat?id=' + r.conversation.id + '&messageId=' + r.message.id })
    },
    async doSearch() {
      const kw = (this.keyword || '').trim()
      if (!kw) { this.clearSearch(); return }
      this.searching = true
      this.searchLoading = true
      try {
        this.results = (await chatApi.search(kw)) || []
      } catch (e) { this.results = [] } finally { this.searchLoading = false }
    },
    clearSearch() {
      this.keyword = ''
      this.searching = false
      this.results = []
    },
    openNotice() {
      uni.navigateTo({ url: '/pages/message/index' })
    },
    manage(c) {
      const list = [
        c.isPinned ? '取消置顶' : '置顶会话',
        c.isMuted ? '取消消息免打扰' : '消息免打扰',
        '设置备注名',
        '删除会话',
      ]
      uni.showActionSheet({
        itemList: list,
        success: async (r) => {
          try {
            if (r.tapIndex === 0) {
              await chatApi.updateSettings(c.id, { isPinned: !c.isPinned })
              c.isPinned = !c.isPinned
              patchConversation(c.id, { isPinned: c.isPinned })
            } else if (r.tapIndex === 1) {
              await chatApi.updateSettings(c.id, { isMuted: !c.isMuted })
              c.isMuted = !c.isMuted
              patchConversation(c.id, { isMuted: c.isMuted })
            } else if (r.tapIndex === 2) {
              this.rename(c)
              return
            } else {
              await chatApi.removeConversation(c.id)
            }
            this.reload()
          } catch (e) {}
        },
      })
    },
    rename(c) {
      uni.showModal({
        title: '设置备注名',
        editable: true,
        placeholderText: '留空则清除备注',
        content: c.remark || '',
        success: async (r) => {
          if (!r.confirm) return
          const remark = (r.content || '').trim()
          try {
            await chatApi.updateSettings(c.id, { remark })
            c.remark = remark
            patchConversation(c.id, { remark, peerName: remark || (c.peer && c.peer.nickname) })
            this.reload()
          } catch (e) {}
        },
      })
    },
    displayName(c) {
      return c.remark || (c.peer && c.peer.nickname) || '用户'
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
.searchbar {
  display: flex; align-items: center; padding: 16rpx 32rpx;
  background: #fff; border-bottom: 1rpx solid #e3e9e4;
}
.search-input {
  flex: 1; height: 68rpx; padding: 0 24rpx; font-size: 28rpx;
  background: #f2f5f3; border-radius: 34rpx;
}
.search-clear {
  margin-left: 16rpx; width: 40rpx; text-align: center;
  color: #9aa8a0; font-size: 40rpx; line-height: 1;
}
.search-btn {
  margin-left: 16rpx; padding: 0 28rpx; height: 68rpx; line-height: 68rpx;
  font-size: 28rpx; color: #fff; background: #2e8b57; border-radius: 34rpx;
}
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
