<template>
  <view class="page container">
    <view class="card">
      <view class="field">
        <text class="field-label">昵称</text>
        <input v-model="form.nickname" class="input" placeholder="昵称" />
      </view>
      <view class="field">
        <text class="field-label">手机号</text>
        <text class="flex1 muted">{{ phone }}</text>
      </view>
      <view class="between" style="padding:24rpx 0;">
        <text>对外展示联系方式</text>
        <switch :checked="form.contactDisplay" color="#2e8b57" @change="e => form.contactDisplay = e.detail.value" />
      </view>
      <view class="field">
        <text class="field-label">联系电话</text>
        <input v-model="form.contactPhone" class="input" placeholder="可选" />
      </view>
    </view>
    <button class="btn btn-block" @tap="save">保存资料</button>

    <view class="card mt24">
      <view class="bold mb16">修改密码</view>
      <view class="field"><text class="field-label">原密码</text><input v-model="pwd.old" password class="input" placeholder="原密码" /></view>
      <view class="field"><text class="field-label">新密码</text><input v-model="pwd.nw" password class="input" placeholder="至少6位" /></view>
      <button class="btn btn-plain btn-block mt16" @tap="changePwd">修改密码</button>
    </view>
  </view>
</template>

<script>
import { userApi } from '../../api/index'
import { requireLogin, getUser, setUser } from '../../utils/request'

export default {
  data() {
    return { me: null, phone: '', form: { nickname: '', contactDisplay: true, contactPhone: '' }, pwd: { old: '', nw: '' } }
  },
  onLoad() {
    if (!requireLogin()) return
    this.me = getUser() || {}
    this.phone = this.me.phone || ''
    this.form.nickname = this.me.nickname || ''
    this.form.contactDisplay = this.me.contactDisplay !== false
    this.form.contactPhone = this.me.contactPhone || ''
  },
  methods: {
    async save() {
      try {
        // 资料页不修改经营类型: 不传 role, 后端 COALESCE 保持原值。
        // (旧实现回传 me.role, 管理员 ADMIN 会被后端白名单拒绝而无法保存资料。)
        const res = await userApi.updateProfile({
          nickname: this.form.nickname, avatarUrl: this.me.avatarUrl, homeRegionId: this.me.homeRegionId,
          contactDisplay: this.form.contactDisplay, contactPhone: this.form.contactPhone,
        })
        setUser(res)
        uni.showToast({ title: '已保存' })
      } catch (e) {}
    },
    async changePwd() {
      if (!this.pwd.old || !this.pwd.nw) return uni.showToast({ title: '请填写完整', icon: 'none' })
      if (this.pwd.nw.length < 6) return uni.showToast({ title: '新密码至少6位', icon: 'none' })
      try {
        await userApi.changePassword(this.pwd.old, this.pwd.nw)
        this.pwd = { old: '', nw: '' }
        uni.showToast({ title: '密码已修改' })
      } catch (e) {}
    },
  },
}
</script>
