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
      try {
        // /api/favorites 返回分页对象 {items,total,page,pageSize}, 直接赋给数组会渲染出
        // 4 个 undefined 卡片; 这里统一解包, 并兼容后端将来改回数组的情况
        const res = await favoriteApi.list({ page: 1, pageSize: 50 })
        this.items = Array.isArray(res) ? res : (res && res.items) || []
      } catch (e) {} finally { this.loading = false }
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
