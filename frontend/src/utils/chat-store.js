// 全局聊天状态: 常驻 WebSocket + 未读角标 + 跨页事件分发
// 由 App.vue 在启动/登录后初始化; 会话列表页与会话页订阅事件, 不再各自建连。
import { reactive } from 'vue'
import { createChatSocket } from './chat-socket'
import { getToken, getUser } from './request'
import { chatApi } from '../api/index'

// pages.json tabBar 中"消息"的索引
const CHAT_TAB_INDEX = 2

export const chatState = reactive({
  connected: false,
  totalUnread: 0,
  // 当前正在查看的会话: 在此期间收到该会话消息不计未读、不弹提醒
  activeConversationId: 0,
  // convId -> { unreadCount, isPinned, isMuted, peerName, lastContent, lastMessageType, lastMessageAt }
  conversations: {},
})

// ---- 事件总线 ----
const listeners = {}
export function onChatEvent(type, fn) {
  (listeners[type] || (listeners[type] = [])).push(fn)
  return () => { listeners[type] = (listeners[type] || []).filter((f) => f !== fn) }
}
function emit(type, payload) {
  (listeners[type] || []).slice().forEach((fn) => { try { fn(payload) } catch (e) {} })
}

let socket = null
let connecting = false

function myId() {
  const me = getUser()
  return me && me.id ? Number(me.id) : 0
}

function setBadge(total) {
  const n = Number(total) || 0
  if (n > 0) {
    uni.setTabBarBadge({ index: CHAT_TAB_INDEX, text: n > 99 ? '99+' : String(n), fail: () => {} })
  } else {
    uni.removeTabBarBadge({ index: CHAT_TAB_INDEX, fail: () => {} })
  }
}

function convOf(convId) {
  const id = Number(convId) || 0
  if (!id) return null
  if (!chatState.conversations[id]) chatState.conversations[id] = { unreadCount: 0 }
  return chatState.conversations[id]
}

function bumpUnread(convId, delta) {
  const conv = convOf(convId)
  if (!conv) return
  conv.unreadCount = Math.max(0, (Number(conv.unreadCount) || 0) + delta)
  chatState.totalUnread = Math.max(0, chatState.totalUnread + delta)
  setBadge(chatState.totalUnread)
}

/** 从服务端拉取未读总数(权威值)并刷新角标。 */
export async function refreshUnread() {
  if (!getToken()) { chatState.totalUnread = 0; setBadge(0); return }
  try {
    const n = Number(await chatApi.unread()) || 0
    chatState.totalUnread = n
    setBadge(n)
  } catch (e) {}
}

/** 会话列表加载后回写元数据(未读/置顶/免打扰/昵称), 供全局提示与角标使用。 */
export function setConversations(list) {
  (list || []).forEach((c) => {
    const conv = convOf(c.id)
    if (!conv) return
    conv.unreadCount = Number(c.unreadCount) || 0
    conv.isPinned = !!c.isPinned
    conv.isMuted = !!c.isMuted
    if (c.peer) conv.peerName = c.peer.nickname
    conv.lastContent = c.lastContent
    conv.lastMessageType = c.lastMessageType
    conv.lastMessageAt = c.lastMessageAt
  })
}

/** 会话设置变更后同步本地(置顶/免打扰)。 */
export function patchConversation(convId, patch) {
  const conv = convOf(convId)
  if (conv) Object.assign(conv, patch)
}

/**
 * 设置/清除当前正在查看的会话。进入时清零该会话未读并扣减总角标;
 * 在此期间收到的消息不计未读、不弹跨页提醒(页面自身会 markRead)。
 */
export function setActiveConversation(convId) {
  const id = Number(convId) || 0
  chatState.activeConversationId = id
  if (!id) return
  const conv = chatState.conversations[id]
  if (conv && conv.unreadCount) {
    chatState.totalUnread = Math.max(0, chatState.totalUnread - conv.unreadCount)
    conv.unreadCount = 0
    setBadge(chatState.totalUnread)
  }
}

function notifyIncoming(d) {
  const conv = chatState.conversations[Number(d.conversationId)]
  if (conv && conv.isMuted) return // 免打扰: 不弹提醒
  const name = (conv && conv.peerName) || '对方'
  uni.showToast({ title: `${name} 发来消息`, icon: 'none', duration: 1500 })
  // #ifndef H5
  if (uni.vibrateShort) uni.vibrateShort({ fail: () => {} })
  // #endif
}

function handleIncoming(d) {
  const convId = Number(d.conversationId || (d.message && d.message.conversationId) || 0)
  const msg = d.message || null
  const fromMe = msg ? Number(msg.senderId) === myId() : false
  const conv = convId ? convOf(convId) : null
  if (conv && msg) {
    conv.lastContent = msg.content
    conv.lastMessageType = msg.messageType
    conv.lastMessageAt = msg.createdAt
  }
  const isActive = convId && convId === chatState.activeConversationId
  if (!fromMe && !isActive) {
    bumpUnread(convId, 1)
    emit('notify', d)
    notifyIncoming(d)
  }
  emit('message', d)
}

/** 已登录则建立常驻连接(幂等)。 */
export function initChat() {
  if (!getToken()) { teardownChat(); return }
  if (socket || connecting) return
  connecting = true
  socket = createChatSocket({
    onMessage: handleIncoming,
    onRead: (d) => emit('read', d),
    onDelivered: (d) => emit('delivered', d),
    onTyping: (d) => emit('typing', d),
    onRecall: (d) => {
      const conv = chatState.conversations[Number(d.conversationId)]
      if (conv) conv.lastContent = '[已撤回]'
      emit('recall', d)
    },
    onAck: (d) => emit('ack', d),
    onStatus: (s) => {
      chatState.connected = s === 'open'
      emit('status', s)
      if (s === 'open') refreshUnread()
    },
  })
  connecting = false
  if (socket) refreshUnread()
}

/** 退出登录/失效时销毁连接与状态。 */
export function teardownChat() {
  if (socket) { socket.close(); socket = null }
  chatState.connected = false
  chatState.activeConversationId = 0
  chatState.totalUnread = 0
  chatState.conversations = {}
  setBadge(0)
}

/** 供会话页复用同一连接发送 typing/delivered。 */
export function getSocket() {
  return socket
}
