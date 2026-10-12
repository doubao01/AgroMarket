<template>
  <view class="page container">
    <view v-if="!loading && !items.length" class="empty">暂无收藏</view>
    <view v-for="f in items" :key="f.id" class="card">
      <view class="between">
        <text class="card-title">{{ f.title || ('#' + f.targetId) }}</text>
        <text class="price">{{ f.price ? f.price : '' }}</text>
      </view>
      <view class="between mt8">
        <text class="tag">{{ f.targetType === 'SUPPLY' ? '供应' : '采购' }}</text>
        <text class="muted" @tap="remove(f)">取消收藏</text>
      </view>
    </view>
  </view>
</template>

<script>
import { favoriteApi } from '../../api/index'
import { requireLogin } from '../../utils/request'

export default {
  data() { return { items: [], loading: false } },
  onLoad() { if (!requireLogin()) return; this.reload() },
  onPullDownRefresh() { this.reload().finally(() => uni.stopPullDownRefresh()) },
  methods: {
    async reload() {
      this.loading = true
      try { this.items = await favoriteApi.list() } catch (e) {} finally { this.loading = false }
    },
    async remove(f) {
      try {
        await favoriteApi.remove(f.targetType, f.targetId)
        this.items = this.items.filter(x => x.id !== f.id)
        uni.showToast({ title: '已取消' })
      } catch (e) {}
    },
  },
}
</script>

<style>
.card-title { font-size: 30rpx; font-weight: 600; flex: 1; margin-right: 16rpx; }
.container { padding: 24rpx; }
</style>
