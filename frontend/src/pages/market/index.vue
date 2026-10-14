<template>
  <view class="page">
    <view class="tabs">
      <view class="tab" :class="{ active: tab === 'supply' }" @tap="switchTab('supply')">供应大厅</view>
      <view class="tab" :class="{ active: tab === 'demand' }" @tap="switchTab('demand')">采购大厅</view>
      <view class="publish-btn" @tap="goPublish">＋ 发布</view>
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
      <text class="sort-item" :class="{ active: orderBy === 'quantity' }" @tap="setSort('quantity')">数量↓</text>
    </view>

    <!-- 品类筛选(横向滚动) -->
    <scroll-view scroll-x class="cat-bar" v-if="categories.length">
      <view
        v-for="cat in categories"
        :key="cat.id"
        class="cat-chip"
        :class="{ active: categoryId === cat.id }"
        @tap="setCategory(cat.id)"
      >{{ cat.name }}</view>
    </scroll-view>

    <view class="container">
      <view v-if="!loading && !items.length" class="empty-wrap">
        <view class="empty">暂无数据{{ keyword ? '，换个关键词试试' : '' }}</view>
        <button class="empty-btn" @tap="goPublish">＋ 我要{{ tab === 'supply' ? '供应' : '采购' }}</button>
      </view>
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
              <text v-if="tab === 'supply' && it.soldQuantity > 0" class="tag sold">已售 {{ it.soldQuantity }}{{ it.unit }}</text>
              <text v-if="tab === 'demand' && it.quoteCount > 0" class="tag sold">{{ it.quoteCount }} 人报价</text>
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
import { supplyApi, demandApi, categoryApi } from '../../api/index'
import RegionPicker from '../../components/region-picker.vue'
import { requireLogin } from '../../utils/request'
import { getLocation, setLocation, getTownLabel, sameLocation } from '../../utils/location'

export default {
  components: { RegionPicker },
  data() {
    return {
      tab: 'supply',
      keyword: '',
      orderBy: '',
      categoryId: null,
      categories: [],
      townId: null,
      villageId: null,
      villageName: '',
      townLabel: '全部乡镇',
      // 常驻乡镇软排序: prefer=乡镇置顶优先(不过滤), filter=硬过滤
      locMode: 'prefer',
      page: 1,
      pageSize: 10,
      items: [],
      loading: false,
      finished: false,
      showRegion: false,
    }
  },
  onLoad(query) {
    // market 是 tabBar 页, switchTab 无法带参, 首页「更多」用 storage 传 tab (见 applyPendingTab)
    if (query.tab === 'supply' || query.tab === 'demand') {
      // 深链显式指定, 丢弃可能遗留的「更多」请求
      this.tab = query.tab
      uni.removeStorageSync('market_tab')
    } else {
      this.applyPendingTab()
    }
    const loc = getLocation()
    this.townId = loc ? loc.townId : null
    this.villageId = loc ? (loc.villageId || null) : null
    this.villageName = loc ? (loc.villageName || '') : ''
    this.townLabel = getTownLabel(loc)
    this.locMode = 'prefer'
    this.reload()
    this.loadCategories()
  },
  onShow() {
    // tabBar 页二次进入只触发 onShow(不触发 onLoad), 故「更多」切 tab 的消费必须放在这里
    const tabChanged = this.applyPendingTab()
    const loc = getLocation()
    const locChanged = !sameLocation(loc, { townId: this.townId, villageId: this.villageId })
    if (locChanged) {
      this.townId = loc ? loc.townId : null
      this.villageId = loc ? (loc.villageId || null) : null
      this.villageName = loc ? (loc.villageName || '') : ''
      this.townLabel = getTownLabel(loc)
      this.locMode = 'prefer'
    }
    if (tabChanged || locChanged) this.reload()
  },
  onPullDownRefresh() {
    this.reload().finally(() => uni.stopPullDownRefresh())
  },
  methods: {
    async loadCategories() {
      try {
        const list = await categoryApi.all()
        // 取一级品类(无 parent)展示, 后端按父子类联查
        this.categories = (list || []).filter((c) => !c.parentId)
      } catch (e) {}
    },
    setCategory(id) {
      this.categoryId = this.categoryId === id ? null : id
      this.reload()
    },
    goPublish() {
      if (requireLogin()) {
        uni.navigateTo({ url: this.tab === 'demand' ? '/pages/demand/publish' : '/pages/supply/publish' })
      }
    },
    switchTab(t) {
      if (this.tab === t) return
      this.tab = t
      this.reload()
    },
    // 消费首页「更多」写入的 pending tab; 返回是否发生了切换 (供 onShow 决定是否 reload)
    applyPendingTab() {
      const pending = uni.getStorageSync('market_tab')
      if (pending !== 'supply' && pending !== 'demand') {
        if (pending) uni.removeStorageSync('market_tab') // 清理非法脏值
        return false
      }
      uni.removeStorageSync('market_tab')
      if (pending === this.tab) return false
      this.tab = pending
      return true
    },
    setSort(o) {
      this.orderBy = o
      this.reload()
    },
    onTownChange(e) {
      this.locMode = e.townId ? 'filter' : (this.townId ? 'prefer' : this.locMode)
      this.townLabel = e.townName || '全部乡镇'
      this.townId = e.townId || null
      this.villageId = null
      this.villageName = ''
      this.persist()
    },
    onVillageChange(e) {
      if (e.villageId) this.locMode = 'filter'
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
      if (this.categoryId) q.categoryId = this.categoryId
      if (this.townId) {
        // 常驻乡镇默认软排序置顶; 用户手动选乡镇时硬过滤
        if (this.locMode === 'prefer') q.preferTownId = this.townId
        else { q.townId = this.townId; q.townIncludeVillages = true }
      }
      if (this.villageId) { q.villageId = this.villageId }
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
.tabs { display: flex; background: #fff; position: relative; }
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
.publish-btn {
  position: absolute; right: 24rpx; top: 50%;
  transform: translateY(-50%);
  font-size: 26rpx; color: #fff; background: #2e8b57;
  border-radius: 999rpx; padding: 10rpx 28rpx;
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
.cat-bar { white-space: nowrap; padding: 4rpx 24rpx 12rpx; background: #fff; }
.cat-chip {
  display: inline-block;
  padding: 10rpx 28rpx;
  margin-right: 16rpx;
  font-size: 26rpx;
  background: #f4f6f4;
  border-radius: 999rpx;
  color: #1f2d24;
}
.cat-chip.active { background: #2e8b57; color: #fff; }
.tag.sold { background: #fdf6ec; color: #e6a23c; }
.empty-wrap { padding: 60rpx 0; text-align: center; }
.empty-btn {
  margin: 30rpx auto 0;
  width: 320rpx;
  font-size: 28rpx;
  color: #fff;
  background: #2e8b57;
  border-radius: 999rpx;
}
</style>
