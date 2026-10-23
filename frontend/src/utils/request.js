// 统一请求封装: 自动附加 token、统一错误处理、401 自动跳转登录
const TOKEN_KEY = 'agro_token'
const USER_KEY = 'agro_user'

// #ifdef H5
// H5 走 vite 开发代理(/api -> 后端), 使用相对路径
export const BASE_URL = ''
// #endif
// #ifndef H5
// 小程序等平台无开发代理, 需填写后端完整地址 (此处为唯一出处, WebSocket 地址亦由此推导)
export const BASE_URL = 'http://127.0.0.1:8080'
// #endif

export function getToken() {
  try { return uni.getStorageSync(TOKEN_KEY) || '' } catch (e) { return '' }
}

export function setToken(token) {
  try { uni.setStorageSync(TOKEN_KEY, token || '') } catch (e) {}
}

export function getUser() {
  try { return uni.getStorageSync(USER_KEY) || null } catch (e) { return null }
}

export function setUser(user) {
  try { uni.setStorageSync(USER_KEY, user || '') } catch (e) {}
}

export function clearAuth() {
  try {
    uni.removeStorageSync(TOKEN_KEY)
    uni.removeStorageSync(USER_KEY)
  } catch (e) {}
}

// 页面同时发多个请求时, 401 会一起到达; 不设标记就会在栈里压入多个登录页
let loginNavigating = false

function toLogin() {
  clearAuth()
  // 通知聊天长连接等订阅方销毁状态: 只清 token 会让 WS 与未读角标继续存活
  try { uni.$emit('auth:expired') } catch (e) {}
  if (loginNavigating) return
  const pages = getCurrentPages()
  const current = pages.length ? pages[pages.length - 1].route : ''
  if (current === 'pages/user/login') return
  loginNavigating = true
  uni.navigateTo({
    url: '/pages/user/login',
    complete: () => setTimeout(() => { loginNavigating = false }, 300),
  })
}

export function request(options) {
  let { url, method = 'GET', data = {}, auth = false, showError = true } = options
  // GET 的可选参数若为 undefined/null, uni.request 会序列化成空串(limit=),
  // 后端 int 绑定空串直接 400。这里统一剔除, 让"不传即不筛选"的语义真正生效。
  if (method === 'GET' && data && typeof data === 'object' && !Array.isArray(data)) {
    const clean = {}
    Object.keys(data).forEach((k) => {
      const v = data[k]
      if (v !== undefined && v !== null && v !== '') clean[k] = v
    })
    data = clean
  }
  return new Promise((resolve, reject) => {
    const header = { 'Content-Type': 'application/json' }
    const token = getToken()
    if (token) header['Authorization'] = 'Bearer ' + token

    uni.request({
      url: BASE_URL + url,
      method,
      data,
      header,
      timeout: 15000,
      success: (res) => {
        const body = res.data || {}
        if (res.statusCode === 401 || body.code === 401) {
          if (auth) toLogin()
          reject({ code: 401, msg: body.msg || '请先登录' })
          return
        }
        if (res.statusCode >= 200 && res.statusCode < 300 && body.code === 0) {
          resolve(body.data)
        } else {
          const msg = body.msg || `请求失败(${res.statusCode})`
          if (showError) uni.showToast({ title: msg, icon: 'none' })
          reject({ code: body.code || res.statusCode, msg })
        }
      },
      fail: (err) => {
        if (showError) uni.showToast({ title: '网络异常，请稍后重试', icon: 'none' })
        reject({ code: -1, msg: '网络异常', raw: err })
      },
    })
  })
}

export const http = {
  get: (url, data, opts) => request({ url, method: 'GET', data, ...opts }),
  post: (url, data, opts) => request({ url, method: 'POST', data, ...opts }),
  put: (url, data, opts) => request({ url, method: 'PUT', data, ...opts }),
  del: (url, data, opts) => request({ url, method: 'DELETE', data, ...opts }),
}

// 静默校验登录态(有本地 token 时拉取用户信息确认有效)
export async function ensureLogin(silent = false) {
  const token = getToken()
  if (!token) return null
  try {
    const me = await request({ url: '/api/user/me', method: 'GET', auth: true, showError: false })
    setUser(me)
    return me
  } catch (e) {
    if (!silent) clearAuth()
    return null
  }
}

// 页面级登录守卫: 未登录则跳转登录页并返回 false
export function requireLogin() {
  if (!getToken()) {
    toLogin()
    return false
  }
  return true
}
