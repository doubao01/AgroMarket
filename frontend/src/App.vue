<script>
import { ensureLogin } from './utils/request'

export default {
  onLaunch: function () {
    // 启动时静默校验本地 token 是否仍有效
    ensureLogin(true)
  },
  onShow: function () {},
  onHide: function () {},
}
</script>

<style>
page {
  --primary: #2e8b57;
  --primary-dark: #246e45;
  --primary-light: #e8f5ee;
  --accent: #f2952b;
  --danger: #dd524d;
  --text: #1f2d24;
  --text-grey: #7a8a80;
  --bg: #f4f6f4;
  --card: #ffffff;
  --border: #e3e9e4;
  background-color: #f4f6f4;
  color: #1f2d24;
  font-size: 28rpx;
}

view, text, button, input, textarea, picker {
  box-sizing: border-box;
}

.page {
  min-height: 100vh;
  padding-bottom: 40rpx;
}

.container {
  padding: 24rpx;
}

.card {
  background: #ffffff;
  border-radius: 20rpx;
  padding: 24rpx;
  margin-bottom: 20rpx;
  box-shadow: 0 4rpx 16rpx rgba(46, 139, 87, 0.06);
}

.row { display: flex; align-items: center; }
.between { display: flex; align-items: center; justify-content: space-between; }
.wrap { flex-wrap: wrap; }
.flex1 { flex: 1; }
.muted { color: #7a8a80; font-size: 24rpx; }
.primary { color: #2e8b57; }
.accent { color: #f2952b; }
.bold { font-weight: 600; }
.mt8 { margin-top: 8rpx; }
.mt16 { margin-top: 16rpx; }
.mt24 { margin-top: 24rpx; }
.mb16 { margin-bottom: 16rpx; }

.price {
  color: #f2952b;
  font-weight: 700;
}
.price-big { color: #f2952b; font-weight: 700; font-size: 40rpx; }

.tag {
  display: inline-block;
  padding: 4rpx 14rpx;
  border-radius: 999rpx;
  font-size: 22rpx;
  background: #e8f5ee;
  color: #2e8b57;
  margin-right: 12rpx;
}

.btn {
  background: #2e8b57;
  color: #fff;
  border-radius: 999rpx;
  font-size: 30rpx;
  line-height: 2.6;
  border: none;
}
.btn::after { border: none; }
.btn-plain {
  background: #fff;
  color: #2e8b57;
  border: 2rpx solid #2e8b57;
}
.btn-accent { background: #f2952b; }
.btn-block { width: 100%; }
.btn-sm { font-size: 26rpx; line-height: 2.2; padding: 0 28rpx; }

.empty {
  text-align: center;
  color: #7a8a80;
  padding: 120rpx 0;
  font-size: 28rpx;
}

.field {
  display: flex;
  align-items: center;
  padding: 24rpx 0;
  border-bottom: 1rpx solid #e3e9e4;
}
.field-label { width: 180rpx; color: #1f2d24; }
.field input, .field textarea { flex: 1; }
.input {
  background: #f4f6f4;
  border-radius: 12rpx;
  padding: 20rpx 24rpx;
  font-size: 28rpx;
}
.chip {
  padding: 10rpx 24rpx;
  border-radius: 999rpx;
  background: #fff;
  border: 1rpx solid #e3e9e4;
  color: #1f2d24;
  font-size: 26rpx;
  margin: 0 16rpx 16rpx 0;
}
.chip.active {
  background: #e8f5ee;
  border-color: #2e8b57;
  color: #2e8b57;
  font-weight: 600;
}
</style>
