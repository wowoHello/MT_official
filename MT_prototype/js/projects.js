/**
 * Projects Module
 * 負責命題專案管理的列表呈現、詳細檢視、結案防呆與 8 階段時程連動表單。
 * Version: 1.0 (DEMO)
 */

// --- 假資料：專案列表 (需涵蓋 8 階段日期與配額) ---
let projectsDb = [
    {
        id: 'P2026-01', name: '115年度 春季全民中檢', status: 'active', year: '115',
        school: '無 (自辦)', creator: '系統管理員',
        stages: [
            { id: 's1', name: '產學計畫區間', start: '2026-02-01', end: '2026-06-30' },
            { id: 's2', name: '命題階段', start: '2026-02-15', end: '2026-03-15' },
            { id: 's3', name: '交互審題', start: '2026-03-16', end: '2026-03-25' },
            { id: 's4', name: '互審修題', start: '2026-03-26', end: '2026-04-05' },
            { id: 's5', name: '專家審題', start: '2026-04-06', end: '2026-04-20' },
            { id: 's6', name: '專審修題', start: '2026-04-21', end: '2026-05-05' },
            { id: 's7', name: '總召審題', start: '2026-05-06', end: '2026-05-20' },
            { id: 's8', name: '總召修題', start: '2026-05-21', end: '2026-05-30' }
        ],
        targets: { 'single': 500, 'select': 200, 'readGroup': 100, 'longText': 50, 'shortGroup': 50, 'listen': 100, 'listenGroup': 30 },
        persons: [
            { id: 'T1001', role: ['命題教師', '互審教師'], quotas: { single: 150, select: 100, readGroup: 25, longText: 10, shortGroup: 10, listen: 25, listenGroup: 10 } },
            { id: 'T1002', role: ['命題教師'], quotas: { single: 150, select: 50, readGroup: 25, longText: 20, shortGroup: 20, listen: 25, listenGroup: 10 } },
            { id: 'C2001', role: ['專審委員'] },
            { id: 'C2002', role: ['專審委員'] },
            { id: 'S3001', role: ['總召(專員)'] },
            { id: 'T1003', role: ['命題教師'], quotas: { single: 100, select: 50, readGroup: 25, longText: 10, shortGroup: 10, listen: 25, listenGroup: 5 } },
            { id: 'T1004', role: ['命題教師'], quotas: { single: 100, select: 0, readGroup: 25, longText: 10, shortGroup: 10, listen: 25, listenGroup: 5 } },
            { id: 'T1005', role: ['互審教師'] },
            { id: 'C2003', role: ['專審委員'] },
            { id: 'C2004', role: ['專家學者'] },
            { id: 'S3002', role: ['總召(專員)'] }
        ]
    },
    {
        id: 'P2026-02', name: '115年度 秋季全民中檢', status: 'preparing', year: '115',
        school: '南臺科大產學', creator: '系統管理員',
        stages: [
            { id: 's1', name: '產學計畫區間', start: '2026-08-01', end: '2026-12-31' },
            { id: 's2', name: '命題階段', start: '2026-08-10', end: '2026-09-10' },
            { id: 's3', name: '交互審題', start: '2026-09-11', end: '2026-09-20' },
            { id: 's4', name: '互審修題', start: '2026-09-21', end: '2026-09-30' },
            { id: 's5', name: '專家審題', start: '2026-10-01', end: '2026-10-15' },
            { id: 's6', name: '專審修題', start: '2026-10-16', end: '2026-10-31' },
            { id: 's7', name: '總召審題', start: '2026-11-01', end: '2026-11-15' },
            { id: 's8', name: '總召修題', start: '2026-11-16', end: '2026-11-30' }
        ],
        targets: { 'single': 400, 'select': 150, 'readGroup': 80, 'longText': 40, 'shortGroup': 40, 'listen': 80, 'listenGroup': 20 },
        persons: [
            { id: 'T1001', role: '命題教師' }
        ]
    },
    {
        id: 'P2025-02', name: '114年度 秋季全民中檢', status: 'closed', year: '114',
        school: '無 (自辦)', creator: '陳督導', closedDate: '2025-09-25',
        stages: [
            { id: 's1', name: '產學計畫區間', start: '2025-08-01', end: '2025-12-31' },
            { id: 's2', name: '命題階段', start: '2025-08-10', end: '2025-09-10' },
            { id: 's3', name: '交互審題', start: '2025-09-11', end: '2025-09-20' },
            { id: 's4', name: '互審修題', start: '2025-09-21', end: '2025-09-30' },
            { id: 's5', name: '專家審題', start: '2025-10-01', end: '2025-10-15' },
            { id: 's6', name: '專審修題', start: '2025-10-16', end: '2025-10-31' },
            { id: 's7', name: '總召審題', start: '2025-11-01', end: '2025-11-15' },
            { id: 's8', name: '總召修題', start: '2025-11-16', end: '2025-11-30' }
        ],
        targets: { 'single': 600, 'select': 200, 'readGroup': 150, 'longText': 80, 'shortGroup': 80, 'listen': 100, 'listenGroup': 50 },
        persons: [
            { id: 'T1001', role: '命題教師' },
            { id: 'T1002', role: '互審教師' },
            { id: 'C2001', role: '專審委員' },
            { id: 'C2002', role: '專審委員' }
        ]
    },
    {
        id: 'P2025-01', name: '114年度 春季全民中檢', status: 'closed', year: '114',
        school: '無 (自辦)', creator: '陳督導', closedDate: '2025-03-25',
        stages: [
            { id: 's1', name: '產學計畫區間', start: '2025-02-01', end: '2025-06-30' },
            { id: 's2', name: '命題階段', start: '2025-02-15', end: '2025-03-15' },
            { id: 's3', name: '交互審題', start: '2025-03-16', end: '2025-03-25' },
            { id: 's4', name: '互審修題', start: '2025-03-26', end: '2025-04-05' },
            { id: 's5', name: '專家審題', start: '2025-04-06', end: '2025-04-20' },
            { id: 's6', name: '專審修題', start: '2025-04-21', end: '2025-05-05' },
            { id: 's7', name: '總召審題', start: '2025-05-06', end: '2025-05-20' },
            { id: 's8', name: '總召修題', start: '2025-05-21', end: '2025-05-30' }
        ],
        targets: { 'single': 500, 'select': 200, 'readGroup': 150, 'longText': 80, 'shortGroup': 80, 'listen': 100, 'listenGroup': 50 },
        persons: []
    }
];

// --- Fake Data: 7種題型種類、教師人才庫、系統權限角色 ---
const questionTypesMap = {
    'single': '一般單選題',
    'select': '精選單選題',
    'readGroup': '閱讀題組',
    'longText': '長文題目',
    'shortGroup': '短文題組',
    'listen': '聽力測驗',
    'listenGroup': '聽力題組'
};

const talentPoolDb = [
    { id: 'T1001', name: '劉雅婷 老師' },
    { id: 'T1002', name: '王健明 老師' },
    { id: 'C2001', name: '李教授' },
    { id: 'C2002', name: '陳副教授' },
    { id: 'S3001', name: '林總召' },
    { id: 'S3002', name: '許編輯' },
    { id: 'T1003', name: '張心怡 老師' },
    { id: 'T1004', name: '吳家豪 老師' },
    { id: 'T1005', name: '林柏宇 老師' },
    { id: 'C2003', name: '郭教授' },
    { id: 'C2004', name: '黃副教授' },
    { id: 'T1006', name: '陳彥廷 老師' },
    { id: 'T1007', name: '蔡佳玲 老師' },
    { id: 'S3003', name: '楊專員' }
];

const availableRolesDb = [
    '命題教師', '互審教師', '專審委員', '總召(專員)', '內部人員'
];

// UI Control State
let currentFilter = 'all'; // all, active, closed
let selectedProjectId = null;
let isEditMode = false;
let currentAllocationData = []; // [ { id: 'T1001', name: '劉雅婷', quotas: { ... } }, ... ]
const todayStr = new Date().toISOString().split('T')[0]; // YYYY-MM-DD for comparisions

document.addEventListener('DOMContentLoaded', () => {
    // [DEMO 用途] 檢查登入權限 (管理層專屬) - 為了方便展示，暫時關閉此限制
    /*
    const userStr = localStorage.getItem('cwt_user');
    if (userStr) {
        const user = JSON.parse(userStr);
        if (user.role !== 'ADMIN') {
            Swal.fire({
                icon: 'error', title: '權限不足', text: '「命題專案管理」為管理員專屬功能。即將導回首頁。',
                showConfirmButton: false, timer: 2000
            }).then(() => window.location.href = 'firstpage.html');
            return;
        }
    }
    */

    // Bind Filter & Search
    document.querySelectorAll('.filter-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
            document.querySelectorAll('.filter-btn').forEach(b => {
                b.classList.remove('bg-[var(--color-morandi)]', 'text-white');
                b.classList.add('bg-white', 'text-gray-500');
            });
            e.target.classList.remove('bg-white', 'text-gray-500');
            e.target.classList.add('bg-[var(--color-morandi)]', 'text-white');
            currentFilter = e.target.getAttribute('data-filter');
            renderProjectList();
        });
    });

    document.getElementById('projectSearch').addEventListener('input', renderProjectList);

    // Initial Render
    // 防呆檢查目前的 currentFilter 是否為 undefined 或空白
    if (!currentFilter || currentFilter === 'null' || currentFilter === 'undefined') {
        currentFilter = 'all';
    }

    // 強制重設 UI 按鈕狀態
    document.querySelectorAll('.filter-btn').forEach(b => {
        b.classList.remove('bg-[var(--color-morandi)]', 'text-white');
        b.classList.add('bg-white', 'text-gray-500');
        if (b.getAttribute('data-filter') === currentFilter) {
            b.classList.remove('bg-white', 'text-gray-500');
            b.classList.add('bg-[var(--color-morandi)]', 'text-white');
        }
    });

    selectedProjectId = localStorage.getItem('cwt_current_project') || projectsDb[0].id;
    renderProjectList();
    renderProjectDetail(selectedProjectId);

    // Initial form elements rendering
    renderFormStageInputs();
    renderFormTargets();
    renderFormPersons(); // Render default list

    // Bind actions
    document.getElementById('btnOpenNewProject').addEventListener('click', () => openPanel(null));
    document.getElementById('btnEditProject').addEventListener('click', () => openPanel(selectedProjectId));
    document.getElementById('btnCloseProject').addEventListener('click', handleCloseProject);

    // Bind Person Search
    document.getElementById('frmPersonSearch').addEventListener('input', (e) => {
        renderFormPersons(e.target.value);
    });

    // Check if coming from navbar project switch
    document.addEventListener('projectChanged', (e) => {
        selectedProjectId = e.detail.id;
        renderProjectList();
        renderProjectDetail(selectedProjectId);
    });

    // Panel controls
    initSlideOverPanel();

    // Bind Auto Distribute button
    document.getElementById('btnAutoDistribute').addEventListener('click', handleAutoDistributeQuotas);
});


// ==============================================
//  Left List & Right Detail Rendering
// ==============================================

function renderProjectList() {
    const listContainer = document.getElementById('sidebarProjectListContainer');
    const searchVal = document.getElementById('projectSearch').value.toLowerCase();

    let filtered = projectsDb.filter(p => {
        const matchSearch = p.name.toLowerCase().includes(searchVal) || p.year.includes(searchVal);
        const matchFilter = currentFilter === 'all' ? true :
            (currentFilter === 'active' ? (p.status === 'active' || p.status === 'preparing') : p.status === 'closed');
        return matchSearch && matchFilter;
    });

    if (filtered.length === 0) {
        listContainer.innerHTML = '<div class="p-6 text-center text-sm text-gray-400">查無命題專案</div>';
        return;
    }

    const html = filtered.map(p => {
        const isSel = p.id === selectedProjectId;

        let statusBadge = '';
        if (p.status === 'active') statusBadge = '<span class="text-[10px] bg-blue-100 text-blue-700 px-1.5 py-0.5 rounded box-content font-bold">進行中</span>';
        else if (p.status === 'preparing') statusBadge = '<span class="text-[10px] bg-amber-100 text-amber-700 px-1.5 py-0.5 rounded box-content font-bold">準備中</span>';
        else statusBadge = '<span class="text-[10px] bg-gray-200 text-gray-600 px-1.5 py-0.5 rounded box-content font-bold">已結案</span>';

        return `
            <div class="px-5 py-4 cursor-pointer border-b border-gray-100 hover:bg-gray-50 flex flex-col gap-1 transition-colors ${isSel ? 'list-item-active' : ''}" onclick="selectProjectFromList('${p.id}')">
                <div class="flex justify-between items-start">
                    <span class="text-sm font-bold ${isSel ? 'text-[var(--color-morandi)]' : 'text-gray-800'} line-clamp-1">${p.name}</span>
                    ${statusBadge}
                </div>
                <div class="flex justify-between items-center mt-1">
                    <span class="text-xs text-gray-500">${p.year}年度 / ${p.school}</span>
                    <i class="fa-solid fa-chevron-right text-[10px] text-gray-300 ${isSel ? 'text-[var(--color-morandi)]' : ''}"></i>
                </div>
            </div>
        `;
    }).join('');

    listContainer.innerHTML = html;
}

window.selectProjectFromList = function (id) {
    selectedProjectId = id;
    renderProjectList();
    renderProjectDetail(id);

    // Sync with global header if using smaller screen
    if (window.innerWidth < 1024) {
        // optionally scroll to top or toggle detail view on mobile
    }
}

function renderProjectDetail(id) {
    const proj = projectsDb.find(p => p.id === id);
    const emptyState = document.getElementById('emptyDetailState');
    const content = document.getElementById('detailContent');

    if (!proj) {
        emptyState.classList.remove('hidden');
        content.classList.add('hidden');
        return;
    }

    emptyState.classList.add('hidden');
    content.classList.remove('hidden');

    // Basic Top Info
    document.getElementById('dtlYear').textContent = `${proj.year}年度`;
    document.getElementById('dtlName').textContent = proj.name;
    document.getElementById('dtlSchoolText').textContent = `合作學校：${proj.school}`;
    document.getElementById('dtlCreator').innerHTML = `<i class="fa-solid fa-user-gear w-4 text-center"></i> <span>建立者：${proj.creator}</span>`;

    const badge = document.getElementById('dtlStatusBadge');
    if (proj.status === 'active') { badge.className = 'bg-blue-100 text-blue-800 text-xs font-bold px-2 py-0.5 rounded'; badge.textContent = '進行中'; }
    else if (proj.status === 'preparing') { badge.className = 'bg-amber-100 text-amber-800 text-xs font-bold px-2 py-0.5 rounded'; badge.textContent = '準備中'; }
    else { badge.className = 'bg-gray-200 text-gray-700 text-xs font-bold px-2 py-0.5 rounded'; badge.textContent = '已結案'; }

    // Disable action buttons if closed
    document.getElementById('btnEditProject').style.display = proj.status === 'closed' ? 'none' : 'flex';
    document.getElementById('btnCloseProject').style.display = proj.status === 'closed' ? 'none' : 'flex';

    // 8 Stages Timeline
    const tlContainer = document.getElementById('dtlTimelineList');
    // Simple state detection relative to today or closedDate
    const targetDate = (proj.status === 'closed' && proj.closedDate) ? proj.closedDate : todayStr;
    let currentStageFound = false;
    let tlHtml = '';

    proj.stages.forEach((st, idx) => {
        const isPast = st.end < targetDate;
        const isCurrent = !isPast && st.start <= targetDate;
        const isFuture = st.start > targetDate;
        if (isCurrent && proj.status !== 'closed') currentStageFound = true;

        let nodeColor = 'bg-gray-200';
        let lineClass = 'border-gray-200';
        let textColor = 'text-gray-400';
        let dateTextColor = 'text-gray-400';

        if (proj.status === 'closed') {
            if (isPast) {
                nodeColor = 'bg-[var(--color-morandi)]';
                lineClass = 'border-[var(--color-morandi)]';
                textColor = 'text-gray-500';
            } else if (isCurrent) {
                // Premature closure point
                nodeColor = 'bg-[var(--color-terracotta)] ring-4 ring-red-50';
                lineClass = 'border-gray-200 border-dashed';
                textColor = 'text-[var(--color-terracotta)] font-bold';
                dateTextColor = 'text-[var(--color-terracotta)] font-bold';
            } else {
                lineClass = 'border-gray-200 border-dashed';
            }
        } else {
            if (isPast) {
                nodeColor = 'bg-[var(--color-sage)]';
                lineClass = 'border-[var(--color-sage)]';
                textColor = 'text-gray-500';
            } else if (isCurrent) {
                nodeColor = 'bg-[var(--color-morandi)] ring-4 ring-blue-50';
                lineClass = 'border-gray-200 border-dashed';
                textColor = 'text-[var(--color-morandi)] font-bold';
                dateTextColor = 'text-[var(--color-morandi)] font-bold';
            } else { // future
                lineClass = 'border-gray-100 border-dashed';
            }
        }

        const isLast = idx === proj.stages.length - 1;

        tlHtml += `
            <div class="relative pb-6">
                ${!isLast ? `<div class="absolute top-4 left-[5px] -ml-px h-full w-0.5 ${lineClass} border-l-[2px]"></div>` : ''}
                <div class="relative flex items-center space-x-3">
                    <div>
                        <span class="h-3 w-3 rounded-full flex items-center justify-center ring-8 ring-white ${nodeColor}"></span>
                    </div>
                    <div class="flex justify-between w-full pr-4">
                        <span class="text-sm ${textColor}">${idx + 1}. ${st.name}</span>
                        <span class="text-xs ${dateTextColor}">${st.start} ~ ${st.end}</span>
                    </div>
                </div>
            </div>
        `;
    });
    tlContainer.innerHTML = tlHtml;

    // Targets Cards
    const tgtContainer = document.getElementById('dtlTargetsList');
    let tgtHtml = '';
    Object.keys(proj.targets).forEach(key => {
        tgtHtml += `
            <div class="bg-gray-50 border border-gray-100 rounded-lg p-3 text-center">
                <div class="text-xs text-gray-500 mb-1">${questionTypesMap[key]}</div>
                <div class="text-lg font-black text-[var(--color-slate-main)]">${proj.targets[key]}</div>
            </div>
        `;
    });
    tgtContainer.innerHTML = tgtHtml;

    // Personnel
    const pContainer = document.getElementById('dtlPersonsList');
    document.getElementById('dtlPersonCount').textContent = `共 ${proj.persons.length} 人`;
    if (proj.persons.length === 0) {
        pContainer.innerHTML = '<div class="text-center text-sm text-gray-400 py-4">尚未指派任何人員。</div>';
    } else {
        let pHtml = '';
        proj.persons.forEach(assigned => {
            const t = talentPoolDb.find(x => x.id === assigned.id);
            if (t) {
                pHtml += `
                    <div class="flex items-center justify-between p-3 border-b border-gray-50 hover:bg-gray-50 transition-colors">
                        <div class="flex items-center gap-2">
                            <div class="w-8 h-8 rounded-full bg-[var(--color-morandi)]/10 text-[var(--color-morandi)] flex items-center justify-center font-bold text-xs">${t.name.charAt(0)}</div>
                            <div>
                                <div class="text-sm font-bold text-gray-700">${t.name}</div>
                                <div class="text-[10px] text-gray-500">${t.id}</div>
                            </div>
                        </div>
                        <div class="flex gap-1 flex-wrap justify-end">
                            ${(Array.isArray(assigned.role) ? assigned.role : [assigned.role]).map(r => `<span class="text-[10px] px-2 py-0.5 rounded bg-[var(--color-oatmeal)] border border-gray-200 text-gray-600 font-bold whitespace-nowrap">${r}</span>`).join('')}
                        </div>
                    </div>
                `;
            }
        });
        pContainer.innerHTML = pHtml;
    }
}

// ==============================================
//  結案機制防呆 US-005
// ==============================================
function handleCloseProject() {
    const proj = projectsDb.find(p => p.id === selectedProjectId);
    if (!proj) return;

    // Check if end date of project (Stage 1 usually) is past today
    const pEnd = proj.stages[0].end;
    if (pEnd > todayStr) {
        Swal.fire({
            title: '期程尚未結束',
            text: `此專案的產學結束日為 ${pEnd}，確定要「提早結案」嗎？`,
            icon: 'warning',
            showCancelButton: true,
            confirmButtonColor: '#D98A6C',
            cancelButtonColor: '#6B8EAD',
            confirmButtonText: '是的，強制結案入庫',
            cancelButtonText: '取消操作'
        }).then((result) => {
            if (result.isConfirmed) executeClose(proj);
        });
    } else {
        Swal.fire({
            title: '確定結案入庫？',
            text: '結案後專案進度將凍結，且只有「狀態為採用」的題目會正式入庫！',
            icon: 'question',
            showCancelButton: true,
            confirmButtonColor: '#8EAB94',
            confirmButtonText: '確定入庫',
            cancelButtonText: '取消'
        }).then((result) => {
            if (result.isConfirmed) executeClose(proj);
        });
    }
}

function executeClose(proj) {
    proj.status = 'closed';
    proj.closedDate = todayStr; // 紀錄結案時間，讓時間軸凍結
    renderProjectList();
    renderProjectDetail(proj.id);
    Swal.fire('已結案', '符合標準之題目已轉入後備題庫。', 'success');
}


// ==============================================
//  Slide-Over Panel (Add / Edit Form) & Date Engine
// ==============================================

const stageDef = [
    { name: '產學計畫區間', note: '［預設約100天］' },
    { name: '命題階段', note: '［預設30天］［倒數5天提醒］' },
    { name: '交互審題', note: '［命題階段後7天］' },
    { name: '互審修題', note: '［交互審題後7天］［倒數5天提醒］' },
    { name: '專家審題', note: '［互審修題後14天］' },
    { name: '專審修題', note: '［專家審題後14天］［倒數5天提醒］' },
    { name: '總召審題', note: '［專審修題後14天］' },
    { name: '總召修題', note: '［總召審題後14天］［倒數5天提醒］' }
];

function initSlideOverPanel() {
    const wrapper = document.getElementById('slideOverWrapper');
    const backdrop = document.getElementById('slideOverBackdrop');
    const panel = document.getElementById('slideOverPanel');
    const closeBtns = document.querySelectorAll('.close-panel-btn');
    const saveBtn = document.getElementById('btnSaveProject');

    window.openPanel = (editId = null) => {
        isEditMode = !!editId;
        document.getElementById('panelTitle').innerHTML = isEditMode
            ? '<i class="fa-solid fa-pen text-[var(--color-morandi)]"></i> <span>編輯專案設定</span>'
            : '<i class="fa-solid fa-layer-group text-[var(--color-sage)]"></i> <span>新增專案 (梯次)</span>';

        saveBtn.innerHTML = isEditMode ? '<i class="fa-solid fa-floppy-disk"></i> 儲存變更' : '<i class="fa-solid fa-plus"></i> 建立專案';

        populateForm(editId);

        wrapper.classList.remove('hidden');
        // setTimeout to allow display:block to apply before opacity transition
        setTimeout(() => {
            backdrop.classList.add('opacity-100');
            panel.classList.remove('translate-x-full');
        }, 10);
    };

    window.closePanel = () => {
        backdrop.classList.remove('opacity-100');
        panel.classList.add('translate-x-full');
        setTimeout(() => wrapper.classList.add('hidden'), 300);
    };

    closeBtns.forEach(btn => btn.addEventListener('click', closePanel));
    backdrop.addEventListener('click', closePanel);

    saveBtn.addEventListener('click', () => {
        // [DEMO] Form validate and save
        const form = document.getElementById('projectForm');
        if (!form.checkValidity()) {
            form.reportValidity();
            return;
        }

        // 檢查配額是否吻合 (Quotas Validation Check)
        const isQuotasValid = validateAllocationsSilent();

        if (!isQuotasValid) {
            Swal.fire({
                title: '配額設定有誤',
                text: '人員的配額加總與專案目標題數不吻合。請問是否仍要強制儲存？（後續仍可再回來修改）',
                icon: 'warning',
                showCancelButton: true,
                confirmButtonColor: '#D98A6C', // var(--color-terracotta)
                cancelButtonColor: '#6B8EAD', // var(--color-morandi)
                confirmButtonText: '強制儲存',
                cancelButtonText: '返回修改'
            }).then((result) => {
                if (result.isConfirmed) {
                    executeSimulatedSave();
                }
            });
            return; // 提前結束，等待使用者決定
        }

        // 若驗證通過，正常儲存
        executeSimulatedSave();
    });
}

function executeSimulatedSave() {
    // Simulate saving
    Swal.fire({
        icon: 'success', title: isEditMode ? '儲存成功' : '建立成功',
        toast: true, position: 'top-end', showConfirmButton: false, timer: 1500
    });

    // (In real app, we would extract data from inputs and update projectsDb)

    closePanel();
}

function renderFormStageInputs() {
    let html = '';
    for (let i = 0; i < 8; i++) {
        const isMain = i === 0;
        const bg = isMain ? 'bg-[var(--color-morandi)]/5 border-[var(--color-morandi)]/20' : 'bg-white border-gray-200';
        html += `
            <div class="${bg} border p-3 rounded flex flex-col sm:flex-row items-start sm:items-center gap-2 sm:gap-4 transition-colors">
                <div class="w-full sm:w-5/12 flex items-center gap-2">
                    <span class="w-5 h-5 rounded-full ${isMain ? 'bg-[var(--color-morandi)]' : 'bg-gray-300'} text-white flex justify-center items-center text-xs flex-shrink-0">${i + 1}</span>
                    <div class="flex flex-col">
                        <span class="font-bold text-sm text-[var(--color-slate-main)]">${stageDef[i].name}</span>
                        <span class="text-[10px] text-gray-500 leading-none mt-0.5">${stageDef[i].note}</span>
                    </div>
                </div>
                <div class="flex-grow flex items-center gap-2 w-full">
                    <input type="date" id="st_s_${i}" class="stage-date-input flex-1 px-2 py-1.5 border border-gray-300 rounded text-sm focus:outline-none focus:ring-1 focus:ring-[var(--color-morandi)]" data-idx="${i}" data-pos="start">
                    <span class="text-gray-400 text-xs text-center"><i class="fa-solid fa-arrow-right"></i></span>
                    <input type="date" id="st_e_${i}" class="stage-date-input flex-1 px-2 py-1.5 border border-gray-300 rounded text-sm focus:outline-none focus:ring-1 focus:ring-[var(--color-morandi)]" data-idx="${i}" data-pos="end">
                </div>
            </div>
        `;
    }
    document.getElementById('datesFormContainer').innerHTML = html;

    // Bind Auto-engine events (onChange)
    document.querySelectorAll('.stage-date-input').forEach(input => {
        input.addEventListener('change', handleDateEngineChange);
    });
}

function renderFormTargets() {
    let tHtml = '';
    Object.keys(questionTypesMap).forEach(key => {
        tHtml += `
            <div>
                <label class="text-[10px] text-gray-500 mb-0.5 block line-clamp-1" title="${questionTypesMap[key]}">${questionTypesMap[key]}</label>
                <input type="number" id="tgt_${key}" min="0" class="w-full px-1 py-1 border border-gray-300 rounded focus:outline-none text-sm text-center">
            </div>
        `;
    });
    document.getElementById('frmTargetsContainer').innerHTML = tHtml;

    // Bind event listener to update validation real-time when target changes
    document.querySelectorAll('[id^="tgt_"]').forEach(el => {
        el.addEventListener('input', validateAllocations);
    });
}

function renderFormPersons(searchVal = "") {
    const s = searchVal.toLowerCase();
    const filtered = talentPoolDb.filter(t => t.name.toLowerCase().includes(s) || t.id.toLowerCase().includes(s));

    // Generate role options mapping
    let roleOptions = availableRolesDb.map(r => `<option value="${r}">${r}</option>`).join('');

    let pHtml = '';

    if (filtered.length === 0) {
        pHtml = '<div class="p-4 text-center text-sm text-gray-400">找不到相符的人員。</div>';
    } else {
        filtered.forEach(t => {
            pHtml += `
                <div class="flex items-start gap-3 p-3 hover:bg-gray-50 transition-colors border-b border-gray-100 last:border-0" id="person_row_${t.id}">
                    <label class="flex items-center gap-3 cursor-pointer mt-1">
                        <input type="checkbox" id="chk_p_${t.id}" value="${t.id}" class="person-checkbox w-4 h-4 text-[var(--color-morandi)] rounded border-gray-300 focus:ring-[var(--color-morandi)] mt-0.5">
                        <div class="flex flex-col w-20">
                            <span class="text-sm font-bold text-gray-700">${t.name}</span>
                            <span class="text-[10px] text-gray-500">${t.id}</span>
                        </div>
                    </label>
                    <div class="flex-grow flex flex-col gap-2" id="roles_container_${t.id}">
                        <!-- 主要身分 (預設顯示) -->
                        <div class="flex items-center gap-2">
                            <select id="sel_p1_${t.id}" class="flex-grow text-xs border border-gray-300 rounded px-2 py-1.5 focus:ring-[var(--color-morandi)] focus:border-[var(--color-morandi)] bg-white disabled:bg-gray-100 disabled:text-gray-400" disabled>
                                <option value="" disabled selected>請選派主要身分...</option>
                                ${roleOptions}
                            </select>
                            <button type="button" class="btn-add-role hidden cursor-pointer text-[var(--color-morandi)] hover:text-blue-700 w-6 h-6 rounded-full hover:bg-blue-50 flex items-center justify-center transition-colors" data-pid="${t.id}" title="新增身分">
                                <i class="fa-solid fa-plus text-xs"></i>
                            </button>
                        </div>
                        
                        <!-- 次要身分 2 (預設隱藏) -->
                        <div class="flex items-center gap-2 hidden" id="wrap_p2_${t.id}">
                            <select id="sel_p2_${t.id}" class="flex-grow text-xs border border-gray-300 rounded px-2 py-1.5 focus:ring-[var(--color-morandi)] focus:border-[var(--color-morandi)] bg-white disabled:bg-gray-100 disabled:text-gray-400" disabled>
                                <option value="">(無次要身分)</option>
                                ${roleOptions}
                            </select>
                            <button type="button" class="btn-remove-role cursor-pointer text-red-400 hover:text-red-600 w-6 h-6 rounded-full hover:bg-red-50 flex items-center justify-center transition-colors" data-pid="${t.id}" data-target="2" title="移除身分">
                                <i class="fa-solid fa-xmark text-xs"></i>
                            </button>
                        </div>

                        <!-- 次要身分 3 (預設隱藏) -->
                        <div class="flex items-center gap-2 hidden" id="wrap_p3_${t.id}">
                            <select id="sel_p3_${t.id}" class="flex-grow text-xs border border-gray-300 rounded px-2 py-1.5 focus:ring-[var(--color-morandi)] focus:border-[var(--color-morandi)] bg-white disabled:bg-gray-100 disabled:text-gray-400" disabled>
                                <option value="">(無次要身分)</option>
                                ${roleOptions}
                            </select>
                            <button type="button" class="btn-remove-role cursor-pointer text-red-400 hover:text-red-600 w-6 h-6 rounded-full hover:bg-red-50 flex items-center justify-center transition-colors" data-pid="${t.id}" data-target="3" title="移除身分">
                                <i class="fa-solid fa-xmark text-xs"></i>
                            </button>
                        </div>
                    </div>
                </div>
            `;
        });
    }

    document.getElementById('frmPersonsContainer').innerHTML = pHtml;

    // Bind Expand Role Buttons
    document.querySelectorAll('.btn-add-role').forEach(btn => {
        btn.addEventListener('click', (e) => {
            const pid = e.currentTarget.getAttribute('data-pid');
            const wrap2 = document.getElementById(`wrap_p2_${pid}`);
            const wrap3 = document.getElementById(`wrap_p3_${pid}`);

            if (wrap2.classList.contains('hidden')) {
                wrap2.classList.remove('hidden');
            } else if (wrap3.classList.contains('hidden')) {
                wrap3.classList.remove('hidden');
                e.currentTarget.classList.add('hidden'); // 最多 3 個，藏起新增按鈕
            }
        });
    });

    // Bind Remove Role Buttons
    document.querySelectorAll('.btn-remove-role').forEach(btn => {
        btn.addEventListener('click', (e) => {
            const pid = e.currentTarget.getAttribute('data-pid');
            const target = e.currentTarget.getAttribute('data-target'); // 2 or 3
            const wrap = document.getElementById(`wrap_p${target}_${pid}`);
            const sel = document.getElementById(`sel_p${target}_${pid}`);

            wrap.classList.add('hidden');
            sel.value = ""; // 清空該欄位值

            // 恢復原本的主要新增按鈕顯示
            const addBtn = document.querySelector(`.btn-add-role[data-pid="${pid}"]`);
            if (addBtn) addBtn.classList.remove('hidden');

            triggerAllocationUpdate();
        });
    });

    // Handle role selection change trigger layout
    document.querySelectorAll('[id^="sel_p"]').forEach(sel => {
        sel.addEventListener('change', triggerAllocationUpdate);
    });

    // Bind checkbox change to toggle select enablement & UI visibility
    document.querySelectorAll('.person-checkbox').forEach(chk => {
        chk.addEventListener('change', (e) => {
            const pid = e.target.value;
            const sel1 = document.getElementById(`sel_p1_${pid}`);
            const sel2 = document.getElementById(`sel_p2_${pid}`);
            const sel3 = document.getElementById(`sel_p3_${pid}`);
            const addBtn = document.querySelector(`.btn-add-role[data-pid="${pid}"]`);
            const wrap2 = document.getElementById(`wrap_p2_${pid}`);
            const wrap3 = document.getElementById(`wrap_p3_${pid}`);

            if (sel1) {
                sel1.disabled = !e.target.checked;
                if (e.target.checked && sel1.value === "") {
                    sel1.value = availableRolesDb[0]; // default first role
                }
            }
            if (sel2) sel2.disabled = !e.target.checked;
            if (sel3) sel3.disabled = !e.target.checked;

            // 勾選時才秀出旁邊的「+新增身分」按鈕 (如果還沒滿3個)
            if (e.target.checked) {
                if (wrap2.classList.contains('hidden') || wrap3.classList.contains('hidden')) {
                    if (addBtn) addBtn.classList.remove('hidden');
                }
            } else {
                if (addBtn) addBtn.classList.add('hidden');
                // 取消勾選自動收折 2,3 並清空
                if (wrap2) { wrap2.classList.add('hidden'); sel2.value = ""; }
                if (wrap3) { wrap3.classList.add('hidden'); sel3.value = ""; }
            }
            triggerAllocationUpdate();
        });
    });
}

function populateForm(projId) {
    const form = document.getElementById('projectForm');
    form.reset();

    if (!projId) {
        // 新增預設：本年度
        document.getElementById('frmYear').value = new Date().getFullYear() - 1911; // 簡單轉民國
        // 重置階段
        for (let i = 0; i < 8; i++) {
            document.getElementById(`st_s_${i}`).value = '';
            document.getElementById(`st_e_${i}`).value = '';
        }
        return;
    }

    const proj = projectsDb.find(p => p.id === projId);
    if (!proj) return;

    // Basic
    document.getElementById('frmYear').value = proj.year;
    document.getElementById('frmName').value = proj.name;
    document.getElementById('frmSchool').value = proj.school.includes('自辦') ? '' : proj.school;

    // Stages
    proj.stages.forEach((st, idx) => {
        document.getElementById(`st_s_${idx}`).value = st.start;
        document.getElementById(`st_e_${idx}`).value = st.end;
    });

    // Targets
    Object.keys(proj.targets).forEach(key => {
        const el = document.getElementById(`tgt_${key}`);
        if (el) el.value = proj.targets[key];
    });

    // Persons (clear all first)
    document.getElementById('frmPersonSearch').value = '';
    renderFormPersons(); // re-render fresh

    proj.persons.forEach(assigned => {
        const chk = document.getElementById(`chk_p_${assigned.id}`);
        const sel1 = document.getElementById(`sel_p1_${assigned.id}`);
        const sel2 = document.getElementById(`sel_p2_${assigned.id}`);
        const sel3 = document.getElementById(`sel_p3_${assigned.id}`);
        const addBtn = document.querySelector(`.btn-add-role[data-pid="${assigned.id}"]`);
        const wrap2 = document.getElementById(`wrap_p2_${assigned.id}`);
        const wrap3 = document.getElementById(`wrap_p3_${assigned.id}`);

        if (chk) {
            chk.checked = true;
            if (sel1) sel1.disabled = false;
            if (sel2) sel2.disabled = false;
            if (sel3) sel3.disabled = false;

            const roles = Array.isArray(assigned.role) ? assigned.role : [assigned.role];
            if (sel1 && roles[0]) { sel1.value = roles[0]; }

            if (sel2 && roles[1]) {
                sel2.value = roles[1];
                wrap2.classList.remove('hidden');
            }

            if (sel3 && roles[2]) {
                sel3.value = roles[2];
                wrap3.classList.remove('hidden');
            }

            // 更新按鈕狀態
            if (roles.length >= 3 && addBtn) {
                addBtn.classList.add('hidden');
            } else if (addBtn) {
                addBtn.classList.remove('hidden');
            }
        }
    });

    // Reconstruct temporary allocation state from project data
    currentAllocationData = [];
    proj.persons.forEach(assigned => {
        if (assigned.role.includes('命題教師')) {
            const t = talentPoolDb.find(x => x.id === assigned.id);
            if (t) {
                currentAllocationData.push({
                    id: t.id,
                    name: t.name,
                    quotas: { ...(assigned.quotas || { single: 0, select: 0, readGroup: 0, longText: 0, shortGroup: 0, listen: 0, listenGroup: 0 }) }
                });
            }
        }
    });

    renderAllocationTable();
}

// ----------------------------------------
// 命題數量配額引擎 (Allocation Engine)
// ----------------------------------------

function triggerAllocationUpdate() {
    // Collect all checked persons who have '命題教師' role selected
    const activeTeachers = [];
    document.querySelectorAll('.person-checkbox:checked').forEach(chk => {
        const pid = chk.value;
        const sel1 = document.getElementById(`sel_p1_${pid}`);
        const sel2 = document.getElementById(`sel_p2_${pid}`);
        const sel3 = document.getElementById(`sel_p3_${pid}`);

        let roles = [
            sel1 && !sel1.disabled ? sel1.value : null,
            sel2 && !sel2.disabled ? sel2.value : null,
            sel3 && !sel3.disabled ? sel3.value : null
        ].filter(Boolean);

        if (roles.includes('命題教師')) {
            const t = talentPoolDb.find(x => x.id === pid);
            if (t) activeTeachers.push({ id: t.id, name: t.name });
        }
    });

    // Re-sync currentAllocationData (preserve existing quotas, add new, remove unchecked)
    const newAllocationData = [];
    activeTeachers.forEach(teacher => {
        const existing = currentAllocationData.find(a => a.id === teacher.id);
        if (existing) {
            newAllocationData.push(existing);
        } else {
            newAllocationData.push({
                id: teacher.id,
                name: teacher.name,
                quotas: { single: 0, select: 0, readGroup: 0, longText: 0, shortGroup: 0, listen: 0, listenGroup: 0 }
            });
        }
    });

    currentAllocationData = newAllocationData;
    renderAllocationTable();
}

function renderAllocationTable() {
    const container = document.getElementById('quotaAllocationContainer');
    if (currentAllocationData.length === 0) {
        container.innerHTML = '<div class="text-sm text-center text-gray-400 py-4 bg-gray-50 rounded border border-gray-100">請先由上方勾選「命題教師」以進行數量配置...</div>';
        validateAllocations();
        return;
    }

    let html = '';
    currentAllocationData.forEach((teacher, idx) => {
        html += `
            <div class="bg-white border border-gray-200 rounded p-3 flex flex-col md:flex-row gap-4 items-start md:items-center">
                <div class="flex items-center gap-2 w-full md:w-32 flex-shrink-0">
                    <div class="w-8 h-8 rounded-full bg-[var(--color-morandi)] text-white flex items-center justify-center font-bold text-xs">${idx + 1}</div>
                    <div class="font-bold text-sm text-[var(--color-slate-main)] truncate">${teacher.name}</div>
                </div>
                <div class="flex-grow grid grid-cols-4 sm:grid-cols-7 gap-2 w-full">
        `;

        Object.keys(questionTypesMap).forEach(key => {
            html += `
                    <div>
                        <label class="block text-[10px] text-gray-500 mb-0.5 truncate" title="${questionTypesMap[key]}">${questionTypesMap[key]}</label>
                        <input type="number" min="0" class="quota-input w-full px-1 py-1 border border-gray-300 rounded text-xs text-center focus:ring-1 focus:ring-[var(--color-morandi)] focus:outline-none" data-pid="${teacher.id}" data-type="${key}" value="${teacher.quotas[key] || 0}">
                    </div>
            `;
        });

        html += `
                </div>
            </div>
        `;
    });

    container.innerHTML = html;

    // Bind inputs to array data
    document.querySelectorAll('.quota-input').forEach(input => {
        input.addEventListener('input', (e) => {
            const pid = e.target.getAttribute('data-pid');
            const type = e.target.getAttribute('data-type');
            const val = parseInt(e.target.value) || 0;
            const teacher = currentAllocationData.find(a => a.id === pid);
            if (teacher) {
                teacher.quotas[type] = val;
            }
            validateAllocations();
        });
    });

    validateAllocations();
}

function handleAutoDistributeQuotas() {
    if (currentAllocationData.length === 0) {
        Swal.fire({ toast: true, position: 'top-end', icon: 'warning', title: '操作無效', text: '尚未指派任何命題教師', showConfirmButton: false, timer: 2000 });
        return;
    }

    const tCount = currentAllocationData.length;

    Object.keys(questionTypesMap).forEach(key => {
        const targetValue = parseInt(document.getElementById(`tgt_${key}`).value) || 0;
        const baseQuota = Math.floor(targetValue / tCount);
        let remainder = targetValue % tCount;

        currentAllocationData.forEach(t => {
            t.quotas[key] = baseQuota;
        });

        // Distribute remainder sequentially
        let idx = 0;
        while (remainder > 0) {
            currentAllocationData[idx].quotas[key]++;
            remainder--;
            idx = (idx + 1) % tCount;
        }
    });

    renderAllocationTable();
    Swal.fire({ toast: true, position: 'top-end', icon: 'success', title: '分配完成', text: '已依據教師人數平均配額', showConfirmButton: false, timer: 2000 });
}

function validateAllocations() {
    const grid = document.getElementById('quotaValidationGrid');
    const msgEl = document.getElementById('quotaValidationMessage');

    let allValid = true;
    let gridHtml = '';

    Object.keys(questionTypesMap).forEach(key => {
        const targetTitle = questionTypesMap[key];
        const targetValue = parseInt(document.getElementById(`tgt_${key}`)?.value) || 0;

        let sum = 0;
        currentAllocationData.forEach(t => {
            sum += (t.quotas[key] || 0);
        });

        const isExact = (sum === targetValue);
        if (!isExact && targetValue > 0) allValid = false;

        const bgColor = isExact ? (targetValue > 0 ? 'bg-[var(--color-sage)]/10 border-[var(--color-sage)]/30' : 'bg-gray-100 border-gray-200') : 'bg-[var(--color-terracotta)]/10 border-[var(--color-terracotta)]/50';
        const textColor = isExact ? (targetValue > 0 ? 'text-[var(--color-sage)]' : 'text-gray-400') : 'text-[var(--color-terracotta)]';

        gridHtml += `
            <div class="flex flex-col items-center justify-center border rounded p-1 ${bgColor}">
                <span class="text-[9px] text-gray-500 mb-0.5 whitespace-nowrap overflow-hidden text-ellipsis w-full text-center">${targetTitle.replace('題', '')}</span>
                <span class="text-xs font-bold flex items-center gap-1 ${textColor}">
                    ${sum} / ${targetValue}
                    ${isExact && targetValue > 0 ? '<i class="fa-solid fa-check text-[9px]"></i>' : ''}
                    ${!isExact && targetValue > 0 ? '<i class="fa-solid fa-xmark text-[9px]"></i>' : ''}
                </span>
            </div>
        `;
    });

    grid.innerHTML = gridHtml;

    if (currentAllocationData.length === 0) {
        msgEl.innerHTML = '<span class="text-gray-400">尚未分配</span>';
    } else if (allValid) {
        msgEl.innerHTML = '<span class="text-[var(--color-sage)] bg-[var(--color-sage)]/10 px-2 py-0.5 rounded font-bold"><i class="fa-solid fa-circle-check"></i> 數量驗證正確</span>';
    } else {
        msgEl.innerHTML = '<span class="text-[var(--color-terracotta)] bg-[var(--color-terracotta)]/10 px-2 py-0.5 rounded font-bold"><i class="fa-solid fa-triangle-exclamation"></i> 檢查未通過，數量不符</span>';
    }
}

function validateAllocationsSilent() {
    if (currentAllocationData.length === 0) return true; // 如果沒指派教師，就當作不檢查這塊（或視業務邏輯改為 false）

    let allValid = true;
    Object.keys(questionTypesMap).forEach(key => {
        const targetValue = parseInt(document.getElementById(`tgt_${key}`)?.value) || 0;
        let sum = 0;
        currentAllocationData.forEach(t => {
            sum += (t.quotas[key] || 0);
        });
        if (sum !== targetValue && targetValue > 0) {
            allValid = false;
        }
    });
    return allValid;
}

function handleDateEngineChange(e) {
    const el = e.target;
    if (!el.value) return;

    const idx = parseInt(el.getAttribute('data-idx'));
    const pos = el.getAttribute('data-pos'); // start or end

    // --- 連動精華 ---
    // 當產學計畫起日 (idx 0) 改變：
    // 1. 自動擷取西元年轉民國年，填入「所屬年度」
    // 2. 整片自動連動推算所有階段日期。
    if (idx === 0 && pos === 'start') {
        const dateObj = new Date(el.value);
        if (!isNaN(dateObj)) {
            document.getElementById('frmYear').value = Math.max(dateObj.getFullYear() - 1911, 100);
        }
        handleAutoCalcDates(`st_s_0`); // 呼叫一鍵自動佈署引擎，以 startDate 為基準
        return;
    }

    // 防呆：同一階段的 end 不得小於 start (若 start 存在)
    if (pos === 'end') {
        const startVal = document.getElementById(`st_s_${idx}`).value;
        if (startVal && el.value < startVal) {
            Swal.fire({ toast: true, position: 'top-end', icon: 'error', title: '日期錯誤', text: '結束日不得小於開始日', showConfirmButton: false, timer: 2000 });
            el.value = startVal;
            return;
        }

        // 如果改了第 N 階段的「結束日」，而且不是最後一個階段 (7)
        // 則把 N+1 階段的「開始日」自動設為這一天（或加一天）
        if (idx >= 0 && idx < 7) {
            const nextStartEl = document.getElementById(`st_s_${idx + 1}`);
            if (nextStartEl) {
                // 加一天的運算 (除了 idx 0 產學區間和子第一階段可以同一天)
                const dateObj = new Date(el.value);
                if (idx !== 0) {
                    dateObj.setDate(dateObj.getDate() + 1);
                }

                // 轉回 YYYY-MM-DD
                const y = dateObj.getFullYear();
                const m = String(dateObj.getMonth() + 1).padStart(2, '0');
                const d = String(dateObj.getDate()).padStart(2, '0');

                nextStartEl.value = `${y}-${m}-${d}`;

                // 為了視覺提示，讓欄位閃一下
                nextStartEl.classList.add('bg-blue-50', 'text-[var(--color-morandi)]');
                setTimeout(() => nextStartEl.classList.remove('bg-blue-50', 'text-[var(--color-morandi)]'), 1000);

                // 如果 N+1 階段的 end 原本小於新的 start，趁機把清空要求重填
                const nextEndEl = document.getElementById(`st_e_${idx + 1}`);
                if (nextEndEl.value && nextEndEl.value < nextStartEl.value) {
                    nextEndEl.value = '';
                }
            }
        }
    }
}

// 魔術棒：一鍵自動把所有空白接續填滿或者是從基點全推
function handleAutoCalcDates(baseElementId) {
    let baseDateStr = document.getElementById(baseElementId)?.value;

    if (!baseDateStr) {
        return;
    }

    // 當從 0 (產學開始) 連動
    const isFromStart = baseElementId === 'st_s_0';
    let startIndexForCalc = 1; // 預設推算目標從 idx 1 (第二階段)
    let dateObj = new Date(baseDateStr);

    if (isFromStart) {
        startIndexForCalc = 1;
        // 注意：原先直接加六個月邏輯已作廢，改為等迴圈算完最後一個階段後對齊
    } else {
        startIndexForCalc = 2;
    }

    // Default span for each stage (days)
    const defaultSpans = [180, 30, 7, 7, 14, 14, 14, 14];

    for (let i = startIndexForCalc; i < 8; i++) {
        // 每個新階段的開始日，都接續著前一個日期的「隔天」
        dateObj.setDate(dateObj.getDate() + 1);

        const sy = dateObj.getFullYear();
        const sm = String(dateObj.getMonth() + 1).padStart(2, '0');
        const sd = String(dateObj.getDate()).padStart(2, '0');
        document.getElementById(`st_s_${i}`).value = `${sy}-${sm}-${sd}`;

        // Find next end based on default span
        dateObj.setDate(dateObj.getDate() + defaultSpans[i] - 1);
        const ey = dateObj.getFullYear();
        const em = String(dateObj.getMonth() + 1).padStart(2, '0');
        const ed = String(dateObj.getDate()).padStart(2, '0');
        document.getElementById(`st_e_${i}`).value = `${ey}-${em}-${ed}`;

        // Let visually notice
        const wrapper = document.getElementById(`st_s_${i}`).parentElement;
        if (wrapper) {
            wrapper.classList.add('bg-blue-50/50');
            setTimeout(() => wrapper.classList.remove('bg-blue-50/50'), 600);
        }
    }

    // 迴圈結束後，統一確保「產學計畫區間」結束日期等於「總召修題」結束日期
    document.getElementById('st_e_0').value = document.getElementById('st_e_7').value;
}
