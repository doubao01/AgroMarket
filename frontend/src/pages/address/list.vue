<template>
  <view class="page container">
    <view v-if="!loading && !list.length" class="empty">暂无收货地址</view>
    <view v-for="a in list" :key="a.id" class="card">
      <view class="between">
        <text class="bold">{{ a.contactName }} {{ a.phone }}</text>
        <text v-if="a.isDefault" class="tag">默认</text>
      </view>
      <view class="muted mt8">{{ a.detail }}</view>
      <view class="mt8"><text class="muted" @tap="remove(a)">删除</text></view>
    </view>

    <view class="card">
      <view class="bold mb16">新增地址</view>
      <view class="field"><text class="field-label">联系人</text><input v-model="form.contactName" class="input" placeholder="姓名" /></view>
      <view class="field"><text class="field-label">电话</text><input v-model="form.phone" type="number" class="input" placeholder="手机号" /></view>
      <view class="field" @tap="showRegion = true">
        <text class="field-label">地区</text>
        <text class="flex1" :class="{ muted: !regionLabel }">{{ regionLabel || '选择乡镇/村' }}</text>
        <text class="muted">›</text>
      </view>
      <view class="field"><text class="field-label">详细地址</text><input v-model="form.detail" class="input" placeholder="门牌号等" /></view>
      <view class="between" style="padding:24rpx 0;"><text>设为默认</text><switch :checked="form.isDefault" color="#2e8b57" @change="e => form.isDefault = e.detail.value" /></view>
      <button class="btn btn-block" @tap="save">保存地址</button>
    </view>

    <region-picker :visible.sync="showRegion" :townId.sync="form.townId" :villageId.sync="form.villageId" @town-change="onTown" @village-change="onVillage" />
  </view>
</template>

<script>
import { addressApi } from '../../api/index'
import { requireLogin } from '../../utils/request'
import RegionPicker from '../../components/region-picker.vue'

export default {
  components: { RegionPicker },
  data() {
    return {
      list: [], loading: false, showRegion: false, regionLabel: '',
      form: { contactName: '', phone: '', townId: null, villageId: null, detail: '', isDefault: false },
    }
  },
  onLoad() { if (!requireLogin()) return; this.reload() },
  methods: {
    async reload() {
      this.loading = true
      try { this.list = await addressApi.list() } catch (e) {} finally { this.loading = false }
    },
    onTown(e) { this.regionLabel = e.townName },
    onVillage(e) { if (e.villageName) this.regionLabel += ' ' + e.villageName },
    async save() {
      const f = this.form
      if (!f.contactName || !f.phone) return uni.showToast({ title: '请填写联系人与电话', icon: 'none' })
      if (!f.townId) return uni.showToast({ title: '请选择地区', icon: 'none' })
      if (!f.detail) return uni.showToast({ title: '请填写详细地址', icon: 'none' })
      try {
        await addressApi.create({
          contactName: f.contactName, phone: f.phone, countyId: null,
          townId: f.townId, villageId: f.villageId || null, detail: f.detail, isDefault: f.isDefault,
        })
        uni.showToast({ title: '已保存' })
        this.form = { contactName: '', phone: '', townId: null, villageId: null, detail: '', isDefault: false }
        this.regionLabel = ''
        this.reload()
      } catch (e) {}
    },
    async remove(a) {
      const r = await uni.showModal({ title: '删除地址', content: '确定删除该地址？' })
      if (!r.confirm) return
      try { await addressApi.remove(a.id); this.reload() } catch (e) {}
    },
  },
}
</script>

<style>
.card-title { font-size: 30rpx; font-weight: 600; }
.container { padding: 24rpx; }
</style>
