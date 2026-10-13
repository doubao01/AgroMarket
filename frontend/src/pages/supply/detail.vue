<template>
  <view class="page">
    <view v-if="item" class="container">
      <view class="card">
        <view class="detail-title">{{ item.title }}</view>
        <view class="price-big mt16">{{ item.price ? item.price + ' ' + item.priceUnit : '面议' }}</view>
        <view class="mt8 muted">可售数量：{{ item.quantity }}{{ item.unit }}
          <text v-if="item.minOrderQty"> · 起订 {{ item.minOrderQty }}{{ item.unit }}</text>
        </view>
        <view v-if="item.soldQuantity > 0" class="mt8 muted">已售 {{ item.soldQuantity }}{{ item.unit }}</view>
        <view class="mt16">
          <text class="tag">{{ item.categoryName || '农产品' }}</text>
          <text v-if="item.negotiable" class="tag">可议价</text>
          <text v-if="item.status !== 'ACTIVE'" class="tag">{{ statusText(item.status) }}</text>
        </view>
      </view>

      <view v-if="item.images && item.images.length" class="card">
        <scroll-view scroll-x class="img-scroll">
          <image
            v-for="(img, i) in item.images"
            :key="img.id || i"
            :src="img.url"
            class="gallery-img"
            mode="aspectFill"
            @tap="previewImg(i)"
          />
        </scroll-view>
      </view>

      <view class="card">
        <view class="between"><text class="field-label">已售</text><text>{{ item.soldQuantity > 0 ? item.soldQuantity + ' ' + item.unit : '暂无成交' }}</text></view>
        <view class="between mt16"><text class="field-label">产地</text><text>{{ (item.countyName || '息县') }} {{ item.townName || '' }} {{ item.villageName || '' }}</text></view>
        <view class="between mt16"><text class="field-label">规格</text><text>{{ item.spec || '—' }}</text></view>
        <view class="between mt16"><text class="field-label">上市时间</text><text>{{ item.harvestTime || '—' }}</text></view>
        <view class="between mt16"><text class="field-label">交付方式</text><text>{{ deliveryText(item.deliveryMode) }}</text></view>
        <view class="between mt16"><text class="field-label">发布者</text><text>{{ item.publisherName }}</text></view>
      </view>

      <view v-if="item.description" class="card">
        <view class="bold mb16">商品描述</view>
        <text class="muted">{{ item.description }}</text>
      </view>

      <view class="card">
        <view class="bold mb16">联系方式</view>
        <view v-if="contactVisible" class="between">
          <text class="primary" style="font-size:36rpx;" @tap="callPhone">{{ item.contactPhone || '未填写' }}</text>
          <text class="muted">{{ item.tradeNote || '' }}</text>
        </view>
        <view v-else class="muted">登录后可查看联系方式</view>
      </view>

      <view v-if="!isOwner" class="report-link" @tap="report">举报该信息</view>
    </view>

    <view class="empty" v-else>{{ loading ? '加载中...' : '信息不存在或已下架' }}</view>

    <view v-if="item && !isOwner" class="bottom-bar">
      <view class="fav-btn" @tap="toggleFav">{{ faved ? '已收藏' : '收藏' }}</view>
      <button class="btn btn-plain btn-sm chat-btn" @tap="chatAction">聊一聊</button>
      <button class="btn btn-block flex1" @tap="openQuote">立即报价 / 议价</button>
    </view>
    <view v-else-if="item && isOwner" class="bottom-bar">
      <button class="btn btn-plain btn-block" @tap="goEdit">编辑我的发布</button>
    </view>

    <view v-if="showQuote" class="modal-mask" @tap="showQuote = false">
      <view class="modal" @tap.stop>
        <view class="bold" style="font-size:32rpx;">向卖家报价</view>
        <view class="mt24 field">
          <text class="field-label">单价({{ item.priceUnit }})</text>
          <input v-model="quoteForm.price" type="digit" class="input" placeholder="请输入报价单价" />
        </view>
        <view class="mt16 field">
          <text class="field-label">数量({{ item.unit }})</text>
          <input v-model="quoteForm.quantity" type="digit" class="input" placeholder="请输入采购数量" />
        </view>
        <view class="mt16 field">
          <text class="field-label">备注</text>
          <input v-model="quoteForm.remark" class="input" placeholder="可选，如交货时间" />
        </view>
        <view class="mt24 muted">提示：起订量 {{ item.minOrderQty || 0 }}{{ item.unit }}，报价后需卖家接受方可下单。</view>
        <button class="btn btn-block mt24" @tap="submitQuote">提交报价</button>
      </view>
    </view>
  </view>
</template>

<script>
import { supplyApi, favoriteApi, quoteApi, chatApi } from '../../api/index'
import { requireLogin, getUser } from '../../utils/request'
import { reportTarget } from '../../utils/report'

export default {
  data() {
    return {
      id: null,
      item: null,
      loading: false,
      faved: false,
      showQuote: false,
      quoteForm: { price: '', quantity: '', remark: '' },
      me: null,
    }
  },
  onLoad(query) {
    this.id = query.id
    this.me = getUser()
    this.load()
  },
  computed: {
    isOwner() { return this.me && this.item && this.me.id === this.item.publisherId },
    contactVisible() { return !!this.me },
  },
  methods: {
    async load() {
      this.loading = true
      try {
        this.item = await supplyApi.detail(this.id)
        if (this.item && this.item.minOrderQty) this.quoteForm.quantity = this.item.minOrderQty
        if (this.me && this.item) {
          try { this.faved = await favoriteApi.check('SUPPLY', this.id) } catch (e) {}
        }
      } catch (e) {} finally { this.loading = false }
    },
    openQuote() {
      if (!requireLogin()) return
      if (this.isOwner) return uni.showToast({ title: '不能对自己的商品报价', icon: 'none' })
      this.showQuote = true
    },
    async contactSeller() {
      if (!requireLogin()) return
      if (this.isOwner) return uni.showToast({ title: '不能与自己聊天', icon: 'none' })
      try {
        const conv = await chatApi.createConversation({ peerUserId: this.item.publisherId, sourceType: 'SUPPLY', sourceId: this.item.id })
        uni.navigateTo({ url: '/pages/chat/chat?id=' + conv.id })
      } catch (e) {}
    },
    chatAction() {
      if (!requireLogin()) return
      if (this.isOwner) return uni.showToast({ title: '不能与自己聊天', icon: 'none' })
      uni.showActionSheet({
        itemList: ['聊一聊', '分享到聊天'],
        success: (r) => {
          if (r.tapIndex === 0) this.contactSeller()
          else this.shareToChat()
        },
      })
    },
    async shareToChat() {
      try {
        const conv = await chatApi.createConversation({ peerUserId: this.item.publisherId, sourceType: 'SUPPLY', sourceId: this.item.id })
        await chatApi.shareProduct(conv.id, { supplyId: this.item.id })
        uni.navigateTo({ url: '/pages/chat/chat?id=' + conv.id })
      } catch (e) {}
    },
    async submitQuote() {
      const price = Number(this.quoteForm.price)
      const quantity = Number(this.quoteForm.quantity)
      if (!price || price <= 0) return uni.showToast({ title: '请输入有效单价', icon: 'none' })
      if (!quantity || quantity <= 0) return uni.showToast({ title: '请输入有效数量', icon: 'none' })
      try {
        await quoteApi.create({
          supplyId: this.item.id,
          price,
          priceUnit: this.item.priceUnit,
          quantity,
          remark: this.quoteForm.remark,
        })
        uni.showToast({ title: '报价已提交' })
        this.showQuote = false
        setTimeout(() => uni.navigateTo({ url: '/pages/quote/list' }), 600)
      } catch (e) {}
    },
    async toggleFav() {
      if (!requireLogin()) return
      try {
        if (this.faved) { await favoriteApi.remove('SUPPLY', this.id); this.faved = false }
        else { await favoriteApi.add('SUPPLY', this.id); this.faved = true }
      } catch (e) {}
    },
    goEdit() { uni.navigateTo({ url: '/pages/supply/publish?id=' + this.item.id }) },
    report() { reportTarget('SUPPLY', this.item.id, '举报该供应信息') },
    previewImg(i) {
      uni.previewImage({ urls: this.item.images.map(x => x.url), current: i })
    },
    callPhone() {
      if (this.item.contactPhone) uni.makePhoneCall({ phoneNumber: this.item.contactPhone })
    },
    statusText(s) {
      return { ACTIVE: '在售', PENDING: '审核中', OFF_SHELF: '已下架', SOLD_OUT: '已售罄', REJECTED: '已驳回' }[s] || s
    },
    deliveryText(m) {
      return { SELF_PICKUP: '自提', DELIVERY: '送货', BOTH: '自提/送货' }[m] || (m || '面议')
    },
  },
}
</script>

<style>
.detail-title { font-size: 36rpx; font-weight: 700; }
.img-scroll { white-space: nowrap; }
.gallery-img {
  display: inline-block; width: 400rpx; height: 400rpx;
  border-radius: 16rpx; margin-right: 16rpx; background: #f4f6f4;
}
.bottom-bar {
  position: fixed;
  left: 0; right: 0; bottom: 0;
  background: #fff;
  padding: 16rpx 24rpx;
  padding-bottom: calc(16rpx + env(safe-area-inset-bottom));
  display: flex;
  align-items: center;
  box-shadow: 0 -4rpx 16rpx rgba(0, 0, 0, 0.05);
}
.fav-btn {
  padding: 0 32rpx;
  line-height: 2.6;
  font-size: 28rpx;
  color: #2e8b57;
  border: 2rpx solid #2e8b57;
  border-radius: 999rpx;
  margin-right: 16rpx;
}
.bottom-bar .btn { margin: 0; }
.chat-btn { margin-right: 16rpx !important; flex: none; }
.modal-mask {
  position: fixed; left: 0; right: 0; top: 0; bottom: 0;
  background: rgba(0, 0, 0, 0.4);
  display: flex; align-items: flex-end;
  z-index: 999;
}
.modal {
  width: 100%;
  background: #fff;
  border-radius: 24rpx 24rpx 0 0;
  padding: 32rpx;
  padding-bottom: calc(32rpx + env(safe-area-inset-bottom));
}
.page { padding-bottom: 140rpx; }
.report-link { text-align: center; font-size: 24rpx; color: #9aa8a0; padding: 24rpx 0; }
</style>
