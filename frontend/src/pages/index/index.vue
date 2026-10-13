<template>
  <view class="page">
    <view class="hero">
      <view class="hero-loc" @tap="showRegion = true">
        <text class="hero-loc-icon">📍</text>
        <text class="hero-loc-name">{{ townLabel }}</text>
        <text class="hero-loc-arrow">▾</text>
      </view>
      <view class="hero-title">息县农贸通</view>
      <view class="hero-sub">豫南粮仓 · 农产品自由交易</view>
      <view class="hero-search" @tap="goMarket">
        <text class="hero-search-icon">🔍</text>
        <text class="hero-search-text">搜索小麦、花生、西瓜等农产品</text>
      </view>
    </view>

    <view class="quick container">
      <view class="quick-item" @tap="go('/pages/supply/publish')">
        <view class="quick-icon">🌾</view>
        <text class="quick-text">发布供应</text>
      </view>
      <view class="quick-item" @tap="go('/pages/demand/publish')">
        <view class="quick-icon">🛒</view>
        <text class="quick-text">发布采购</text>
      </view>
      <view class="quick-item" @tap="go('/pages/market/index?tab=supply')">
        <view class="quick-icon">📦</view>
        <text class="quick-text">供应大厅</text>
      </view>
      <view class="quick-item" @tap="go('/pages/market/index?tab=demand')">
        <view class="quick-icon">📋</view>
        <text class="quick-text">采购大厅</text>
      </view>
    </view>

    <view v-if="notices.length" class="notice-bar container">
      <text class="notice-tag">公告</text>
      <swiper class="notice-swiper" vertical autoplay circular interval="4000">
        <swiper-item v-for="n in notices" :key="n.id">
          <text class="notice-text" @tap="go('/pages/notice/index')">{{ n.title }}</text>
        </swiper-item>
      </swiper>
    </view>

    <view v-if="banners.length" class="banner-wrap container">
      <swiper class="banner-swiper" circular autoplay interval="4000">
        <swiper-item v-for="b in banners" :key="b.id">
          <image :src="b.imageUrl" class="banner-img" mode="aspectFill" @tap="openBanner(b)" />
        </swiper-item>
      </swiper>
    </view>

    <view class="container">
      <view class="between section-head">
        <text class="section-title">热点讨论</text>
        <text class="section-more" @tap="go('/pages/community/index')">更多 ›</text>
      </view>
      <view v-if="hotPosts.length" class="card hot-card" @tap="go('/pages/community/index')">
        <view v-for="p in hotPosts" :key="p.id" class="hot-row" @tap.stop="go('/pages/community/detail?id=' + p.id)">
          <text class="hot-flame">热</text>
          <text class="hot-title">{{ p.title }}</text>
          <text class="muted hot-meta">{{ p.commentCount }} 评</text>
        </view>
      </view>
    </view>

    <view class="container">
      <view class="between section-head">
        <text class="section-title">最新供应</text>
        <text class="section-more" @tap="goMarketTab('supply')">更多 ›</text>
      </view>
      <view v-if="!loadingSupplies && !supplies.length" class="empty">暂无供应信息</view>
      <view
        v-for="s in supplies"
        :key="s.id"
        class="card supply-card"
        @tap="go('/pages/supply/detail?id=' + s.id)"
      >
        <view class="between">
          <text class="card-title">{{ s.title }}</text>
          <text class="price">{{ s.price ? s.price + ' ' + s.priceUnit : '面议' }}</text>
        </view>
        <view class="mt8 muted">{{ s.quantity }}{{ s.unit }} · {{ s.townName || '息县' }}{{ s.villageName ? ' ' + s.villageName : '' }}</view>
        <view class="mt8">
          <text class="tag">{{ s.categoryName || '农产品' }}</text>
          <text v-if="s.negotiable" class="tag">可议价</text>
        </view>
      </view>
    </view>

    <view class="container">
      <view class="between section-head">
        <text class="section-title">最新采购</text>
        <text class="section-more" @tap="goMarketTab('demand')">更多 ›</text>
      </view>
      <view v-if="!loadingDemands && !demands.length" class="empty">暂无采购需求</view>
      <view
        v-for="d in demands"
        :key="d.id"
        class="card supply-card"
        @tap="go('/pages/demand/detail?id=' + d.id)"
      >
        <view class="between">
          <text class="card-title">{{ d.title }}</text>
          <text class="price">{{ d.priceMin || d.priceMax ? (d.priceMin || '') + (d.priceMax ? '~' + d.priceMax : '') + ' ' + d.priceUnit : '面议' }}</text>
        </view>
        <view class="mt8 muted">{{ d.quantity }}{{ d.unit }} · {{ d.townName || '息县' }}</view>
      </view>
    </view>

    <region-picker
      v-model:visible="showRegion"
      allow-all
      close-on-town-select
      v-model:townId="townId"
      v-model:villageId="villageId"
      @town-change="onTownChange"
      @village-change="onVillageChange"
    />
  </view>
</template>

<script>
import { supplyApi, demandApi, noticeApi, bannerApi, communityApi } from '../../api/index'
import RegionPicker from '../../components/region-picker.vue'
import { getLocation, setLocation, getTownLabel } from '../../utils/location'

export default {
  components: { RegionPicker },
  data() {
    return {
      supplies: [], demands: [], notices: [], banners: [], hotPosts: [],
      loadingSupplies: false, loadingDemands: false,
      showRegion: false,
      townId: null, villageId: null, villageName: '', townLabel: '全部乡镇',
    }
  },
  onShow() { this.syncLocation(); this.load() },
  methods: {
    syncLocation() {
      const loc = getLocation()
      this.townId = loc ? loc.townId : null
      this.villageId = loc ? (loc.villageId || null) : null
      this.villageName = loc ? (loc.villageName || '') : ''
      this.townLabel = getTownLabel(loc)
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
      setLocation({ countyId: 3, townId: this.townId, townName: this.townLabel === '全部乡镇' ? '' : this.townLabel, villageId: this.villageId, villageName: this.villageName })
      this.load()
    },
    listQuery(page, pageSize) {
      const q = { page, pageSize }
      if (this.townId) { q.townId = this.townId; q.townIncludeVillages = true }
      if (this.villageId) q.villageId = this.villageId
      return q
    },
    async load() {
      bannerApi.list().then((b) => { this.banners = b || [] }).catch(() => {})
      communityApi.posts({ orderBy: 'hot', page: 1, pageSize: 3 }).then((r) => { this.hotPosts = (r.items || []) }).catch(() => {})
      this.loadingSupplies = true
      this.loadingDemands = true
      try { this.supplies = (await supplyApi.list(this.listQuery(1, 5))).items || [] } catch (e) {}
      this.loadingSupplies = false
      try { this.demands = (await demandApi.list(this.listQuery(1, 4))).items || [] } catch (e) {}
      this.loadingDemands = false
      try { this.notices = (await noticeApi.list()) || [] } catch (e) {}
    },
    go(url) { uni.navigateTo({ url }) },
    goMarketTab(tab) {
      // market 是 tabBar 页,navigateTo 无法打开;用 storage 传 tab
      uni.setStorageSync('market_tab', tab)
      uni.switchTab({ url: '/pages/market/index' })
    },
    goMarket() { uni.switchTab({ url: '/pages/market/index' }) },
    openBanner(b) {
      if (b.linkType === 'PAGE' && b.linkValue) {
        if (b.linkValue.indexOf('/pages/market/index') === 0) {
          const m = (b.linkValue.match(/tab=(supply|demand)/) || [])[1]
          this.goMarketTab(m || 'supply')
        } else uni.navigateTo({ url: b.linkValue })
      }
      // #ifdef H5
      else if (b.linkType === 'URL' && b.linkValue) window.open(b.linkValue)
      // #endif
    },
  },
}
</script>

<style>
.banner-wrap { margin-top: -60rpx; position: relative; z-index: 2; }
.banner-swiper { height: 240rpx; border-radius: 16rpx; overflow: hidden; }
.banner-img { width: 100%; height: 240rpx; border-radius: 16rpx; }
.hot-card { padding: 20rpx 24rpx; }
.hot-row { display: flex; align-items: center; padding: 14rpx 0; }
.hot-flame {
  flex-shrink: 0; font-size: 22rpx; color: #e6532e; background: #fdeee8;
  border-radius: 6rpx; padding: 2rpx 10rpx; margin-right: 16rpx; font-weight: 600;
}
.hot-title { flex: 1; font-size: 28rpx; color: #1f2d24; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
.hot-meta { flex-shrink: 0; margin-left: 16rpx; font-size: 24rpx; }
.hero {
  background: linear-gradient(135deg, #2e8b57, #3aa86a);
  padding: 40rpx 32rpx 80rpx;
  color: #fff;
}
.hero-loc {
  display: inline-flex;
  align-items: center;
  max-width: 60%;
  margin-bottom: 20rpx;
  padding: 8rpx 20rpx;
  border-radius: 999rpx;
  background: rgba(255, 255, 255, 0.18);
}
.hero-loc-icon { font-size: 26rpx; margin-right: 8rpx; }
.hero-loc-name {
  font-size: 28rpx;
  font-weight: 600;
  max-width: 320rpx;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.hero-loc-arrow { font-size: 24rpx; margin-left: 8rpx; opacity: 0.9; }
.hero-title { font-size: 48rpx; font-weight: 700; }
.hero-sub { font-size: 26rpx; opacity: 0.9; margin-top: 8rpx; }
.hero-search {
  margin-top: 32rpx;
  background: #fff;
  border-radius: 999rpx;
  padding: 20rpx 28rpx;
  display: flex;
  align-items: center;
}
.hero-search-icon { margin-right: 12rpx; }
.hero-search-text { color: #9aa8a0; font-size: 26rpx; }
.quick { display: flex; margin-top: -56rpx; }
.quick-item {
  flex: 1;
  background: #fff;
  border-radius: 20rpx;
  margin: 0 8rpx;
  padding: 28rpx 0;
  text-align: center;
  box-shadow: 0 4rpx 16rpx rgba(46, 139, 87, 0.08);
}
.quick-icon { font-size: 48rpx; }
.quick-text { display: block; font-size: 24rpx; margin-top: 12rpx; color: #1f2d24; }
.notice-bar { display: flex; align-items: center; background: #fff; border-radius: 16rpx; padding: 20rpx 24rpx; }
.notice-tag { background: #e8f5ee; color: #2e8b57; font-size: 22rpx; padding: 4rpx 14rpx; border-radius: 8rpx; margin-right: 16rpx; }
.notice-swiper { flex: 1; height: 44rpx; }
.notice-text { font-size: 26rpx; color: #1f2d24; line-height: 44rpx; }
.section-head { margin-bottom: 16rpx; }
.section-title { font-size: 32rpx; font-weight: 600; }
.section-more { font-size: 26rpx; color: #7a8a80; }
.card-title { font-size: 30rpx; font-weight: 600; flex: 1; margin-right: 16rpx; }
.container { padding: 24rpx; }
.quick.container { padding: 0 16rpx; margin-top: -56rpx; }
</style>
