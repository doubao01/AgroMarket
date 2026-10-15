<template>
  <view class="page">
    <view class="tabs">
      <view class="tab" :class="{ active: tab === 'user' }" @tap="switchTab('user')">用户管理</view>
      <view class="tab" :class="{ active: tab === 'notice' }" @tap="switchTab('notice')">公告发布</view>
      <view class="tab" :class="{ active: tab === 'banner' }" @tap="switchTab('banner')">焦点图</view>
      <view class="tab" :class="{ active: tab === 'post' }" @tap="switchTab('post')">社区治理</view>
      <view class="tab" :class="{ active: tab === 'log' }" @tap="switchTab('log')">操作日志</view>
    </view>

    <!-- 用户管理 -->
    <view v-if="tab === 'user'" class="container">
      <view class="card search-bar">
        <input v-model="userKw" class="input flex1" placeholder="手机号/昵称搜索" confirm-type="search" @confirm="loadUsers" />
        <text class="mini" @tap="loadUsers">搜索</text>
      </view>
      <view v-if="!users.length" class="empty">暂无用户</view>
      <view v-for="u in users" :key="u.id" class="card">
        <view class="between">
          <text class="card-title">{{ u.nickname || '用户 ' + u.id }}</text>
          <text class="tag" :class="{ danger: u.status === 'FROZEN' }">{{ userStatusText(u.status) }}</text>
        </view>
        <view class="muted mt8">{{ maskPhone(u.phone) }} · {{ roleText(u.role) }} · 注册于 {{ fmt(u.createdAt) }}</view>
        <view class="actions mt16">
          <text v-if="u.status !== 'FROZEN'" class="mini danger" @tap="setFreeze(u, true)">冻结</text>
          <text v-else class="mini" @tap="setFreeze(u, false)">解冻</text>
        </view>
      </view>
    </view>

    <!-- 公告发布 -->
    <view v-else-if="tab === 'notice'" class="container">
      <view class="card">
        <view class="field">
          <text class="field-label">公告标题 *</text>
          <input v-model="noticeForm.title" class="input" placeholder="如：市场交易规则更新" />
        </view>
        <view class="field" style="align-items:flex-start;">
          <text class="field-label">公告内容 *</text>
          <textarea v-model="noticeForm.content" class="input" style="min-height:160rpx;" placeholder="公告正文" />
        </view>
        <button class="btn btn-block" :disabled="publishing" @tap="publishNotice">{{ publishing ? '发布中...' : '发布公告' }}</button>
      </view>
      <view class="section-title mt24">已发布公告</view>
      <view v-if="!notices.length" class="empty">暂无公告</view>
      <view v-for="n in notices" :key="n.id" class="card">
        <view class="between">
          <text class="card-title">{{ n.title }}</text>
          <text class="mini danger" @tap="unpublishNotice(n)">下线</text>
        </view>
        <view class="muted mt8">{{ n.content }}</view>
        <view class="muted mt8">{{ fmt(n.createdAt) }}</view>
      </view>
    </view>

    <!-- 焦点图管理 -->
    <view v-else-if="tab === 'banner'" class="container">
      <view class="card">
        <view class="field"><text class="field-label">标题 *</text><input v-model="bannerForm.title" class="input" placeholder="运营位标题" /></view>
        <view class="field" style="align-items:flex-start;">
          <text class="field-label">图片 *</text>
          <view>
            <view v-if="bannerForm.imageUrl" class="img-wrap">
              <image :src="bannerForm.imageUrl" class="banner-preview" mode="aspectFill" @tap="chooseBannerImage" />
              <text class="muted" style="display:block;text-align:center;" @tap="chooseBannerImage">点击更换</text>
            </view>
            <view v-else class="img-add" @tap="chooseBannerImage">＋ 上传图片</view>
          </view>
        </view>
        <view class="field">
          <text class="field-label">链接类型</text>
          <picker :range="['无链接', '站内页面', '外部链接']" :value="bannerLinkTypeIndex" @change="(e) => bannerLinkTypeIndex = Number(e.detail.value)">
            <text>{{ ['无链接', '站内页面', '外部链接'][bannerLinkTypeIndex] }}</text>
          </picker>
        </view>
        <view class="field" v-if="bannerLinkTypeIndex > 0">
          <text class="field-label">链接值 *</text>
          <input v-model="bannerForm.linkValue" class="input" :placeholder="bannerLinkTypeIndex === 1 ? '如 /pages/market/index?tab=supply' : 'https://...'" />
        </view>
        <view class="field"><text class="field-label">排序</text><input v-model="bannerForm.sortOrder" class="input" type="number" placeholder="数字越小越靠前" /></view>
        <button class="btn btn-block" @tap="saveBanner">保存焦点图</button>
      </view>
      <view class="section-title mt24">已配置焦点图</view>
      <view v-if="!banners.length" class="empty">暂无焦点图</view>
      <view v-for="b in banners" :key="b.id" class="card">
        <view class="between">
          <text class="card-title">{{ b.title }}</text>
          <text class="mini danger" @tap="removeBanner(b)">删除</text>
        </view>
        <image :src="b.imageUrl" class="banner-preview mt8" mode="aspectFill" />
        <view class="muted mt8">排序 {{ b.sortOrder }} · {{ bannerLinkText(b) }}</view>
      </view>
    </view>

    <!-- 社区治理 -->
    <view v-else-if="tab === 'post'" class="container">
      <view v-if="!posts.length" class="empty">暂无帖子</view>
      <view v-for="p in posts" :key="p.id" class="card">
        <view class="between">
          <text class="card-title">{{ p.title }}</text>
          <text class="tag" :class="{ danger: p.status !== 'ACTIVE' }">{{ p.status === 'ACTIVE' ? '展示中' : '已隐藏' }}</text>
        </view>
        <view class="muted mt8">{{ p.nickname }} · {{ p.topic }} · 赞 {{ p.likeCount }} / 评 {{ p.commentCount }} · {{ fmt(p.createdAt) }}</view>
        <view class="actions mt16">
          <text v-if="p.status === 'ACTIVE'" class="mini danger" @tap="setPostStatus(p, 'OFF')">隐藏</text>
          <text v-else class="mini" @tap="setPostStatus(p, 'ACTIVE')">恢复展示</text>
        </view>
      </view>
      <view v-if="posts.length && postHasMore" class="load-more muted" @tap="loadPosts(true)">加载更多</view>
    </view>

    <!-- 操作日志 -->
    <view v-else class="container">
      <view class="card search-bar">
        <picker :range="logModules" :value="logModuleIndex" @change="onLogModule">
          <text class="flex1">{{ logModules[logModuleIndex] }}</text>
        </picker>
      </view>
      <view v-if="!logs.length" class="empty">暂无日志</view>
      <view v-for="(l, i) in logs" :key="l.id || i" class="card">
        <view class="between">
          <text class="card-title">{{ l.module || l.action || '日志' }}</text>
          <text class="muted">{{ fmt(l.createdAt) }}</text>
        </view>
        <view class="muted mt8">{{ logDetail(l) }}</view>
      </view>
      <view v-if="logs.length && logHasMore" class="load-more muted" @tap="loadLogs(true)">加载更多</view>
    </view>
  </view>
</template>

<script>
import { adminApi, noticeApi, bannerApi, communityAdminApi, uploadImageSmart } from '../../api/index'
import { requireLogin, getUser } from '../../utils/request'

export default {
  data() {
    return {
      tab: 'user',
      users: [], userKw: '',
      notices: [], noticeForm: { title: '', content: '' }, publishing: false,
      banners: [], bannerForm: { title: '', imageUrl: '', linkValue: '', sortOrder: 0 }, bannerLinkTypeIndex: 0,
      posts: [], postPage: 1, postHasMore: false,
      logs: [], logPage: 1, logHasMore: false, logModules: ['全部模块', 'USER', 'SUPPLY', 'DEMAND', 'ORDER', 'AUTH', 'REPORT'], logModuleIndex: 0,
    }
  },
  onLoad() {
    if (!requireLogin()) return
    const me = getUser()
    if (!me || me.role !== 'ADMIN') {
      uni.showToast({ title: '需要管理员权限', icon: 'none' })
      return setTimeout(() => uni.navigateBack(), 800)
    }
    this.loadUsers()
  },
  methods: {
    switchTab(t) {
      this.tab = t
      if (t === 'user' && !this.users.length) this.loadUsers()
      if (t === 'notice' && !this.notices.length) this.loadNotices()
      if (t === 'banner' && !this.banners.length) this.loadBanners()
      if (t === 'post' && !this.posts.length) { this.postPage = 1; this.posts = []; this.loadPosts() }
      if (t === 'log' && !this.logs.length) { this.logPage = 1; this.logs = []; this.loadLogs() }
    },
    async loadBanners() {
      try { this.banners = (await bannerApi.list()) || [] } catch (e) { this.banners = [] }
    },
    async chooseBannerImage() {
      uni.chooseImage({
        count: 1,
        success: async (res) => {
          try { this.bannerForm.imageUrl = await uploadImageSmart(res.tempFilePaths[0]) } catch (e) {}
        },
      })
    },
    async saveBanner() {
      const f = this.bannerForm
      if (!f.title) return uni.showToast({ title: '请填写标题', icon: 'none' })
      if (!f.imageUrl) return uni.showToast({ title: '请上传图片', icon: 'none' })
      const linkType = ['NONE', 'PAGE', 'URL'][this.bannerLinkTypeIndex]
      if (linkType !== 'NONE' && !f.linkValue) return uni.showToast({ title: '请填写链接值', icon: 'none' })
      try {
        await communityAdminApi.saveBanner({
          title: f.title, imageUrl: f.imageUrl, linkType,
          linkValue: linkType === 'NONE' ? null : f.linkValue,
          sortOrder: Number(f.sortOrder) || 0, enabled: true,
        })
        uni.showToast({ title: '已保存' })
        f.title = ''; f.imageUrl = ''; f.linkValue = ''; f.sortOrder = 0
        this.bannerLinkTypeIndex = 0
        this.loadBanners()
      } catch (e) {}
    },
    async removeBanner(b) {
      const ok = await new Promise((r) => uni.showModal({ title: '删除焦点图', content: b.title, success: (x) => r(x.confirm) }))
      if (!ok) return
      try { await communityAdminApi.deleteBanner(b.id); this.loadBanners() } catch (e) {}
    },
    bannerLinkText(b) {
      return { NONE: '无链接', PAGE: '站内 ' + b.linkValue, URL: '外链' }[b.linkType] || b.linkType
    },
    async loadPosts(more) {
      try {
        const page = more ? this.postPage : 1
        const res = await communityAdminApi.posts({ page, pageSize: 20 })
        const items = (res && res.items) || []
        this.posts = more ? this.posts.concat(items) : items
        this.postHasMore = items.length >= 20
        if (this.postHasMore) this.postPage = page + 1
      } catch (e) { if (!more) this.posts = [] }
    },
    async setPostStatus(p, status) {
      try {
        await communityAdminApi.setPostStatus(p.id, status)
        p.status = status
        uni.showToast({ title: status === 'OFF' ? '已隐藏' : '已恢复' })
      } catch (e) {}
    },
    async loadUsers() {
      try {
        const res = await adminApi.users({ keyword: this.userKw || undefined, page: 1, pageSize: 30 })
        this.users = (res && res.items) || []
      } catch (e) { this.users = [] }
    },
    async setFreeze(u, freeze) {
      const confirm = await new Promise(resolve => uni.showModal({
        title: freeze ? '冻结用户' : '解冻用户',
        content: freeze ? `确定冻结 ${u.nickname || u.phone}？冻结后无法登录。` : `确定解冻 ${u.nickname || u.phone}？`,
        success: r => resolve(r.confirm),
      }))
      if (!confirm) return
      try {
        await adminApi.freeze(u.id, freeze)
        uni.showToast({ title: freeze ? '已冻结' : '已解冻' })
        this.loadUsers()
      } catch (e) {}
    },
    async loadNotices() {
      try { this.notices = (await noticeApi.list()) || [] } catch (e) { this.notices = [] }
    },
    async publishNotice() {
      const f = this.noticeForm
      if (!f.title) return uni.showToast({ title: '请填写标题', icon: 'none' })
      if (!f.content) return uni.showToast({ title: '请填写内容', icon: 'none' })
      this.publishing = true
      try {
        await adminApi.createNotice({ title: f.title, content: f.content })
        uni.showToast({ title: '公告已发布' })
        f.title = ''; f.content = ''
        this.loadNotices()
      } catch (e) {} finally { this.publishing = false }
    },
    async unpublishNotice(n) {
      try {
        await adminApi.unpublishNotice(n.id)
        uni.showToast({ title: '公告已下线' })
        this.loadNotices()
      } catch (e) {}
    },
    onLogModule(e) {
      this.logModuleIndex = Number(e.detail.value)
      this.logPage = 1; this.logs = []
      this.loadLogs()
    },
    async loadLogs(more) {
      try {
        const module = this.logModuleIndex > 0 ? this.logModules[this.logModuleIndex] : undefined
        const page = more ? this.logPage : 1
        const res = await adminApi.logs({ module, page, pageSize: 20 })
        const items = (res && res.items) || []
        this.logs = more ? this.logs.concat(items) : items
        this.logHasMore = items.length >= 20
        if (this.logHasMore) this.logPage = page + 1
      } catch (e) { if (!more) this.logs = [] }
    },
    userStatusText(s) { return { ACTIVE: '正常', FROZEN: '已冻结' }[s] || s || '' },
    roleText(r) { return { FARMER: '农户', BUYER: '采购商', COOP: '合作社', ENTERPRISE: '企业', ADMIN: '管理员' }[r] || r || '' },
    maskPhone(p) { return p ? p.slice(0, 3) + '****' + p.slice(-4) : '' },
    fmt(t) { return t ? String(t).replace('T', ' ').slice(0, 16) : '' },
    logDetail(l) {
      const parts = [l.operatorName || l.operatorId, l.action, l.detail || l.summary]
      return parts.filter(Boolean).join(' · ')
    },
  },
}
</script>

<style>
.tabs { display: flex; background: #fff; }
.tab { flex: 1; text-align: center; padding: 24rpx 0; font-size: 28rpx; color: #7a8a80; }
.tab.active { color: #2e8b57; font-weight: 600; }
.container { padding: 24rpx; }
.card-title { font-size: 30rpx; font-weight: 600; flex: 1; margin-right: 16rpx; }
.actions { display: flex; }
.mini { font-size: 26rpx; color: #2e8b57; border: 1rpx solid #2e8b57; border-radius: 999rpx; padding: 8rpx 28rpx; margin-left: 20rpx; }
.mini.danger { color: #dd524d; border-color: #dd524d; }
.search-bar { display: flex; align-items: center; }
.search-bar .mini { white-space: nowrap; }
.tag.danger { color: #dd524d; border-color: #dd524d; }
.section-title { font-size: 30rpx; font-weight: 600; }
.field { display: flex; align-items: center; margin-bottom: 20rpx; gap: 16rpx; }
.field-label { font-size: 26rpx; color: #666; flex-shrink: 0; width: 130rpx; }
.input { background: #f4f6f4; border-radius: 8rpx; padding: 16rpx 20rpx; font-size: 28rpx; flex: 1; }
.img-add { width: 300rpx; height: 160rpx; border: 2rpx dashed #c0c8c0; border-radius: 8rpx; display: flex; align-items: center; justify-content: center; font-size: 28rpx; color: #9aa8a0; }
.banner-preview { width: 300rpx; height: 150rpx; border-radius: 8rpx; background: #f4f6f4; }
.btn-block { width: 100%; }
.load-more { text-align: center; padding: 24rpx 0; }
</style>
