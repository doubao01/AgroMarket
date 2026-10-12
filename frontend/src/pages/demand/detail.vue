<template>
  <view class="page">
    <view v-if="item" class="container">
      <view class="card">
        <view class="detail-title">{{ item.title }}</view>
        <view class="price-big mt16">{{ demandPrice }}</view>
        <view class="mt8 muted">需求数量：{{ item.quantity }}{{ item.unit }}</view>
        <view class="mt16">
          <text class="tag">{{ item.categoryName || '农产品' }}</text>
          <text v-if="item.expectDate" class="tag">期望 {{ fmtDate(item.expectDate) }}</text>
          <text v-if="item.status !== 'ACTIVE'" class="tag">{{ item.status }}</text>
        </view>
      </view>
      <view class="card">
        <view class="between"><text class="field-label">交货地</text><text>{{ (item.countyName || '息县') }} {{ item.townName || '' }} {{ item.villageName || '' }}</text></view>
        <view class="between mt16"><text class="field-label">规格要求</text><text>{{ item.specRequired || '—' }}</text></view>
        <view class="between mt16"><text class="field-label">包装要求</text><text>{{ item.packagingNote || '—' }}</text></view>
        <view class="between mt16"><text class="field-label">发布者</text><text>{{ item.publisherName }}</text></view>
      </view>
      <view v-if="item.description" class="card">
        <view class="bold mb16">采购说明</view>
        <text class="muted">{{ item.description }}</text>
      </view>
      <view class="card">
        <view class="bold mb16">联系方式</view>
        <view v-if="me" class="between">
          <text class="primary" style="font-size:36rpx;" @tap="callPhone">{{ item.contactPhone || '未填写' }}</text>
          <text class="muted">{{ item.contactName || '' }}</text>
        </view>
        <view v-else class="muted">登录后可查看联系方式</view>
      </view>

      <view v-if="!isOwner" class="report-link" @tap="report">举报该信息</view>
    </view>
    <view class="empty" v-else>{{ loading ? '加载中...' : '信息不存在或已关闭' }}</view>

    <view v-if="item && !isOwner" class="bottom-bar">
      <button class="btn btn-plain btn-sm chat-btn" @tap="chatAction">聊一聊</button>
      <button class="btn btn-block flex1" @tap="openQuote">我要供货 / 报价</button>
    </view>

    <view v-if="showQuote" class="modal-mask" @tap="showQuote = false">
      <view class="modal" @tap.stop>
        <view class="bold" style="font-size:32rpx;">提交供货报价</view>
        <view class="mt24 field">
          <text class="field-label">单价({{ item.priceUnit }})</text>
          <input v-model="quoteForm.price" type="digit" class="input" placeholder="请输入单价" />
        </view>
        <view class="mt16 field">
          <text class="field-label">数量({{ item.unit }})</text>
          <input v-model="quoteForm.quantity" type="digit" class="input" placeholder="可供数量" />
        </view>
        <view class="mt16 field">
          <text class="field-label">备注</text>
          <input v-model="quoteForm.remark" class="input" placeholder="可选" />
        </view>
        <button class="btn btn-block mt24" @tap="submitQuote">提交报价</button>
      </view>
    </view>
  </view>
</template>

<script>
import { demandApi, quoteApi, chatApi } from '../../api/index'
import { requireLogin, getUser } from '../../utils/request'
import { reportTarget } from '../../utils/report'

export default {
  data() {
    return {
      id: null, item: null, loading: false, showQuote: false, me: null,
      quoteForm: { price: '', quantity: '', remark: '' },
    }
  },
  onLoad(query) {
    this.id = query.id
    this.me = getUser()
    this.load()
  },
  computed: {
    isOwner() { return this.me && this.item && this.me.id === this.item.publisherId },
    demandPrice() {
      if (!this.item) return ''
      const r = (this.item.priceMin || '') + (this.item.priceMax ? '~' + this.item.priceMax : '')
      return r ? r + ' ' + this.item.priceUnit : '面议'
    },
  },
  methods: {
    async load() {
      this.loading = true
      try {
        this.item = await demandApi.detail(this.id)
        if (this.item) this.quoteForm.quantity = this.item.quantity
      } catch (e) {} finally { this.loading = false }
    },
    openQuote() {
      if (!requireLogin()) return
      if (this.isOwner) return uni.showToast({ title: '不能对自己的需求报价', icon: 'none' })
      this.showQuote = true
    },
    async contactBuyer() {
      if (!requireLogin()) return
      if (this.isOwner) return uni.showToast({ title: '不能与自己聊天', icon: 'none' })
      try {
        const conv = await chatApi.createConversation({ peerUserId: this.item.publisherId, sourceType: 'DEMAND', sourceId: this.item.id })
        uni.navigateTo({ url: '/pages/chat/chat?id=' + conv.id })
      } catch (e) {}
    },
    chatAction() {
      if (!requireLogin()) return
      if (this.isOwner) return uni.showToast({ title: '不能与自己聊天', icon: 'none' })
      uni.showActionSheet({
        itemList: ['聊一聊', '分享到聊天'],
        success: (r) => {
          if (r.tapIndex === 0) this.contactBuyer()
          else this.shareToChat()
        },
      })
    },
    async shareToChat() {
      try {
        const conv = await chatApi.createConversation({ peerUserId: this.item.publisherId, sourceType: 'DEMAND', sourceId: this.item.id })
        await chatApi.shareProduct(conv.id, { demandId: this.item.id })
        uni.navigateTo({ url: '/pages/chat/chat?id=' + conv.id })
      } catch (e) {}
    },
    async submitQuote() {
      const price = Number(this.quoteForm.price)
      const quantity = Number(this.quoteForm.quantity)
      if (!price || price <= 0) return uni.showToast({ title: '请输入有效单价', icon: 'none' })
      if (!quantity || quantity <= 0) return uni.showToast({ title: '请输入有效数量', icon: 'none' })
      try {
        await quoteApi.create({ demandId: this.item.id, price, priceUnit: this.item.priceUnit, quantity, remark: this.quoteForm.remark })
        uni.showToast({ title: '报价已提交' })
        this.showQuote = false
        setTimeout(() => uni.navigateTo({ url: '/pages/quote/list' }), 600)
      } catch (e) {}
    },
    callPhone() { if (this.item.contactPhone) uni.makePhoneCall({ phoneNumber: this.item.contactPhone }) },
    report() { reportTarget('DEMAND', this.item.id, '举报该采购信息') },
    fmtDate(d) { return (d || '').slice(0, 10) },
  },
}
</script>

<style>
.detail-title { font-size: 36rpx; font-weight: 700; }
.bottom-bar {
  position: fixed; left: 0; right: 0; bottom: 0;
  background: #fff; padding: 16rpx 24rpx;
  padding-bottom: calc(16rpx + env(safe-area-inset-bottom));
  box-shadow: 0 -4rpx 16rpx rgba(0, 0, 0, 0.05);
  display: flex; align-items: center;
}
.bottom-bar .btn { margin: 0; }
.chat-btn { margin-right: 16rpx !important; flex: none; }
.modal-mask {
  position: fixed; left: 0; right: 0; top: 0; bottom: 0;
  background: rgba(0, 0, 0, 0.4); display: flex; align-items: flex-end; z-index: 999;
}
.modal {
  width: 100%; background: #fff; border-radius: 24rpx 24rpx 0 0;
  padding: 32rpx; padding-bottom: calc(32rpx + env(safe-area-inset-bottom));
}
.page { padding-bottom: 140rpx; }
.report-link { text-align: center; font-size: 24rpx; color: #9aa8a0; padding: 24rpx 0; }
</style>
