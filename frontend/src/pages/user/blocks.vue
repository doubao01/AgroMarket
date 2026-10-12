<template>
  <view class="page">
    <view class="container">
      <view v-if="!loading && !items.length" class="empty">暂无拉黑用户</view>
      <view v-for="u in items" :key="u.id" class="card row">
        <view class="info">
          <text class="name">{{ u.nickname || ('用户' + u.id) }}</text>
          <text v-if="u.subjectName" class="sub">{{ u.subjectName }}</text>
        </view>
        <text class="unblock" @tap="unblock(u)">取消拉黑</text>
      </view>
    </view>
  </view>
</template>

<script>
import { chatApi } from '../../api/index'
import { requireLogin } from '../../utils/request'

export default {
  data() { return { items: [], loading: false } },
  onLoad() { if (!requireLogin()) return },
  onShow() { if (requireLogin()) this.reload() },
  onPullDownRefresh() { this.reload().finally(() => uni.stopPullDownRefresh()) },
  methods: {
    async reload() {
      this.loading = true
      try { this.items = (await chatApi.blocks()) || [] } catch (e) { this.items = [] } finally { this.loading = false }
    },
    unblock(u) {
      uni.showModal({
        title: '取消拉黑',
        content: `解除对「${u.nickname || u.id}」的拉黑？解除后双方可正常沟通。`,
        success: async (r) => {
          if (!r.confirm) return
          try { await chatApi.removeBlock(u.id); uni.showToast({ title: '已解除', icon: 'none' }); this.reload() } catch (e) {}
        },
      })
    },
  },
}
</script>

<style>
.container { padding: 24rpx; }
.row { display: flex; align-items: center; justify-content: space-between; }
.info { display: flex; flex-direction: column; }
.name { font-size: 30rpx; font-weight: 600; }
.sub { font-size: 24rpx; color: #7a8a80; margin-top: 6rpx; }
.unblock { font-size: 26rpx; color: #dd524d; border: 1rpx solid #dd524d; border-radius: 999rpx; padding: 8rpx 28rpx; }
</style>
