// 站内聊天 WebSocket 客户端封装 (uni.connectSocket, H5 与小程序通用)
// 协议见后端 ChatRealtimeHub: connected / pong / message / read / ack / error
import { getToken, BASE_URL } from './request'

function wsBase() {
  // #ifdef H5
  // H5 走 vite 代理(/ws -> 后端), 使用当前页面主机
  const proto = location.protocol === 'https:' ? 'wss' : 'ws'
  return `${proto}://${location.host}`
  // #endif
  // #ifndef H5
  // 小程序等平台无代理, 复用 request.js 的 BASE_URL 推导 (http->ws, https->wss)
  return BASE_URL.replace(/^http/, 'ws')
  // #endif
}

/**
 * 创建聊天连接。返回控制器 { isOpen, send, close } 或 null(未登录)。
 * @param {{onMessage?:Function, onRead?:Function, onDelivered?:Function, onTyping?:Function, onRecall?:Function, onAck?:Function, onStatus?:(s:'open'|'closed')=>void}} handlers
 */
export function createChatSocket(handlers = {}) {
  const token = getToken()
  if (!token) return null

  let task = null
  let opened = false
  let closed = false
  let retry = 0
  let heartbeat = null

  const stopHeartbeat = () => { if (heartbeat) { clearInterval(heartbeat); heartbeat = null } }
  const startHeartbeat = () => {
    stopHeartbeat()
    heartbeat = setInterval(() => {
      if (opened) { try { task.send({ data: JSON.stringify({ type: 'ping' }) }) } catch (e) {} }
    }, 25000)
  }

  const scheduleRetry = () => {
    if (closed || retry >= 5) return
    retry += 1
    setTimeout(connect, Math.min(1000 * retry, 8000))
  }

  function connect() {
    if (closed) return
    task = uni.connectSocket({
      url: `${wsBase()}/ws/chat?token=${encodeURIComponent(token)}`,
      complete: () => {},
    })
    task.onOpen(() => {
      opened = true
      retry = 0
      startHeartbeat()
      handlers.onStatus && handlers.onStatus('open')
    })
    task.onMessage((res) => {
      let data
      try { data = JSON.parse(res.data) } catch (e) { return }
      if (data.type === 'message') handlers.onMessage && handlers.onMessage(data)
      else if (data.type === 'read') handlers.onRead && handlers.onRead(data)
      else if (data.type === 'delivered') handlers.onDelivered && handlers.onDelivered(data)
      else if (data.type === 'typing') handlers.onTyping && handlers.onTyping(data)
      else if (data.type === 'recall') handlers.onRecall && handlers.onRecall(data)
      else if (data.type === 'ack') handlers.onAck && handlers.onAck(data)
      else if (data.type === 'error') uni.showToast({ title: data.msg || '发送失败', icon: 'none' })
    })
    task.onClose(() => {
      opened = false
      stopHeartbeat()
      handlers.onStatus && handlers.onStatus('closed')
      scheduleRetry()
    })
    task.onError(() => {
      opened = false
      stopHeartbeat()
      scheduleRetry()
    })
  }

  connect()

  return {
    isOpen: () => opened,
    send(obj) {
      if (!opened) return false
      try { task.send({ data: JSON.stringify(obj) }); return true } catch (e) { return false }
    },
    close() {
      closed = true
      stopHeartbeat()
      try { task.close({}) } catch (e) {}
    },
  }
}
