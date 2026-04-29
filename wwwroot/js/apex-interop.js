// ======================================================================
//  apex-interop.js — ApexCharts JS Interop 封裝
//  提供 render / update / destroy 三個方法供 Blazor C# 端呼叫
//  所有圖表實例以 elementId 為 key 統一管理，避免重複建立或記憶體洩漏
// ======================================================================

window.apexInterop = {
    /** @type {Object.<string, ApexCharts>} 已建立的圖表實例 */
    charts: {},

    /**
     * 初始化或重繪圖表。
     * 若已有同 id 實例先 destroy 再重建，確保切換梯次時資料乾淨。
     * @param {string} elementId - DOM 元素 id
     * @param {object} options   - ApexCharts 完整設定物件
     */
    render(elementId, options) {
        // 先銷毀舊實例
        if (this.charts[elementId]) {
            this.charts[elementId].destroy();
            delete this.charts[elementId];
        }

        const el = document.getElementById(elementId);
        if (!el) return;

        const chart = new ApexCharts(el, options);
        chart.render();
        this.charts[elementId] = chart;
    },

    /**
     * 更新圖表資料與設定（不重建 DOM）。
     * @param {string} elementId
     * @param {object} options
     */
    update(elementId, options) {
        const chart = this.charts[elementId];
        if (chart) {
            chart.updateOptions(options, true, true);
        }
    },

    /**
     * 題型缺口達成率 — 100% 堆疊水平條圖。
     * formatter 函式必須在 JS 端定義，C# 端只傳純資料。
     * @param {string} elementId - DOM 元素 id（chart-achievement）
     * @param {Array<{typeName:string, produced:number, target:number, fillColor:string}>} items
     */
    renderAchievement(elementId, items) {
        const producedData = items.map(it => ({ x: it.typeName, y: it.produced, fillColor: it.fillColor }));
        // target=0 時第二系列設為 1，避免 100% 模式出現 NaN；label 內另行判斷隱藏
        const gapData = items.map(it => ({
            x: it.typeName,
            y: it.target === 0 ? 1 : Math.max(0, it.target - it.produced)
        }));

        const options = {
            chart: {
                type: 'bar',
                height: '100%',
                stacked: true,
                stackType: '100%',
                toolbar: { show: false },
                fontFamily: 'Noto Sans TC, sans-serif'
            },
            plotOptions: {
                bar: {
                    horizontal: true,
                    barHeight: '70%',
                    borderRadius: 4,
                    borderRadiusApplication: 'end',
                    borderRadiusWhenStacked: 'last'
                }
            },
            series: [
                { name: '已產出', data: producedData },
                { name: '缺口',   data: gapData }
            ],
            colors: ['#8EAB94', '#E5E7EB'],
            fill: { opacity: 1 },
            states: {
                // hover 時整條變深 8%，提升互動回饋（state-clarity）
                hover:  { filter: { type: 'darken', value: 0.92 } },
                active: { allowMultipleDataPointsSelection: false,
                          filter: { type: 'darken', value: 0.86 } }
            },
            // 預設 dataLabels 會貼齊 produced 區段，導致達成率低時標籤偏左。
            // 改用 annotations.points 把標籤置中於整條 bar（100% 模式 x=50 即中央）。
            dataLabels: { enabled: false },
            annotations: {
                points: items
                    .filter(it => it.target > 0)
                    .map(it => {
                        const pct = Math.round(it.produced / it.target * 100);
                        return {
                            x: 50,
                            y: it.typeName,
                            marker: { size: 0, strokeWidth: 0 },
                            label: {
                                text: `${it.produced}/${it.target} (${pct}%)`,
                                borderWidth: 0,
                                offsetY: 0,
                                style: {
                                    fontSize: '11px',
                                    fontWeight: 600,
                                    fontFamily: 'Noto Sans TC, sans-serif',
                                    color: '#374151',
                                    background: 'rgba(255,255,255,0.92)',
                                    padding: { left: 6, right: 6, top: 2, bottom: 2 }
                                }
                            }
                        };
                    })
            },
            xaxis: {
                labels: { show: false },
                axisBorder: { show: false },
                axisTicks: { show: false }
            },
            yaxis: {
                labels: { style: { fontSize: '12px', colors: '#374151' } }
            },
            grid: { show: false },
            tooltip: {
                shared: true,
                intersect: false,
                // 自訂 HTML tooltip 完全控制版面，避免 ApexCharts 自動加 series-name 前綴造成重複
                custom: function({ dataPointIndex }) {
                    const item = items[dataPointIndex];
                    if (!item) return '';
                    const head = `
                        <div style="padding:6px 10px;border-bottom:1px solid #E5E7EB;
                                    font-weight:600;font-size:12px;color:#374151;">
                            ${item.typeName}
                        </div>`;

                    if (item.target === 0) {
                        return `<div style="font-family:Noto Sans TC,sans-serif;min-width:160px;">
                            ${head}
                            <div style="padding:8px 10px;font-size:12px;color:#6B7280;">尚未設定目標</div>
                        </div>`;
                    }

                    const gap  = Math.max(0, item.target - item.produced);
                    const pct  = Math.round(item.produced / item.target * 100);
                    const row = (dotColor, label, value) => `
                        <div style="display:flex;align-items:center;gap:8px;
                                    padding:4px 10px;font-size:12px;color:#374151;">
                            <span style="width:8px;height:8px;border-radius:50%;
                                         background:${dotColor};display:inline-block;"></span>
                            <span style="flex:1;">${label}</span>
                            <span style="font-variant-numeric:tabular-nums;font-weight:600;">
                                ${value}
                            </span>
                        </div>`;

                    return `
                        <div style="font-family:Noto Sans TC,sans-serif;min-width:200px;
                                    background:#fff;border-radius:6px;overflow:hidden;">
                            ${head}
                            ${row('#8EAB94', '已產出', `${item.produced} 題`)}
                            ${row('#D1D5DB', '缺口',   `${gap} 題`)}
                            <div style="padding:6px 10px;border-top:1px solid #F3F4F6;
                                        font-size:11px;color:#6B7280;
                                        display:flex;justify-content:space-between;">
                                <span>達成率</span>
                                <span style="font-variant-numeric:tabular-nums;font-weight:600;color:#374151;">
                                    ${pct}%（目標 ${item.target}）
                                </span>
                            </div>
                        </div>`;
                }
            },
            legend: { show: false }
        };

        if (this.charts[elementId]) {
            this.charts[elementId].destroy();
            delete this.charts[elementId];
        }
        const el = document.getElementById(elementId);
        if (!el) return;
        const chart = new ApexCharts(el, options);
        chart.render();
        this.charts[elementId] = chart;
    },

    /**
     * 銷毀圖表並釋放記憶體。
     * 頁面離開或梯次切換時呼叫，避免記憶體洩漏。
     * @param {string} elementId
     */
    destroy(elementId) {
        const chart = this.charts[elementId];
        if (chart) {
            chart.destroy();
            delete this.charts[elementId];
        }
    }
};
