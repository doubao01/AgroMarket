import { http } from '../utils/request'

export const authApi = {
  sendSms: (phone) => http.post('/api/auth/sms/send', { phone }),
  loginByCode: (phone, code) => http.post('/api/auth/login/code', { phone, code }),
  loginByPassword: (phone, password) => http.post('/api/auth/login/password', { phone, password }),
  register: (data) => http.post('/api/auth/register', data),
}

export const userApi = {
  me: () => http.get('/api/user/me', {}, { auth: true }),
  updateProfile: (data) => http.put('/api/user/profile', data, { auth: true }),
  changePassword: (oldPassword, newPassword) => http.post('/api/user/password', { oldPassword, newPassword }, { auth: true }),
  getMerchantAuth: () => http.get('/api/user/auth/merchant', {}, { auth: true }),
  submitMerchantAuth: (data) => http.post('/api/user/auth/merchant', data, { auth: true }),
}

export const regionApi = {
  children: (parentId) => http.get('/api/regions/children', parentId ? { parentId } : {}),
}

export const categoryApi = {
  all: () => http.get('/api/categories'),
}

export const supplyApi = {
  list: (query) => http.get('/api/supplies', query),
  detail: (id) => http.get('/api/supplies/' + id),
  create: (data) => http.post('/api/supplies', data, { auth: true }),
  update: (id, data) => http.put('/api/supplies/' + id, data, { auth: true }),
  changeStatus: (id, op) => http.post(`/api/supplies/${id}/status/${op}`, {}, { auth: true }),
}

export const demandApi = {
  list: (query) => http.get('/api/demands', query),
  detail: (id) => http.get('/api/demands/' + id),
  create: (data) => http.post('/api/demands', data, { auth: true }),
  update: (id, data) => http.put('/api/demands/' + id, data, { auth: true }),
  changeStatus: (id, op) => http.post(`/api/demands/${id}/status/${op}`, {}, { auth: true }),
}

export const quoteApi = {
  create: (data) => http.post('/api/quotes', data, { auth: true }),
  list: (query) => http.get('/api/quotes', query, { auth: true }),
  detail: (id) => http.get('/api/quotes/' + id, {}, { auth: true }),
  counter: (id, data) => http.post(`/api/quotes/${id}/counter`, data, { auth: true }),
  accept: (id) => http.post(`/api/quotes/${id}/accept`, {}, { auth: true }),
  reject: (id) => http.post(`/api/quotes/${id}/reject`, {}, { auth: true }),
}

export const orderApi = {
  createFromQuote: (quoteId, data) => http.post(`/api/orders/from-quote/${quoteId}`, data || {}, { auth: true }),
  list: (query) => http.get('/api/orders', query, { auth: true }),
  detail: (id) => http.get('/api/orders/' + id, {}, { auth: true }),
  changeStatus: (id, operation, data) => http.post(`/api/orders/${id}/status/${operation}`, data || {}, { auth: true }),
}

export const paymentApi = {
  channels: () => http.get('/api/payments/channels', {}, { auth: true }),
  create: (orderId, channel) => http.post('/api/payments', { orderId, channel }, { auth: true }),
  byOrder: (orderId) => http.get('/api/payments/order/' + orderId, {}, { auth: true }),
}

export const favoriteApi = {
  list: (query) => http.get('/api/favorites', query || {}, { auth: true }),
  add: (targetType, targetId) => http.post(`/api/favorites/${targetType}/${targetId}`, {}, { auth: true }),
  remove: (targetType, targetId) => http.del(`/api/favorites/${targetType}/${targetId}`, {}, { auth: true }),
  check: (targetType, targetId) => http.get('/api/favorites/check', { targetType, targetId }, { auth: true }),
}

export const messageApi = {
  list: (query) => http.get('/api/messages', query || {}, { auth: true }),
  read: (id) => http.post(`/api/messages/${id}/read`, {}, { auth: true }),
  readAll: () => http.post('/api/messages/read-all', {}, { auth: true }),
  unreadCount: () => http.get('/api/messages/unread-count', {}, { auth: true, showError: false }),
}

export const chatApi = {
  createConversation: (data) => http.post('/api/chat/conversations', data, { auth: true }),
  conversations: (query) => http.get('/api/chat/conversations', query || {}, { auth: true }),
  conversation: (id) => http.get('/api/chat/conversations/' + id, {}, { auth: true }),
  updateSettings: (id, data) => http.put(`/api/chat/conversations/${id}/settings`, data, { auth: true }),
  removeConversation: (id) => http.del('/api/chat/conversations/' + id, {}, { auth: true }),
  messages: (id, query) => http.get(`/api/chat/conversations/${id}/messages`, query || {}, { auth: true }),
  send: (id, data) => http.post(`/api/chat/conversations/${id}/messages`, data, { auth: true }),
  markRead: (id, lastReadMessageId) => http.post(`/api/chat/conversations/${id}/read`, { lastReadMessageId }, { auth: true }),
  markDelivered: (id, lastDeliveredMessageId) => http.post(`/api/chat/conversations/${id}/delivered`, { lastDeliveredMessageId }, { auth: true, showError: false }),
  typing: (id, typing) => http.post(`/api/chat/conversations/${id}/typing`, { typing }, { auth: true, showError: false }),
  recall: (messageId) => http.post(`/api/chat/messages/${messageId}/recall`, {}, { auth: true }),
  shareProduct: (id, data) => http.post(`/api/chat/conversations/${id}/share-product`, data, { auth: true }),
  createQuote: (id, data) => http.post(`/api/chat/conversations/${id}/quote`, data, { auth: true }),
  createOrder: (id, data) => http.post(`/api/chat/conversations/${id}/order`, data, { auth: true }),
  acceptQuote: (id, quoteId) => http.post(`/api/chat/conversations/${id}/quotes/${quoteId}/accept`, {}, { auth: true }),
  rejectQuote: (id, quoteId) => http.post(`/api/chat/conversations/${id}/quotes/${quoteId}/reject`, {}, { auth: true }),
  counterQuote: (id, quoteId, data) => http.post(`/api/chat/conversations/${id}/quotes/${quoteId}/counter`, data, { auth: true }),
  blocks: () => http.get('/api/chat/blocks', {}, { auth: true }),
  addBlock: (userId) => http.post('/api/chat/blocks', { userId }, { auth: true }),
  removeBlock: (userId) => http.del('/api/chat/blocks/' + userId, {}, { auth: true }),
  unread: () => http.get('/api/chat/unread', {}, { auth: true, showError: false }),
  search: (keyword, conversationId, limit) => http.get('/api/chat/search', { keyword, conversationId, limit }, { auth: true }),
}

export const addressApi = {
  list: () => http.get('/api/addresses', {}, { auth: true }),
  create: (data) => http.post('/api/addresses', data, { auth: true }),
  remove: (id) => http.del('/api/addresses/' + id, {}, { auth: true }),
}

export const reportApi = {
  create: (data) => http.post('/api/reports', data, { auth: true }),
}

export const noticeApi = {
  list: () => http.get('/api/notices'),
}

// 焦点图 (首页轮播)
export const bannerApi = {
  list: () => http.get('/api/banners'),
}

// 社区 (发帖/评论/点赞)
export const communityApi = {
  posts: (query) => http.get('/api/community/posts', query || {}),
  post: (id) => http.get('/api/community/posts/' + id),
  createPost: (data) => http.post('/api/community/posts', data, { auth: true }),
  deletePost: (id) => http.del('/api/community/posts/' + id, {}, { auth: true }),
  addComment: (postId, data) => http.post(`/api/community/posts/${postId}/comments`, data, { auth: true }),
  toggleLike: (postId) => http.post(`/api/community/posts/${postId}/like`, {}, { auth: true }),
}

// 社区/焦点图管理
export const communityAdminApi = {
  saveBanner: (data) => http.post('/api/admin/community/banners', data, { auth: true }),
  deleteBanner: (id) => http.del('/api/admin/community/banners/' + id, {}, { auth: true }),
  posts: (query) => http.get('/api/admin/community/posts', query || {}, { auth: true }),
  setPostStatus: (id, status) => http.post(`/api/admin/community/posts/${id}/status/${status}`, {}, { auth: true }),
}

export const adminApi = {
  stats: () => http.get('/api/admin/stats', {}, { auth: true }),
  users: (query) => http.get('/api/admin/users', query || {}, { auth: true }),
  freeze: (id, freeze) => http.post(`/api/admin/users/${id}/freeze/${freeze}`, {}, { auth: true }),
  auths: (query) => http.get('/api/admin/auths', query || {}, { auth: true }),
  auditAuth: (id, approve) => http.post(`/api/admin/auths/${id}/audit/${approve}`, {}, { auth: true }),
  supplies: (query) => http.get('/api/admin/supplies', query || {}, { auth: true }),
  auditSupply: (id, approve) => http.post(`/api/admin/supplies/${id}/audit/${approve}`, {}, { auth: true }),
  demands: (query) => http.get('/api/admin/demands', query || {}, { auth: true }),
  auditDemand: (id, approve) => http.post(`/api/admin/demands/${id}/audit/${approve}`, {}, { auth: true }),
  reports: (query) => http.get('/api/admin/reports', query || {}, { auth: true }),
  handleReport: (id, resolve) => http.post(`/api/admin/reports/${id}/handle/${resolve}`, {}, { auth: true }),
  logs: (query) => http.get('/api/admin/logs', query || {}, { auth: true }),
  createNotice: (data) => http.post('/api/admin/notices', data, { auth: true }),
  unpublishNotice: (id) => http.post(`/api/admin/notices/${id}/unpublish`, {}, { auth: true }),
  createRegion: (data) => http.post('/api/admin/regions', data, { auth: true }),
  setRegionEnabled: (id, enabled) => http.post(`/api/admin/regions/${id}/enabled/${enabled}`, {}, { auth: true }),
  renameRegion: (id, name) => http.post(`/api/admin/regions/${id}/rename`, { name }, { auth: true }),
}

// 多节点联动 (数据上报 / 同步)
export const federationApi = {
  self: () => http.get('/api/admin/federation/self', {}, { auth: true }),
  nodes: () => http.get('/api/admin/federation/nodes', {}, { auth: true }),
  saveNode: (data) => http.post('/api/admin/federation/nodes', data, { auth: true }),
  updateNode: (id, data) => http.put('/api/admin/federation/nodes/' + id, data, { auth: true }),
  setNodeStatus: (id, status) => http.post(`/api/admin/federation/nodes/${id}/status`, { status }, { auth: true }),
  removeNode: (id) => http.del('/api/admin/federation/nodes/' + id, {}, { auth: true }),
  issueToken: (data) => http.post('/api/admin/federation/nodes/token', data || {}, { auth: true }),
  reports: (query) => http.get('/api/admin/federation/reports', query || {}, { auth: true }),
  metrics: (query) => http.get('/api/admin/federation/metrics', query || {}, { auth: true }),
  records: (query) => http.get('/api/admin/federation/records', query || {}, { auth: true }),
  aggregate: (range, regionPrefix) => http.get('/api/admin/federation/aggregate', { range, regionPrefix: regionPrefix || undefined }, { auth: true }),
  syncPush: (node) => http.post('/api/admin/federation/sync/push' + (node ? '?node=' + encodeURIComponent(node) : ''), {}, { auth: true }),
  syncPull: (node) => http.post('/api/admin/federation/sync/pull' + (node ? '?node=' + encodeURIComponent(node) : ''), {}, { auth: true }),
  health: () => http.get('/api/admin/federation/health', {}, { auth: true }),
  settings: () => http.get('/api/admin/federation/settings', {}, { auth: true }),
  updateSettings: (values) => http.put('/api/admin/federation/settings', values, { auth: true }),
  key: () => http.get('/api/admin/federation/key', {}, { auth: true }),
  generateKey: (force) => http.post('/api/admin/federation/key/generate' + (force ? '?force=true' : ''), {}, { auth: true }),
  fetchPeerKey: (id) => http.post(`/api/admin/federation/nodes/${id}/key/fetch`, {}, { auth: true }),
}

// 报警中心 (规则 / 记录 / 手动扫描)
export const alertApi = {
  rules: () => http.get('/api/admin/alerts/rules', {}, { auth: true }),
  updateRule: (code, data) => http.put('/api/admin/alerts/rules/' + code, data, { auth: true }),
  records: (query) => http.get('/api/admin/alerts/records', query || {}, { auth: true }),
  scan: () => http.post('/api/admin/alerts/scan', {}, { auth: true }),
}

// 小程序订阅消息授权结果上报 (一次性订阅, 每次授权 +1 额度)
export const wechatApi = {
  subscribeReport: (templateKey, accepted) => http.post('/api/wechat/subscribe-report', { templateKey, accepted }, { auth: true }),
}

export const dashboardApi = {
  overview: () => http.get('/api/admin/dashboard/overview', {}, { auth: true }),
  trend: (range) => http.get('/api/admin/dashboard/trend', { range }, { auth: true }),
  category: () => http.get('/api/admin/dashboard/category', {}, { auth: true }),
  region: () => http.get('/api/admin/dashboard/region', {}, { auth: true }),
  users: () => http.get('/api/admin/dashboard/users', {}, { auth: true }),
  userTrend: (range) => http.get('/api/admin/dashboard/users/trend', { range }, { auth: true }),
  chat: (range) => http.get('/api/admin/dashboard/chat', { range }, { auth: true }),
}

export const aiApi = {
  chat: (data) => http.post('/api/ai/chat', data, { auth: true }),
  streamChat: (data, handlers) => streamChatRequest(data, handlers),
  conversations: (query) => http.get('/api/ai/conversations', query || {}, { auth: true }),
  messages: (id, query) => http.get(`/api/ai/conversations/${id}/messages`, query || {}, { auth: true }),
  removeConversation: (id) => http.del('/api/ai/conversations/' + id, {}, { auth: true }),
  renameConversation: (id, title) => http.put(`/api/ai/conversations/${id}`, { title }, { auth: true }),
  assistPublish: (data) => http.post('/api/ai/assist/publish', data, { auth: true }),
  insight: () => http.post('/api/ai/insight', {}, { auth: true }),
  ask: (question) => http.post('/api/ai/ask', { question }, { auth: true }),
}

function parseSseBlock(block, handlers) {
  if (!block || !block.trim()) return
  let event = 'message'
  let dataStr = ''
  const lines = block.split('\n')
  for (const line of lines) {
    if (line.indexOf('event:') === 0) event = line.slice(6).trim()
    else if (line.indexOf('data:') === 0) dataStr += line.slice(5).trim()
  }
  if (!dataStr) return
  let payload
  try { payload = JSON.parse(dataStr) } catch (e) { payload = dataStr }
  if (event === 'status' && handlers.onStatus) handlers.onStatus(payload.text || payload)
  else if (event === 'delta' && handlers.onDelta) handlers.onDelta(payload.text || payload)
  else if (event === 'done' && handlers.onDone) handlers.onDone(payload)
  else if (event === 'error' && handlers.onError) handlers.onError(payload)
}

function decodeUtf8(bytes) {
  if (typeof TextDecoder !== 'undefined') return new TextDecoder().decode(bytes)
  let out = ''
  for (let i = 0; i < bytes.length;) {
    const c = bytes[i++]
    if (c < 128) out += String.fromCharCode(c)
    else if (c > 191 && c < 224) out += String.fromCharCode(((c & 31) << 6) | (bytes[i++] & 63))
    else if (c > 223 && c < 240) out += String.fromCharCode(((c & 15) << 12) | ((bytes[i++] & 63) << 6) | (bytes[i++] & 63))
    else out += String.fromCharCode(((c & 7) << 18) | ((bytes[i++] & 63) << 12) | ((bytes[i++] & 63) << 6) | (bytes[i++] & 63))
  }
  return out
}

function streamChatRequest(data, handlers = {}) {
  // #ifdef H5
  return new Promise(async (resolve, reject) => {
    try {
      const resp = await fetch(apiBase() + '/api/ai/chat/stream', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...authHeader(),
        },
        body: JSON.stringify(data),
      })
      if (!resp.ok) {
        const text = await resp.text()
        const err = { msg: text || '请求失败' }
        if (handlers.onError) handlers.onError(err)
        return reject(err)
      }
      const reader = resp.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''
      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        const blocks = buffer.split('\n\n')
        buffer = blocks.pop()
        for (const b of blocks) parseSseBlock(b, handlers)
      }
      resolve()
    } catch (e) {
      if (handlers.onError) handlers.onError(e)
      reject(e)
    }
  })
  // #endif

  // #ifndef H5
  return new Promise((resolve, reject) => {
    let buffer = ''
    const req = uni.request({
      url: apiBase() + '/api/ai/chat/stream',
      method: 'POST',
      header: {
        'Content-Type': 'application/json',
        ...authHeader(),
      },
      data,
      enableChunked: true,
      success: () => resolve(),
      fail: (err) => {
        if (handlers.onError) handlers.onError(err)
        reject(err)
      },
    })
    req.onChunkReceived((res) => {
      let text = ''
      if (res.data instanceof ArrayBuffer) {
        text = decodeUtf8(new Uint8Array(res.data))
      } else if (typeof res.data === 'string') {
        text = res.data
      }
      buffer += text
      const blocks = buffer.split('\n\n')
      buffer = blocks.pop()
      for (const b of blocks) parseSseBlock(b, handlers)
    })
  })
  // #endif
}

function apiBase() {
  // #ifdef H5
  return ''
  // #endif
  // #ifndef H5
  return 'http://127.0.0.1:8080'
  // #endif
}

function authHeader() {
  const token = uni.getStorageSync('agro_token')
  return token ? { Authorization: 'Bearer ' + token } : {}
}

function fileNameOf(filePath) {
  const raw = String(filePath || '').split('?')[0]
  const name = raw.split('/').pop()
  return name && name.indexOf('.') > -1 ? name : 'image.jpg'
}

export function uploadFile(filePath) {
  return new Promise((resolve, reject) => {
    uni.uploadFile({
      url: apiBase() + '/api/files/upload',
      filePath,
      name: 'file',
      header: authHeader(),
      success: (res) => {
        try {
          const body = JSON.parse(res.data)
          if (body.code === 0) resolve(body.data)
          else reject({ msg: body.msg || '上传失败' })
        } catch (e) { reject({ msg: '上传失败' }) }
      },
      fail: () => reject({ msg: '上传失败' }),
    })
  })
}

function postJson(url, data) {
  return new Promise((resolve, reject) => {
    uni.request({
      url: apiBase() + url,
      method: 'POST',
      header: Object.assign({ 'Content-Type': 'application/json' }, authHeader()),
      data,
      success: (res) => {
        if (res.statusCode >= 200 && res.statusCode < 300 && res.data && res.data.code === 0) resolve(res.data.data)
        else reject({ msg: (res.data && res.data.msg) || '请求失败' })
      },
      fail: () => reject({ msg: '网络请求失败' }),
    })
  })
}

// #ifdef H5
async function uploadChunk({ uploadId, index, filePath, start, end }) {
  const blob = await (await fetch(filePath)).blob()
  const slice = blob.slice(start, end)
  const fd = new FormData()
  fd.append('uploadId', uploadId)
  fd.append('index', String(index))
  fd.append('file', slice, fileNameOf(filePath))
  const headers = authHeader()
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', apiBase() + '/api/files/upload/chunk')
    Object.keys(headers).forEach((k) => xhr.setRequestHeader(k, headers[k]))
    xhr.onload = () => {
      try {
        const body = JSON.parse(xhr.responseText)
        if (body.code === 0) resolve(body.data)
        else reject({ msg: body.msg || '分片上传失败' })
      } catch (e) { reject({ msg: '分片上传失败' }) }
    }
    xhr.onerror = () => reject({ msg: '分片上传失败' })
    xhr.send(fd)
  })
}
// #endif

// #ifndef H5
function readChunk(filePath, start, end) {
  return new Promise((resolve, reject) => {
    const fs = uni.getFileSystemManager()
    fs.open({
      filePath,
      flag: 'r',
      success: (res) => {
        const buffer = new ArrayBuffer(end - start)
        fs.read({
          fd: res.fd,
          arrayBuffer: buffer,
          position: start,
          length: end - start,
          success: () => { fs.close({ fd: res.fd }); resolve(buffer) },
          fail: (e) => { fs.close({ fd: res.fd }); reject(e) },
        })
      },
      fail: reject,
    })
  })
}

function uploadChunk({ uploadId, index, filePath, start, end }) {
  return readChunk(filePath, start, end).then((buffer) => {
    const fs = uni.getFileSystemManager()
    const tmp = `${uni.env.USER_DATA_PATH}/.chunk_${uploadId}_${index}`
    fs.writeFileSync(tmp, buffer)
    return new Promise((resolve, reject) => {
      uni.uploadFile({
        url: apiBase() + '/api/files/upload/chunk',
        filePath: tmp,
        name: 'file',
        formData: { uploadId, index: String(index) },
        header: authHeader(),
        success: (res) => {
          try {
            const body = JSON.parse(res.data)
            if (body.code === 0) resolve(body.data)
            else reject({ msg: body.msg || '分片上传失败' })
          } catch (e) { reject({ msg: '分片上传失败' }) }
        },
        fail: () => reject({ msg: '分片上传失败' }),
        complete: () => { try { fs.unlinkSync(tmp) } catch (e) { /* ignore */ } },
      })
    })
  })
}
// #endif

/**
 * 分片断点续传上传。支持中断后重传: 重新 init 会复用已上传分片(需服务端保留会话),
 * 本实现按当前会话续传, 未上传分片自动补齐。
 * @param {string} filePath 本地文件路径
 * @param {number} fileSize 文件字节数
 * @param {(percent:number)=>void} [onProgress] 进度回调 0-100
 */
export async function uploadFileResumable(filePath, fileSize, onProgress) {
  const init = await postJson('/api/files/upload/init', { fileName: fileNameOf(filePath), fileSize })
  const { uploadId, chunkSize, totalChunks } = init
  const uploaded = new Set(init.uploadedChunks || [])
  const notify = (i) => onProgress && onProgress(Math.round(((i + 1) / totalChunks) * 100))

  for (let i = 0; i < totalChunks; i++) {
    if (uploaded.has(i)) { notify(i); continue }
    const start = i * chunkSize
    const end = Math.min(start + chunkSize, fileSize)
    await uploadChunk({ uploadId, index: i, filePath, start, end })
    notify(i)
  }
  return postJson('/api/files/upload/complete', { uploadId })
}

/**
 * 智能上传: 小文件直传, 大文件走分片续传。
 * @param {string} filePath
 * @param {number} [fileSize] 文件字节数, 缺省视为小文件直传
 * @param {(percent:number)=>void} [onProgress]
 */
export function uploadImageSmart(filePath, fileSize, onProgress) {
  const THRESHOLD = 2 * 1024 * 1024
  if (fileSize && fileSize > THRESHOLD) return uploadFileResumable(filePath, fileSize, onProgress)
  return uploadFile(filePath)
}
