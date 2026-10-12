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
  list: () => http.get('/api/favorites', {}, { auth: true }),
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

export const dashboardApi = {
  overview: () => http.get('/api/admin/dashboard/overview', {}, { auth: true }),
  trend: (range) => http.get('/api/admin/dashboard/trend', { range }, { auth: true }),
  category: () => http.get('/api/admin/dashboard/category', {}, { auth: true }),
  region: () => http.get('/api/admin/dashboard/region', {}, { auth: true }),
  users: () => http.get('/api/admin/dashboard/users', {}, { auth: true }),
  chat: (range) => http.get('/api/admin/dashboard/chat', { range }, { auth: true }),
}

export const aiApi = {
  chat: (data) => http.post('/api/ai/chat', data, { auth: true }),
  conversations: (query) => http.get('/api/ai/conversations', query || {}, { auth: true }),
  messages: (id, query) => http.get(`/api/ai/conversations/${id}/messages`, query || {}, { auth: true }),
  removeConversation: (id) => http.del('/api/ai/conversations/' + id, {}, { auth: true }),
  renameConversation: (id, title) => http.put(`/api/ai/conversations/${id}`, { title }, { auth: true }),
  assistPublish: (data) => http.post('/api/ai/assist/publish', data, { auth: true }),
  insight: () => http.post('/api/ai/insight', {}, { auth: true }),
  ask: (question) => http.post('/api/ai/ask', { question }, { auth: true }),
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
