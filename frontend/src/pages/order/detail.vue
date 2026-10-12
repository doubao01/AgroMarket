<template>
  <view class="page">
    <view v-if="o" class="container">
      <view class="card status-card">
        <text class="status-title">{{ statusText(o.status) }}</text>
        <text class="muted mt8">{{ statusHint }}</text>
      </view>

      <view class="card">
        <view class="bold mb16">商品明细</view>
        <view v-for="it in o.items" :key="it.id" class="between" style="padding:12rpx 0;">
          <view class="flex1">
            <view>{{ it.productName }}</view>
            <view class="muted">{{ it.spec || '' }} · {{ it.quantity }}{{ it.unit }} × {{ it.price }} {{ it.priceUnit }}</view>
          </view>
          <text class="price">{{ it.amount }} 元</text>
        </view>
        <view class="between mt16" style="border-top:1rpx solid #e3e9e4;padding-top:16rpx;">
          <text>合计</text><text class="price-big">{{ o.totalAmount }} 元</text>
        </view>
      </view>

      <view class="card">
        <view class="between"><text class="field-label">订单号</text><text class="muted">{{ o.orderNo }}</text></view>
        <view class="between mt16"><text class="field-label">买方</text><text>{{ o.buyerName }}</text></view>
        <view class="between mt16"><text class="field-label">卖方</text><text>{{ o.sellerName }}</text></view>
        <view class="between mt16"><text class="field-label">交付方式</text><text>{{ o.deliveryMode || '—' }}</text></view>
        <view v-if="o.addressSnapshot" class="between mt16"><text class="field-label">交付地址</text><text>{{ o.addressSnapshot }}</text></view>
        <view v-if="o.remark" class="between mt16"><text class="field-label">备注</text><text>{{ o.remark }}</text></view>
        <view v-if="o.cancelReason" class="between mt16"><text class="field-label">取消原因</text><text>{{ o.cancelReason }}</text></view>
      </view>

      <view v-if="o.statusLogs && o.statusLogs.length" class="card">
        <view class="bold mb16">状态记录</view>
        <view v-for="l in o.statusLogs" :key="l.id" class="log-item">
          <view class="log-dot"></view>
          <view class="flex1">
            <view class="log-text">{{ statusText(l.toStatus) }} <text class="muted">· {{ fmt(l.createdAt) }}</text></view>
            <view v-if="l.note" class="muted">{{ l.note }}</view>
          </view>
        </view>
      </view>
    </view>
    <view v-else class="empty">{{ loading ? '加载中...' : '订单不存在' }}</view>

    <view v-if="o" class="bottom-bar">
      <button v-if="canSellerConfirm" class="btn flex1" @tap="changeStatus('confirm', '确认订单')">确认订单</button>
      <button v-if="canPay" class="btn flex1" @tap="doPay">去支付</button>
      <button v-if="canShip" class="btn flex1" @tap="changeStatus('ship', '发货')">发货</button>
      <button v-if="canReceive" class="btn flex1" @tap="changeStatus('receive', '确认收货')">确认收货</button>
      <button v-if="canCancel" class="btn btn-plain flex1" style="margin-left:20rpx;" @tap="doCancel">取消订单</button>
    </view>
  </view>
</template>

<script>
import { orderApi, paymentApi } from '../../api/index'
import { requireLogin, getUser } from '../../utils/request'

export default {
  data() { return { id: null, o: null, loading: false, me: { id: 0 } } },
  onLoad(query) { if (!requireLogin()) return; this.id = query.id; this.me = getUser() || { id: 0 }; this.load() },
  computed: {
    isBuyer() { return this.o && this.o.buyerId === this.me.id },
    isSeller() { return this.o && this.o.sellerId === this.me.id },
    canSellerConfirm() { return this.o && this.o.status === 'PENDING_CONFIRM' && this.isSeller },
    canPay() { return this.o && this.o.status === 'TO_PAY' && this.isBuyer },
    canShip() { return this.o && this.o.status === 'TO_SHIP' && this.isSeller },
    canReceive() { return this.o && this.o.status === 'SHIPPING' && this.isBuyer },
    canCancel() {
      return this.o && ['PENDING_CONFIRM', 'TO_PAY'].includes(this.o.status) && (this.isBuyer || this.isSeller)
    },
    statusHint() {
      if (!this.o) return ''
      return {
        PENDING_CONFIRM: '等待卖方确认订单',
        TO_PAY: '等待买方付款',
        TO_SHIP: '已付款，等待卖方发货',
        SHIPPING: '卖方已发货，等待买方收货',
        CANCELLED: '订单已取消',
        AFTER_SALE: '售后处理中',
        COMPLETED: '交易已完成',
      }[this.o.status] || ''
    },
  },
  methods: {
    async load() {
      this.loading = true
      try { this.o = await orderApi.detail(this.id) } catch (e) {} finally { this.loading = false }
    },
    async changeStatus(op, label) {
      try {
        this.o = await orderApi.changeStatus(this.id, op, {})
        uni.showToast({ title: label + '成功' })
      } catch (e) {}
    },
    async doPay() {
      let channels = []
      try { channels = await paymentApi.channels() } catch (e) {}
      const avail = (channels || []).filter(c => c.available)
      if (!avail.length) { uni.showToast({ title: '暂无可用支付渠道', icon: 'none' }); return }
      uni.showActionSheet({
        itemList: avail.map(c => c.name),
        success: async (r) => {
          const ch = avail[r.tapIndex]
          try {
            const p = await paymentApi.create(this.id, ch.channel)
            if (p.paidNow) uni.showToast({ title: '支付成功' })
            else uni.showToast({ title: '已下单，请完成支付', icon: 'none' })
            this.load()
          } catch (e) {}
        },
      })
    },
    async doCancel() {
      const r = await uni.showModal({ title: '取消订单', editable: true, placeholderText: '请输入取消原因' })
      if (!r.confirm) return
      try {
        this.o = await orderApi.changeStatus(this.id, 'cancel', { reason: r.content || '用户取消' })
        uni.showToast({ title: '已取消' })
      } catch (e) {}
    },
    statusText(s) {
      return {
        PENDING_CONFIRM: '待卖方确认', TO_PAY: '待付款', TO_SHIP: '待发货', SHIPPING: '配送中',
        TO_RECEIVE: '待收货', COMPLETED: '已完成', CANCELLED: '已取消', AFTER_SALE: '售后中',
      }[s] || s
    },
    fmt(t) { return (t || '').replace('T', ' ').slice(0, 16) },
  },
}
</script>

<style>
.status-card { background: linear-gradient(135deg, #2e8b57, #3aa86a); color: #fff; }
.status-title { font-size: 40rpx; font-weight: 700; }
.status-card .muted { color: rgba(255, 255, 255, 0.9); }
.log-item { display: flex; padding: 10rpx 0; }
.log-dot { width: 16rpx; height: 16rpx; border-radius: 50%; background: #2e8b57; margin: 12rpx 16rpx 0 0; }
.log-text { font-size: 28rpx; }
.bottom-bar { position: fixed; left: 0; right: 0; bottom: 0; background: #fff; padding: 16rpx 24rpx; padding-bottom: calc(16rpx + env(safe-area-inset-bottom)); display: flex; box-shadow: 0 -4rpx 16rpx rgba(0, 0, 0, 0.05); }
.bottom-bar .btn { margin: 0; }
.page { padding-bottom: 140rpx; }
</style>
