<template>
  <view class="page container">
    <view class="field">
      <text class="field-label">板块 *</text>
      <picker :range="topicNames" :value="topicIndex" @change="onTopic">
        <text>{{ topicNames[topicIndex] }}</text>
      </picker>
    </view>
    <view class="field">
      <text class="field-label">标题 *</text>
      <input v-model="form.title" class="input" maxlength="100" placeholder="一句话说清主题（至少 2 个字）" />
    </view>
    <view class="field" style="align-items:flex-start;">
      <text class="field-label">正文 *</text>
      <textarea v-model="form.content" class="input" style="min-height:260rpx;" maxlength="10000" placeholder="分享行情、求助、交易心得…（至少 5 个字）" />
    </view>
    <view class="field" style="align-items:flex-start;">
      <text class="field-label">图片</text>
      <view>
        <view class="imgs">
          <view v-for="(img, i) in form.images" :key="i" class="img-wrap">
            <image :src="img" class="img" mode="aspectFill" @tap="preview(i)" />
            <text class="img-del" @tap="removeImg(i)">×</text>
          </view>
          <view v-if="form.images.length < 6" class="img-add" @tap="chooseImage">＋</view>
        </view>
        <view class="muted mt8">最多 6 张，第一张为封面</view>
      </view>
    </view>
    <button class="btn btn-block mt24" :disabled="submitting" @tap="submit">{{ submitting ? '发布中...' : '发布帖子' }}</button>
  </view>
</template>

<script>
import { communityApi } from '../../api/index'
import { requireLogin } from '../../utils/request'
import { uploadImageSmart } from '../../api/index'

const TOPICS = [
  { key: 'MARKET', name: '行情讨论' },
  { key: 'CHAT', name: '闲聊' },
  { key: 'HELP', name: '求助互助' },
  { key: 'TRADE', name: '交易心得' },
]

export default {
  data() {
    return { topicIndex: 0, form: { title: '', content: '', images: [] }, submitting: false }
  },
  computed: {
    topicNames() { return TOPICS.map((t) => t.name) },
  },
  onLoad() { if (!requireLogin()) return },
  methods: {
    onTopic(e) { this.topicIndex = Number(e.detail.value) },
    chooseImage() {
      uni.chooseImage({
        count: 6 - this.form.images.length,
        success: async (res) => {
          for (const path of res.tempFilePaths) {
            try {
              // uploadImageSmart 返回 { url, ... }, 直接把对象塞进 images 会让后端存成 [object Object]
              const up = await uploadImageSmart(path)
              if (up && up.url) this.form.images.push(up.url)
            } catch (e) {}
          }
        },
      })
    },
    removeImg(i) { this.form.images.splice(i, 1) },
    preview(i) { uni.previewImage({ urls: this.form.images, current: i }) },
    async submit() {
      const f = this.form
      if (f.title.trim().length < 2) return uni.showToast({ title: '标题至少 2 个字', icon: 'none' })
      if (f.content.trim().length < 5) return uni.showToast({ title: '正文至少 5 个字', icon: 'none' })
      this.submitting = true
      try {
        await communityApi.createPost({
          topic: TOPICS[this.topicIndex].key,
          title: f.title.trim(),
          content: f.content.trim(),
          images: f.images.length ? f.images : null,
        })
        uni.showToast({ title: '已发布' })
        setTimeout(() => uni.navigateBack(), 700)
      } catch (e) {} finally { this.submitting = false }
    },
  },
}
</script>

<style>
.field { display: flex; align-items: center; margin-bottom: 20rpx; gap: 16rpx; }
.field-label { font-size: 26rpx; color: #666; flex-shrink: 0; width: 110rpx; }
.input { background: #f4f6f4; border-radius: 8rpx; padding: 16rpx 20rpx; font-size: 28rpx; flex: 1; }
.imgs { display: flex; flex-wrap: wrap; gap: 12rpx; }
.img-wrap { position: relative; }
.img { width: 160rpx; height: 160rpx; border-radius: 8rpx; background: #f4f6f4; }
.img-del { position: absolute; top: -10rpx; right: -10rpx; width: 40rpx; height: 40rpx; line-height: 40rpx; text-align: center; background: #dd524d; color: #fff; border-radius: 999rpx; font-size: 26rpx; }
.img-add { width: 160rpx; height: 160rpx; border: 2rpx dashed #c0c8c0; border-radius: 8rpx; display: flex; align-items: center; justify-content: center; font-size: 60rpx; color: #9aa8a0; }
.btn-block { width: 100%; }
</style>
