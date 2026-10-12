<template>
  <view class="page container">
    <view v-if="!list.length" class="empty">暂无公告</view>
    <view v-for="n in list" :key="n.id" class="card">
      <view class="bold">{{ n.title }}</view>
      <view class="muted mt8">{{ fmt(n.createdAt) }}</view>
      <view class="mt16" style="line-height:1.6;">{{ n.content }}</view>
    </view>
  </view>
</template>

<script>
import { noticeApi } from '../../api/index'

export default {
  data() { return { list: [] } },
  onLoad() { this.load() },
  methods: {
    async load() { try { this.list = (await noticeApi.list()) || [] } catch (e) {} },
    fmt(t) { return (t || '').replace('T', ' ').slice(0, 16) },
  },
}
</script>

<style>
.container { padding: 24rpx; }
</style>
