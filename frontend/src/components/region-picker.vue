<template>
  <view v-if="visible" class="rp-mask" @tap="close">
    <view class="rp-panel" @tap.stop>
      <view class="rp-head between">
        <text class="rp-title">选择地区（息县）</text>
        <text class="rp-close" @tap="close">完成</text>
      </view>
      <view v-if="loading" class="rp-loading">加载中...</view>
      <view v-else class="rp-cols">
        <scroll-view scroll-y class="rp-col">
          <view
            v-for="t in towns"
            :key="t.id"
            class="rp-item"
            :class="{ active: t.id === townId }"
            @tap="pickTown(t)"
          >{{ t.name }}</view>
        </scroll-view>
        <scroll-view scroll-y class="rp-col">
          <view class="rp-item" :class="{ active: !villageId }" @tap="pickVillage(null)">不限村</view>
          <view
            v-for="v in villages"
            :key="v.id"
            class="rp-item"
            :class="{ active: v.id === villageId }"
            @tap="pickVillage(v)"
          >{{ v.name }}</view>
          <view v-if="!villages.length" class="rp-empty">该乡镇村数据待补充</view>
        </scroll-view>
      </view>
    </view>
  </view>
</template>

<script>
import { regionApi } from '../api/index'

export default {
  name: 'RegionPicker',
  props: {
    visible: { type: Boolean, default: false },
    countyId: { type: [Number, String], default: 3 },
    townId: { type: [Number, String], default: null },
    villageId: { type: [Number, String], default: null },
  },
  data() {
    return {
      towns: [],
      villages: [],
      loading: false,
    }
  },
  watch: {
    visible(val) {
      if (val) this.init()
    },
  },
  methods: {
    async init() {
      if (!this.towns.length) {
        this.loading = true
        try {
          this.towns = await regionApi.children(3)
        } finally {
          this.loading = false
        }
      }
      if (this.townId) await this.loadVillages(this.townId)
    },
    async loadVillages(townId) {
      try {
        this.villages = await regionApi.children(townId)
      } catch (e) {
        this.villages = []
      }
    },
    async pickTown(t) {
      this.$emit('update:townId', t.id)
      this.$emit('update:villageId', null)
      this.$emit('town-change', { countyId: this.countyId, townId: t.id, townName: t.name })
      await this.loadVillages(t.id)
    },
    pickVillage(v) {
      this.$emit('update:villageId', v ? v.id : null)
      this.$emit('village-change', v ? { villageId: v.id, villageName: v.name } : { villageId: null, villageName: '' })
      if (v) this.close()
    },
    close() {
      this.$emit('update:visible', false)
      this.$emit('close')
    },
  },
}
</script>

<style>
.rp-mask {
  position: fixed;
  left: 0; right: 0; top: 0; bottom: 0;
  background: rgba(0, 0, 0, 0.4);
  z-index: 999;
  display: flex;
  align-items: flex-end;
}
.rp-panel {
  width: 100%;
  background: #fff;
  border-radius: 24rpx 24rpx 0 0;
  padding-bottom: env(safe-area-inset-bottom);
}
.rp-head { padding: 28rpx 32rpx; border-bottom: 1rpx solid #e3e9e4; }
.rp-title { font-size: 30rpx; font-weight: 600; }
.rp-close { color: #2e8b57; font-size: 30rpx; }
.rp-cols { display: flex; height: 640rpx; }
.rp-col { flex: 1; height: 640rpx; }
.rp-col:first-child { border-right: 1rpx solid #e3e9e4; }
.rp-item { padding: 26rpx 32rpx; font-size: 28rpx; }
.rp-item.active { color: #2e8b57; font-weight: 600; background: #e8f5ee; }
.rp-loading, .rp-empty { padding: 40rpx; text-align: center; color: #7a8a80; font-size: 26rpx; }
</style>
