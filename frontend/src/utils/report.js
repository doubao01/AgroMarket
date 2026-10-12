import { reportApi } from '../api/index'
import { requireLogin } from './request'

/**
 * 打开举报弹窗并提交。
 * @param {string} targetType SUPPLY | DEMAND | USER | QUOTE
 * @param {number|string} targetId
 * @param {string} [title] 弹窗标题
 */
export function reportTarget(targetType, targetId, title) {
  if (!requireLogin()) return
  uni.showModal({
    title: title || '举报',
    editable: true,
    placeholderText: '请填写举报原因（至少 5 个字）',
    success: async (r) => {
      if (!r.confirm) return
      const reason = (r.content || '').trim()
      if (reason.length < 5) {
        uni.showToast({ title: '举报原因至少 5 个字', icon: 'none' })
        return
      }
      try {
        await reportApi.create({ targetType, targetId, reason })
        uni.showToast({ title: '举报已提交，等待处理' })
      } catch (e) {}
    },
  })
}
