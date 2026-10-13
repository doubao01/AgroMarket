<template>
  <view class="page">
    <view class="tabs">
      <view class="tab" :class="{ active: tab === 'supply' }" @tap="switchTab('supply')">供应大厅</view>
      <view class="tab" :class="{ active: tab === 'demand' }" @tap="switchTab('demand')">采购大厅</view>
    </view>

    <view class="filter-bar">
      <view class="search">
        <input v-model="keyword" class="search-input" placeholder="搜索标题关键词" confirm-type="search" @confirm="reload" />
      </view>
      <view class="filter-chip" :class="{ active: !!townId }" @tap="showRegion = true">
        {{ townLabel }}
      </view>
    </view>

    <view class="sort-bar">
      <text class="sort-item" :class="{ active: orderBy === '' }" @tap="setSort('')">最新</text>
      <text class="sort-item" :class="{ active: orderBy === 'price_asc' }" @tap="setSort('price_asc')">价格↑</text>
      <text class="sort-item" :class="{ active: orderBy === 'price_desc' }" @tap="setSort('price_desc')">价格↓</text>
    </view>

    <view class="container">
      <view v-if="!loading && !items.length" class="empty">暂无数据</view>
      <view
        v-for="it in items"
        :key="it.id"
        class="card"
        @tap="goDetail(it.id)"
      >
        <view style="display:flex;">
          <image v-if="it.images && it.images.length" :src="it.images[0].url" class="list-thumb" mode="aspectFill" />
          <view class="flex1">
            <view class="between">
              <text class="card-title">{{ it.title }}</text>
              <text class="price">{{ priceText(it) }}</text>
            </view>
            <view class="mt8 muted">
              {{ it.quantity }}{{ it.unit }} · {{ it.townName || '息县' }}{{ it.villageName ? ' ' + it.villageName : '' }}
            </view>
            <view class="mt8">
              <text class="tag">{{ it.categoryName || '农产品' }}</text>
              <text v-if="it.negotiable" class="tag">可议价</text>
              <text class="tag">{{ it.publisherName }}</text>
            </view>
          </view>
        </view>
      </view>
      <view v-if="loading" class="muted" style="text-align:center;padding:30rpx;">加载中...</view>
      <view v-else-if="items.length && !finished" class="muted" style="text-align:center;padding:30rpx;" @tap="loadMore">加载更多</view>
    </view>

    <region-picker
      v-model:visible="showRegion"
      allow-all
      v-model:townId="townId"
      v-model:villageId="villageId"
      @town-change="onTownChange"
      @village-change="onVillageChange"
    />
  </view>
</template>

<script>
import { supplyApi, demandApi } from '../../api/index'
import RegionPicker from '../../components/region-picker.vue'
import { getLocation, setLocation, getTownLabel, sameLocation } from '../../utils/location'

export default {
  components: { RegionPicker },
  data() {
    return {
      tab: 'supply',
      keyword: '',
      orderBy: '',
      townId: null,
      villageId: null,
      villageName: '',
      townLabel: '全部乡镇',
      page: 1,
      pageSize: 10,
      items: [],
      loading: false,
      finished: false,
      showRegion: false,
    }
  },
  onLoad(query) {
    if (query.tab === 'demand') this.tab = 'demand'
    const loc = getLocation()
    this.townId = loc ? loc.townId : null
    this.villageId = loc ? (loc.villageId || null) : null
    this.villageName = loc ? (loc.villageName || '') : ''
    this.townLabel = getTownLabel(loc)
    this.reload()
  },
  onShow() {
    const loc = getLocation()
    if (!sameLocation(loc, { townId: this.townId, villageId: this.villageId })) {
      this.townId = loc ? loc.townId : null
      this.villageId = loc ? (loc.villageId || null) : null
      this.villageName = loc ? (loc.villageName || '') : ''
      this.townLabel = getTownLabel(loc)
      this.reload()
    }
  },
  onPullDownRefresh() {
    this.reload().finally(() => uni.stopPullDownRefresh())
  },
  methods: {
    switchTab(t) {
      if (this.tab === t) return
      this.tab = t
      this.reload()
    },
    setSort(o) {
      this.orderBy = o
      this.reload()
    },
    onTownChange(e) {
      this.townLabel = e.townName || '全部乡镇'
      this.villageId = null
      this.villageName = ''
      this.persist()
    },
    onVillageChange(e) {
      this.villageId = e.villageId || null
      this.villageName = e.villageName || ''
      this.persist()
    },
    persist() {
      setLocation({
        countyId: 3,
        townId: this.townId,
        townName: this.townLabel === '全部乡镇' ? '' : this.townLabel,
        villageId: this.villageId,
        villageName: this.villageName,
      })
      this.reload()
    },
    buildQuery() {
      const q = { page: this.page, pageSize: this.pageSize, townIncludeVillages: true }
      if (this.keyword) q.keyword = this.keyword
      if (this.orderBy) q.orderBy = this.orderBy
      if (this.townId) q.townId = this.townId
      if (this.villageId) q.villageId = this.villageId
      return q
    },
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
        const api = this.tab === 'supply' ? supplyApi : demandApi
        const res = await api.list(this.buildQuery())
        const list = res.items || []
        this.items = this.page === 1 ? list : this.items.concat(list)
        if (this.items.length >= res.total || list.length < this.pageSize) this.finished = true
        else this.page += 1
      } catch (e) {
      } finally {
        this.loading = false
      }
    },
    priceText(it) {
      if (this.tab === 'supply') return it.price ? it.price + ' ' + it.priceUnit : '面议'
      const r = (it.priceMin || '') + (it.priceMax ? '~' + it.priceMax : '')
      return r ? r + ' ' + it.priceUnit : '面议'
    },
    goDetail(id) {
      const base = this.tab === 'supply' ? '/pages/supply/detail?id=' : '/pages/demand/detail?id='
      uni.navigateTo({ url: base + id })
    },
  },
}
</script>

<style>
.page { padding-bottom: 20rpx; }
.tabs { display: flex; background: #fff; }
.tab { flex: 1; text-align: center; padding: 26rpx 0; font-size: 30rpx; color: #7a8a80; position: relative; }
.tab.active { color: #2e8b57; font-weight: 600; }
.tab.active::after {
  content: '';
  position: absolute;
  left: 50%; bottom: 0;
  transform: translateX(-50%);
  width: 60rpx; height: 6rpx;
  background: #2e8b57;
  border-radius: 999rpx;
}
.filter-bar { display: flex; align-items: center; background: #fff; padding: 0 24rpx 20rpx; }
.search { flex: 1; background: #f4f6f4; border-radius: 999rpx; padding: 14rpx 24rpx; margin-right: 16rpx; }
.search-input { font-size: 28rpx; }
.filter-chip {
  padding: 14rpx 24rpx;
  border-radius: 999rpx;
  font-size: 26rpx;
  background: #f4f6f4;
  color: #1f2d24;
  max-width: 240rpx;
  overflow: hidden;
  white-space: nowrap;
}
.filter-chip.active { background: #e8f5ee; color: #2e8b57; }
.sort-bar { display: flex; align-items: center; padding: 16rpx 32rpx; }
.sort-item { font-size: 26rpx; color: #7a8a80; margin-right: 40rpx; }
.sort-item.active { color: #2e8b57; font-weight: 600; }
.card-title { font-size: 30rpx; font-weight: 600; flex: 1; margin-right: 16rpx; }
.list-thumb { width: 150rpx; height: 150rpx; border-radius: 12rpx; margin-right: 20rpx; background: #f4f6f4; flex-shrink: 0; }
.container { padding: 8rpx 24rpx 24rpx; }
</style>
