<template>
  <view class="page container">
    <view v-if="q" class="card">
      <view class="between">
        <text class="bold" style="font-size:32rpx;">报价单 {{ q.quoteNo.slice(0, 10) }}</text>
        <text class="tag">{{ statusText(q.status) }}</text>
      </view>
      <view class="between mt24"><text class="field-label">商品</text><text>{{ q.supplyTitle || q.demandTitle || '—' }}</text></view>
      <view class="between mt16"><text class="field-label">单价</text><text class="price">{{ q.price }} {{ q.priceUnit }}</text></view>
      <view class="between mt16"><text class="field-label">数量</text><text>{{ q.quantity }}</text></view>
      <view class="between mt16"><text class="field-label">买方</text><text>{{ q.buyerName }}</text></view>
      <view class="between mt16"><text class="field-label">卖方</text><text>{{ q.sellerName }}</text></view>
      <view v-if="q.remark" class="mt16"><text class="field-label">备注</text><text class="muted" style="margin-left:16rpx;">{{ q.remark }}</text></view>
    </view>
    <view v-else class="empty">{{ loading ? '加载中...' : '报价不存在' }}</view>

    <view v-if="q && canRespond" class="card">
      <view class="bold mb16">还价</view>
      <view class="field"><text class="field-label">单价</text><input v-model="counter.price" type="digit" class="input" :placeholder="String(q.price)" /></view>
      <view class="field"><text class="field-label">数量</text><input v-model="counter.quantity" type="digit" class="input" :placeholder="String(q.quantity)" /></view>
      <view class="field"><text class="field-label">备注</text><input v-model="counter.remark" class="input" placeholder="可选" /></view>
      <button class="btn btn-plain btn-block mt16" @tap="doCounter">提交还价</button>
    </view>

    <view v-if="q && canRespond" class="action-row">
      <button class="btn flex1" @tap="doAccept">接受报价</button>
      <button class="btn btn-accent flex1" style="margin-left:20rpx;" @tap="doReject">拒绝</button>
    </view>
    <view v-if="q && canCreateOrder" class="action-row">
      <button class="btn btn-block" @tap="showOrder = true">确认下单</button>
    </view>
    <view v-if="q && q.orderId" class="action-row">
      <button class="btn btn-plain btn-block" @tap="goOrder">查看订单</button>
    </view>

    <view v-if="showOrder" class="modal-mask" @tap="showOrder = false">
      <view class="modal" @tap.stop>
        <view class="bold" style="font-size:32rpx;">确认下单</view>
        <view class="muted mt8">成交价 {{ q.price }} {{ q.priceUnit }} × {{ q.quantity }} = <text class="price">{{ (q.price * q.quantity).toFixed(2) }} 元</text></view>
        <view class="field mt16">
          <text class="field-label">交付方式</text>
          <input v-model="orderForm.deliveryMode" class="input" placeholder="如 自提/送货" />
        </view>
        <view class="field">
          <text class="field-label">交付地址</text>
          <input v-model="orderForm.addressSnapshot" class="input" placeholder="可选" />
        </view>
        <view class="field">
          <text class="field-label">备注</text>
          <input v-model="orderForm.remark" class="input" placeholder="可选" />
        </view>
        <view class="muted mt8">提示：平台支付功能尚未接入，下单后订单进入待卖方确认状态。</view>
        <button class="btn btn-block mt24" @tap="doCreateOrder">提交订单</button>
      </view>
    </view>
  </view>
</template>

<script>
import { quoteApi, orderApi } from '../../api/index'
import { requireLogin, getUser } from '../../utils/request'
import { requestSubscribe } from '../../utils/subscribe'

export default {
  data() {
    return {
      id: null, q: null, loading: false, me: { id: 0 },
      counter: { price: '', quantity: '', remark: '' },
      orderForm: { deliveryMode: '', addressSnapshot: '', remark: '' },
      showOrder: false,
    }
  },
  onLoad(query) { if (!requireLogin()) return; this.id = query.id; this.me = getUser() || { id: 0 }; this.load() },
  computed: {
    isParticipant() { return this.q && (this.q.buyerId === this.me.id || this.q.sellerId === this.me.id) },
    canRespond() {
      return this.q && this.q.status === 'PENDING' && this.isParticipant && this.q.initiatorId !== this.me.id
    },
    canCreateOrder() {
      return this.q && this.q.status === 'ACCEPTED' && this.q.buyerId === this.me.id
    },
  },
  methods: {
    async load() {
      this.loading = true
      try { this.q = await quoteApi.detail(this.id) } catch (e) {} finally { this.loading = false }
    },
    async doAccept() {
      try { this.q = await quoteApi.accept(this.id); uni.showToast({ title: '已接受报价' }) } catch (e) {}
    },
    async doReject() {
      const r = await uni.showModal({ title: '确认', content: '确定拒绝该报价？' })
      if (!r.confirm) return
      try { this.q = await quoteApi.reject(this.id); uni.showToast({ title: '已拒绝' }) } catch (e) {}
    },
    async doCounter() {
      const price = Number(this.counter.price) || this.q.price
      const quantity = Number(this.counter.quantity) || this.q.quantity
      try {
        this.q = await quoteApi.counter(this.id, { price, quantity, remark: this.counter.remark })
        uni.showToast({ title: '还价已提交' })
        this.counter = { price: '', quantity: '', remark: '' }
      } catch (e) {}
    },
    async doCreateOrder() {
      try {
        requestSubscribe('ORDER')
        const o = await orderApi.createFromQuote(this.id, this.orderForm)
        this.showOrder = false
        uni.showToast({ title: '订单已创建' })
        setTimeout(() => uni.redirectTo({ url: '/pages/order/detail?id=' + o.id }), 700)
      } catch (e) {}
    },
    goOrder() { uni.navigateTo({ url: '/pages/order/detail?id=' + this.q.orderId }) },
    statusText(s) {
      return { PENDING: '待回复', NEGOTIATED: '已还价', ACCEPTED: '已接受', REJECTED: '已拒绝', DEALED: '已成交', CLOSED: '已关闭' }[s] || s
    },
  },
}
</script>

<style>
.action-row { display: flex; margin-top: 20rpx; }
.action-row .btn { margin: 0; }
.modal-mask { position: fixed; left: 0; right: 0; top: 0; bottom: 0; background: rgba(0, 0, 0, 0.4); display: flex; align-items: flex-end; z-index: 999; }
.modal { width: 100%; background: #fff; border-radius: 24rpx 24rpx 0 0; padding: 32rpx; padding-bottom: calc(32rpx + env(safe-area-inset-bottom)); }
</style>
