/**
 * Teachers Module (教師管理系統 / 人才庫)
 * 負責教師人才庫的 CRUD、基本資料與任教背景檢視、跨專案命題歷程、帳號啟停用管理。
 * Version: 1.0 (DEMO)
 */

// --- 假資料：教師人才庫 ---
let teachersDb = [
    {
        id: 'T1001', name: '劉雅婷', gender: 'female',
        email: 'yating.liu@ntnu.edu.tw', phone: '0912-345-001', idNumber: 'F298765432',
        school: '國立臺灣師範大學', department: '國文學系', title: '副教授',
        expertise: '古典文學、詩詞賞析、文言文教學', years: 15, education: '博士',
        accountStatus: 'active', note: '資深命題教師，擅長高難度文言文題型。',
        createdAt: '2024-06-15', firstLogin: true,
        participatedProjects: ['P2026-01', 'P2026-02', 'P2025-02']
    },
    {
        id: 'T1002', name: '王健明', gender: 'male',
        email: 'jianming.wang@ncku.edu.tw', phone: '0923-456-002', idNumber: 'A156789012',
        school: '國立成功大學', department: '中國文學系', title: '教授',
        expertise: '現代文學、散文創作、閱讀教育', years: 22, education: '博士',
        accountStatus: 'active', note: '',
        createdAt: '2024-06-15', firstLogin: true,
        participatedProjects: ['P2026-01', 'P2025-02']
    },
    {
        id: 'T1003', name: '張心怡', gender: 'female',
        email: 'xinyi.zhang@nknu.edu.tw', phone: '0934-567-003', idNumber: 'F287654321',
        school: '國立高雄師範大學', department: '國文學系', title: '助理教授',
        expertise: '語文測驗、語言學、華語教學', years: 8, education: '博士',
        accountStatus: 'active', note: '語測專長，適合命製聽力相關題目。',
        createdAt: '2025-01-10', firstLogin: false,
        participatedProjects: ['P2026-01']
    },
    {
        id: 'T1004', name: '吳家豪', gender: 'male',
        email: 'jiahao.wu@nchu.edu.tw', phone: '0945-678-004', idNumber: 'B167890123',
        school: '國立中興大學', department: '中國文學系', title: '講師',
        expertise: '短文寫作、閱讀理解、素養導向評量', years: 6, education: '碩士',
        accountStatus: 'active', note: '',
        createdAt: '2025-01-10', firstLogin: false,
        participatedProjects: ['P2026-01']
    },
    {
        id: 'T1005', name: '林柏宇', gender: 'male',
        email: 'boyu.lin@ntue.edu.tw', phone: '0956-789-005', idNumber: 'A178901234',
        school: '國立臺北教育大學', department: '語文與創作學系', title: '教師',
        expertise: '兒童文學、繪本教學、創意寫作', years: 10, education: '碩士',
        accountStatus: 'active', note: '互審經驗豐富。',
        createdAt: '2025-02-20', firstLogin: false,
        participatedProjects: ['P2026-01']
    },
    {
        id: 'T1006', name: '陳彥廷', gender: 'male',
        email: 'yanting.chen@ncue.edu.tw', phone: '0967-890-006', idNumber: 'N189012345',
        school: '國立彰化師範大學', department: '國文學系', title: '助理教授',
        expertise: '文字學、訓詁學、聲韻學', years: 5, education: '博士',
        accountStatus: 'inactive', note: '因學期休假暫停參與，預計下學年回歸。',
        createdAt: '2024-09-01', firstLogin: true,
        participatedProjects: ['P2025-02', 'P2025-01']
    },
    {
        id: 'T1007', name: '蔡佳玲', gender: 'female',
        email: 'jialing.tsai@fju.edu.tw', phone: '0978-901-007', idNumber: 'F276543210',
        school: '輔仁大學', department: '中國文學系', title: '兼任教師',
        expertise: '現代詩、文學批評、性別文學', years: 4, education: '碩士',
        accountStatus: 'active', note: '',
        createdAt: '2025-03-01', firstLogin: false,
        participatedProjects: []
    },
    {
        id: 'C2001', name: '李明華', gender: 'male',
        email: 'minghua.li@ntu.edu.tw', phone: '0911-222-008', idNumber: 'A112345678',
        school: '國立臺灣大學', department: '中國文學系', title: '教授',
        expertise: '先秦文學、古典小說、文獻學', years: 28, education: '博士',
        accountStatus: 'active', note: '資深專審委員。',
        createdAt: '2024-06-15', firstLogin: true,
        participatedProjects: ['P2026-01', 'P2025-02']
    },
    {
        id: 'C2002', name: '陳淑芬', gender: 'female',
        email: 'shufen.chen@nccu.edu.tw', phone: '0922-333-009', idNumber: 'F231234567',
        school: '國立政治大學', department: '中國文學系', title: '副教授',
        expertise: '唐宋文學、詞學研究、古典戲曲', years: 18, education: '博士',
        accountStatus: 'active', note: '',
        createdAt: '2024-06-15', firstLogin: true,
        participatedProjects: ['P2026-01']
    },
    {
        id: 'C2003', name: '郭志遠', gender: 'male',
        email: 'zhiyuan.guo@nknu.edu.tw', phone: '0933-444-010', idNumber: 'E145678901',
        school: '國立高雄師範大學', department: '國文學系', title: '教授',
        expertise: '語文教育、課程設計、評量研究', years: 20, education: '博士',
        accountStatus: 'active', note: '',
        createdAt: '2025-01-10', firstLogin: false,
        participatedProjects: ['P2026-01']
    },
    {
        id: 'C2004', name: '黃雅芳', gender: 'female',
        email: 'yafang.huang@thu.edu.tw', phone: '0944-555-011', idNumber: 'F256789012',
        school: '東海大學', department: '中國文學系', title: '副教授',
        expertise: '現代小說、比較文學、文化研究', years: 12, education: '博士',
        accountStatus: 'inactive', note: '已離職。',
        createdAt: '2024-09-01', firstLogin: true,
        participatedProjects: ['P2026-01']
    },
    {
        id: 'S3001', name: '林淑華', gender: 'female',
        email: 'shuhua.lin@cwt.org.tw', phone: '0955-666-012', idNumber: 'F267890123',
        school: 'CWT 中檢中心', department: '命題組', title: '教師',
        expertise: '專案管理、試題品質管控、統計分析', years: 10, education: '碩士',
        accountStatus: 'active', note: '總召，負責最終審查裁決。',
        createdAt: '2024-06-15', firstLogin: true,
        participatedProjects: ['P2026-01', 'P2025-02', 'P2025-01']
    },
    {
        id: 'S3002', name: '許志豪', gender: 'male',
        email: 'zhihao.xu@cwt.org.tw', phone: '0966-777-013', idNumber: 'A189012345',
        school: 'CWT 中檢中心', department: '編審組', title: '教師',
        expertise: '文字編輯、校對審閱', years: 7, education: '碩士',
        accountStatus: 'active', note: '',
        createdAt: '2024-06-15', firstLogin: true,
        participatedProjects: ['P2026-01']
    },
    {
        id: 'S3003', name: '楊美玲', gender: 'female',
        email: 'meiling.yang@cwt.org.tw', phone: '0977-888-014', idNumber: 'F278901234',
        school: 'CWT 中檢中心', department: '行政組', title: '教師',
        expertise: '行政管理、教育行政', years: 5, education: '學士',
        accountStatus: 'active', note: '',
        createdAt: '2025-02-01', firstLogin: false,
        participatedProjects: []
    }
];

// --- 假資料：跨專案命題歷程紀錄 ---
const questionHistoryDb = [
    // T1001 的命題歷程
    { teacherId: 'T1001', projectId: 'P2026-01', projectName: '115年度 春季全民中檢', type: 'single', level: '中高級', status: 'adopted', qid: 'Q2026-001' },
    { teacherId: 'T1001', projectId: 'P2026-01', projectName: '115年度 春季全民中檢', type: 'select', level: '高級', status: 'expert_reviewing', qid: 'Q2026-002' },
    { teacherId: 'T1001', projectId: 'P2026-01', projectName: '115年度 春季全民中檢', type: 'readGroup', level: '優級', status: 'draft', qid: 'Q2026-003' },
    { teacherId: 'T1001', projectId: 'P2026-01', projectName: '115年度 春季全民中檢', type: 'longText', level: '中級', status: 'completed', qid: 'Q2026-004' },
    { teacherId: 'T1001', projectId: 'P2025-02', projectName: '114年度 秋季全民中檢', type: 'single', level: '初級', status: 'adopted', qid: 'Q2025-101' },
    { teacherId: 'T1001', projectId: 'P2025-02', projectName: '114年度 秋季全民中檢', type: 'single', level: '中級', status: 'adopted', qid: 'Q2025-102' },
    { teacherId: 'T1001', projectId: 'P2025-02', projectName: '114年度 秋季全民中檢', type: 'readGroup', level: '中高級', status: 'rejected', qid: 'Q2025-103' },
    // T1002
    { teacherId: 'T1002', projectId: 'P2026-01', projectName: '115年度 春季全民中檢', type: 'single', level: '初級', status: 'peer_reviewing', qid: 'Q2026-010' },
    { teacherId: 'T1002', projectId: 'P2026-01', projectName: '115年度 春季全民中檢', type: 'longText', level: '高級', status: 'adopted', qid: 'Q2026-011' },
    { teacherId: 'T1002', projectId: 'P2025-02', projectName: '114年度 秋季全民中檢', type: 'single', level: '中級', status: 'adopted', qid: 'Q2025-201' },
    { teacherId: 'T1002', projectId: 'P2025-02', projectName: '114年度 秋季全民中檢', type: 'select', level: '高級', status: 'adopted', qid: 'Q2025-202' },
    // T1003
    { teacherId: 'T1003', projectId: 'P2026-01', projectName: '115年度 春季全民中檢', type: 'listen', level: '難度二', status: 'adopted', qid: 'Q2026-020' },
    { teacherId: 'T1003', projectId: 'P2026-01', projectName: '115年度 春季全民中檢', type: 'listenGroup', level: '難度三', status: 'final_reviewing', qid: 'Q2026-021' },
    // T1004
    { teacherId: 'T1004', projectId: 'P2026-01', projectName: '115年度 春季全民中檢', type: 'shortGroup', level: '中級', status: 'draft', qid: 'Q2026-030' },
    { teacherId: 'T1004', projectId: 'P2026-01', projectName: '115年度 春季全民中檢', type: 'single', level: '初級', status: 'completed', qid: 'Q2026-031' },
    // T1005
    { teacherId: 'T1005', projectId: 'P2026-01', projectName: '115年度 春季全民中檢', type: 'single', level: '中高級', status: 'adopted', qid: 'Q2026-040' },
    // C2001
    { teacherId: 'C2001', projectId: 'P2025-02', projectName: '114年度 秋季全民中檢', type: 'single', level: '高級', status: 'adopted', qid: 'Q2025-301' },
    // S3001
    { teacherId: 'S3001', projectId: 'P2025-02', projectName: '114年度 秋季全民中檢', type: 'readGroup', level: '優級', status: 'adopted', qid: 'Q2025-401' },
    { teacherId: 'S3001', projectId: 'P2025-01', projectName: '114年度 春季全民中檢', type: 'single', level: '中級', status: 'adopted', qid: 'Q2025-501' }
];

// --- 假資料：專案對照表 (重用 shared.js 的 mockProjects) ---
const projectsRef = [
    { id: 'P2026-01', name: '115年度 春季全民中檢', status: 'active', year: '115' },
    { id: 'P2026-02', name: '115年度 秋季全民中檢', status: 'preparing', year: '115' },
    { id: 'P2025-02', name: '114年度 秋季全民中檢', status: 'closed', year: '114' },
    { id: 'P2025-01', name: '114年度 春季全民中檢', status: 'closed', year: '114' }
];

// --- 常數對照 ---
const questionTypesMap = {
    'single': '一般單選題',
    'select': '精選單選題',
    'readGroup': '閱讀題組',
    'longText': '長文題目',
    'shortGroup': '短文題組',
    'listen': '聽力測驗',
    'listenGroup': '聽力題組'
};

const statusMap = {
    'draft': { label: '草稿', color: 'bg-gray-100 text-gray-600' },
    'completed': { label: '命題完成', color: 'bg-blue-100 text-blue-700' },
    'pending': { label: '命題送審', color: 'bg-indigo-100 text-indigo-700' },
    'peer_reviewing': { label: '互審中', color: 'bg-yellow-100 text-yellow-700' },
    'peer_editing': { label: '互審修題', color: 'bg-orange-100 text-orange-700' },
    'expert_reviewing': { label: '專審中', color: 'bg-purple-100 text-purple-700' },
    'expert_editing': { label: '專審修題', color: 'bg-pink-100 text-pink-700' },
    'final_reviewing': { label: '總審中', color: 'bg-cyan-100 text-cyan-700' },
    'final_editing': { label: '總審修題', color: 'bg-teal-100 text-teal-700' },
    'adopted': { label: '採用', color: 'bg-emerald-100 text-emerald-700' },
    'rejected': { label: '不採用', color: 'bg-red-100 text-red-600' }
};

const roleOptions = ['命題教師', '互審教師', '專審委員', '專家學者', '總召(專員)'];

// --- 假資料：教師在各專案中的身分指派 (一位教師在同一梯次可擁有多種身分) ---
const projectRolesDb = {
    'P2026-01': [
        { teacherId: 'T1001', roles: ['命題教師', '互審教師'] },
        { teacherId: 'T1002', roles: ['命題教師'] },
        { teacherId: 'T1003', roles: ['命題教師'] },
        { teacherId: 'T1004', roles: ['命題教師'] },
        { teacherId: 'T1005', roles: ['互審教師'] },
        { teacherId: 'C2001', roles: ['專審委員'] },
        { teacherId: 'C2002', roles: ['專審委員'] },
        { teacherId: 'C2003', roles: ['專審委員'] },
        { teacherId: 'C2004', roles: ['專家學者'] },
        { teacherId: 'S3001', roles: ['總召(專員)'] },
        { teacherId: 'S3002', roles: ['總召(專員)'] }
    ],
    'P2026-02': [
        { teacherId: 'T1001', roles: ['命題教師'] }
    ],
    'P2025-02': [
        { teacherId: 'T1001', roles: ['命題教師'] },
        { teacherId: 'T1002', roles: ['互審教師'] },
        { teacherId: 'T1006', roles: ['命題教師', '互審教師'] },
        { teacherId: 'C2001', roles: ['專審委員'] },
        { teacherId: 'C2002', roles: ['專審委員'] },
        { teacherId: 'S3001', roles: ['總召(專員)'] }
    ],
    'P2025-01': [
        { teacherId: 'T1006', roles: ['命題教師'] },
        { teacherId: 'S3001', roles: ['總召(專員)'] }
    ]
};

// 取得教師在指定梯次中的身分
function getTeacherRolesInProject(teacherId, projectId) {
    const projectRoles = projectRolesDb[projectId];
    if (!projectRoles) return [];
    const entry = projectRoles.find(r => r.teacherId === teacherId);
    return entry ? entry.roles : [];
}

// 身分標籤的顏色對照
const roleBadgeStyles = {
    '命題教師': 'bg-blue-50 text-blue-600',
    '互審教師': 'bg-teal-50 text-teal-600',
    '專審委員': 'bg-purple-50 text-purple-600',
    '專家學者': 'bg-violet-50 text-violet-600',
    '總召(專員)': 'bg-amber-50 text-amber-600'
};

const projectStatusMap = {
    'active': { label: '進行中', color: 'bg-blue-100 text-blue-800' },
    'preparing': { label: '準備中', color: 'bg-amber-100 text-amber-800' },
    'closed': { label: '已結案', color: 'bg-gray-200 text-gray-600' }
};

// --- 狀態管理 ---
let currentFilter = 'all';
let currentSearchQuery = '';
let selectedTeacherId = null;
let editingTeacherId = null; // null = 新增, 有值 = 編輯
let currentDetailTab = 'info';

// ==================== INIT ====================
document.addEventListener('DOMContentLoaded', () => {
    renderStats();
    renderTeacherList();
    initFilterButtons();
    initSearchInput();
    initSlideOverPanel();
    initDetailTabs();
    initActionButtons();
    initAssignProjectModal();

    // 監聽梯次切換事件 — 切換時重新渲染左側列表的身分標籤 + 統計 + 詳情
    document.addEventListener('projectChanged', () => {
        renderStats();
        renderTeacherList();
        if (selectedTeacherId) renderDetail(selectedTeacherId);
    });
});

// ==================== 統計卡片 ====================
function renderStats() {
    const total = teachersDb.length;
    const active = teachersDb.filter(t => t.accountStatus === 'active').length;
    const inactive = teachersDb.filter(t => t.accountStatus === 'inactive').length;

    const currentProjectId = localStorage.getItem('cwt_current_project') || 'P2026-01';
    const currentProjectCount = teachersDb.filter(t => t.participatedProjects.includes(currentProjectId)).length;

    animateCounter('statTotal', total);
    animateCounter('statActive', active);
    animateCounter('statInactive', inactive);
    animateCounter('statCurrentProject', currentProjectCount);
}

function animateCounter(elementId, target) {
    const el = document.getElementById(elementId);
    if (!el) return;
    let current = 0;
    const increment = Math.max(1, Math.ceil(target / 15));
    const timer = setInterval(() => {
        current += increment;
        if (current >= target) { current = target; clearInterval(timer); }
        el.textContent = current;
    }, 40);
}

// ==================== 左側列表 ====================
function renderTeacherList() {
    const container = document.getElementById('sidebarTeacherListContainer');
    if (!container) return;

    let filtered = teachersDb.filter(t => {
        // 狀態篩選
        if (currentFilter === 'active' && t.accountStatus !== 'active') return false;
        if (currentFilter === 'inactive' && t.accountStatus !== 'inactive') return false;

        // 關鍵字搜尋
        if (currentSearchQuery) {
            const q = currentSearchQuery.toLowerCase();
            const searchFields = [t.name, t.id, t.school, t.email, t.department || ''].join(' ').toLowerCase();
            if (!searchFields.includes(q)) return false;
        }

        return true;
    });

    if (filtered.length === 0) {
        container.innerHTML = `
            <div class="p-8 text-center text-gray-400">
                <i class="fa-solid fa-search text-3xl mb-2 text-gray-300"></i>
                <p class="text-sm">查無相符教師</p>
            </div>
        `;
        return;
    }

    const currentProjectId = localStorage.getItem('cwt_current_project') || 'P2026-01';

    container.innerHTML = filtered.map(t => {
        const isSelected = t.id === selectedTeacherId;
        const statusDot = t.accountStatus === 'active'
            ? '<span class="w-2 h-2 rounded-full bg-green-500 flex-shrink-0"></span>'
            : '<span class="w-2 h-2 rounded-full bg-red-400 flex-shrink-0"></span>';

        // 依據當前梯次取得此教師的身分（可能有多個）
        const rolesInProject = getTeacherRolesInProject(t.id, currentProjectId);
        let roleBadgesHtml = '';
        if (rolesInProject.length > 0) {
            roleBadgesHtml = rolesInProject.map(role => {
                const style = roleBadgeStyles[role] || 'bg-gray-100 text-gray-600';
                return `<span class="text-[10px] font-bold px-1.5 py-0.5 rounded ${style}">${role}</span>`;
            }).join(' ');
        } else {
            // 該教師不在此梯次中 — 顯示淡灰提示
            roleBadgesHtml = '<span class="text-[10px] text-gray-400 italic">未參與此梯次</span>';
        }

        return `
            <div class="teacher-list-item px-4 py-3 border-b border-gray-50 cursor-pointer hover:bg-gray-50 transition-colors ${isSelected ? 'list-item-active' : ''}"
                 data-id="${t.id}">
                <div class="flex items-center gap-3">
                    <div class="w-10 h-10 rounded-full flex items-center justify-center font-bold text-sm flex-shrink-0 ${t.accountStatus === 'active' ? 'bg-[var(--color-morandi)]/10 text-[var(--color-morandi)]' : 'bg-gray-200 text-gray-500'}">
                        ${t.name.charAt(0)}
                    </div>
                    <div class="flex-grow min-w-0">
                        <div class="flex items-center gap-2">
                            <span class="font-bold text-sm text-[var(--color-slate-main)] truncate">${t.name}</span>
                            ${statusDot}
                        </div>
                        <div class="flex items-center gap-2 text-xs text-gray-500 mt-0.5">
                            <span class="font-mono">${t.id}</span>
                            <span class="text-gray-300">|</span>
                            <span class="truncate">${t.school}</span>
                        </div>
                    </div>
                    <div class="flex-shrink-0 flex flex-col items-end gap-0.5">
                        ${roleBadgesHtml}
                    </div>
                </div>
            </div>
        `;
    }).join('');

    // 綁定點擊事件
    container.querySelectorAll('.teacher-list-item').forEach(el => {
        el.addEventListener('click', () => {
            const id = el.getAttribute('data-id');
            selectTeacher(id);
        });
    });
}

function selectTeacher(id) {
    selectedTeacherId = id;
    renderTeacherList(); // 更新左側選中狀態
    renderDetail(id);
}

// ==================== 右側詳細資料 ====================
function renderDetail(teacherId) {
    const teacher = teachersDb.find(t => t.id === teacherId);
    if (!teacher) return;

    const emptyState = document.getElementById('emptyDetailState');
    const detailContent = document.getElementById('detailContent');
    if (emptyState) emptyState.classList.add('hidden');
    if (detailContent) detailContent.classList.remove('hidden');

    // 頂部資訊
    document.getElementById('dtlAvatar').textContent = teacher.name.charAt(0);
    document.getElementById('dtlAvatar').className = `w-16 h-16 rounded-full flex items-center justify-center font-bold text-2xl flex-shrink-0 ${teacher.accountStatus === 'active' ? 'bg-[var(--color-morandi)] text-white' : 'bg-gray-400 text-white'}`;
    document.getElementById('dtlName').textContent = teacher.name;
    document.getElementById('dtlId').textContent = `ID: ${teacher.id}`;
    document.getElementById('dtlEmail').innerHTML = `<i class="fa-solid fa-envelope w-4 text-center"></i> <span>${teacher.email}</span>`;

    const statusBadge = document.getElementById('dtlStatusBadge');
    if (teacher.accountStatus === 'active') {
        statusBadge.className = 'text-xs font-bold px-2 py-0.5 rounded bg-green-100 text-green-700';
        statusBadge.textContent = '啟用中';
    } else {
        statusBadge.className = 'text-xs font-bold px-2 py-0.5 rounded bg-red-100 text-red-600';
        statusBadge.textContent = '已停用';
    }

    // 停用/啟用按鈕
    const toggleBtn = document.getElementById('btnToggleAccount');
    if (teacher.accountStatus === 'active') {
        toggleBtn.innerHTML = '<i class="fa-solid fa-user-lock"></i> <span>停用帳號</span>';
        toggleBtn.className = 'cursor-pointer w-full py-2 bg-[var(--color-terracotta)] text-white rounded-lg text-sm font-medium hover:opacity-90 transition-colors focus:outline-none shadow-sm flex items-center justify-center gap-2';
    } else {
        toggleBtn.innerHTML = '<i class="fa-solid fa-user-check"></i> <span>啟用帳號</span>';
        toggleBtn.className = 'cursor-pointer w-full py-2 bg-[var(--color-sage)] text-white rounded-lg text-sm font-medium hover:opacity-90 transition-colors focus:outline-none shadow-sm flex items-center justify-center gap-2';
    }

    // 渲染當前 Tab
    renderInfoTab(teacher);
    renderHistoryTab(teacher);
    renderProjectsTab(teacher);
}

// --- Tab: 基本資料 ---
function renderInfoTab(teacher) {
    const genderMap = { 'male': '男', 'female': '女' };
    const personalInfo = document.getElementById('dtlPersonalInfo');
    personalInfo.innerHTML = `
        ${infoRow('姓名', teacher.name)}
        ${infoRow('性別', genderMap[teacher.gender] || '未設定')}
        ${infoRow('電子信箱', teacher.email)}
        ${infoRow('聯絡電話', teacher.phone || '未設定')}
        ${infoRow('身分證字號', teacher.idNumber ? maskIdNumber(teacher.idNumber) : '未設定')}
        ${infoRow('帳號建立日期', teacher.createdAt)}
        ${infoRow('首次登入狀態', teacher.firstLogin ? '<span class="text-amber-600 font-bold">尚未首次登入</span>' : '<span class="text-green-600 font-bold">已完成首次登入</span>')}
        ${teacher.note ? infoRow('備註', teacher.note) : ''}
    `;

    const teachingBg = document.getElementById('dtlTeachingBg');
    teachingBg.innerHTML = `
        ${infoRow('任教學校', teacher.school)}
        ${infoRow('系所 / 科別', teacher.department || '未設定')}
        ${infoRow('職稱', teacher.title || '未設定')}
        ${infoRow('專長領域', teacher.expertise || '未設定')}
        ${infoRow('教學年資', teacher.years ? `${teacher.years} 年` : '未設定')}
        ${infoRow('最高學歷', teacher.education || '未設定')}
    `;
}

function infoRow(label, value) {
    return `
        <div class="flex items-start py-2 border-b border-gray-50 last:border-0">
            <span class="text-xs font-bold text-gray-500 w-28 flex-shrink-0 pt-0.5">${label}</span>
            <span class="text-sm text-[var(--color-slate-main)]">${value}</span>
        </div>
    `;
}

function maskIdNumber(idNum) {
    if (idNum.length < 4) return idNum;
    return idNum.substring(0, 3) + '****' + idNum.substring(idNum.length - 3);
}

// --- Tab: 命題歷程 ---
function renderHistoryTab(teacher) {
    const history = questionHistoryDb.filter(h => h.teacherId === teacher.id);

    // 統計卡片
    const statsCards = document.getElementById('historyStatsCards');
    const totalQuestions = history.length;
    const adopted = history.filter(h => h.status === 'adopted').length;
    const rejected = history.filter(h => h.status === 'rejected').length;
    const inProgress = history.filter(h => !['adopted', 'rejected'].includes(h.status)).length;

    statsCards.innerHTML = `
        <div class="bg-white rounded-xl shadow-sm border border-gray-200 p-3">
            <div class="text-gray-500 text-[10px] font-bold mb-0.5">累計產出</div>
            <div class="text-xl font-bold text-[var(--color-slate-main)]">${totalQuestions} <span class="text-xs font-normal text-gray-400">題</span></div>
        </div>
        <div class="bg-white rounded-xl shadow-sm border border-gray-200 p-3">
            <div class="text-gray-500 text-[10px] font-bold mb-0.5">已採用</div>
            <div class="text-xl font-bold text-[var(--color-sage)]">${adopted} <span class="text-xs font-normal text-gray-400">題</span></div>
        </div>
        <div class="bg-white rounded-xl shadow-sm border border-gray-200 p-3">
            <div class="text-gray-500 text-[10px] font-bold mb-0.5">不採用</div>
            <div class="text-xl font-bold text-[var(--color-terracotta)]">${rejected} <span class="text-xs font-normal text-gray-400">題</span></div>
        </div>
        <div class="bg-white rounded-xl shadow-sm border border-gray-200 p-3">
            <div class="text-gray-500 text-[10px] font-bold mb-0.5">審查中</div>
            <div class="text-xl font-bold text-[var(--color-morandi)]">${inProgress} <span class="text-xs font-normal text-gray-400">題</span></div>
        </div>
    `;

    // 歷程列表下拉過濾
    const filterSelect = document.getElementById('historyProjectFilter');
    const uniqueProjects = [...new Set(history.map(h => h.projectId))];
    filterSelect.innerHTML = '<option value="all">全部專案</option>' +
        uniqueProjects.map(pid => {
            const proj = projectsRef.find(p => p.id === pid);
            return `<option value="${pid}">${proj ? proj.name : pid}</option>`;
        }).join('');

    // 渲染歷程列表
    renderHistoryList(history, 'all');

    filterSelect.onchange = () => {
        renderHistoryList(history, filterSelect.value);
    };
}

function renderHistoryList(history, projectFilter) {
    const container = document.getElementById('historyListContainer');
    let filtered = projectFilter === 'all' ? history : history.filter(h => h.projectId === projectFilter);

    if (filtered.length === 0) {
        container.innerHTML = `<div class="p-8 text-center text-gray-400 text-sm">此教師尚無命題紀錄。</div>`;
        return;
    }

    container.innerHTML = filtered.map(h => {
        const st = statusMap[h.status] || { label: h.status, color: 'bg-gray-100 text-gray-600' };
        return `
            <div class="flex items-center px-6 py-3 border-b border-gray-50 hover:bg-gray-50 transition-colors text-sm">
                <div class="w-28 flex-shrink-0">
                    <span class="font-mono font-bold text-[var(--color-morandi)]">${h.qid}</span>
                </div>
                <div class="flex-grow min-w-0">
                    <span class="text-gray-700">${questionTypesMap[h.type] || h.type}</span>
                    <span class="text-gray-300 mx-1">|</span>
                    <span class="text-gray-500">${h.level}</span>
                </div>
                <div class="w-40 flex-shrink-0 text-xs text-gray-500 truncate hidden sm:block">${h.projectName}</div>
                <div class="w-24 flex-shrink-0 text-right">
                    <span class="text-xs px-2 py-0.5 rounded-full font-bold ${st.color}">${st.label}</span>
                </div>
            </div>
        `;
    }).join('');
}

// --- Tab: 參與專案 ---
function renderProjectsTab(teacher) {
    const container = document.getElementById('projectsListContainer');
    const projects = teacher.participatedProjects || [];

    if (projects.length === 0) {
        container.innerHTML = `
            <div class="bg-white rounded-xl border border-gray-200 p-8 text-center text-gray-400">
                <i class="fa-solid fa-folder-open text-3xl mb-2 text-gray-300"></i>
                <p class="text-sm">此教師尚未參與任何專案。</p>
            </div>
        `;
        return;
    }

    container.innerHTML = projects.map(pid => {
        const proj = projectsRef.find(p => p.id === pid);
        if (!proj) return '';

        const pStatus = projectStatusMap[proj.status] || { label: proj.status, color: 'bg-gray-100 text-gray-600' };
        const historyForProject = questionHistoryDb.filter(h => h.teacherId === teacher.id && h.projectId === pid);
        const adoptedCount = historyForProject.filter(h => h.status === 'adopted').length;
        const totalCount = historyForProject.length;

        return `
            <div class="bg-white rounded-xl shadow-sm border border-gray-200 p-5 hover:shadow-md transition-shadow">
                <div class="flex items-start justify-between mb-3">
                    <div>
                        <div class="flex items-center gap-2 mb-1">
                            <span class="text-xs font-bold px-2 py-0.5 rounded ${pStatus.color}">${pStatus.label}</span>
                            <span class="text-xs text-gray-400 font-mono">${pid}</span>
                        </div>
                        <h4 class="text-base font-bold text-[var(--color-slate-main)]">${proj.name}</h4>
                    </div>
                    ${proj.status !== 'closed' ? `<button class="text-xs text-red-500 hover:text-red-700 font-medium transition-colors btn-remove-project" data-project="${pid}"><i class="fa-solid fa-circle-minus mr-0.5"></i> 移除</button>` : ''}
                </div>
                <div class="flex gap-4 text-sm">
                    <div class="flex items-center gap-1 text-gray-600">
                        <i class="fa-solid fa-pen-nib text-[var(--color-morandi)] text-xs"></i>
                        <span>命題 <span class="font-bold">${totalCount}</span> 題</span>
                    </div>
                    <div class="flex items-center gap-1 text-gray-600">
                        <i class="fa-solid fa-circle-check text-[var(--color-sage)] text-xs"></i>
                        <span>採用 <span class="font-bold">${adoptedCount}</span> 題</span>
                    </div>
                    ${totalCount > 0 ? `
                        <div class="flex items-center gap-1 text-gray-600">
                            <i class="fa-solid fa-chart-line text-[var(--color-terracotta)] text-xs"></i>
                            <span>採用率 <span class="font-bold">${totalCount > 0 ? Math.round(adoptedCount / totalCount * 100) : 0}%</span></span>
                        </div>
                    ` : ''}
                </div>
            </div>
        `;
    }).join('');

    // 移除專案按鈕事件
    container.querySelectorAll('.btn-remove-project').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            const projectId = btn.getAttribute('data-project');
            const projName = projectsRef.find(p => p.id === projectId)?.name || projectId;

            Swal.fire({
                title: '確認移除?',
                html: `將此教師從 <b>${projName}</b> 中移除？`,
                icon: 'warning',
                showCancelButton: true,
                confirmButtonColor: '#D98A6C',
                cancelButtonColor: '#6B8EAD',
                confirmButtonText: '確認移除',
                cancelButtonText: '取消'
            }).then(result => {
                if (result.isConfirmed) {
                    const teacher = teachersDb.find(t => t.id === selectedTeacherId);
                    if (teacher) {
                        teacher.participatedProjects = teacher.participatedProjects.filter(p => p !== projectId);
                        renderDetail(selectedTeacherId);
                        renderStats();
                        Swal.fire({ icon: 'success', title: '已移除', toast: true, position: 'top-end', showConfirmButton: false, timer: 1500 });
                    }
                }
            });
        });
    });
}

// ==================== 篩選按鈕 ====================
function initFilterButtons() {
    document.querySelectorAll('.filter-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            currentFilter = btn.getAttribute('data-filter');

            // 更新按鈕樣式
            document.querySelectorAll('.filter-btn').forEach(b => {
                b.className = 'filter-btn flex-1 py-1 text-xs font-bold border border-gray-200 text-gray-500 rounded hover:bg-gray-100';
            });
            btn.className = 'filter-btn flex-1 py-1 text-xs font-bold rounded bg-[var(--color-morandi)] text-white';

            renderTeacherList();
        });
    });
}

// ==================== 搜尋輸入 ====================
function initSearchInput() {
    const searchInput = document.getElementById('teacherSearch');
    if (!searchInput) return;

    let debounceTimer;
    searchInput.addEventListener('input', () => {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(() => {
            currentSearchQuery = searchInput.value.trim();
            renderTeacherList();
        }, 200);
    });
}

// ==================== Slide-over Panel ====================
function initSlideOverPanel() {
    const wrapper = document.getElementById('slideOverWrapper');
    const backdrop = document.getElementById('slideOverBackdrop');
    const panel = document.getElementById('slideOverPanel');
    const closeBtns = document.querySelectorAll('.close-panel-btn');

    // 新增教師按鈕
    document.getElementById('btnOpenNewTeacher')?.addEventListener('click', () => {
        editingTeacherId = null;
        resetForm();
        document.querySelector('#panelTitle span').textContent = '新增教師';
        document.querySelector('#panelTitle i').className = 'fa-solid fa-user-plus text-[var(--color-morandi)]';
        openPanel();
    });

    // 編輯教師按鈕
    document.getElementById('btnEditTeacher')?.addEventListener('click', () => {
        if (!selectedTeacherId) return;
        editingTeacherId = selectedTeacherId;
        populateForm(selectedTeacherId);
        document.querySelector('#panelTitle span').textContent = '編輯教師';
        document.querySelector('#panelTitle i').className = 'fa-solid fa-pen-to-square text-[var(--color-morandi)]';
        openPanel();
    });

    // 儲存按鈕
    document.getElementById('btnSaveTeacher')?.addEventListener('click', saveTeacher);

    // 關閉按鈕
    closeBtns.forEach(btn => btn.addEventListener('click', closePanel));
    backdrop?.addEventListener('click', closePanel);

    function openPanel() {
        wrapper.classList.remove('hidden');
        void wrapper.offsetWidth;
        backdrop.classList.remove('opacity-0');
        backdrop.classList.add('opacity-100');
        panel.classList.remove('translate-x-full');
        panel.classList.add('translate-x-0');
    }
}

function closePanel() {
    const wrapper = document.getElementById('slideOverWrapper');
    const backdrop = document.getElementById('slideOverBackdrop');
    const panel = document.getElementById('slideOverPanel');

    backdrop.classList.remove('opacity-100');
    backdrop.classList.add('opacity-0');
    panel.classList.remove('translate-x-0');
    panel.classList.add('translate-x-full');

    setTimeout(() => wrapper.classList.add('hidden'), 300);
}

function resetForm() {
    document.getElementById('frmName').value = '';
    document.getElementById('frmGender').value = '';
    document.getElementById('frmEmail').value = '';
    document.getElementById('frmPhone').value = '';
    document.getElementById('frmIdNumber').value = '';
    document.getElementById('frmSchool').value = '';
    document.getElementById('frmDepartment').value = '';
    document.getElementById('frmTitle').value = '';
    document.getElementById('frmExpertise').value = '';
    document.getElementById('frmYears').value = '';
    document.getElementById('frmEducation').value = '';
    document.getElementById('frmNote').value = '';
    document.querySelector('input[name="frmAccountStatus"][value="active"]').checked = true;
}

function populateForm(teacherId) {
    const teacher = teachersDb.find(t => t.id === teacherId);
    if (!teacher) return;

    document.getElementById('frmName').value = teacher.name;
    document.getElementById('frmGender').value = teacher.gender || '';
    document.getElementById('frmEmail').value = teacher.email;
    document.getElementById('frmPhone').value = teacher.phone || '';
    document.getElementById('frmIdNumber').value = teacher.idNumber || '';
    document.getElementById('frmSchool').value = teacher.school;
    document.getElementById('frmDepartment').value = teacher.department || '';
    document.getElementById('frmTitle').value = teacher.title || '';
    document.getElementById('frmExpertise').value = teacher.expertise || '';
    document.getElementById('frmYears').value = teacher.years || '';
    document.getElementById('frmEducation').value = teacher.education || '';
    document.getElementById('frmNote').value = teacher.note || '';

    const statusRadio = document.querySelector(`input[name="frmAccountStatus"][value="${teacher.accountStatus}"]`);
    if (statusRadio) statusRadio.checked = true;
}

function saveTeacher() {
    const name = document.getElementById('frmName').value.trim();
    const email = document.getElementById('frmEmail').value.trim();
    const school = document.getElementById('frmSchool').value.trim();

    // 基本驗證
    if (!name || !email || !school) {
        Swal.fire({ icon: 'warning', title: '請填寫必要欄位', text: '姓名、電子信箱、任教學校為必填。', confirmButtonColor: '#6B8EAD' });
        return;
    }

    const formData = {
        name,
        gender: document.getElementById('frmGender').value,
        email,
        phone: document.getElementById('frmPhone').value.trim(),
        idNumber: document.getElementById('frmIdNumber').value.trim(),
        school,
        department: document.getElementById('frmDepartment').value.trim(),
        title: document.getElementById('frmTitle').value,
        expertise: document.getElementById('frmExpertise').value.trim(),
        years: parseInt(document.getElementById('frmYears').value) || 0,
        education: document.getElementById('frmEducation').value,
        note: document.getElementById('frmNote').value.trim(),
        accountStatus: document.querySelector('input[name="frmAccountStatus"]:checked').value
    };

    if (editingTeacherId) {
        // 編輯模式
        const idx = teachersDb.findIndex(t => t.id === editingTeacherId);
        if (idx >= 0) {
            teachersDb[idx] = { ...teachersDb[idx], ...formData };
        }
        Swal.fire({ icon: 'success', title: '已更新教師資料', toast: true, position: 'top-end', showConfirmButton: false, timer: 2000, timerProgressBar: true });
    } else {
        // 新增模式 - 產生新 ID
        const maxNum = teachersDb
            .filter(t => t.id.startsWith('T'))
            .map(t => parseInt(t.id.replace('T', '')))
            .reduce((max, n) => Math.max(max, n), 1000);
        const newId = `T${maxNum + 1}`;

        teachersDb.push({
            id: newId,
            ...formData,
            createdAt: new Date().toISOString().split('T')[0],
            firstLogin: true,
            participatedProjects: []
        });

        selectedTeacherId = newId;
        Swal.fire({ icon: 'success', title: '已新增教師', text: `帳號 ${email}，預設密碼 Cwt2026!`, confirmButtonColor: '#6B8EAD' });
    }

    closePanel();
    renderStats();
    renderTeacherList();
    if (selectedTeacherId) renderDetail(selectedTeacherId);
}

// ==================== Detail Tabs ====================
function initDetailTabs() {
    document.querySelectorAll('.detail-tab-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const tab = btn.getAttribute('data-tab');
            currentDetailTab = tab;

            // 更新 Tab 按鈕樣式
            document.querySelectorAll('.detail-tab-btn').forEach(b => {
                b.classList.remove('active-tab');
                b.classList.add('text-gray-500');
            });
            btn.classList.add('active-tab');
            btn.classList.remove('text-gray-500');

            // 顯示對應面板
            document.querySelectorAll('.tab-panel').forEach(p => p.classList.add('hidden'));
            const panelId = tab === 'info' ? 'tabPanelInfo' : tab === 'history' ? 'tabPanelHistory' : 'tabPanelProjects';
            document.getElementById(panelId)?.classList.remove('hidden');
        });
    });
}

// ==================== Action Buttons ====================
function initActionButtons() {
    // 停用/啟用帳號
    document.getElementById('btnToggleAccount')?.addEventListener('click', () => {
        if (!selectedTeacherId) return;
        const teacher = teachersDb.find(t => t.id === selectedTeacherId);
        if (!teacher) return;

        const newStatus = teacher.accountStatus === 'active' ? 'inactive' : 'active';
        const actionText = newStatus === 'active' ? '啟用' : '停用';

        Swal.fire({
            title: `確認${actionText}帳號?`,
            html: `${teacher.name} 的帳號將被<b>${actionText}</b>。`,
            icon: 'warning',
            showCancelButton: true,
            confirmButtonColor: newStatus === 'active' ? '#8EAB94' : '#D98A6C',
            cancelButtonColor: '#6B8EAD',
            confirmButtonText: `確認${actionText}`,
            cancelButtonText: '取消'
        }).then(result => {
            if (result.isConfirmed) {
                teacher.accountStatus = newStatus;
                renderStats();
                renderTeacherList();
                renderDetail(selectedTeacherId);
                Swal.fire({ icon: 'success', title: `帳號已${actionText}`, toast: true, position: 'top-end', showConfirmButton: false, timer: 1500 });
            }
        });
    });

    // 重設密碼
    document.getElementById('btnResetPassword')?.addEventListener('click', () => {
        if (!selectedTeacherId) return;
        const teacher = teachersDb.find(t => t.id === selectedTeacherId);
        if (!teacher) return;

        Swal.fire({
            title: '確認重設密碼?',
            html: `將 <b>${teacher.name}</b> 的密碼重設為預設密碼 <code>Cwt2026!</code>，<br>下次登入時系統將要求變更密碼。`,
            icon: 'question',
            showCancelButton: true,
            confirmButtonColor: '#6B8EAD',
            cancelButtonColor: '#d33',
            confirmButtonText: '確認重設',
            cancelButtonText: '取消'
        }).then(result => {
            if (result.isConfirmed) {
                teacher.firstLogin = true;
                renderDetail(selectedTeacherId);
                Swal.fire({ icon: 'success', title: '密碼已重設', text: '預設密碼: Cwt2026!', toast: true, position: 'top-end', showConfirmButton: false, timer: 2000 });
            }
        });
    });

    // 匯出名單（模擬）
    document.getElementById('btnExportTeachers')?.addEventListener('click', () => {
        Swal.fire({
            icon: 'info',
            title: '匯出功能 (DEMO)',
            text: '此為模擬功能，實際上線後將匯出 CSV / Excel 格式之教師名單。',
            confirmButtonColor: '#6B8EAD'
        });
    });
}

// ==================== 加入梯次 Modal ====================
function initAssignProjectModal() {
    const modal = document.getElementById('assignProjectModal');
    const panel = document.getElementById('assignProjectPanel');
    const closeBtns = document.querySelectorAll('.close-assign-modal');

    // 開啟 Modal
    document.getElementById('btnAssignProject')?.addEventListener('click', () => {
        if (!selectedTeacherId) return;
        const teacher = teachersDb.find(t => t.id === selectedTeacherId);
        if (!teacher) return;

        // 過濾掉已參與的專案（僅顯示未加入且未結案的）
        const availableProjects = projectsRef.filter(p =>
            !teacher.participatedProjects.includes(p.id) && p.status !== 'closed'
        );

        const select = document.getElementById('assignProjectSelect');
        if (availableProjects.length === 0) {
            select.innerHTML = '<option value="">無可加入的專案</option>';
        } else {
            select.innerHTML = availableProjects.map(p => `<option value="${p.id}">${p.name}</option>`).join('');
        }

        // 身分勾選
        const roleContainer = document.getElementById('assignRoleCheckboxes');
        roleContainer.innerHTML = roleOptions.map(role => `
            <label class="flex items-center gap-2 cursor-pointer bg-gray-50 px-3 py-2 rounded border border-gray-200 hover:bg-gray-100 transition-colors">
                <input type="checkbox" value="${role}" class="assign-role-cb text-[var(--color-morandi)] focus:ring-[var(--color-morandi)] rounded">
                <span class="text-sm">${role}</span>
            </label>
        `).join('');

        openAssignModal();
    });

    // 確認加入
    document.getElementById('btnConfirmAssign')?.addEventListener('click', () => {
        const projectId = document.getElementById('assignProjectSelect').value;
        if (!projectId) {
            Swal.fire({ icon: 'warning', title: '請選擇專案', confirmButtonColor: '#6B8EAD' });
            return;
        }

        const selectedRoles = [...document.querySelectorAll('.assign-role-cb:checked')].map(cb => cb.value);
        if (selectedRoles.length === 0) {
            Swal.fire({ icon: 'warning', title: '請至少選擇一個身分', confirmButtonColor: '#6B8EAD' });
            return;
        }

        const teacher = teachersDb.find(t => t.id === selectedTeacherId);
        if (teacher) {
            teacher.participatedProjects.push(projectId);
            renderDetail(selectedTeacherId);
            renderStats();
        }

        closeAssignModal();
        const projName = projectsRef.find(p => p.id === projectId)?.name || projectId;
        Swal.fire({
            icon: 'success',
            title: '已加入專案',
            html: `${teacher.name} 已加入 <b>${projName}</b>，<br>身分：${selectedRoles.join('、')}`,
            toast: true,
            position: 'top-end',
            showConfirmButton: false,
            timer: 2500,
            timerProgressBar: true
        });
    });

    // 關閉 Modal
    closeBtns.forEach(btn => btn.addEventListener('click', closeAssignModal));
    modal?.querySelector('.modal-backdrop')?.addEventListener('click', closeAssignModal);

    function openAssignModal() {
        modal.classList.remove('hidden');
        void modal.offsetWidth;
        panel.classList.remove('scale-95', 'opacity-0');
        panel.classList.add('scale-100', 'opacity-100');
    }
}

function closeAssignModal() {
    const modal = document.getElementById('assignProjectModal');
    const panel = document.getElementById('assignProjectPanel');
    panel.classList.remove('scale-100', 'opacity-100');
    panel.classList.add('scale-95', 'opacity-0');
    setTimeout(() => modal.classList.add('hidden'), 300);
}
