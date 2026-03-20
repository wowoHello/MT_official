/**
 * Roles & Permissions Module
 * 負責角色與權限管理頁面的業務邏輯：
 * - Tab 1：人員帳號管理（內部人員 CRUD）
 * - Tab 2：角色權限管理（角色卡片 + 功能區塊開關）
 * Version: 1.0 (DEMO)
 */

// ===========================
// 假資料 — 功能區塊定義
// ===========================
const MODULES = [
    { key: 'dashboard', label: '命題儀表板', icon: 'fa-chart-pie', page: 'dashboard.html' },
    { key: 'projects', label: '命題專案管理', icon: 'fa-box-archive', page: 'projects.html' },
    { key: 'overview', label: '命題總覽', icon: 'fa-globe', page: 'overview.html' },
    { key: 'compose', label: '命題任務', icon: 'fa-pen-to-square', page: 'cwt-list.html' },
    { key: 'review', label: '審題任務', icon: 'fa-magnifying-glass-chart', page: 'reviews.html' },
    { key: 'teachers', label: '教師管理系統', icon: 'fa-chalkboard-user', page: 'teachers.html' },
    { key: 'roles', label: '角色與權限管理', icon: 'fa-id-card-clip', page: 'roles.html' },
    { key: 'announcements', label: '系統公告/使用說明', icon: 'fa-bullhorn', page: 'announcements.html' }
];

// ===========================
// 假資料 — 角色清單
// ===========================
const mockRoles = [
    {
        id: 'R001',
        name: '命題教師',
        category: 'external',
        description: '負責命製試題的外部教師，可進行命題與互審任務。',
        isDefault: true,
        permissions: {
            dashboard: false, projects: false, overview: false,
            compose: true, review: true, teachers: false,
            roles: false, announcements: true
        },
        announcementPerm: 'view' // 'view' | 'edit'
    },
    {
        id: 'R002',
        name: '審題委員',
        category: 'external',
        description: '外部專家學者，負責專審階段的題目審查工作。',
        isDefault: true,
        permissions: {
            dashboard: false, projects: false, overview: false,
            compose: false, review: true, teachers: false,
            roles: false, announcements: true
        },
        announcementPerm: 'view'
    },
    {
        id: 'R003',
        name: '總召',
        category: 'internal',
        description: '負責總召審題、最終裁決，可檢視儀表板與命題總覽。',
        isDefault: true,
        permissions: {
            dashboard: true, projects: false, overview: true,
            compose: false, review: true, teachers: false,
            roles: false, announcements: true
        },
        announcementPerm: 'view'
    },
    {
        id: 'R004',
        name: '系統管理員',
        category: 'internal',
        description: '擁有系統所有功能的最高權限管理者。',
        isDefault: false,
        permissions: {
            dashboard: true, projects: true, overview: true,
            compose: true, review: true, teachers: true,
            roles: true, announcements: true
        },
        announcementPerm: 'edit'
    },
    {
        id: 'R005',
        name: '計畫主持人',
        category: 'internal',
        description: '產學計畫負責人，管理專案、教師與進度監控。',
        isDefault: false,
        permissions: {
            dashboard: true, projects: true, overview: true,
            compose: false, review: false, teachers: true,
            roles: false, announcements: true
        },
        announcementPerm: 'edit'
    },
    {
        id: 'R006',
        name: '教務管理者',
        category: 'internal',
        description: '負責檢視命題進度與總覽資料的行政人員。',
        isDefault: false,
        permissions: {
            dashboard: true, projects: false, overview: true,
            compose: false, review: false, teachers: false,
            roles: false, announcements: true
        },
        announcementPerm: 'view'
    }
];

// ===========================
// 假資料 — 內部人員帳號
// ===========================
const mockAccounts = [
    {
        id: 'A001', name: '劉明杰', username: 'admin',
        email: 'jay@cwt.com.tw', roleId: 'R004',
        title: '系統工程師', status: 'active',
        note: '負責系統開發與維運', createdAt: '2025-08-01',
        firstLogin: true
    },
    {
        id: 'A002', name: '陳佳琪', username: 'manager01',
        email: 'chiaki@cwt.com.tw', roleId: 'R005',
        title: '計畫主持人', status: 'active',
        note: '', createdAt: '2025-09-15',
        firstLogin: true
    },
    {
        id: 'A003', name: '林志偉', username: 'supervisor',
        email: 'wei@cwt.com.tw', roleId: 'R003',
        title: '總召集人', status: 'active',
        note: '115年度春季梯次總召', createdAt: '2025-10-01',
        firstLogin: false
    },
    {
        id: 'A004', name: '黃雅萍', username: 'edu_admin',
        email: 'yaping@cwt.com.tw', roleId: 'R006',
        title: '教務行政', status: 'active',
        note: '', createdAt: '2025-11-20',
        firstLogin: true
    },
    {
        id: 'A005', name: '張書豪', username: 'admin02',
        email: 'hao@cwt.com.tw', roleId: 'R004',
        title: '資深工程師', status: 'active',
        note: '協助系統維護', createdAt: '2025-12-01',
        firstLogin: false
    },
    {
        id: 'A006', name: '王美玲', username: 'meiling',
        email: '', roleId: 'R005',
        title: '協同主持人', status: 'inactive',
        note: '114年度計畫結束後暫停帳號', createdAt: '2024-06-10',
        firstLogin: false
    },
    {
        id: 'A007', name: '吳建廷', username: 'tingwu',
        email: 'ting@cwt.com.tw', roleId: 'R003',
        title: '總召集人', status: 'active',
        note: '115年度秋季梯次總召', createdAt: '2026-01-05',
        firstLogin: true
    }
];

// ===========================
// 狀態變數
// ===========================
let currentTab = 'accounts';
let currentAccountFilter = 'all';
let selectedAccountId = null;
let editingAccountId = null; // null = 新增, 有值 = 編輯
let editingRoleId = null;

// ===========================
// DOMContentLoaded 初始化
// ===========================
document.addEventListener('DOMContentLoaded', () => {
    initTabs();
    initAccountPanel();
    initRoleModal();

    // 首次渲染
    renderAccountList();
    updateAccountStats();
    renderRoleCards();
    updateRoleStats();

    // 搜尋
    document.getElementById('accountSearch').addEventListener('input', () => renderAccountList());

    // 篩選按鈕
    document.querySelectorAll('.acct-filter-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            currentAccountFilter = btn.dataset.filter;
            // 樣式切換
            document.querySelectorAll('.acct-filter-btn').forEach(b => {
                b.className = 'acct-filter-btn flex-1 py-1 text-xs font-bold border border-gray-200 text-gray-500 rounded hover:bg-gray-100 cursor-pointer';
            });
            btn.className = 'acct-filter-btn flex-1 py-1 text-xs font-bold rounded bg-[var(--color-morandi)] text-white cursor-pointer';
            renderAccountList();
        });
    });

    // 新增人員按鈕
    document.getElementById('btnNewAccount').addEventListener('click', () => openAccountPanel(null));

    // 新增角色按鈕
    document.getElementById('btnNewRole').addEventListener('click', () => openRoleModal(null));

    // 編輯按鈕（詳情面板）
    document.getElementById('btnEditAccount').addEventListener('click', () => {
        if (selectedAccountId) openAccountPanel(selectedAccountId);
    });

    // 停用/啟用帳號
    document.getElementById('btnToggleAccountStatus').addEventListener('click', () => {
        if (!selectedAccountId) return;
        const acct = mockAccounts.find(a => a.id === selectedAccountId);
        if (!acct) return;

        const isActive = acct.status === 'active';
        const action = isActive ? '停用' : '啟用';

        Swal.fire({
            title: `確認${action}帳號？`,
            text: `即將${action}「${acct.name}」的帳號。`,
            icon: 'warning',
            showCancelButton: true,
            confirmButtonColor: '#6B8EAD',
            cancelButtonColor: '#d33',
            confirmButtonText: `是的，${action}`,
            cancelButtonText: '取消'
        }).then(result => {
            if (result.isConfirmed) {
                acct.status = isActive ? 'inactive' : 'active';
                renderAccountList();
                updateAccountStats();
                showAccountDetail(selectedAccountId);
                Swal.fire({ icon: 'success', title: `已${action}`, text: `帳號已成功${action}。`, timer: 1500, showConfirmButton: false });
            }
        });
    });

    // 重設密碼
    document.getElementById('btnResetAccountPwd').addEventListener('click', () => {
        if (!selectedAccountId) return;
        const acct = mockAccounts.find(a => a.id === selectedAccountId);
        if (!acct) return;

        Swal.fire({
            title: '確認重設密碼？',
            html: `將「${acct.name}」的密碼重設為預設密碼 <b class="font-mono">01024304</b>。`,
            icon: 'question',
            showCancelButton: true,
            confirmButtonColor: '#6B8EAD',
            cancelButtonColor: '#d33',
            confirmButtonText: '確認重設',
            cancelButtonText: '取消'
        }).then(result => {
            if (result.isConfirmed) {
                acct.firstLogin = true;
                showAccountDetail(selectedAccountId);
                Swal.fire({ icon: 'success', title: '密碼已重設', text: '下次登入將要求變更密碼。', timer: 1500, showConfirmButton: false });
            }
        });
    });
});

// ===========================
// Tab 切換
// ===========================
function initTabs() {
    document.querySelectorAll('.role-tab-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const tab = btn.dataset.tab;
            currentTab = tab;

            // 標籤樣式切換
            document.querySelectorAll('.role-tab-btn').forEach(b => {
                b.classList.remove('active-tab');
                b.classList.add('text-gray-500');
            });
            btn.classList.add('active-tab');
            btn.classList.remove('text-gray-500');

            // 面板切換
            document.getElementById('tabPanelAccounts').classList.toggle('hidden', tab !== 'accounts');
            document.getElementById('tabPanelRoles').classList.toggle('hidden', tab !== 'roles');

            // 按鈕切換
            document.getElementById('btnNewAccount').classList.toggle('hidden', tab !== 'accounts');
            document.getElementById('btnNewRole').classList.toggle('hidden', tab !== 'roles');
        });
    });
}

// ===========================
// 人員帳號列表渲染
// ===========================
function renderAccountList() {
    const container = document.getElementById('accountListContainer');
    const keyword = document.getElementById('accountSearch').value.trim().toLowerCase();

    let list = [...mockAccounts];

    // 狀態篩選
    if (currentAccountFilter === 'active') list = list.filter(a => a.status === 'active');
    if (currentAccountFilter === 'inactive') list = list.filter(a => a.status === 'inactive');

    // 關鍵字篩選
    if (keyword) {
        list = list.filter(a =>
            a.name.toLowerCase().includes(keyword) ||
            a.username.toLowerCase().includes(keyword) ||
            (a.email && a.email.toLowerCase().includes(keyword))
        );
    }

    if (list.length === 0) {
        container.innerHTML = `
            <div class="p-8 text-center text-gray-400">
                <i class="fa-solid fa-user-slash text-3xl mb-2"></i>
                <p class="text-sm">無符合條件的人員</p>
            </div>`;
        return;
    }

    container.innerHTML = list.map(acct => {
        const role = mockRoles.find(r => r.id === acct.roleId);
        const isSelected = acct.id === selectedAccountId;
        const statusDot = acct.status === 'active'
            ? '<span class="w-2 h-2 rounded-full bg-green-400 flex-shrink-0"></span>'
            : '<span class="w-2 h-2 rounded-full bg-red-400 flex-shrink-0"></span>';

        // 角色標籤顏色
        const roleBadgeColor = role?.isDefault
            ? 'bg-amber-100 text-amber-700'
            : 'bg-purple-100 text-purple-700';

        return `
            <div class="account-list-item px-4 py-3 border-b border-gray-100 hover:bg-gray-50 transition-colors cursor-pointer flex items-start gap-3 ${isSelected ? 'list-item-active' : ''}"
                 data-id="${acct.id}">
                <div class="w-10 h-10 rounded-full bg-[var(--color-morandi)] text-white flex items-center justify-center font-bold text-sm flex-shrink-0 mt-0.5">
                    ${acct.name.charAt(0)}
                </div>
                <div class="min-w-0 flex-grow">
                    <div class="flex items-center gap-2 mb-0.5">
                        ${statusDot}
                        <span class="font-bold text-sm text-[var(--color-slate-main)] truncate">${acct.name}</span>
                        <span class="text-[10px] font-bold px-1.5 py-0.5 rounded ${roleBadgeColor} flex-shrink-0">${role?.name || '未指派'}</span>
                    </div>
                    <p class="text-xs text-gray-500 font-mono truncate">@${acct.username}</p>
                    ${acct.title ? `<p class="text-xs text-gray-400 truncate mt-0.5">${acct.title}</p>` : ''}
                </div>
            </div>`;
    }).join('');

    // 綁定點擊事件
    container.querySelectorAll('.account-list-item').forEach(el => {
        el.addEventListener('click', () => {
            selectedAccountId = el.dataset.id;
            showAccountDetail(selectedAccountId);
            renderAccountList(); // 更新 active 狀態
        });
    });
}

// ===========================
// 人員詳情顯示
// ===========================
function showAccountDetail(id) {
    const acct = mockAccounts.find(a => a.id === id);
    if (!acct) return;

    const role = mockRoles.find(r => r.id === acct.roleId);
    const emptyState = document.getElementById('acctEmptyDetailState');
    const detailContent = document.getElementById('acctDetailContent');

    emptyState.classList.add('hidden');
    detailContent.classList.remove('hidden');

    // 頭像與基本資訊
    document.getElementById('acctDtlAvatar').textContent = acct.name.charAt(0);
    document.getElementById('acctDtlAvatar').className = `w-16 h-16 rounded-full ${acct.status === 'active' ? 'bg-[var(--color-morandi)]' : 'bg-gray-400'} text-white flex items-center justify-center font-bold text-2xl flex-shrink-0`;
    document.getElementById('acctDtlName').textContent = acct.name;
    document.getElementById('acctDtlAccount').textContent = `帳號: @${acct.username}`;

    const emailEl = document.getElementById('acctDtlEmail');
    emailEl.querySelector('span').textContent = acct.email || '未填寫';

    // 狀態徽章
    const badge = document.getElementById('acctDtlStatusBadge');
    if (acct.status === 'active') {
        badge.className = 'text-xs font-bold px-2 py-0.5 rounded bg-green-100 text-green-700';
        badge.textContent = '啟用';
    } else {
        badge.className = 'text-xs font-bold px-2 py-0.5 rounded bg-red-100 text-red-600';
        badge.textContent = '停用';
    }

    // 停用/啟用按鈕文字
    const toggleBtn = document.getElementById('btnToggleAccountStatus');
    if (acct.status === 'active') {
        toggleBtn.innerHTML = '<i class="fa-solid fa-user-lock"></i> <span>停用帳號</span>';
        toggleBtn.className = 'cursor-pointer w-full py-2 bg-[var(--color-terracotta)] text-white rounded-lg text-sm font-medium hover:opacity-90 transition-colors focus:outline-none shadow-sm flex items-center justify-center gap-2';
    } else {
        toggleBtn.innerHTML = '<i class="fa-solid fa-user-check"></i> <span>啟用帳號</span>';
        toggleBtn.className = 'cursor-pointer w-full py-2 bg-[var(--color-sage)] text-white rounded-lg text-sm font-medium hover:opacity-90 transition-colors focus:outline-none shadow-sm flex items-center justify-center gap-2';
    }

    // 基本資料卡
    const basicInfo = document.getElementById('acctDtlBasicInfo');
    basicInfo.innerHTML = buildInfoRow('姓名', acct.name)
        + buildInfoRow('登入帳號', `@${acct.username}`)
        + buildInfoRow('電子信箱', acct.email || '<span class="text-gray-400">未填寫</span>')
        + buildInfoRow('公司職稱', acct.title || '<span class="text-gray-400">未填寫</span>')
        + buildInfoRow('建立日期', acct.createdAt)
        + buildInfoRow('首次登入', acct.firstLogin ? '<span class="text-amber-600 font-bold">尚未登入</span>' : '<span class="text-green-600">已完成</span>')
        + buildInfoRow('備註', acct.note || '<span class="text-gray-400">無</span>');

    // 權限資訊卡
    const permInfo = document.getElementById('acctDtlPermInfo');
    const permModules = role ? MODULES.filter(m => role.permissions[m.key]) : [];
    const permBadges = permModules.map(m =>
        `<span class="inline-flex items-center gap-1 text-xs font-medium px-2.5 py-1 rounded-lg bg-[var(--color-morandi)]/10 text-[var(--color-morandi)]">
            <i class="fa-solid ${m.icon} text-[10px]"></i> ${m.label}
        </span>`
    ).join('');

    // 公告權限層級
    const annPerm = role?.announcementPerm === 'edit'
        ? '<span class="text-xs font-bold text-[var(--color-sage)]"><i class="fa-solid fa-pen mr-1"></i>瀏覽與編輯</span>'
        : '<span class="text-xs font-bold text-gray-500"><i class="fa-solid fa-eye mr-1"></i>僅瀏覽</span>';

    permInfo.innerHTML = buildInfoRow('身份別', `<span class="font-bold">${role?.name || '未指派'}</span>` + (role?.isDefault ? ' <span class="text-[10px] text-amber-600 bg-amber-50 px-1.5 py-0.5 rounded ml-1">預設角色</span>' : ''))
        + buildInfoRow('角色分類', role?.category === 'internal' ? '內部人員' : '外部人員')
        + `<div class="pt-2 border-t border-gray-100">
             <p class="text-xs font-bold text-gray-500 mb-2">可存取功能區塊</p>
             <div class="flex flex-wrap gap-1.5">${permBadges || '<span class="text-xs text-gray-400">無權限</span>'}</div>
           </div>`
        + (role?.permissions.announcements ? `<div class="pt-2 border-t border-gray-100">${buildInfoRow('公告權限層級', annPerm)}</div>` : '');
}

// 建立資訊列的共用函式
function buildInfoRow(label, value) {
    return `<div class="flex items-start text-sm py-1.5">
                <span class="w-24 text-gray-500 font-medium flex-shrink-0">${label}</span>
                <span class="text-[var(--color-slate-main)]">${value}</span>
            </div>`;
}

// ===========================
// 人員帳號統計
// ===========================
function updateAccountStats() {
    document.getElementById('acctStatTotal').textContent = mockAccounts.length;
    document.getElementById('acctStatActive').textContent = mockAccounts.filter(a => a.status === 'active').length;
    document.getElementById('acctStatInactive').textContent = mockAccounts.filter(a => a.status === 'inactive').length;
    // 系統管理員（roleId = R004）
    document.getElementById('acctStatAdmin').textContent = mockAccounts.filter(a => a.roleId === 'R004').length;
}

// ===========================
// 帳號 Slide-over 面板
// ===========================
function initAccountPanel() {
    const wrapper = document.getElementById('accountSlideOver');
    const backdrop = document.getElementById('accountSlideBackdrop');
    const panel = document.getElementById('accountSlidePanel');

    // 關閉按鈕
    document.querySelectorAll('.close-account-panel').forEach(btn => {
        btn.addEventListener('click', closeAccountPanel);
    });

    // 點擊背景關閉
    backdrop.addEventListener('click', closeAccountPanel);

    // 儲存
    document.getElementById('btnSaveAccount').addEventListener('click', saveAccount);
}

function openAccountPanel(accountId) {
    editingAccountId = accountId;
    const wrapper = document.getElementById('accountSlideOver');
    const backdrop = document.getElementById('accountSlideBackdrop');
    const panel = document.getElementById('accountSlidePanel');
    const titleEl = document.getElementById('accountPanelTitle');

    // 填充角色下拉選單（僅內部人員角色）
    const roleSelect = document.getElementById('frmAcctRole');
    roleSelect.innerHTML = '<option value="">請選擇身份別</option>' +
        mockRoles.filter(r => r.category === 'internal').map(r =>
            `<option value="${r.id}">${r.name}${r.isDefault ? ' (預設)' : ''}</option>`
        ).join('');

    if (accountId) {
        // 編輯模式
        const acct = mockAccounts.find(a => a.id === accountId);
        if (!acct) return;
        titleEl.querySelector('span').textContent = '編輯人員';
        titleEl.querySelector('i').className = 'fa-solid fa-user-pen text-[var(--color-morandi)]';

        document.getElementById('frmAcctName').value = acct.name;
        document.getElementById('frmAcctUsername').value = acct.username;
        document.getElementById('frmAcctUsername').disabled = true; // 帳號不可修改
        document.getElementById('frmAcctEmail').value = acct.email || '';
        document.getElementById('frmAcctRole').value = acct.roleId;
        document.getElementById('frmAcctTitle').value = acct.title || '';
        document.getElementById('frmAcctNote').value = acct.note || '';
        document.querySelector(`input[name="frmAcctStatus"][value="${acct.status}"]`).checked = true;
    } else {
        // 新增模式
        titleEl.querySelector('span').textContent = '新增人員';
        titleEl.querySelector('i').className = 'fa-solid fa-user-plus text-[var(--color-morandi)]';

        document.getElementById('accountForm').reset();
        document.getElementById('frmAcctUsername').disabled = false;
    }

    // 開啟動畫
    wrapper.classList.remove('hidden');
    requestAnimationFrame(() => {
        backdrop.classList.remove('opacity-0');
        backdrop.classList.add('opacity-100');
        panel.classList.remove('translate-x-full');
        panel.classList.add('translate-x-0');
    });
}

function closeAccountPanel() {
    const wrapper = document.getElementById('accountSlideOver');
    const backdrop = document.getElementById('accountSlideBackdrop');
    const panel = document.getElementById('accountSlidePanel');

    backdrop.classList.remove('opacity-100');
    backdrop.classList.add('opacity-0');
    panel.classList.remove('translate-x-0');
    panel.classList.add('translate-x-full');

    setTimeout(() => {
        wrapper.classList.add('hidden');
        editingAccountId = null;
    }, 300);
}

function saveAccount() {
    const name = document.getElementById('frmAcctName').value.trim();
    const username = document.getElementById('frmAcctUsername').value.trim();
    const roleId = document.getElementById('frmAcctRole').value;

    // 基本驗證
    if (!name || !username || !roleId) {
        Swal.fire({ icon: 'warning', title: '欄位不完整', text: '請填寫姓名、帳號與身份別。', confirmButtonColor: '#6B8EAD' });
        return;
    }

    // 帳號重複檢查（僅新增）
    if (!editingAccountId) {
        const exists = mockAccounts.some(a => a.username.toLowerCase() === username.toLowerCase());
        if (exists) {
            Swal.fire({ icon: 'error', title: '帳號已存在', text: '此帳號名稱已被使用，請更換。', confirmButtonColor: '#6B8EAD' });
            return;
        }
    }

    const email = document.getElementById('frmAcctEmail').value.trim();
    const title = document.getElementById('frmAcctTitle').value.trim();
    const note = document.getElementById('frmAcctNote').value.trim();
    const status = document.querySelector('input[name="frmAcctStatus"]:checked').value;

    if (editingAccountId) {
        // 更新
        const acct = mockAccounts.find(a => a.id === editingAccountId);
        if (acct) {
            acct.name = name;
            acct.email = email;
            acct.roleId = roleId;
            acct.title = title;
            acct.note = note;
            acct.status = status;
        }
    } else {
        // 新增
        const newId = 'A' + String(mockAccounts.length + 1).padStart(3, '0');
        mockAccounts.push({
            id: newId, name, username, email, roleId, title,
            status, note,
            createdAt: new Date().toISOString().split('T')[0],
            firstLogin: true
        });
    }

    closeAccountPanel();
    renderAccountList();
    updateAccountStats();

    // 如果正在檢視已編輯的帳號，更新詳情
    if (editingAccountId && selectedAccountId === editingAccountId) {
        showAccountDetail(selectedAccountId);
    }

    Swal.fire({
        icon: 'success',
        title: editingAccountId ? '已更新' : '已新增',
        text: `人員「${name}」已成功${editingAccountId ? '更新' : '新增'}。`,
        toast: true,
        position: 'top-end',
        showConfirmButton: false,
        timer: 2000,
        timerProgressBar: true
    });
}

// ===========================
// 角色權限卡片渲染
// ===========================
function renderRoleCards() {
    const container = document.getElementById('roleCardsContainer');

    container.innerHTML = mockRoles.map(role => {
        const enabledCount = Object.values(role.permissions).filter(Boolean).length;
        const totalCount = MODULES.length;

        const categoryBadge = role.category === 'internal'
            ? '<span class="text-[10px] font-bold px-2 py-0.5 rounded-full bg-blue-100 text-blue-700"><i class="fa-solid fa-building mr-0.5"></i>內部人員</span>'
            : '<span class="text-[10px] font-bold px-2 py-0.5 rounded-full bg-green-100 text-green-700"><i class="fa-solid fa-user-graduate mr-0.5"></i>外部人員</span>';

        const lockIcon = role.isDefault
            ? '<span class="text-xs text-amber-500" title="預設角色，權限不可修改"><i class="fa-solid fa-lock"></i></span>'
            : '';

        // 取得已開啟的功能標籤
        const permTags = MODULES
            .filter(m => role.permissions[m.key])
            .map(m => `<span class="inline-flex items-center gap-1 text-[10px] font-medium px-2 py-0.5 rounded bg-gray-100 text-gray-600">
                          <i class="fa-solid ${m.icon} text-[9px]"></i> ${m.label}
                       </span>`)
            .join('');

        // 使用此角色的帳號數量
        const accountCount = mockAccounts.filter(a => a.roleId === role.id).length;

        return `
            <div class="role-card bg-white rounded-2xl shadow-sm p-6 flex flex-col relative overflow-hidden">
                <!-- 角色標頭 -->
                <div class="flex items-start justify-between mb-3">
                    <div class="flex items-center gap-2">
                        <div class="w-10 h-10 rounded-xl flex items-center justify-center text-lg ${role.isDefault ? 'bg-amber-50 text-amber-600' : 'bg-purple-50 text-purple-600'}">
                            <i class="fa-solid fa-shield-halved"></i>
                        </div>
                        <div>
                            <div class="flex items-center gap-1.5">
                                <h3 class="font-bold text-[var(--color-slate-main)]">${role.name}</h3>
                                ${lockIcon}
                            </div>
                            <div class="flex items-center gap-1.5 mt-0.5">
                                ${categoryBadge}
                            </div>
                        </div>
                    </div>
                    <span class="text-xs font-bold px-2 py-1 rounded-full bg-[var(--color-morandi)]/10 text-[var(--color-morandi)]">
                        ${enabledCount}/${totalCount}
                    </span>
                </div>

                <!-- 角色描述 -->
                <p class="text-sm text-gray-500 mb-4 leading-relaxed flex-grow">${role.description}</p>

                <!-- 功能標籤 -->
                <div class="flex flex-wrap gap-1 mb-4 min-h-[28px]">
                    ${permTags || '<span class="text-xs text-gray-400">無任何功能權限</span>'}
                </div>

                <!-- 底部操作 -->
                <div class="flex items-center justify-between pt-3 border-t border-gray-100">
                    <span class="text-xs text-gray-400">
                        <i class="fa-solid fa-users mr-1"></i> ${accountCount} 位使用者
                    </span>
                    <button class="edit-role-btn cursor-pointer text-sm font-medium text-[var(--color-morandi)] hover:text-[#5b7a95] transition-colors flex items-center gap-1"
                            data-id="${role.id}">
                        <i class="fa-solid fa-pen-to-square"></i> ${role.isDefault ? '檢視' : '編輯'}
                    </button>
                </div>
            </div>`;
    }).join('');

    // 綁定編輯按鈕
    container.querySelectorAll('.edit-role-btn').forEach(btn => {
        btn.addEventListener('click', () => openRoleModal(btn.dataset.id));
    });
}

// ===========================
// 角色統計
// ===========================
function updateRoleStats() {
    document.getElementById('roleStatTotal').textContent = mockRoles.length;
    document.getElementById('roleStatInternal').textContent = mockRoles.filter(r => r.category === 'internal').length;
    document.getElementById('roleStatExternal').textContent = mockRoles.filter(r => r.category === 'external').length;
}

// ===========================
// 角色 Modal
// ===========================
function initRoleModal() {
    const modal = document.getElementById('roleModal');
    const panel = document.getElementById('roleModalPanel');
    const backdrop = modal.querySelector('.role-modal-backdrop');

    // 關閉按鈕
    document.querySelectorAll('.close-role-modal').forEach(btn => {
        btn.addEventListener('click', closeRoleModal);
    });

    // 點擊背景關閉
    backdrop.addEventListener('click', closeRoleModal);

    // 儲存
    document.getElementById('btnSaveRole').addEventListener('click', saveRole);
}

function openRoleModal(roleId) {
    editingRoleId = roleId;
    const modal = document.getElementById('roleModal');
    const panel = document.getElementById('roleModalPanel');
    const titleEl = document.getElementById('roleModalTitle');
    const lockHint = document.getElementById('roleModalLockHint');
    const saveBtn = document.getElementById('btnSaveRole');

    let role = roleId ? mockRoles.find(r => r.id === roleId) : null;
    const isDefault = role?.isDefault || false;

    if (role) {
        titleEl.querySelector('span').textContent = isDefault ? '檢視角色' : '編輯角色';
        document.getElementById('frmRoleName').value = role.name;
        document.getElementById('frmRoleCategory').value = role.category;
        document.getElementById('frmRoleDesc').value = role.description;
    } else {
        titleEl.querySelector('span').textContent = '新增角色';
        document.getElementById('frmRoleName').value = '';
        document.getElementById('frmRoleCategory').value = 'internal';
        document.getElementById('frmRoleDesc').value = '';
    }

    // 鎖定輸入（預設角色）
    document.getElementById('frmRoleName').disabled = isDefault;
    document.getElementById('frmRoleCategory').disabled = isDefault;
    document.getElementById('frmRoleDesc').disabled = isDefault;
    lockHint.classList.toggle('hidden', !isDefault);
    saveBtn.classList.toggle('hidden', isDefault);

    // 渲染 Toggle 開關
    renderPermissionToggles(role, isDefault);

    // 開啟動畫
    modal.classList.remove('hidden');
    requestAnimationFrame(() => {
        panel.classList.remove('scale-95', 'opacity-0');
        panel.classList.add('scale-100', 'opacity-100');
    });
}

function closeRoleModal() {
    const modal = document.getElementById('roleModal');
    const panel = document.getElementById('roleModalPanel');

    panel.classList.remove('scale-100', 'opacity-100');
    panel.classList.add('scale-95', 'opacity-0');
    setTimeout(() => {
        modal.classList.add('hidden');
        editingRoleId = null;
    }, 300);
}

// 渲染功能區塊 Toggle 開關
function renderPermissionToggles(role, isLocked) {
    const container = document.getElementById('permissionTogglesContainer');
    const permissions = role?.permissions || {};

    container.innerHTML = MODULES.map(mod => {
        const isOn = !!permissions[mod.key];
        const activeClass = isOn ? 'active' : '';
        const disabledClass = isLocked ? 'disabled' : '';

        return `
            <div class="flex items-center justify-between py-2.5 px-3 rounded-lg hover:bg-gray-50 transition-colors ${isLocked ? 'opacity-60' : ''}">
                <div class="flex items-center gap-3">
                    <div class="w-8 h-8 rounded-lg bg-gray-100 flex items-center justify-center text-gray-500 text-sm">
                        <i class="fa-solid ${mod.icon}"></i>
                    </div>
                    <div>
                        <span class="text-sm font-medium text-[var(--color-slate-main)]">${mod.label}</span>
                        <p class="text-[10px] text-gray-400">${mod.page}</p>
                    </div>
                </div>
                <div class="toggle-switch ${activeClass} ${disabledClass}" data-module="${mod.key}"></div>
            </div>`;
    }).join('');

    // 綁定 Toggle 事件
    if (!isLocked) {
        container.querySelectorAll('.toggle-switch').forEach(toggle => {
            toggle.addEventListener('click', () => {
                toggle.classList.toggle('active');

                // 處理「系統公告」的進階權限
                const modKey = toggle.dataset.module;
                if (modKey === 'announcements') {
                    const levelEl = document.getElementById('announcementPermLevel');
                    levelEl.classList.toggle('hidden', !toggle.classList.contains('active'));
                }
            });
        });
    }

    // 初始化公告進階權限下拉
    const annToggle = container.querySelector('[data-module="announcements"]');
    const levelEl = document.getElementById('announcementPermLevel');
    if (annToggle?.classList.contains('active')) {
        levelEl.classList.remove('hidden');
        if (role?.announcementPerm) {
            document.getElementById('frmAnnouncementPerm').value = role.announcementPerm;
        }
    } else {
        levelEl.classList.add('hidden');
    }

    // 鎖定下拉選單
    document.getElementById('frmAnnouncementPerm').disabled = isLocked;
}

function saveRole() {
    const name = document.getElementById('frmRoleName').value.trim();
    const category = document.getElementById('frmRoleCategory').value;
    const description = document.getElementById('frmRoleDesc').value.trim();

    if (!name) {
        Swal.fire({ icon: 'warning', title: '欄位不完整', text: '請填寫角色名稱。', confirmButtonColor: '#6B8EAD' });
        return;
    }

    // 收集權限
    const permissions = {};
    document.querySelectorAll('#permissionTogglesContainer .toggle-switch').forEach(toggle => {
        permissions[toggle.dataset.module] = toggle.classList.contains('active');
    });

    const announcementPerm = permissions.announcements
        ? document.getElementById('frmAnnouncementPerm').value
        : 'view';

    if (editingRoleId) {
        // 更新
        const role = mockRoles.find(r => r.id === editingRoleId);
        if (role && !role.isDefault) {
            role.name = name;
            role.category = category;
            role.description = description;
            role.permissions = permissions;
            role.announcementPerm = announcementPerm;
        }
    } else {
        // 新增
        const newId = 'R' + String(mockRoles.length + 1).padStart(3, '0');
        mockRoles.push({
            id: newId, name, category, description,
            isDefault: false,
            permissions,
            announcementPerm
        });
    }

    closeRoleModal();
    renderRoleCards();
    updateRoleStats();

    // 重新填充帳號的角色下拉（如果帳號面板開著）
    renderAccountList();

    Swal.fire({
        icon: 'success',
        title: editingRoleId ? '已更新' : '已新增',
        text: `角色「${name}」已成功${editingRoleId ? '更新' : '新增'}。`,
        toast: true,
        position: 'top-end',
        showConfirmButton: false,
        timer: 2000,
        timerProgressBar: true
    });
}
