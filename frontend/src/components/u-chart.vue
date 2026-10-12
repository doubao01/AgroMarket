<template>
  <view class="uchart-wrap" :style="{ height: cHeight + 'px' }">
    <canvas
      :id="canvasId"
      :canvas-id="canvasId"
      type="2d"
      class="uchart-canvas"
      :style="{ width: cWidth + 'px', height: cHeight + 'px' }"
    />
    <view v-if="!hasData" class="uchart-empty">暂无数据</view>
  </view>
</template>

<script>
import uCharts from '@qiun/ucharts'

let seq = 0

export default {
  name: 'u-chart',
  props: {
    chartType: { type: String, default: 'column' },
    categories: { type: Array, default: () => [] },
    series: { type: Array, default: () => [] },
    dark: { type: Boolean, default: false },
    height: { type: Number, default: 240 },
    legend: { type: Boolean, default: true },
    dataLabel: { type: Boolean, default: false },
  },
  data() {
    return {
      canvasId: 'uchart_' + Date.now() + '_' + (++seq),
      cWidth: 320,
      cHeight: this.height,
      chart: null,
      ready: false,
    }
  },
  computed: {
    hasData() {
      return this.series.some(s => (s.data || []).length > 0)
    },
    theme() {
      return this.dark
        ? { fontColor: '#c9dcd0', gridColor: '#2a4334', legendFontColor: '#c9dcd0', background: 'transparent' }
        : { fontColor: '#4a5a50', gridColor: '#e4ebe6', legendFontColor: '#7a8a80', background: 'transparent' }
    },
  },
  watch: {
    categories() { this.draw() },
    series: { handler() { this.draw() }, deep: true },
    dark() { this.draw() },
  },
  mounted() {
    this.measure()
  },
  methods: {
    measure() {
      const query = uni.createSelectorQuery().in(this)
      query.select('#' + this.canvasId).fields({ node: true, size: true }, res => {
        if (!res || !res.node) return
        this.cWidth = res.width || this.cWidth
        this.cHeight = res.height || this.cHeight
        this.canvasNode = res.node
        this.ready = true
        this.draw()
      }).exec()
    },
    draw() {
      if (!this.ready || !this.hasData) return
      const ctx = this.canvasNode.getContext('2d')
      const dpr = (uni.getSystemInfoSync().pixelRatio) || 2
      this.canvasNode.width = this.cWidth * dpr
      this.canvasNode.height = this.cHeight * dpr
      ctx.scale(dpr, dpr)
      const t = this.theme
      const base = {
        context: ctx,
        width: this.cWidth,
        height: this.cHeight,
        pixelRatio: 1,
        background: t.background,
        fontColor: t.fontColor,
        legend: this.legend
          ? { show: true, position: 'bottom', float: 'center', fontSize: 11, fontColor: t.legendFontColor }
          : { show: false },
        padding: [10, 10, 0, 10],
        dataLabel: this.dataLabel,
        enableScroll: false,
        animation: true,
      }
      const manyPoints = this.categories.length > 15
      if (this.chartType === 'ring') {
        this.chart = new uCharts({
          ...base,
          type: 'ring',
          extra: { ring: { ringWidth: 24, offsetAngle: 0, labelWidth: 12 }, pie: {} },
          series: this.series,
          subtitle: { name: '', fontSize: 0 },
          title: { name: '', fontSize: 0 },
        })
      } else if (this.chartType === 'mix') {
        this.chart = new uCharts({
          ...base,
          type: 'mix',
          categories: this.categories,
          series: this.series,
          xAxis: { disableGrid: true, fontSize: 10, fontColor: t.fontColor, labelCount: manyPoints ? 6 : this.categories.length, rotateLabel: manyPoints },
          yAxis: { gridType: 'dash', dashLength: 4, gridColor: t.gridColor, fontSize: 10, fontColor: t.fontColor, splitNumber: 4 },
          extra: { column: { width: 8, barBorderCircle: true }, line: { width: 2, activeType: 'none' } },
        })
      } else {
        this.chart = new uCharts({
          ...base,
          type: 'column',
          categories: this.categories,
          series: this.series,
          xAxis: { disableGrid: true, fontSize: 10, fontColor: t.fontColor, labelCount: manyPoints ? 6 : this.categories.length, rotateLabel: manyPoints },
          yAxis: { gridType: 'dash', dashLength: 4, gridColor: t.gridColor, fontSize: 10, fontColor: t.fontColor, splitNumber: 4 },
          extra: { column: { width: 8, barBorderCircle: true } },
        })
      }
    },
  },
}
</script>

<style scoped>
.uchart-wrap { position: relative; }
.uchart-canvas { display: block; }
.uchart-empty {
  position: absolute; left: 0; right: 0; top: 40%;
  text-align: center; font-size: 24rpx; color: #a7b3ac;
}
</style>
