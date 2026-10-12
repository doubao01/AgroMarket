<template>
  <view class="page chat-page">
    <scroll-view
      class="msg-scroll"
      scroll-y
      :scroll-into-view="scrollInto"
      :scroll-with-animation="true"
      @scrolltoupper="loadOlder"
    >
      <view v-if="hasMore" class="load-more" @tap="loadOlder">加载更早的消息</view>
      <view v-else class="load-more muted">没有更多了</view>

      <template v-for="(m, i) in messages" :key="m.id">
        <view v-if="showTime(i)" class="time-divider">{{ timeLabel(m.createdAt) }}</view>

        <view v-if="m.status === 'RECALLED'" class="recall-row">
          <text class="recall-text">{{ recallText(m) }}</text>
        </view>

        <view v-else class="msg-row" :class="{ mine: mine(m) }" :id="'msg-' + m.id">
          <view class="bubble" :class="{ 'bubble-mine': mine(m), 'bubble-card': isCard(m) }" @longpress="openMenu(m)">
            <view v-if="quoted(m)" class="quote">
              <text class="quote-text">{{ snippet(quoted(m)) }}</text>
            </view>
            <image
              v-if="m.messageType === 'IMAGE' && imgUrl(m)"
              class="msg-img"
              :src="imgUrl(m)"
              mode="widthFix"
              @tap="previewImg(m)"
            />
            <view v-else-if="m.messageType === 'PRODUCT_CARD'" class="card" @tap="openCard(m)">
              <image v-if="m.payload && m.payload.image" class="card-img" :src="m.payload.image" mode="aspectFill" />
              <view class="card-body">
                <text class="card-title">{{ m.payload && m.payload.title }}</text>
                <text class="card-sub">{{ productSub(m) }}</text>
                <text class="card-hint">点击查看商品详情</text>
              </view>
            </view>
            <view v-else-if="m.messageType === 'QUOTE'" class="card" @tap="openQuote(m)">
              <view class="card-body">
                <text class="card-title">报价单</text>
                <text class="card-sub">{{ m.payload && m.payload.title }}</text>
                <text class="card-price">{{ quotePrice(m) }}</text>
                <text class="card-hint">状态：{{ quoteStatus(m) }}　点击查看</text>
              </view>
            </view>
            <view v-else-if="m.messageType === 'ORDER_CARD'" class="card" @tap="openOrder(m)">
              <view class="card-body">
                <text class="card-title">订单</text>
                <text class="card-sub">{{ m.payload && m.payload.orderNo }}</text>
                <text class="card-price">￥{{ m.payload && m.payload.totalAmount }}</text>
                <text class="card-hint">状态：{{ orderStatus(m) }}　点击查看</text>
              </view>
            </view>
            <text v-else class="bubble-text">{{ display(m) }}</text>
          </view>
          <view class="meta-row">
            <text class="msg-time">{{ fmtTime(m.createdAt) }}</text>
            <text v-if="mine(m)" class="msg-status" :class="{ failed: m._failed }">{{ statusOf(m) }}</text>
          </view>
        </view>
      </template>
    </scroll-view>

    <view v-if="typingPeer" class="typing-bar">
      <text class="typing-text">对方正在输入…</text>
    </view>

    <view v-if="replyTo" class="reply-preview">
      <text class="reply-preview-text">{{ snippet(replyTo) }}</text>
      <text class="reply-cancel" @tap="replyTo = null">×</text>
    </view>

    <view class="input-bar">
      <button class="btn-icon" @tap="openPlus">＋</button>
      <input
        class="msg-input"
        v-model="input"
        placeholder="输入消息…"
        confirm-type="send"
        :adjust-position="true"
        @input="onInput"
        @confirm="send"
      />
      <button class="btn btn-sm send-btn" :disabled="sending || !input.trim()" @tap="send">发送</button>
    </view>
  </view>
</template>

<script>
import { chatApi, uploadImageSmart, supplyApi, demandApi } from '../../api/index'
import { requireLogin, getUser } from '../../utils/request'
import { createChatSocket } from '../../utils/chat-socket'

const TIME_GAP = 5 * 60 * 1000
const RECALL_WINDOW = 2 * 60 * 1000

export default {
  data() {
    return {
      convId: 0,
      peer: {},
      myId: 0,
      messages: [],
      input: '',
      hasMore: false,
      nextCursor: null,
      scrollInto: '',
      sending: false,
      timer: null,
      socket: null,
      wsConnected: false,
      loading: false,
      replyTo: null,
      peerLastRead: 0,
      peerLastDelivered: 0,
      typingPeer: false,
      typingTimer: null,
      typingSentAt: 0,
      typingStopTimer: null,
      convSourceType: '',
      convSourceId: 0,
      convSource: null,
    }
  },
  onLoad(options) {
    if (!requireLogin()) return
    this.convId = Number(options.id || 0)
    const me = getUser()
    this.myId = me && me.id ? Number(me.id) : 0
    this.loadConv()
    this.loadLatest()
    this.startPoll()
    this.connectWs()
  },
  onShow() {
    if (this.convId) {
      this.loadLatest()
      if (!this.socket || !this.socket.isOpen()) this.connectWs()
      if (!this.wsConnected) this.startPoll()
    }
  },
  onHide() { this.stopPoll() },
  onUnload() {
    this.stopPoll()
    this.stopTyping()
    if (this.socket) { this.socket.close(); this.socket = null }
  },
  methods: {
    connectWs() {
      if (this.socket) { this.socket.close(); this.socket = null }
      this.socket = createChatSocket({
        onMessage: (d) => {
          if (Number(d.conversationId) !== this.convId || !d.message) return
          this.merge([d.message])
          this.scrollToBottom()
          this.markRead()
          this.markDelivered(d.message.id)
          if (Number(d.message.senderId) !== this.myId) this.clearTyping()
        },
        onRead: (d) => {
          if (Number(d.conversationId) !== this.convId) return
          this.peerLastRead = Number(d.lastReadMessageId || 0)
        },
        onDelivered: (d) => {
          if (Number(d.conversationId) !== this.convId) return
          this.peerLastDelivered = Number(d.lastDeliveredMessageId || 0)
        },
        onTyping: (d) => {
          if (Number(d.conversationId) !== this.convId) return
          if (d.typing) this.showTyping()
          else this.clearTyping()
        },
        onRecall: (d) => {
          if (Number(d.conversationId) !== this.convId) return
          this.applyRecall(Number(d.messageId))
        },
        onAck: (d) => {
          if (Number(d.conversationId) !== this.convId || !d.clientMsgId) return
          this.settlePending(d.clientMsgId, Number(d.messageId))
        },
        onStatus: (s) => {
          this.wsConnected = s === 'open'
          if (s === 'open') this.stopPoll()
          else this.startPoll()
        },
      })
    },
    async loadConv() {
      try {
        const c = await chatApi.conversation(this.convId)
        this.peer = c.peer || {}
        this.convSourceType = c.sourceType || ''
        this.convSourceId = c.sourceId || 0
        uni.setNavigationBarTitle({ title: this.peer.nickname || '聊天' })
        this.loadSource()
      } catch (e) {}
    },
    async loadSource() {
      const st = this.convSourceType
      const sid = Number(this.convSourceId)
      if (!sid) return
      try {
        if (st === 'SUPPLY' || st === 'PRODUCT') this.convSource = await supplyApi.detail(sid)
        else if (st === 'DEMAND') this.convSource = await demandApi.detail(sid)
      } catch (e) {}
    },
    async loadLatest() {
      if (this.loading) return
      this.loading = true
      try {
        const res = await chatApi.messages(this.convId, { limit: 20 })
        this.hasMore = res.hasMore
        this.nextCursor = res.nextCursor
        if (res.peerLastReadMessageId) this.peerLastRead = Number(res.peerLastReadMessageId)
        if (res.peerLastDeliveredMessageId) this.peerLastDelivered = Number(res.peerLastDeliveredMessageId)
        this.merge(res.items || [])
        this.scrollToBottom()
        this.markRead()
      } catch (e) {} finally { this.loading = false }
    },
    async loadOlder() {
      if (!this.hasMore || !this.nextCursor || this.loading) return
      this.loading = true
      try {
        const res = await chatApi.messages(this.convId, { beforeId: this.nextCursor, limit: 20 })
        this.hasMore = res.hasMore
        this.nextCursor = res.nextCursor
        this.merge(res.items || [])
      } catch (e) {} finally { this.loading = false }
    },
    merge(list) {
      const map = {}
      this.messages.concat(list).forEach((m) => { if (m && m.id != null) map[m.id] = m })
      this.messages = Object.keys(map).map((k) => map[k]).sort((a, b) => a.id - b.id)
    },
    async send() {
      const text = (this.input || '').trim()
      if (!text || this.sending) return
      this.sending = true
      const clientMsgId = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
      const temp = {
        id: -Date.now(),
        conversationId: this.convId,
        senderId: this.myId,
        messageType: 'TEXT',
        content: text,
        replyToMessageId: this.replyTo ? this.replyTo.id : null,
        status: 'SENT',
        createdAt: new Date().toISOString(),
        _pending: true,
        clientMsgId,
      }
      this.merge([temp])
      this.input = ''
      this.replyTo = null
      this.scrollToBottom()
      this.stopTyping()
      try {
        const msg = await chatApi.send(this.convId, {
          clientMsgId,
          messageType: 'TEXT',
          content: text,
          replyToMessageId: temp.replyToMessageId,
        })
        this.settlePending(clientMsgId, msg.id, msg)
      } catch (e) {
        temp._pending = false
        temp._failed = true
      } finally { this.sending = false }
    },
    settlePending(clientMsgId, messageId, full) {
      const idx = this.messages.findIndex((m) => m.clientMsgId === clientMsgId)
      if (idx === -1) return
      if (full) this.messages.splice(idx, 1, full)
      else {
        const m = this.messages[idx]
        m.id = messageId
        m._pending = false
      }
      this.messages = this.messages.slice()
    },
    async pickImage() {
      uni.chooseImage({
        count: 1,
        success: async (res) => {
          const path = res.tempFilePaths && res.tempFilePaths[0]
          if (!path) return
          const size = res.tempFiles && res.tempFiles[0] ? res.tempFiles[0].size : 0
          uni.showLoading({ title: '上传中' })
          try {
            const up = await uploadImageSmart(path, size)
            const url = up && up.url
            if (!url) throw new Error('no url')
            const clientMsgId = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
            const msg = await chatApi.send(this.convId, {
              clientMsgId,
              messageType: 'IMAGE',
              payload: { url },
              replyToMessageId: this.replyTo ? this.replyTo.id : null,
            })
            this.merge([msg])
            this.replyTo = null
            this.scrollToBottom()
          } catch (e) {
            uni.showToast({ title: '图片发送失败', icon: 'none' })
          } finally { uni.hideLoading() }
        },
      })
    },
    openMenu(m) {
      if (m._pending) return
      const items = []
      const actions = []
      if (m.messageType === 'TEXT' && m.content) { items.push('复制'); actions.push('copy') }
      if (m.status !== 'RECALLED') { items.push('引用'); actions.push('quote') }
      if (this.mine(m) && m.status !== 'RECALLED' && this.canRecall(m)) { items.push('撤回'); actions.push('recall') }
      items.push('拉黑对方'); actions.push('block')
      if (!items.length) return
      uni.showActionSheet({
        itemList: items,
        success: (res) => {
          const act = actions[res.tapIndex]
          if (act === 'copy') uni.setClipboardData({ data: m.content })
          else if (act === 'quote') { this.replyTo = m }
          else if (act === 'recall') this.recall(m)
          else if (act === 'block') this.blockPeer()
        },
      })
    },
    async blockPeer() {
      const peerId = this.peer && this.peer.id
      if (!peerId) return
      const res = await new Promise((resolve) => {
        uni.showModal({
          title: '拉黑对方',
          content: '拉黑后双方将无法互发消息，确定继续？',
          success: (r) => resolve(r.confirm),
        })
      })
      if (!res) return
      try {
        await chatApi.addBlock(peerId)
        uni.showToast({ title: '已拉黑', icon: 'none' })
      } catch (e) {}
    },
    isCard(m) {
      return ['PRODUCT_CARD', 'QUOTE', 'ORDER_CARD'].includes(m.messageType)
    },
    productSub(m) {
      const p = m.payload || {}
      const parts = []
      if (p.spec) parts.push(p.spec)
      if (p.price != null) parts.push(`参考价 ${p.price} ${p.priceUnit || ''}`)
      else if (p.priceMin != null || p.priceMax != null) parts.push(`预算 ${p.priceMin || ''}~${p.priceMax || ''}`)
      return parts.join('　')
    },
    quotePrice(m) {
      const p = m.payload || {}
      return `${p.price} ${p.priceUnit || ''} × ${p.quantity}`
    },
    quoteStatus(m) {
      const p = m.payload || {}
      const map = { PENDING: '待回复', NEGOTIATED: '已还价', ACCEPTED: '已接受', REJECTED: '已拒绝', DEALED: '已成交' }
      return map[p.status] || p.status || ''
    },
    orderStatus(m) {
      const p = m.payload || {}
      const map = { PENDING_CONFIRM: '待确认', TO_PAY: '待付款', TO_SHIP: '待发货', SHIPPING: '运输中', COMPLETED: '已完成', CANCELLED: '已取消', AFTER_SALE: '售后中' }
      return map[p.status] || p.status || ''
    },
    openCard(m) {
      const p = m.payload || {}
      if (p.kind === 'SUPPLY') uni.navigateTo({ url: '/pages/supply/detail?id=' + p.id })
      else if (p.kind === 'DEMAND') uni.navigateTo({ url: '/pages/demand/detail?id=' + p.id })
    },
    openQuote(m) {
      const p = m.payload || {}
      const items = ['查看报价详情']
      const actions = ['view']
      const isPend = p.status === 'PENDING'
      const isInitiator = Number(p.initiatorId) === this.myId
      if (isPend && !isInitiator) {
        items.push('接受报价', '还价', '拒绝报价')
        actions.push('accept', 'counter', 'reject')
      }
      if (p.status === 'ACCEPTED' && p.buyerId === this.myId) {
        items.push('去下单')
        actions.push('order')
      }
      uni.showActionSheet({
        itemList: items,
        success: (r) => {
          const act = actions[r.tapIndex]
          if (act === 'accept') this.actQuote(m, 'accept')
          else if (act === 'reject') this.actQuote(m, 'reject')
          else if (act === 'counter') this.counterQuote(m)
          else if (act === 'order') uni.navigateTo({ url: '/pages/quote/list' })
          else uni.navigateTo({ url: '/pages/quote/list' })
        },
      })
    },
    async actQuote(m, action) {
      const p = m.payload || {}
      if (!p.quoteId) return
      const label = action === 'accept' ? '接受报价' : '拒绝报价'
      const confirmRes = await new Promise((resolve) => {
        uni.showModal({
          title: label,
          content: `确认${label}？`,
          success: (r) => resolve(r.confirm),
        })
      })
      if (!confirmRes) return
      this.submitTrade(action === 'accept' ? '已接受报价' : '已拒绝报价',
        () => (action === 'accept'
          ? chatApi.acceptQuote(this.convId, p.quoteId)
          : chatApi.rejectQuote(this.convId, p.quoteId)))
    },
    counterQuote(m) {
      const p = m.payload || {}
      if (!p.quoteId) return
      this.promptNumber('还价单价（' + (p.priceUnit || '元/斤') + '）', String(p.price || ''), (price) => {
        this.promptNumber('数量', String(p.quantity || ''), (quantity) => {
          this.submitTrade('已还价', () => chatApi.counterQuote(this.convId, p.quoteId, {
            price: Number(price), quantity: Number(quantity),
          }))
        })
      })
    },
    openOrder(m) {
      const p = m.payload || {}
      uni.navigateTo({ url: '/pages/order/detail?id=' + p.orderId })
    },
    openPlus() {
      const items = ['发送图片']
      const actions = ['image']
      const st = this.convSourceType
      const sid = this.convSourceId
      if ((st === 'SUPPLY' || st === 'PRODUCT') && sid) {
        items.push('分享该商品', '发起报价', '立即下单')
        actions.push('share', 'quote', 'order')
      } else if (st === 'DEMAND' && sid) {
        items.push('分享该需求', '发起报价')
        actions.push('share', 'quote')
      }
      uni.showActionSheet({
        itemList: items,
        success: (r) => {
          const act = actions[r.tapIndex]
          if (act === 'image') this.pickImage()
          else if (act === 'share') this.shareCurrent()
          else if (act === 'quote') this.startQuote()
          else if (act === 'order') this.startOrder()
        },
      })
    },
    async shareCurrent() {
      const st = this.convSourceType
      const sid = Number(this.convSourceId)
      if (!sid) return
      try {
        const data = (st === 'DEMAND') ? { demandId: sid } : { supplyId: sid }
        const msg = await chatApi.shareProduct(this.convId, data)
        this.merge([msg])
        this.scrollToBottom()
      } catch (e) {}
    },
    startQuote() {
      const st = this.convSourceType
      const sid = Number(this.convSourceId)
      if (!sid) return
      const src = this.convSource || {}
      const priceUnit = src.priceUnit || '元/斤'
      const defPrice = src.price != null ? String(src.price) : ''
      this.promptNumber('报价单价（' + priceUnit + '）', defPrice, (price) => {
        this.promptNumber('报价数量（' + (src.unit || '') + '）', src.minOrderQty != null ? String(src.minOrderQty) : '', (quantity) => {
          const data = { price: Number(price), priceUnit, quantity: Number(quantity) }
          if (st === 'DEMAND') data.demandId = sid
          else data.supplyId = sid
          this.submitTrade('报价已发送', () => chatApi.createQuote(this.convId, data))
        })
      })
    },
    startOrder() {
      const st = this.convSourceType
      const sid = Number(this.convSourceId)
      if (!sid || st === 'DEMAND') return
      const src = this.convSource || {}
      const priceUnit = src.priceUnit || '元/斤'
      this.promptNumber('下单数量（' + (src.unit || '') + '）', src.minOrderQty != null ? String(src.minOrderQty) : '', (quantity) => {
        this.promptText('交付地址', '请输入提货/收货地址', '', (address) => {
          const data = {
            supplyId: sid,
            price: Number(src.price || 0),
            priceUnit,
            quantity: Number(quantity),
            deliveryMode: 'LOGISTICS',
            addressSnapshot: address,
          }
          this.submitTrade('订单已创建', () => chatApi.createOrder(this.convId, data))
        })
      })
    },
    promptNumber(title, def, cb) {
      uni.showModal({
        title,
        editable: true,
        placeholderText: def ? `默认 ${def}` : '请输入',
        content: def || '',
        success: (r) => {
          if (!r.confirm) return
          const v = (r.content || '').trim() || def
          const n = Number(v)
          if (!v || isNaN(n) || n <= 0) { uni.showToast({ title: '请输入有效数字', icon: 'none' }); return }
          cb(v)
        },
      })
    },
    promptText(title, placeholder, def, cb) {
      uni.showModal({
        title,
        editable: true,
        placeholderText: placeholder,
        content: def || '',
        success: (r) => { if (r.confirm) cb((r.content || '').trim()) },
      })
    },
    async submitTrade(okMsg, fn) {
      uni.showLoading({ title: '处理中' })
      try {
        await fn()
        uni.showToast({ title: okMsg, icon: 'none' })
        await this.loadLatest()
        this.scrollToBottom()
      } catch (e) {
        uni.showToast({ title: (e && e.msg) || '操作失败', icon: 'none' })
      } finally { uni.hideLoading() }
    },
    canRecall(m) {
      const t = new Date(m.createdAt).getTime()
      return !!t && Date.now() - t <= RECALL_WINDOW
    },
    async recall(m) {
      try {
        await chatApi.recall(m.id)
        this.applyRecall(m.id)
      } catch (e) {}
    },
    applyRecall(messageId) {
      const m = this.messages.find((x) => x.id === messageId)
      if (!m) return
      m.status = 'RECALLED'
      m.content = null
      m.payload = null
      m.recalledAt = new Date().toISOString()
      this.messages = this.messages.slice()
    },
    onInput(e) {
      const val = e && e.detail ? e.detail.value : this.input
      if (val && val.trim()) this.sendTyping()
    },
    sendTyping() {
      const now = Date.now()
      if (now - this.typingSentAt < 2500) {
        this.scheduleTypingStop()
        return
      }
      this.typingSentAt = now
      if (this.socket && this.socket.isOpen()) this.socket.send({ type: 'typing', conversationId: this.convId, typing: true })
      else chatApi.typing(this.convId, true).catch(() => {})
      this.scheduleTypingStop()
    },
    scheduleTypingStop() {
      if (this.typingStopTimer) clearTimeout(this.typingStopTimer)
      this.typingStopTimer = setTimeout(() => this.stopTyping(), 3000)
    },
    stopTyping() {
      if (this.typingStopTimer) { clearTimeout(this.typingStopTimer); this.typingStopTimer = null }
      if (!this.typingSentAt) return
      this.typingSentAt = 0
      if (this.socket && this.socket.isOpen()) this.socket.send({ type: 'typing', conversationId: this.convId, typing: false })
      else chatApi.typing(this.convId, false).catch(() => {})
    },
    showTyping() {
      this.typingPeer = true
      if (this.typingTimer) clearTimeout(this.typingTimer)
      this.typingTimer = setTimeout(() => { this.typingPeer = false }, 4000)
    },
    clearTyping() {
      this.typingPeer = false
      if (this.typingTimer) { clearTimeout(this.typingTimer); this.typingTimer = null }
    },
    markRead() {
      const last = this.messages.filter((m) => m.id > 0).pop()
      if (!last) return
      chatApi.markRead(this.convId, last.id).catch(() => {})
    },
    markDelivered(messageId) {
      if (!messageId || messageId <= 0) return
      if (this.socket && this.socket.isOpen()) {
        this.socket.send({ type: 'delivered', conversationId: this.convId, lastDeliveredMessageId: messageId })
      } else {
        chatApi.markDelivered(this.convId, messageId).catch(() => {})
      }
    },
    scrollToBottom() {
      const last = this.messages[this.messages.length - 1]
      if (!last) return
      this.$nextTick(() => { this.scrollInto = 'msg-' + last.id })
    },
    startPoll() {
      this.stopPoll()
      this.timer = setInterval(async () => {
        try {
          const res = await chatApi.messages(this.convId, { limit: 20 })
          if (res.peerLastReadMessageId) this.peerLastRead = Number(res.peerLastReadMessageId)
          if (res.peerLastDeliveredMessageId) this.peerLastDelivered = Number(res.peerLastDeliveredMessageId)
          const fresh = (res.items || []).filter((m) => !this.messages.some((x) => x.id === m.id))
          if (fresh.length) {
            this.merge(fresh)
            this.scrollToBottom()
            this.markRead()
          }
        } catch (e) {}
      }, 4000)
    },
    stopPoll() {
      if (this.timer) { clearInterval(this.timer); this.timer = null }
    },
    mine(m) { return Number(m.senderId) === this.myId },
    quoted(m) {
      if (!m.replyToMessageId) return null
      return this.messages.find((x) => x.id === m.replyToMessageId) || null
    },
    snippet(m) {
      if (!m) return '原消息'
      if (m.status === 'RECALLED') return '消息已撤回'
      if (m.messageType === 'IMAGE') return '[图片]'
      if (m.messageType && m.messageType !== 'TEXT') return this.typeLabel(m.messageType)
      const c = m.content || ''
      return c.length > 30 ? c.slice(0, 30) + '…' : c
    },
    display(m) {
      if (m.messageType && m.messageType !== 'TEXT') return this.typeLabel(m.messageType)
      return m.content || ''
    },
    typeLabel(type) {
      const map = { IMAGE: '[图片]', FILE: '[文件]', PRODUCT_CARD: '[商品信息]', INQUIRY: '[询价]', QUOTE: '[报价]', ORDER_CARD: '[订单]', SYSTEM: '[系统消息]' }
      return map[type] || '[消息]'
    },
    imgUrl(m) {
      if (!m.payload) return ''
      return m.payload.url || m.payload.thumbUrl || ''
    },
    previewImg(m) {
      const url = this.imgUrl(m)
      if (url) uni.previewImage({ urls: [url], current: url })
    },
    recallText(m) {
      return this.mine(m) ? '你撤回了一条消息' : '对方撤回了一条消息'
    },
    statusOf(m) {
      if (m.status === 'RECALLED') return ''
      if (m._failed) return '发送失败'
      if (m._pending) return '发送中'
      if (m.id <= 0) return ''
      if (this.peerLastRead && m.id <= this.peerLastRead) return '已读'
      if (this.peerLastDelivered && m.id <= this.peerLastDelivered) return '已送达'
      return '已发送'
    },
    showTime(i) {
      if (i === 0) return true
      const cur = new Date(this.messages[i].createdAt).getTime()
      const prev = new Date(this.messages[i - 1].createdAt).getTime()
      return cur - prev > TIME_GAP
    },
    timeLabel(t) {
      const d = new Date(t)
      if (isNaN(d.getTime())) return ''
      const now = new Date()
      const hm = `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
      const sameDay = d.toDateString() === now.toDateString()
      if (sameDay) return hm
      const yesterday = new Date(now.getTime() - 86400000)
      if (d.toDateString() === yesterday.toDateString()) return `昨天 ${hm}`
      return `${d.getMonth() + 1}月${d.getDate()}日 ${hm}`
    },
    fmtTime(t) {
      if (!t) return ''
      const d = new Date(t)
      if (isNaN(d.getTime())) return String(t).replace('T', ' ').slice(11, 16)
      return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
    },
  },
}
</script>

<style>
.chat-page { display: flex; flex-direction: column; height: 100vh; padding-bottom: 0; }
.msg-scroll { flex: 1; padding: 24rpx; }
.load-more { text-align: center; font-size: 24rpx; color: #2e8b57; padding: 16rpx 0; }
.time-divider { text-align: center; font-size: 22rpx; color: #a7b3ac; margin: 16rpx 0 24rpx; }
.recall-row { text-align: center; margin: 16rpx 0; }
.recall-text { font-size: 23rpx; color: #a7b3ac; background: #eef1ee; border-radius: 999rpx; padding: 6rpx 22rpx; }
.msg-row { display: flex; flex-direction: column; margin-bottom: 28rpx; align-items: flex-start; }
.msg-row.mine { align-items: flex-end; }
.bubble {
  max-width: 76%; padding: 18rpx 24rpx; border-radius: 18rpx;
  background: #ffffff; box-shadow: 0 2rpx 8rpx rgba(46, 139, 87, 0.06);
}
.bubble-mine { background: #2e8b57; }
.bubble-text { font-size: 29rpx; line-height: 1.5; word-break: break-all; }
.bubble-mine .bubble-text { color: #fff; }
.msg-img { max-width: 360rpx; border-radius: 12rpx; }
.quote {
  border-left: 4rpx solid #b6c9bd; padding-left: 14rpx; margin-bottom: 12rpx;
  background: rgba(0, 0, 0, 0.03); border-radius: 6rpx; padding-top: 6rpx; padding-bottom: 6rpx;
}
.bubble-mine .quote { border-left-color: #bfe3cd; background: rgba(255, 255, 255, 0.16); }
.quote-text { font-size: 23rpx; color: #8a978e; }
.bubble-mine .quote-text { color: #d6efe0; }
.meta-row { display: flex; align-items: center; margin-top: 8rpx; }
.msg-time { font-size: 20rpx; color: #a7b3ac; }
.msg-status { font-size: 20rpx; color: #a7b3ac; margin-left: 12rpx; }
.msg-status.failed { color: #e05a4f; }
.typing-bar { padding: 0 28rpx 8rpx; }
.typing-text { font-size: 22rpx; color: #8a978e; }
.reply-preview {
  display: flex; align-items: center; justify-content: space-between;
  padding: 12rpx 24rpx; background: #f4f6f4; border-top: 1rpx solid #e3e9e4;
}
.reply-preview-text { font-size: 24rpx; color: #6b7a70; flex: 1; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
.reply-cancel { font-size: 40rpx; color: #a7b3ac; padding: 0 12rpx; line-height: 1; }
.input-bar {
  display: flex; align-items: center; padding: 16rpx 24rpx;
  background: #fff; border-top: 1rpx solid #e3e9e4;
  padding-bottom: calc(16rpx + env(safe-area-inset-bottom));
}
.btn-icon {
  width: 68rpx; height: 68rpx; line-height: 64rpx; text-align: center;
  border-radius: 50%; background: #f4f6f4; color: #2e8b57; font-size: 40rpx;
  padding: 0; margin-right: 16rpx;
}
.btn-icon::after { border: none; }
.msg-input {
  flex: 1; background: #f4f6f4; border-radius: 999rpx;
  padding: 18rpx 28rpx; font-size: 28rpx;
}
.send-btn { margin-left: 20rpx; }
.bubble-card { padding: 0; overflow: hidden; background: #fff; }
.card { width: 460rpx; }
.card-img { width: 460rpx; height: 260rpx; display: block; }
.card-body { padding: 18rpx 22rpx; }
.card-title { font-size: 28rpx; font-weight: 600; color: #22322a; display: block; }
.card-sub { font-size: 23rpx; color: #7a8a80; margin-top: 8rpx; display: block; }
.card-price { font-size: 30rpx; color: #f2952b; font-weight: 600; margin-top: 8rpx; display: block; }
.card-hint { font-size: 21rpx; color: #a7b3ac; margin-top: 10rpx; display: block; }
</style>
