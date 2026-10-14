<template>
  <view class="page">
    <view class="tabs">
      <view class="tab" :class="{ active: tab === 'hot' }" @tap="switchTab('hot')">热点</view>
      <view class="tab" :class="{ active: tab === 'new' }" @tap="switchTab('new')">最新</view>
      <view class="publish-btn" @tap="goPublish">＋ 发帖</view>
    </view>

    <view class="topic-bar">
      <view
        v-for="t in topics"
        :key="t.key"
        class="topic-chip"
        :class="{ active: topic === t.key }"
        @tap="setTopic(t.key)"
      >{{ t.name }}</view>
    </view>

    <view class="container">
      <view v-if="!loading && !items.length" class="empty-wrap">
        <view class="empty">还没有帖子，来聊聊行情吧</view>
        <button class="empty-btn" @tap="goPublish">＋ 发第一帖</button>
      </view>
      <view v-for="p in items" :key="p.id" class="card" @tap="goDetail(p.id)">
        <view class="between">
          <text class="card-title">{{ p.title }}</text>
          <text class="tag">{{ topicName(p.topic) }}</text>
        </view>
        <view class="content-preview mt8">{{ p.content }}</view>
        <view class="between mt8">
          <text class="muted">{{ p.nickname }} · {{ fmt(p.createdAt) }}</text>
          <view class="meta">
            <text class="muted">赞 {{ p.likeCount }}</text>
            <text class="muted" style="margin-left:24rpx;">评 {{ p.commentCount }}</text>
          </view>
        </view>
      </view>
      <view v-if="loading" class="muted" style="text-align:center;padding:30rpx;">加载中...</view>
      <view v-else-if="items.length && !finished" class="muted" style="text-align:center;padding:30rpx;" @tap="loadMore">加载更多</view>
    </view>
  </view>
</template>

<script>
import { communityApi } from '../../api/index'
import { requireLogin } from '../../utils/request'

const TOPICS = [
  { key: null, name: '全部' },
  { key: 'MARKET', name: '行情讨论' },
  { key: 'CHAT', name: '闲聊' },
  { key: 'HELP', name: '求助互助' },
  { key: 'TRADE', name: '交易心得' },
]

export default {
  data() {
    return { tab: 'hot', topic: null, topics: TOPICS, items: [], page: 1, pageSize: 10, loading: false, finished: false }
  },
  onShow() { this.reload() },
  onPullDownRefresh() { this.reload().finally(() => uni.stopPullDownRefresh()) },
  methods: {
    switchTab(t) {
      if (this.tab === t) return
      this.tab = t
      this.reload()
    },
    setTopic(k) {
      if (this.topic === k) return
      this.topic = k
      this.reload()
    },
    goPublish() {
      if (requireLogin()) uni.navigateTo({ url: '/pages/community/publish' })
    },
    goDetail(id) { uni.navigateTo({ url: '/pages/community/detail?id=' + id }) },
    async reload() {
      this.page = 1
      this.finished = false
      this.items = []
      return this.loadMore()
    },
    async loadMore() {
      if (this.loading || this.finished) return
      this.loading = true
      try {
        const q = { page: this.page, pageSize: this.pageSize, orderBy: this.tab === 'hot' ? 'hot' : 'new' }
        if (this.topic) q.topic = this.topic
        const res = await communityApi.posts(q)
        const list = res.items || []
        this.items = this.page === 1 ? list : this.items.concat(list)
        if (this.items.length >= res.total || list.length < this.pageSize) this.finished = true
        else this.page += 1
      } catch (e) {} finally { this.loading = false }
    },
    topicName(t) { return (TOPICS.find((x) => x.key === t) || {}).name || t },
    fmt(t) { return (t || '').replace('T', ' ').slice(5, 16) },
  },
}
</script>

<style>
.page { padding-bottom: 20rpx; }
.tabs { display: flex; background: #fff; position: relative; }
.tab { flex: 1; text-align: center; padding: 26rpx 0; font-size: 30rpx; color: #7a8a80; position: relative; }
.tab.active { color: #2e8b57; font-weight: 600; }
.tab.active::after {
  content: ''; position: absolute; left: 50%; bottom: 0; transform: translateX(-50%);
  width: 60rpx; height: 6rpx; background: #2e8b57; border-radius: 999rpx;
}
.publish-btn {
  position: absolute; right: 24rpx; top: 50%; transform: translateY(-50%);
  font-size: 26rpx; color: #fff; background: #2e8b57; border-radius: 999rpx; padding: 10rpx 28rpx;
}
.topic-bar { display: flex; flex-wrap: wrap; gap: 16rpx; background: #fff; padding: 0 24rpx 20rpx; }
.topic-chip { padding: 10rpx 28rpx; font-size: 26rpx; background: #f4f6f4; border-radius: 999rpx; color: #1f2d24; }
.topic-chip.active { background: #2e8b57; color: #fff; }
.container { padding: 20rpx 24rpx 24rpx; }
.card { background: #fff; border-radius: 12rpx; padding: 24rpx; margin-bottom: 20rpx; }
.card-title { font-size: 30rpx; font-weight: 600; flex: 1; margin-right: 16rpx; }
.content-preview { font-size: 27rpx; color: #444; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
.meta { flex-shrink: 0; }
.empty-wrap { padding: 60rpx 0; text-align: center; }
.empty-btn { margin: 30rpx auto 0; width: 320rpx; font-size: 28rpx; color: #fff; background: #2e8b57; border-radius: 999rpx; }
</style>
