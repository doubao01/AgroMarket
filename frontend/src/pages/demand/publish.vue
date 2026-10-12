<template>
  <view class="page container">
    <view class="card">
      <view class="field">
        <text class="field-label">标题 *</text>
        <input v-model="form.title" class="input" placeholder="如：长期收购优质花生 20吨" />
      </view>
      <view class="field" @tap="showCat = true">
        <text class="field-label">分类 *</text>
        <text class="flex1" :class="{ muted: !catName }">{{ catName || '请选择分类' }}</text>
        <text class="muted">›</text>
      </view>
      <view class="field">
        <text class="field-label">数量 *</text>
        <input v-model="form.quantity" type="digit" class="input" placeholder="数量" />
        <input v-model="form.unit" class="input" style="max-width:160rpx;margin-left:16rpx;" placeholder="单位" />
      </view>
      <view class="field">
        <text class="field-label">价格区间</text>
        <input v-model="form.priceMin" type="digit" class="input" placeholder="最低" />
        <text class="muted" style="padding:0 12rpx;">~</text>
        <input v-model="form.priceMax" type="digit" class="input" placeholder="最高" />
        <input v-model="form.priceUnit" class="input" style="max-width:180rpx;margin-left:16rpx;" placeholder="元/斤" />
      </view>
      <view class="field" @tap="showRegion = true">
        <text class="field-label">交货地 *</text>
        <text class="flex1" :class="{ muted: !regionLabel }">{{ regionLabel || '请选择乡镇' }}</text>
        <text class="muted">›</text>
      </view>
    </view>

    <view class="card">
      <view class="field">
        <text class="field-label">规格要求</text>
        <input v-model="form.specRequired" class="input" placeholder="如 水分≤13%" />
      </view>
      <view class="field">
        <text class="field-label">期望交付</text>
        <picker mode="date" :value="form.expectDate" @change="e => form.expectDate = e.detail.value">
          <text class="flex1" :class="{ muted: !form.expectDate }">{{ form.expectDate || '选择日期(可选)' }}</text>
        </picker>
      </view>
      <view class="field">
        <text class="field-label">包装要求</text>
        <input v-model="form.packagingNote" class="input" placeholder="可选" />
      </view>
      <view class="field">
        <text class="field-label">联系人</text>
        <input v-model="form.contactName" class="input" placeholder="联系人姓名" />
      </view>
      <view class="field">
        <text class="field-label">联系电话</text>
        <input v-model="form.contactPhone" class="input" placeholder="联系电话" />
      </view>
      <view class="field" style="align-items:flex-start;">
        <view class="between" style="width:100%;">
          <text class="field-label">说明</text>
          <text class="ai-assist-btn" @tap="aiAssist">{{ aiLoading ? '生成中...' : 'AI 帮我写' }}</text>
        </view>
        <textarea v-model="form.description" class="input" style="min-height:160rpx;" placeholder="补充说明" />
      </view>
    </view>

    <view class="muted" style="padding:0 8rpx 24rpx;">{{ editId ? '修改后将重新提交审核，审核通过前市场展示原内容。' : '提交后需管理员审核通过才会在市场展示。' }}</view>
    <button class="btn btn-block" :disabled="submitting" @tap="submit">{{ submitting ? '提交中...' : (editId ? '保存修改' : '提交审核') }}</button>

    <view v-if="showCat" class="modal-mask" @tap="showCat = false">
      <view class="modal" @tap.stop>
        <view class="bold mb16">选择分类</view>
        <view class="wrap">
          <text v-for="c in categories" :key="c.id" class="chip" :class="{ active: c.id === form.categoryId }" @tap="pickCat(c)">{{ c.name }}</text>
        </view>
      </view>
    </view>

    <region-picker
      :visible.sync="showRegion"
      :townId.sync="form.townId"
      :villageId.sync="form.villageId"
      @town-change="onTownChange"
      @village-change="onVillageChange"
    />
  </view>
</template>

<script>
import { demandApi, categoryApi, aiApi } from '../../api/index'
import { requireLogin } from '../../utils/request'
import RegionPicker from '../../components/region-picker.vue'

export default {
  components: { RegionPicker },
  data() {
    return {
      categories: [], showCat: false, showRegion: false, catName: '', regionLabel: '', submitting: false, aiLoading: false,
      editId: null,
      form: {
        title: '', categoryId: null, quantity: '', unit: '斤', priceMin: '', priceMax: '', priceUnit: '元/斤',
        specRequired: '', countyId: 3, townId: null, villageId: null, expectDate: '', packagingNote: '',
        contactName: '', contactPhone: '', description: '', effectiveDays: 30,
      },
    }
  },
  onLoad(options) { if (!requireLogin()) return; this.loadCategories(); if (options && options.id) this.loadForEdit(Number(options.id)) },
  methods: {
    async loadForEdit(id) {
      this.editId = id
      uni.setNavigationBarTitle({ title: '编辑采购' })
      try {
        const d = await demandApi.detail(id)
        if (!d) return
        const f = this.form
        f.title = d.title
        f.categoryId = d.categoryId
        f.quantity = String(d.quantity)
        f.unit = d.unit || '斤'
        f.priceMin = d.priceMin != null ? String(d.priceMin) : ''
        f.priceMax = d.priceMax != null ? String(d.priceMax) : ''
        f.priceUnit = d.priceUnit || '元/斤'
        f.specRequired = d.specRequired || ''
        f.countyId = d.countyId || 3
        f.townId = d.townId || null
        f.villageId = d.villageId || null
        f.expectDate = d.expectDate ? String(d.expectDate).slice(0, 10) : ''
        f.packagingNote = d.packagingNote || ''
        f.contactName = d.contactName || ''
        f.contactPhone = d.contactPhone || ''
        f.description = d.description || ''
        this.catName = d.categoryName || ''
        this.regionLabel = d.townName || ''
      } catch (e) {
        uni.showToast({ title: (e && e.msg) || '加载失败', icon: 'none' })
      }
    },
    async loadCategories() {
      try { const all = await categoryApi.all(); this.categories = (all || []).filter(c => c.parentId != null) } catch (e) {}
    },
    pickCat(c) { this.form.categoryId = c.id; this.catName = c.name; this.showCat = false },
    onTownChange(e) { this.regionLabel = e.townName; this.form.countyId = e.countyId },
    onVillageChange(e) { if (e.villageName) this.regionLabel += ' ' + e.villageName },
    async aiAssist() {
      this.aiLoading = true
      try {
        const f = this.form
        const res = await aiApi.assistPublish({
          type: 'DEMAND',
          title: f.title,
          category: this.catName,
          keywords: f.specRequired,
          quantity: f.quantity ? Number(f.quantity) : null,
          priceHint: f.priceMin ? Number(f.priceMin) : null,
        })
        if (res.description) f.description = res.description
        if (!f.priceMin && res.priceMin) f.priceMin = String(res.priceMin)
        if (!f.priceMax && res.priceMax) f.priceMax = String(res.priceMax)
        if (res.priceUnit) f.priceUnit = res.priceUnit
        if (res.suggestedCategory && !this.catName) {
          const hit = this.categories.find(c => c.name === res.suggestedCategory)
          if (hit) this.pickCat(hit)
        }
        uni.showToast({ title: '已生成，请检查', icon: 'none' })
      } catch (e) {
        uni.showToast({ title: (e && e.msg) || 'AI 未配置', icon: 'none' })
      } finally {
        this.aiLoading = false
      }
    },
    async submit() {
      const f = this.form
      if (!f.title) return uni.showToast({ title: '请填写标题', icon: 'none' })
      if (!f.categoryId) return uni.showToast({ title: '请选择分类', icon: 'none' })
      if (!f.quantity || Number(f.quantity) <= 0) return uni.showToast({ title: '请填写有效数量', icon: 'none' })
      if (!f.townId) return uni.showToast({ title: '请选择交货地', icon: 'none' })
      const payload = {
        title: f.title, categoryId: f.categoryId, description: f.description,
        quantity: Number(f.quantity), unit: f.unit,
        priceMin: f.priceMin ? Number(f.priceMin) : null, priceMax: f.priceMax ? Number(f.priceMax) : null,
        priceUnit: f.priceUnit, specRequired: f.specRequired || null,
        countyId: f.countyId, townId: f.townId, villageId: f.villageId || null,
        expectDate: f.expectDate ? f.expectDate + 'T00:00:00' : null,
        packagingNote: f.packagingNote || null, contactName: f.contactName || null,
        contactPhone: f.contactPhone || null, effectiveDays: f.effectiveDays,
      }
      this.submitting = true
      try {
        if (this.editId) await demandApi.update(this.editId, payload)
        else await demandApi.create(payload)
        uni.showToast({ title: this.editId ? '已更新，等待审核' : '已提交，等待审核' })
        setTimeout(() => uni.redirectTo({ url: '/pages/user/listings' }), 800)
      } catch (e) {} finally { this.submitting = false }
    },
  },
}
</script>

<style>
.modal-mask { position: fixed; left: 0; right: 0; top: 0; bottom: 0; background: rgba(0, 0, 0, 0.4); display: flex; align-items: flex-end; z-index: 999; }
.modal { width: 100%; background: #fff; border-radius: 24rpx 24rpx 0 0; padding: 32rpx; max-height: 70vh; overflow-y: auto; }
.ai-assist-btn { font-size: 24rpx; color: #2e8b57; border: 1rpx solid #2e8b57; border-radius: 999rpx; padding: 4rpx 20rpx; }
</style>
