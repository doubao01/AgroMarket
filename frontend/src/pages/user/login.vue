<template>
  <view class="page">
    <view class="logo-area">
      <view class="logo">🌾</view>
      <view class="app-name">息县农贸通</view>
      <view class="muted">豫南粮仓 · 农产品自由交易平台</view>
    </view>

    <view class="container">
      <view class="tabs">
        <view class="tab" :class="{ active: mode === 'login' }" @tap="mode = 'login'">登录</view>
        <view class="tab" :class="{ active: mode === 'register' }" @tap="mode = 'register'">注册</view>
      </view>

      <view class="card">
        <view class="field">
          <text class="field-label">手机号</text>
          <input v-model="phone" type="number" maxlength="11" class="input" placeholder="请输入手机号" />
        </view>
        <view class="field">
          <text class="field-label">密码</text>
          <input v-model="password" password class="input" placeholder="请输入密码" />
        </view>
        <view v-if="mode === 'register'" class="field">
          <text class="field-label">昵称</text>
          <input v-model="nickname" class="input" placeholder="请输入昵称" />
        </view>
        <view v-if="mode === 'register'" class="field">
          <text class="field-label">身份</text>
          <picker :range="roleNames" :value="roleIndex" @change="e => roleIndex = Number(e.detail.value)">
            <text class="flex1">{{ roleNames[roleIndex] }}</text>
          </picker>
        </view>
        <view v-if="mode === 'register'" class="field">
          <text class="field-label">验证码</text>
          <input v-model="code" class="input" placeholder="短信验证码" />
          <text class="code-btn" @tap="sendSms">{{ codeSent ? '重新发送' : '获取验证码' }}</text>
        </view>
      </view>

      <button class="btn btn-block" :disabled="submitting" @tap="submit">
        {{ submitting ? '请稍候...' : (mode === 'login' ? '登录' : '注册并登录') }}
      </button>

      <view v-if="mode === 'login'" class="muted mt16" style="text-align:center;">
        测试账号：13800000001 / Abc123456（管理员）
      </view>
      <view v-if="mode === 'register'" class="muted mt16" style="text-align:center;">
        开发环境短信验证码会打印在后端日志中
      </view>
    </view>
  </view>
</template>

<script>
import { authApi } from '../../api/index'
import { setToken, setUser } from '../../utils/request'

export default {
  data() {
    return {
      mode: 'login',
      phone: '',
      password: '',
      nickname: '',
      code: '',
      codeSent: false,
      submitting: false,
      roleNames: ['农户', '合作社', '采购商', '商户'],
      roleValues: ['FARMER', 'COOP', 'BUYER', 'MERCHANT'],
      roleIndex: 0,
    }
  },
  methods: {
    validPhone() {
      return /^1[3-9]\d{9}$/.test(this.phone)
    },
    async sendSms() {
      if (!this.validPhone()) return uni.showToast({ title: '手机号格式不正确', icon: 'none' })
      try {
        await authApi.sendSms(this.phone)
        this.codeSent = true
        uni.showToast({ title: '验证码已发送', icon: 'none' })
      } catch (e) {}
    },
    async submit() {
      if (!this.validPhone()) return uni.showToast({ title: '手机号格式不正确', icon: 'none' })
      if (this.mode === 'login') {
        if (!this.password) return uni.showToast({ title: '请输入密码', icon: 'none' })
        this.submitting = true
        try {
          const res = await authApi.loginByPassword(this.phone, this.password)
          this.afterAuth(res)
        } catch (e) {} finally { this.submitting = false }
      } else {
        if (!this.nickname) return uni.showToast({ title: '请输入昵称', icon: 'none' })
        if (!this.password || this.password.length < 6) return uni.showToast({ title: '密码至少6位', icon: 'none' })
        if (!this.code) return uni.showToast({ title: '请输入验证码', icon: 'none' })
        this.submitting = true
        try {
          const res = await authApi.register({
            phone: this.phone, code: this.code, password: this.password,
            nickname: this.nickname, role: this.roleValues[this.roleIndex], homeRegionId: null,
          })
          this.afterAuth(res)
        } catch (e) {} finally { this.submitting = false }
      }
    },
    afterAuth(res) {
      setToken(res.token)
      setUser(res)
      uni.showToast({ title: '登录成功' })
      setTimeout(() => {
        const pages = getCurrentPages()
        if (pages.length > 1) uni.navigateBack()
        else uni.switchTab({ url: '/pages/index/index' })
      }, 600)
    },
  },
}
</script>

<style>
.logo-area { text-align: center; padding: 100rpx 0 40rpx; }
.logo { font-size: 96rpx; }
.app-name { font-size: 44rpx; font-weight: 700; margin: 16rpx 0 8rpx; color: #2e8b57; }
.tabs { display: flex; margin-bottom: 20rpx; }
.tab { flex: 1; text-align: center; padding: 20rpx 0; font-size: 32rpx; color: #7a8a80; }
.tab.active { color: #2e8b57; font-weight: 700; }
.code-btn { color: #2e8b57; font-size: 26rpx; padding-left: 16rpx; }
.container { padding: 24rpx 48rpx; }
</style>
