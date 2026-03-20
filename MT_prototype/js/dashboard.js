/**
 * Dashboard Module
 * 處理命題儀表板的數字統計與 Chart.js 動態渲染邏輯。
 * Version: 1.0 (DEMO)
 */

// --- Fake Data per Project ID ---
const dashboardDataDb = {
    'P2026-01': {
        stats: { target: 1200, achieved: 450, reviewing: 600, warning: 24 },
        doughnut: [450, 750], // Achieved, Remaining
        barLabels: ['單選題', '精選題', '閱讀測驗', '聽力測驗', '短文寫作'],
        barDataDraft: [50, 20, 10, 5, 2],
        barDataReviewing: [300, 150, 80, 50, 20],
        barDataAchieved: [200, 100, 70, 60, 20],
        urgents: [
            { type: '修題逾期', teacher: '王小明 老師', task: '聽力測驗 T-1029', days: -2 },
            { type: '繳交逾期', teacher: '陳大文 老師', task: '單選題 批次 #4', days: -1 },
            { type: '退回未改', teacher: '李雪 老師', task: '閱讀測驗 R-302', days: -1 },
            { type: '即將逾期', teacher: '林偉 老師', task: '單選題 批次 #5', days: 1 }
        ],
        logs: [
            { time: '10:45 AM', action: '王小明 提交了 5 題單選題至互審區。' },
            { time: '09:30 AM', action: '系統 自動寄發 3 封逾期提醒信件。' },
            { time: '08:15 AM', action: '總召(專員) 將 R-302 退回重修。' },
            { time: '昨天 17:00', action: '陳大文 登入系統。' }
        ]
    },
    'P2026-02': {
        stats: { target: 800, achieved: 0, reviewing: 10, warning: 0 },
        doughnut: [0, 800],
        barLabels: ['單選題', '精選題', '閱讀測驗', '聽力測驗', '短文寫作'],
        barDataDraft: [5, 3, 2, 0, 0],
        barDataReviewing: [0, 0, 0, 0, 0],
        barDataAchieved: [0, 0, 0, 0, 0],
        urgents: [],
        logs: [
            { time: '昨天', action: '管理員 建立了 P2026-02 秋季梯次。' },
            { time: '昨天', action: '系統 發送啟動通知給 45 位合作教師。' }
        ]
    },
    // fallback data
    'default': {
        stats: { target: 1000, achieved: 1000, reviewing: 0, warning: 0 },
        doughnut: [1000, 0],
        barLabels: ['單選題', '精選題', '閱讀測驗', '聽力測驗', '短文寫作'],
        barDataDraft: [0, 0, 0, 0, 0],
        barDataReviewing: [0, 0, 0, 0, 0],
        barDataAchieved: [400, 250, 150, 150, 50],
        urgents: [],
        logs: [
            { time: '2025/12', action: '梯次已正式結案歸檔。' }
        ]
    }
};

// Global Chart Instances
let doughnutChartInstance = null;
let barChartInstance = null;

// Theming Colors (align with Tailwind)
const colors = {
    morandi: '#6B8EAD',
    morandiLight: 'rgba(107, 142, 173, 0.4)',
    sage: '#8EAB94',
    sageLight: 'rgba(142, 171, 148, 0.4)',
    terracotta: '#D98A6C',
    amber: '#f59e0b',
    slateMain: '#374151',
    gray300: '#d1d5db',
    gray100: '#f3f4f6'
};

document.addEventListener('DOMContentLoaded', () => {
    // [DEMO 用途] 確保留在 dashboard.html 頁面有角色權限可以看。為了方便長官檢閱所有畫面，暫時註解權限跳轉。
    /*
    const userStr = localStorage.getItem('cwt_user');
    if (userStr) {
        const user = JSON.parse(userStr);
        if (user.role !== 'ADMIN') {
            Swal.fire({
                icon: 'error', title: '權限不足', text: '您不具有檢視「命題儀錶板」的權限。即將為您導回首頁。',
                showConfirmButton: false, timer: 2000
            }).then(() => window.location.href = 'firstpage.html');
            return;
        }
    }
    */

    // 初始化圖表載體 (空白圖表)，隨後立刻調用資料餵入程序
    initCharts();

    // 取得當前專案資料並繪製
    const currentProjId = localStorage.getItem('cwt_current_project');
    loadDashboardData(currentProjId);

    // 監聽 Navbar 拋出的專案改變事件
    document.addEventListener('projectChanged', (e) => {
        loadDashboardData(e.detail.id);
    });
});

/**
 * 載入並刷新 Dashboard 資料
 */
function loadDashboardData(projectId) {
    // Use fallback data if project ID not perfectly matched in DEMO DB
    const data = dashboardDataDb[projectId] || dashboardDataDb['default'];

    // 1. Update Top Stat Cards
    animateValue('statTotalTarget', data.stats.target);
    animateValue('statAchieved', data.stats.achieved);
    animateValue('statReviewing', data.stats.reviewing);
    animateValue('statWarning', data.stats.warning);

    // 2. Update Charts
    updateDoughnutChart(data);
    updateBarChart(data);

    // 3. Update Lists
    renderUrgentsList(data.urgents);
    renderLogsList(data.logs);
}

/**
 * 數字跑到定值的簡單動畫
 */
function animateValue(id, target) {
    const obj = document.getElementById(id);
    if (!obj) return;

    // 如果是 0 就直接給避免閃屏
    if (target === 0) {
        obj.innerHTML = '0';
        return;
    }

    let start = 0;
    const duration = 800;
    const stepTime = Math.abs(Math.floor(duration / target));
    const stepDelay = stepTime < 5 ? 5 : stepTime;

    let current = start;
    const increment = target > 50 ? Math.ceil(target / 20) : 1;

    const timer = setInterval(() => {
        current += increment;
        if (current >= target) {
            current = target;
            clearInterval(timer);
        }
        obj.innerHTML = current.toLocaleString(); // add commas
    }, stepDelay);
}

// ================= CHART.JS CONFIGURATIONS =================

function initCharts() {
    // Setup Doughnut Chart
    const ctxD = document.getElementById('doughnutChart').getContext('2d');
    doughnutChartInstance = new Chart(ctxD, {
        type: 'doughnut',
        data: {
            labels: ['已達成題數', '剩餘缺口'],
            datasets: [{
                data: [0, 10], // initial fake
                backgroundColor: [colors.sage, colors.gray100],
                borderWidth: 0,
                hoverOffset: 4
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            cutout: '75%', // make ring thinner
            plugins: {
                legend: { position: 'bottom', labels: { font: { family: "'Noto Sans TC', sans-serif" } } },
                tooltip: { bodyFont: { family: "'Noto Sans TC', sans-serif" } }
            }
        }
    });

    // Setup Bar Chart (Stacked)
    const ctxB = document.getElementById('barChart').getContext('2d');
    barChartInstance = new Chart(ctxB, {
        type: 'bar',
        data: {
            labels: [],
            datasets: []
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            scales: {
                x: { stacked: true, grid: { display: false } },
                y: { stacked: true, border: { display: false } }
            },
            plugins: {
                legend: { position: 'bottom', labels: { boxWidth: 12, font: { family: "'Noto Sans TC', sans-serif" } } },
                tooltip: {
                    mode: 'index',
                    intersect: false,
                    bodyFont: { family: "'Noto Sans TC', sans-serif" }
                }
            }
        }
    });
}

function updateDoughnutChart(data) {
    if (!doughnutChartInstance) return;

    const dData = data.doughnut;
    const total = dData[0] + dData[1];
    const percentage = total === 0 ? 0 : Math.round((dData[0] / total) * 100);

    doughnutChartInstance.data.datasets[0].data = dData;
    doughnutChartInstance.update();

    // 更新中間下方的達成率文字
    const percentEl = document.getElementById('overallProgressText');
    if (percentEl) {
        percentEl.innerHTML = `整體達成率：<span class="text-[var(--color-sage)] ml-1">${percentage}%</span>`;
    }
}

function updateBarChart(data) {
    if (!barChartInstance) return;

    barChartInstance.data.labels = data.barLabels;
    barChartInstance.data.datasets = [
        {
            label: '已結案入庫',
            data: data.barDataAchieved,
            backgroundColor: colors.sage,
            borderRadius: 4
        },
        {
            label: '各階審修中',
            data: data.barDataReviewing,
            backgroundColor: colors.amber,
            borderRadius: 4
        },
        {
            label: '教師草稿',
            data: data.barDataDraft,
            backgroundColor: colors.gray300,
            borderRadius: 4
        }
    ];
    barChartInstance.update();
}


// ================= LIST RENDERERS =================

function renderUrgentsList(urgents) {
    const list = document.getElementById('urgentTasksList');
    if (!list) return;

    if (urgents.length === 0) {
        list.innerHTML = `<li class="p-8 text-center text-sm text-gray-400">目前沒有逾期或緊急的事項 🎉</li>`;
        return;
    }

    let html = '';
    urgents.forEach(u => {
        const isWarning = u.days < 0;
        const badgeColor = isWarning ? 'bg-red-100 text-red-600' : 'bg-orange-100 text-orange-600';
        const dayStr = isWarning ? `逾期 ${Math.abs(u.days)} 天` : `剩餘 ${u.days} 天`;

        html += `
            <li class="px-5 py-3 hover:bg-gray-50 flex items-center justify-between gap-2 transition-colors">
                <div class="flex flex-col">
                    <span class="text-xs font-bold ${isWarning ? 'text-[var(--color-terracotta)]' : 'text-amber-600'} mb-0.5">${u.type}</span>
                    <span class="text-sm font-bold text-[var(--color-slate-main)]">${u.teacher} <span class="text-gray-500 font-normal">| ${u.task}</span></span>
                </div>
                <div class="flex-shrink-0">
                    <span class="text-xs font-bold px-2 py-1 rounded ${badgeColor}">${dayStr}</span>
                </div>
            </li>
        `;
    });
    list.innerHTML = html;
}

function renderLogsList(logs) {
    const list = document.getElementById('auditLogList');
    if (!list) return;

    if (logs.length === 0) {
        list.innerHTML = `<li class="text-sm text-gray-400 pl-8">尚無任何稽核紀錄。</li>`;
        return;
    }

    let html = '';
    logs.forEach((log, index) => {
        // 第一個項目特別打亮標記
        const markerColor = index === 0 ? 'bg-[var(--color-morandi)] ring-4 ring-blue-50' : 'bg-gray-300 ring-4 ring-white';
        const textColor = index === 0 ? 'text-[var(--color-slate-main)] font-bold' : 'text-gray-600';

        html += `
            <li class="relative pl-10">
                <div class="absolute w-3 h-3 rounded-full left-[11px] top-1.5 ${markerColor} z-10 box-content shadow-sm"></div>
                <div class="flex flex-col">
                    <span class="text-xs text-gray-400 mb-0.5">${log.time}</span>
                    <span class="text-sm ${textColor} leading-relaxed">${log.action}</span>
                </div>
            </li>
        `;
    });
    list.innerHTML = html;
}
