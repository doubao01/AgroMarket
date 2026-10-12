<template>
  <view class="assistant">
    <scroll-view class="msg-list" scroll-y :scroll-into-view="scrollId" scroll-with-animation>
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

export default {
  data() {
    return {
      messages: [],
      text: '',
      sending: false,
      sessionId: '',
    }
  },
  computed: {
    scrollId() { return 'm' + Math.max(0, this.messages.length - 1) },
  },
  methods: {
    async send() {
      const content = (this.text || '').trim()
      if (!content || this.sending) return
      this.messages.push({ role: 'user', content })
      this.text = ''
      this.sending = true
      try {
        const res = await aiApi.chat({ message: content, sessionId: this.sessionId })
        this.sessionId = res.sessionId
        this.messages.push({ role: 'assistant', content: res.reply })
      } catch (e) {
        this.messages.push({ role: 'assistant', content: (e && e.msg) || 'AI 服务暂不可用' })
      } finally {
        this.sending = false
      }
    },
  },
}
</script>

<style scoped>
.assistant { display: flex; flex-direction: column; height: 100vh; background: #f4f6f4; }
.msg-list { flex: 1; padding: 24rpx; box-sizing: border-box; }
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
