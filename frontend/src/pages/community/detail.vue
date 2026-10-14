<template>
  <view class="page">
    <view class="container">
      <view v-if="!loading && !post" class="empty">帖子不存在或已被隐藏</view>
      <template v-if="post">
        <view class="card">
          <view class="between">
            <text class="tag">{{ topicName(post.topic) }}</text>
            <text v-if="isMine" class="mini danger" @tap="removePost">删除</text>
          </view>
          <view class="post-title mt16">{{ post.title }}</view>
          <view class="post-content mt8">{{ post.content }}</view>
          <view v-if="imageList.length" class="imgs mt16">
            <image v-for="(img, i) in imageList" :key="i" :src="img" class="post-img" mode="aspectFill" @tap="preview(i)" />
          </view>
          <view class="between mt16">
            <text class="muted">{{ post.nickname }} · {{ fmt(post.createdAt) }}</text>
            <text class="like" :class="{ active: liked }" @tap="toggleLike">{{ liked ? '已赞' : '点赞' }} {{ post.likeCount }}</text>
          </view>
        </view>

        <view class="section-title mt24">评论 {{ comments.length }}</view>
        <view v-if="!comments.length" class="empty">还没有评论，来抢沙发</view>
        <view v-for="c in comments" :key="c.id" class="card comment">
          <view class="between">
            <text class="cmt-name">{{ c.nickname }}</text>
            <text class="muted">{{ fmt(c.createdAt) }}</text>
          </view>
          <view class="mt8 cmt-content" @tap="replyTo(c)">
            <text v-if="c.parentId" class="muted">回复 @{{ c.replyNickname || '楼主' }}：</text>{{ c.content }}
          </view>
        </view>

        <view class="reply-bar">
          <input v-model="draft" class="reply-input" :placeholder="replyTarget ? ('回复 @' + replyTarget.nickname) : '友善交流，文明发言'" confirm-type="send" @confirm="submit" />
          <text class="reply-send" @tap="submit">发送</text>
        </view>
        <view v-if="replyTarget" class="reply-target muted" @tap="clearReply">正在回复 @{{ replyTarget.nickname }}，点击取消</view>
      </template>
    </view>
  </view>
</template>

<script>
import { communityApi } from '../../api/index'
import { requireLogin, getUser } from '../../utils/request'

const TOPIC_NAMES = { MARKET: '行情讨论', CHAT: '闲聊', HELP: '求助互助', TRADE: '交易心得' }

export default {
  data() {
    return { id: 0, post: null, liked: false, comments: [], draft: '', replyTarget: null, loading: true }
  },
  computed: {
    isMine() { const me = getUser(); return me && this.post && Number(me.id) === this.post.userId },
    imageList() {
      if (!this.post || !this.post.images) return []
      try { return JSON.parse(this.post.images) || [] } catch (e) { return [] }
    },
  },
  onLoad(query) { this.id = Number(query.id) || 0 },
  onShow() { if (this.id) this.reload() },
  onPullDownRefresh() { this.reload().finally(() => uni.stopPullDownRefresh()) },
  methods: {
    async reload() {
      this.loading = true
      try {
        const res = await communityApi.post(this.id)
        this.post = res.post
        this.liked = !!res.liked
        this.comments = res.comments || []
      } catch (e) { this.post = null } finally { this.loading = false }
    },
    async toggleLike() {
      if (!requireLogin()) return
      try {
        const res = await communityApi.toggleLike(this.id)
        this.liked = !!res.liked
        this.post.likeCount = Math.max(0, this.post.likeCount + (this.liked ? 1 : -1))
      } catch (e) {}
    },
    replyTo(c) {
      if (!requireLogin()) return
      this.replyTarget = c
    },
    clearReply() { this.replyTarget = null },
    async submit() {
      if (!requireLogin()) return
      const content = (this.draft || '').trim()
      if (!content) return
      try {
        await communityApi.addComment(this.id, { content, parentId: this.replyTarget ? this.replyTarget.id : null })
        this.draft = ''
        this.replyTarget = null
        uni.showToast({ title: '评论成功', icon: 'success' })
        this.reload()
      } catch (e) {}
    },
    async removePost() {
      const ok = await new Promise((r) => uni.showModal({ title: '删除帖子', content: '删除后无法恢复，确定？', success: (x) => r(x.confirm) }))
      if (!ok) return
      try {
        await communityApi.deletePost(this.id)
        uni.showToast({ title: '已删除' })
        setTimeout(() => uni.navigateBack(), 600)
      } catch (e) {}
    },
    preview(i) { uni.previewImage({ urls: this.imageList, current: i }) },
    topicName(t) { return TOPIC_NAMES[t] || t },
    fmt(t) { return (t || '').replace('T', ' ').slice(0, 16) },
  },
}
</script>

<style>
.page { padding-bottom: 140rpx; }
.container { padding: 20rpx 24rpx; }
.card { background: #fff; border-radius: 12rpx; padding: 24rpx; margin-bottom: 20rpx; }
.post-title { font-size: 34rpx; font-weight: 700; }
.post-content { font-size: 29rpx; line-height: 1.7; color: #333; white-space: pre-wrap; }
.imgs { display: flex; flex-wrap: wrap; gap: 12rpx; }
.post-img { width: 200rpx; height: 200rpx; border-radius: 8rpx; background: #f4f6f4; }
.like { font-size: 28rpx; color: #7a8a80; }
.like.active { color: #dd524d; }
.section-title { font-size: 30rpx; font-weight: 600; margin-bottom: 16rpx; }
.comment .cmt-name { font-size: 26rpx; font-weight: 600; color: #2e8b57; }
.cmt-content { font-size: 28rpx; color: #333; }
.reply-bar {
  position: fixed; left: 0; right: 0; bottom: 0; display: flex; align-items: center;
  background: #fff; padding: 16rpx 24rpx calc(16rpx + env(safe-area-inset-bottom));
  box-shadow: 0 -4rpx 16rpx rgba(0,0,0,0.06);
}
.reply-input { flex: 1; background: #f4f6f4; border-radius: 999rpx; padding: 14rpx 28rpx; font-size: 28rpx; margin-right: 20rpx; }
.reply-send { font-size: 28rpx; color: #fff; background: #2e8b57; border-radius: 999rpx; padding: 12rpx 32rpx; }
.reply-target { padding: 0 24rpx 16rpx; }
.mini { font-size: 26rpx; color: #2e8b57; }
.mini.danger { color: #dd524d; }
</style>
