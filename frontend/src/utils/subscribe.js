// 微信小程序一次性订阅消息授权上报 (R2.1)
// 仅 mp-weixin 平台生效; 模板 ID 未配置时静默跳过 (与后端 WeChat 未配置降级策略一致)。
// 模板 ID 运营在微信后台申请后, 填入下方 TEMPLATE_IDS 即自动启用。
import { wechatApi } from '../api/index'

// msgType -> 模板 ID (空串表示未申请)
const TEMPLATE_IDS = {
  QUOTE: '',  // 新报价/报价状态变更
  ORDER: '',  // 订单状态变更
  AUDIT: '',  // 审核结果
}

/**
 * 在触发业务动作 (提交报价/创建订单/发布供需) 前调用。
 * 请求一次性订阅授权并把授权结果上报后端 (+1 额度), 全程静默失败。
 * @param {'QUOTE'|'ORDER'|'AUDIT'} templateKey
 */
export function requestSubscribe(templateKey) {
  // #ifdef MP-WEIXIN
  const tmplId = TEMPLATE_IDS[templateKey]
  if (!tmplId) return
  try {
    uni.requestSubscribeMessage({
      tmplIds: [tmplId],
      complete: (res) => {
        const accepted = !!res && res[tmplId] === 'accept'
        try { wechatApi.subscribeReport(templateKey, accepted) } catch (e) {}
      },
    })
  } catch (e) {}
  // #endif
}
