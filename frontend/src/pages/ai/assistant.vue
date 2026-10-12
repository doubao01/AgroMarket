<template>
  <view class="assistant">
    <view class="ai-topbar">
      <text class="topbar-btn" @tap="toggleDrawer">历史</text>
      <text class="topbar-title">{{ currentTitle || 'AI 助手' }}</text>
      <text class="topbar-btn" @tap="newChat">新对话</text>
    </view>

    <view v-if="drawerOpen" class="drawer-mask" @tap="drawerOpen = false"></view>
    <view class="drawer" :class="{ open: drawerOpen }">
      <view class="drawer-head">
        <text class="drawer-title">对话历史</text>
        <text class="drawer-new" @tap="newChat">＋ 新对话</text>
      </view>
      <scroll-view class="drawer-list" scroll-y>
        <view v-if="!conversations.length" class="drawer-empty">暂无历史对话</view>
        <view
          v-for="c in conversations"
          :key="c.id"
          class="drawer-item"
          :class="{ active: c.id === convId }"
          @tap="openConversation(c)"
          @longpress="manageConversation(c)"
        >
          <text class="drawer-item-title">{{ c.title || '新对话' }}</text>
          <text class="drawer-item-time">{{ fmtTime(c.lastMessageAt || c.createdAt) }}</text>
        </view>
      </scroll-view>
    </view>

    <scroll-view class="msg-list" scroll-y :scroll-into-view="scrollId" scroll-with-animation>
      <view v-if="hasMore" class="load-more" @tap="loadOlder">加载更早的对话</view>
      <view class="tip">AI 助手可解答平台使用、供需对接、农事常识等问题，数据类问题会实时查询平台。</view>
      <view v-for="(m, i) in messages" :key="i" :id="'m' + i" class="msg" :class="m.role">
        <view class="bubble">{{ m.content }}</view>
      </view>
      <view v-if="sending" class="msg assistant"><view class="bubble typing">正在思考...</view></view>
      <view id="bottom-anchor" style="height: 20rpx"></view>
    </scroll-view>

    <view class="input-bar">
      <input class="input" v-model="text" placeholder="请输入问题" confirm-type="send" @confirm="send" />
      <button class="send-btn" :disabled="sending" @tap="send">发送</button>
    </view>
  </view>
</template>

<script>
import { aiApi } from '../../api/index.js'
import { requireLogin } from '../../utils/request'

export default {
  data() {
    return {
      messages: [],
      text: '',
      sending: false,
      sessionId: '',
      convId: 0,
      currentTitle: '',
      conversations: [],
      drawerOpen: false,
      hasMore: false,
      nextCursor: null,
      loading: false,
    }
  },
  computed: {
    scrollId() { return 'm' + Math.max(0, this.messages.length - 1) },
  },
  onLoad() { if (requireLogin()) this.reloadConversations(false) },
  methods: {
    async reloadConversations(openFirst) {
      try {
        const res = await aiApi.conversations({ page: 1, pageSize: 50 })
        this.conversations = res.items || []
        if (openFirst && this.conversations.length) this.openConversation(this.conversations[0])
      } catch (e) {}
    },
    toggleDrawer() {
      this.drawerOpen = !this.drawerOpen
      if (this.drawerOpen) this.reloadConversations(false)
    },
    newChat() {
      this.drawerOpen = false
      this.convId = 0
      this.sessionId = ''
      this.currentTitle = ''
      this.messages = []
      this.hasMore = false
      this.nextCursor = null
    },
    async openConversation(c) {
      this.drawerOpen = false
      this.convId = c.id
      this.sessionId = c.sessionId
      this.currentTitle = c.title || ''
      this.messages = []
      this.hasMore = false
      this.nextCursor = null
      try {
        const res = await aiApi.messages(c.id, { limit: 20 })
        this.hasMore = res.hasMore
        this.nextCursor = res.nextCursor
        this.messages = (res.items || []).map((m) => ({ role: m.role === 'ASSISTANT' ? 'assistant' : 'user', content: m.content }))
      } catch (e) {}
    },
    async loadOlder() {
      if (!this.hasMore || !this.nextCursor || this.loading) return
      this.loading = true
      try {
        const res = await aiApi.messages(this.convId, { beforeId: this.nextCursor, limit: 20 })
        this.hasMore = res.hasMore
        this.nextCursor = res.nextCursor
        const older = (res.items || []).map((m) => ({ role: m.role === 'ASSISTANT' ? 'assistant' : 'user', content: m.content }))
        this.messages = older.concat(this.messages)
      } catch (e) {} finally { this.loading = false }
    },
    manageConversation(c) {
      uni.showActionSheet({
        itemList: ['重命名', '删除对话'],
        success: async (r) => {
          try {
            if (r.tapIndex === 0) {
              this.promptRename(c)
            } else {
              await aiApi.removeConversation(c.id)
              if (c.id === this.convId) this.newChat()
              this.reloadConversations(false)
            }
          } catch (e) {}
        },
      })
    },
    promptRename(c) {
      uni.showModal({
        title: '重命名对话',
        editable: true,
        placeholderText: c.title || '新对话',
        success: async (r) => {
          if (!r.confirm) return
          const title = (r.content || '').trim()
          if (!title) return
          try {
            await aiApi.renameConversation(c.id, title)
            if (c.id === this.convId) this.currentTitle = title
            this.reloadConversations(false)
          } catch (e) {}
        },
      })
    },
    async send() {
      const content = (this.text || '').trim()
      if (!content || this.sending) return
      this.messages.push({ role: 'user', content })
      this.text = ''
      this.sending = true
      try {
        const res = await aiApi.chat({ message: content, sessionId: this.sessionId || undefined })
        this.sessionId = res.sessionId
        this.messages.push({ role: 'assistant', content: res.reply })
        if (!this.currentTitle) this.currentTitle = content.length > 30 ? content.slice(0, 30) : content
        if (!this.convId) {
          const list = await aiApi.conversations({ page: 1, pageSize: 1 })
          const first = (list.items || [])[0]
          if (first) { this.convId = first.id; this.currentTitle = first.title || this.currentTitle }
        }
      } catch (e) {
        this.messages.push({ role: 'assistant', content: (e && e.msg) || 'AI 服务暂不可用' })
      } finally {
        this.sending = false
      }
    },
    fmtTime(t) {
      if (!t) return ''
      const d = String(t).replace('T', ' ')
      return d.slice(5, 16)
    },
  },
}
</script>

<style scoped>
.assistant { display: flex; flex-direction: column; height: 100vh; background: #f4f6f4; }
.ai-topbar {
  display: flex; align-items: center; justify-content: space-between;
  padding: 16rpx 28rpx; background: #2e8b57; color: #fff;
}
.topbar-title { font-size: 30rpx; font-weight: 600; flex: 1; text-align: center; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.topbar-btn { font-size: 28rpx; color: #fff; min-width: 90rpx; }
.drawer-mask { position: fixed; left: 0; top: 0; right: 0; bottom: 0; background: rgba(0,0,0,0.35); z-index: 10; }
.drawer {
  position: fixed; left: 0; top: 0; bottom: 0; width: 560rpx; background: #fff; z-index: 11;
  transform: translateX(-100%); transition: transform 0.2s ease; display: flex; flex-direction: column;
}
.drawer.open { transform: translateX(0); }
.drawer-head { display: flex; align-items: center; justify-content: space-between; padding: 28rpx 28rpx 20rpx; }
.drawer-title { font-size: 30rpx; font-weight: 600; color: #22322a; }
.drawer-new { font-size: 26rpx; color: #2e8b57; }
.drawer-list { flex: 1; }
.drawer-empty { padding: 60rpx 0; text-align: center; color: #a7b3ac; font-size: 26rpx; }
.drawer-item { padding: 22rpx 28rpx; border-bottom: 1rpx solid #eef2ef; }
.drawer-item.active { background: #e8f5ee; }
.drawer-item-title { display: block; font-size: 28rpx; color: #22322a; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.drawer-item-time { display: block; margin-top: 6rpx; font-size: 22rpx; color: #a7b3ac; }
.msg-list { flex: 1; padding: 24rpx; box-sizing: border-box; }
.load-more { text-align: center; color: #2e8b57; font-size: 24rpx; padding: 12rpx 0; }
.tip { font-size: 22rpx; color: #a7b3ac; text-align: center; padding: 20rpx 40rpx; line-height: 1.5; }
.msg { display: flex; margin-bottom: 20rpx; }
.msg.user { justify-content: flex-end; }
.bubble { max-width: 76%; padding: 20rpx 24rpx; border-radius: 16rpx; font-size: 28rpx; line-height: 1.5; word-break: break-word; }
.msg.user .bubble { background: #2e8b57; color: #fff; border-top-right-radius: 4rpx; }
.msg.assistant .bubble { background: #fff; color: #22322a; border-top-left-radius: 4rpx; }
.bubble.typing { color: #a7b3ac; }
.input-bar { display: flex; align-items: center; padding: 16rpx 24rpx; background: #fff; padding-bottom: calc(16rpx + env(safe-area-inset-bottom)); }
.input { flex: 1; background: #f0f3f1; border-radius: 12rpx; padding: 18rpx 22rpx; font-size: 28rpx; }
.send-btn { margin-left: 16rpx; background: #2e8b57; color: #fff; font-size: 28rpx; }
</style>