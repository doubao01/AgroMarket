<template>
  <view class="page container">
    <view v-if="current" class="card">
      <view class="between">
        <text class="bold">当前认证状态</text>
        <text :class="statusClass">{{ auditText(current.auditStatus) }}</text>
      </view>
      <view class="mt8 muted">{{ current.subjectType }} · {{ current.subjectName }}</view>
      <view v-if="current.auditRemark" class="mt8 muted">审核备注：{{ current.auditRemark }}</view>
    </view>

    <view class="card">
      <view class="field">
        <text class="field-label">主体类型</text>
        <picker :range="types" :value="typeIndex" @change="e => typeIndex = Number(e.detail.value)">
          <text class="flex1">{{ types[typeIndex] }}</text>
        </picker>
      </view>
      <view class="field">
        <text class="field-label">主体名称</text>
        <input v-model="form.subjectName" class="input" placeholder="姓名 / 合作社名称" />
      </view>
      <view class="field">
        <text class="field-label">证件号</text>
        <input v-model="form.idNumber" class="input" placeholder="身份证号 / 统一社会信用代码" />
      </view>
      <view class="field">
        <text class="field-label">证件照片</text>
        <input v-model="form.licenseUrl" class="input" placeholder="图片URL（可选）" />
      </view>
    </view>
    <view class="muted" style="padding:0 8rpx 24rpx;">提交后由平台管理员审核。认证通过后可获得认证标识。</view>
    <button class="btn btn-block" :disabled="submitting" @tap="submit">提交认证</button>
  </view>
</template>

<script>
import { userApi } from '../../api/index'
import { requireLogin } from '../../utils/request'

export default {
  data() {
    return {
      current: null, submitting: false,
      types: ['个人农户', '合作社', '企业/商户'],
      // 必须与后端 UserService.SubmitAuthAsync 的白名单一致(PERSONAL/COOP/ENTERPRISE),
      // 旧值 PERSON/COMPANY 会被判"主体类型不合法"直接 400
      typeValues: ['PERSONAL', 'COOP', 'ENTERPRISE'],
      typeIndex: 0,
      form: { subjectName: '', idNumber: '', licenseUrl: '' },
    }
  },
  computed: {
    statusClass() { return this.current && this.current.auditStatus === 'APPROVED' ? 'primary bold' : 'accent bold' },
  },
  onLoad() { if (!requireLogin()) return; this.load() },
  methods: {
    async load() {
      try {
        this.current = await userApi.getMerchantAuth()
        if (this.current) {
          this.form.subjectName = this.current.subjectName || ''
          this.form.idNumber = this.current.idNumber || ''
          this.form.licenseUrl = this.current.licenseUrl || ''
          const i = this.typeValues.indexOf(this.current.subjectType)
          if (i >= 0) this.typeIndex = i
        }
      } catch (e) {}
    },
    async submit() {
      if (!this.form.subjectName) return uni.showToast({ title: '请填写主体名称', icon: 'none' })
      this.submitting = true
      try {
        this.current = await userApi.submitMerchantAuth({
          subjectType: this.typeValues[this.typeIndex],
          subjectName: this.form.subjectName,
          idNumber: this.form.idNumber || null,
          licenseUrl: this.form.licenseUrl || null,
        })
        uni.showToast({ title: '已提交，等待审核' })
      } catch (e) {} finally { this.submitting = false }
    },
    auditText(s) { return { PENDING: '审核中', APPROVED: '已认证', REJECTED: '已驳回' }[s] || s },
  },
}
</script>
