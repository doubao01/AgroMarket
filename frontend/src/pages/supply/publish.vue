<template>
  <view class="page container">
    <view class="card">
      <view class="field">
        <text class="field-label">标题 *</text>
        <input v-model="form.title" class="input" placeholder="如：新收优质小麦 8000斤" />
      </view>
      <view class="field" @tap="showCat = true">
        <text class="field-label">分类 *</text>
        <text class="flex1" :class="{ muted: !catName }">{{ catName || '请选择分类' }}</text>
        <text class="muted">›</text>
      </view>
      <view class="field">
        <text class="field-label">数量 *</text>
        <input v-model="form.quantity" type="digit" class="input" placeholder="数量" />
        <input v-model="form.unit" class="input" style="max-width:160rpx;margin-left:16rpx;" placeholder="单位(斤/吨)" />
      </view>
      <view class="field">
        <text class="field-label">单价</text>
        <input v-model="form.price" type="digit" class="input" placeholder="留空表示面议" />
        <input v-model="form.priceUnit" class="input" style="max-width:200rpx;margin-left:16rpx;" placeholder="元/斤" />
      </view>
      <view class="between" style="padding:24rpx 0;">
        <text>价格可议</text>
        <switch :checked="form.negotiable" color="#2e8b57" @change="e => form.negotiable = e.detail.value" />
      </view>
      <view class="field">
        <text class="field-label">起订量</text>
        <input v-model="form.minOrderQty" type="digit" class="input" placeholder="可选" />
      </view>
      <view class="field" @tap="showRegion = true">
        <text class="field-label">产地 *</text>
        <text class="flex1" :class="{ muted: !regionLabel }">{{ regionLabel || '请选择乡镇' }}</text>
        <text class="muted">›</text>
      </view>
    </view>

    <view class="card">
      <view class="field">
        <text class="field-label">规格</text>
        <input v-model="form.spec" class="input" placeholder="如 水分≤12.5%" />
      </view>
      <view class="field">
        <text class="field-label">上市时间</text>
        <input v-model="form.harvestTime" class="input" placeholder="如 2026年5月" />
      </view>
      <view class="field">
        <text class="field-label">交付方式</text>
        <picker :range="deliveryOptions" :value="deliveryIndex" @change="onDelivery">
          <text class="flex1">{{ deliveryOptions[deliveryIndex] }}</text>
        </picker>
      </view>
      <view class="field">
        <text class="field-label">联系人电话</text>
        <input v-model="form.contactPhone" class="input" placeholder="买家联系用" />
      </view>
      <view class="field" style="align-items:flex-start;">
        <view class="between" style="width:100%;">
          <text class="field-label">描述</text>
          <text class="ai-assist-btn" @tap="aiAssist">{{ aiLoading ? '生成中...' : 'AI 帮我写' }}</text>
        </view>
        <textarea v-model="form.description" class="input" style="min-height:160rpx;" placeholder="补充说明" />
      </view>
    </view>

    <view class="card">
      <view class="field-label">商品图片（最多 9 张）</view>
      <view class="wrap mt16">
        <view v-for="(img, i) in form.images" :key="i" class="thumb">
          <image :src="img" class="thumb-img" mode="aspectFill" />
          <view class="thumb-del" @tap="removeImage(i)">×</view>
        </view>
        <view v-if="form.images.length < 9" class="thumb thumb-add" @tap="chooseImages">＋</view>
      </view>
    </view>

    <view class="muted" style="padding:0 8rpx 24rpx;">{{ editId ? '修改后将重新提交审核，审核通过前市场展示原内容。' : '提交后需管理员审核通过才会在市场展示。' }}</view>
    <button class="btn btn-block" :disabled="submitting" @tap="submit">{{ submitting ? '提交中...' : (editId ? '保存修改' : '提交审核') }}</button>

    <view v-if="showCat" class="modal-mask" @tap="showCat = false">
      <view class="modal" @tap.stop>
        <view class="bold mb16">选择分类</view>
        <view class="wrap">
          <text
            v-for="c in categories"
            :key="c.id"
            class="chip"
            :class="{ active: c.id === form.categoryId }"
            @tap="pickCat(c)"
          >{{ c.name }}</text>
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
import { supplyApi, categoryApi, uploadImageSmart, aiApi } from '../../api/index'
import { requireLogin } from '../../utils/request'
import { getLocation } from '../../utils/location'
import RegionPicker from '../../components/region-picker.vue'

export default {
  components: { RegionPicker },
  data() {
    return {
      categories: [],
      showCat: false,
      showRegion: false,
      catName: '',
      regionLabel: '',
      aiLoading: false,
      deliveryOptions: ['自提', '送货上门', '自提/送货均可'],
      deliveryIndex: 0,
      deliveryValues: ['SELF_PICKUP', 'DELIVERY', 'BOTH'],
      submitting: false,
      editId: null,
      form: {
        title: '', categoryId: null, quantity: '', unit: '斤', price: '', priceUnit: '元/斤',
        negotiable: true, minOrderQty: '', countyId: 3, townId: null, villageId: null,
        spec: '', harvestTime: '', contactPhone: '', description: '', deliveryMode: 'SELF_PICKUP',
        effectiveDays: 30, images: [],
      },
    }
  },
  onLoad(options) {
    if (!requireLogin()) return
    this.loadCategories()
    if (options && options.id) this.loadForEdit(Number(options.id))
    else this.applyLocation()
  },
  methods: {
    applyLocation() {
      const loc = getLocation()
      if (!loc) return
      this.form.countyId = loc.countyId || 3
      this.form.townId = loc.townId
      this.form.villageId = loc.villageId || null
      this.regionLabel = loc.townName + (loc.villageName ? ' ' + loc.villageName : '')
    },
    async loadForEdit(id) {
      this.editId = id
      uni.setNavigationBarTitle({ title: '编辑供应' })
      try {
        const d = await supplyApi.detail(id)
        if (!d) return
        const f = this.form
        f.title = d.title
        f.categoryId = d.categoryId
        f.quantity = String(d.quantity)
        f.unit = d.unit || '斤'
        f.price = d.price != null ? String(d.price) : ''
        f.priceUnit = d.priceUnit || '元/斤'
        f.negotiable = d.negotiable !== false
        f.minOrderQty = d.minOrderQty != null ? String(d.minOrderQty) : ''
        f.countyId = d.countyId || 3
        f.townId = d.townId || null
        f.villageId = d.villageId || null
        f.spec = d.spec || ''
        f.harvestTime = d.harvestTime || ''
        f.contactPhone = d.contactPhone || ''
        f.description = d.description || ''
        f.images = (d.images || []).map(x => (typeof x === 'string' ? x : x.url))
        this.catName = d.categoryName || ''
        this.regionLabel = d.townName || ''
        this.deliveryIndex = Math.max(0, this.deliveryValues.indexOf(d.deliveryMode))
        f.deliveryMode = d.deliveryMode || 'SELF_PICKUP'
      } catch (e) {
        uni.showToast({ title: (e && e.msg) || '加载失败', icon: 'none' })
      }
    },
    async loadCategories() {
      try {
        const all = await categoryApi.all()
        this.categories = (all || []).filter(c => c.parentId != null)
      } catch (e) {}
    },
    pickCat(c) {
      this.form.categoryId = c.id
      this.catName = c.name
      this.showCat = false
    },
    onDelivery(e) {
      this.deliveryIndex = Number(e.detail.value)
      this.form.deliveryMode = this.deliveryValues[this.deliveryIndex]
    },
    async aiAssist() {
      this.aiLoading = true
      try {
        const res = await aiApi.assistPublish({
          type: 'SUPPLY',
          title: this.form.title,
          category: this.catName,
          keywords: this.form.spec,
          quantity: this.form.quantity ? Number(this.form.quantity) : null,
          priceHint: this.form.price ? Number(this.form.price) : null,
        })
        if (res.description) this.form.description = res.description
        if (!this.form.price && res.priceMin) {
          this.form.price = String(res.priceMin)
          if (res.priceUnit) this.form.priceUnit = res.priceUnit
        }
        if (res.suggestedCategory && !this.catName) {
          const hit = this.categories.find(c => c.name === res.suggestedCategory)
          if (hit) this.pickCat(hit)
        }
        this.offerTitles(res.titleSuggestions)
        uni.showToast({ title: '已生成，请检查', icon: 'none' })
      } catch (e) {
        uni.showToast({ title: (e && e.msg) || 'AI 未配置', icon: 'none' })
      } finally {
        this.aiLoading = false
      }
    },
    onTownChange(e) { this.regionLabel = e.townName; this.form.countyId = e.countyId },
    onVillageChange(e) { if (e.villageName) this.regionLabel += ' ' + e.villageName },
    offerTitles(list) {
      if (!list || !list.length) return
      uni.showActionSheet({
        itemList: list.slice(0, 3),
        success: (r) => { this.form.title = list[r.tapIndex] },
        fail: () => {},
      })
    },
    chooseImages() {
      const remain = 9 - this.form.images.length
      uni.chooseImage({
        count: remain,
        success: async (res) => {
          const paths = res.tempFilePaths || []
          const files = res.tempFiles || []
          for (let i = 0; i < paths.length; i++) {
            const p = paths[i]
            const size = files[i] && files[i].size
            try {
              const r = await uploadImageSmart(p, size, (percent) => {
                if (percent < 100) uni.showLoading({ title: `上传中 ${percent}%`, mask: true })
              })
              uni.hideLoading()
              if (r && r.url) this.form.images.push(r.url)
            } catch (e) {
              uni.hideLoading()
              uni.showToast({ title: (e && e.msg) || '图片上传失败', icon: 'none' })
            }
          }
        },
      })
    },
    removeImage(i) { this.form.images.splice(i, 1) },
    async submit() {
      const f = this.form
      if (!f.title) return uni.showToast({ title: '请填写标题', icon: 'none' })
      if (!f.categoryId) return uni.showToast({ title: '请选择分类', icon: 'none' })
      if (!f.quantity || Number(f.quantity) <= 0) return uni.showToast({ title: '请填写有效数量', icon: 'none' })
      if (!f.townId) return uni.showToast({ title: '请选择产地乡镇', icon: 'none' })
      const payload = {
        title: f.title, categoryId: f.categoryId, description: f.description,
        quantity: Number(f.quantity), unit: f.unit,
        price: f.price ? Number(f.price) : null, priceUnit: f.priceUnit,
        negotiable: f.negotiable, minOrderQty: f.minOrderQty ? Number(f.minOrderQty) : null,
        countyId: f.countyId, townId: f.townId, villageId: f.villageId || null,
        spec: f.spec || null, harvestTime: f.harvestTime || null,
        deliveryMode: f.deliveryMode, contactPhone: f.contactPhone || null,
        tradeNote: null, effectiveDays: f.effectiveDays, images: f.images,
      }
      this.submitting = true
      try {
        if (this.editId) await supplyApi.update(this.editId, payload)
        else await supplyApi.create(payload)
        uni.showToast({ title: this.editId ? '已更新，等待审核' : '已提交，等待审核' })
        setTimeout(() => uni.redirectTo({ url: '/pages/user/listings' }), 800)
      } catch (e) {} finally { this.submitting = false }
    },
  },
}
</script>

<style>
.modal-mask {
  position: fixed; left: 0; right: 0; top: 0; bottom: 0;
  background: rgba(0, 0, 0, 0.4);
  display: flex; align-items: flex-end; z-index: 999;
}
.modal {
  width: 100%; background: #fff;
  border-radius: 24rpx 24rpx 0 0;
  padding: 32rpx; max-height: 70vh; overflow-y: auto;
}
.thumb {
  position: relative; width: 160rpx; height: 160rpx;
  border-radius: 12rpx; overflow: hidden; margin: 0 16rpx 16rpx 0;
  background: #f4f6f4;
}
.thumb-img { width: 100%; height: 100%; }
.thumb-add {
  display: flex; align-items: center; justify-content: center;
  font-size: 56rpx; color: #9aa8a0; border: 2rpx dashed #cdd8d0;
}
.thumb-del {
  position: absolute; top: 0; right: 0; width: 40rpx; height: 40rpx;
  line-height: 36rpx; text-align: center; color: #fff; font-size: 32rpx;
  background: rgba(0, 0, 0, 0.5); border-bottom-left-radius: 12rpx;
}
.ai-assist-btn { font-size: 24rpx; color: #2e8b57; border: 1rpx solid #2e8b57; border-radius: 999rpx; padding: 4rpx 20rpx; }
</style>
