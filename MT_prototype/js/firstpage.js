/**
 * FirstPage Module
 * 負責首頁的 8 張導覽卡片渲染、側邊欄最新消息與急件提醒、以及公告彈窗。
 * Version: 1.0 (DEMO)
 */

// 假資料：功能表列 (對應 US-002)
const menuItems = [
    {
        id: 'nav-dashboard',
        title: '命題儀錶板',
        desc: '監控各題型缺口與整體命題進度。',
        icon: 'fa-chart-pie',
        color: 'text-indigo-600',
        bgColor: 'bg-indigo-50',
        url: 'dashboard.html',
        roles: ['ADMIN'] // 僅內部職員/長官可見
    },
    {
        id: 'nav-project',
        title: '命題專案管理',
        desc: '梯次設定、派發工作與階段時程管控。',
        icon: 'fa-box-archive',
        color: 'text-amber-600',
        bgColor: 'bg-amber-50',
        url: 'projects.html',
        roles: ['ADMIN']
    },
    {
        id: 'nav-overview',
        title: '命題總覽',
        desc: '全梯次試題列表、題目內容與審題全局結果檢視。',
        icon: 'fa-globe',
        color: 'text-emerald-600',
        bgColor: 'bg-emerald-50',
        url: 'overview.html',
        roles: ['ADMIN']
    },
    {
        id: 'nav-my-tasks',
        title: '命題任務',
        desc: '進行試題命製、修題，並檢視本人考題狀態。',
        icon: 'fa-pen-to-square',
        color: 'text-[var(--color-morandi)]',
        bgColor: 'bg-blue-50',
        url: 'cwt-list.html',
        roles: ['ADMIN', 'TEACHER'] // 教師核心功能
    },
    {
        id: 'nav-review',
        title: '審題任務',
        desc: '互審、專審、總審工作區塊與處置辦理。',
        icon: 'fa-magnifying-glass-chart',
        color: 'text-[var(--color-terracotta)]',
        bgColor: 'bg-red-50',
        url: 'reviews.html',
        roles: ['ADMIN', 'TEACHER'] // 委員與總召等同特殊教師
    },
    {
        id: 'nav-teachers',
        title: '教師管理系統',
        desc: '命審題人員庫維護、資歷檢視與過往命題歷程。',
        icon: 'fa-chalkboard-user',
        color: 'text-cyan-600',
        bgColor: 'bg-cyan-50',
        url: 'teachers.html',
        roles: ['ADMIN']
    },
    {
        id: 'nav-roles',
        title: '角色與權限管理',
        desc: '系統帳號新增、外部人員身份指派及功能開關。',
        icon: 'fa-id-card-clip',
        color: 'text-purple-600',
        bgColor: 'bg-purple-50',
        url: 'roles.html',
        roles: ['ADMIN']
    },
    {
        id: 'nav-settings',
        title: '系統公告 / 使用說明',
        desc: '管理內部看板公告及操作手冊下載佈局。',
        icon: 'fa-bullhorn',
        color: 'text-pink-600',
        bgColor: 'bg-pink-50',
        url: 'announcements.html',
        roles: ['ADMIN', 'TEACHER']
    }
];

// 假資料：系統公告與今日提醒 (依照目前梯次變化)
const remindersDb = [
    { type: 'urgent', text: '【命題階段】距離結案倒數 3 天！您尚有 2 題未完成。', project: 'P2026-01', link: 'cwt-list.html?tab=compose' },
    { type: 'urgent', text: '【互審修題】請留意，此階段即將於今日 23:59 關閉。', project: 'P2026-01', link: 'cwt-list.html?tab=revision' },
    { type: 'urgent', text: '【專家審題】您受邀參與的梯次有 3 題待審，請撥冗處理。', project: 'P2026-01', link: 'reviews.html' },
    { type: 'normal', title: '題型規格異動通知', date: '2026-03-08', isTop: true, project: 'ALL', content: '自115年度秋季起，長文閱讀題將新增「跨領域素養」指標，請各位教師在命題時於表單右側屬性欄位正確勾選，詳情請參考最新的使用說明手冊。' },
    { type: 'normal', title: '系統維護提前公告', date: '2026-03-05', isTop: false, project: 'ALL', content: '資料庫將於本週末凌晨 02:00~04:00 進行搬遷與擴容作業，屆時命題平台將暫時無法登入與存檔，請預先將草稿儲存。' },
    { type: 'normal', title: 'P2026-01 梯次準備期程啟動', date: '2026-02-28', isTop: false, project: 'P2026-01', content: '各位教師好，115年度春季全民中檢命題專案已建檔，請負責此梯次的老師登入後確認左方倒數時程，並開始準備素材。' }
];

document.addEventListener('DOMContentLoaded', () => {
    // Top Right Date Display
    displayCurrentDate();

    // Render Cards
    renderMenuCards();

    // Render Reminders
    renderReminders(localStorage.getItem('cwt_current_project'));

    // Listen to shared.js project change event
    document.addEventListener('projectChanged', (e) => {
        renderReminders(e.detail.id);
        renderMenuCards(); // Option to refresh badges/counters per project in the future
    });

    // Initialize Notice Modal
    initNoticeModal();
});

/**
 * 顯示日期與星期
 */
function displayCurrentDate() {
    const el = document.getElementById('currentDateDisplay');
    if (!el) return;
    const now = new Date();
    const days = ['日', '一', '二', '三', '四', '五', '六'];

    // Formatting: YYYY/MM/DD (星期X)
    const yyyy = now.getFullYear();
    const mm = String(now.getMonth() + 1).padStart(2, '0');
    const dd = String(now.getDate()).padStart(2, '0');
    const day = days[now.getDay()];

    el.innerHTML = `<i class="fa-regular fa-calendar"></i> <span class="tracking-wide">${yyyy}/${mm}/${dd} (星期${day})</span>`;
}

/**
 * 依照使用者身分渲染 Menu Cards
 */
function renderMenuCards() {
    const container = document.getElementById('menuCardsContainer');
    if (!container) return;

    const userStr = localStorage.getItem('cwt_user');
    if (!userStr) return;
    const user = JSON.parse(userStr);
    const role = user.role || 'TEACHER'; // Default fallback

    let html = '';

    menuItems.forEach((item, index) => {
        // 檢查權限
        const hasAccess = item.roles.includes('ADMIN') || item.roles.includes(role);

        // 若無權限，使用灰色禁用樣式，且點擊無效；若有權限則套用正常顏色
        const cardStyle = hasAccess
            ? `bg-white cursor-pointer group menu-card`
            : `bg-gray-50 opacity-60 cursor-not-allowed border-transparent grayscale`;

        const iconBg = hasAccess ? item.bgColor : 'bg-gray-200 text-gray-400';
        const iconColor = hasAccess ? item.color : 'text-gray-500';
        const titleColor = hasAccess ? 'text-[var(--color-slate-main)] group-hover:text-[var(--color-morandi)] transition-colors' : 'text-gray-500';

        // 模擬延遲進場動畫 (Tailwind animate classes optional)
        const animationDelay = index * 50;

        html += `
            <div class="${cardStyle} rounded-2xl shadow-sm p-6 flex flex-col h-full relative overflow-hidden" 
                 style="animation: fadeIn 0.5s ease-out ${animationDelay}ms both;"
                 ${hasAccess ? `onclick="window.location.href='${item.url}';"` : ''}>
                
                ${!hasAccess ? '<div class="absolute top-3 right-3 text-xs font-bold text-gray-400 bg-gray-200 px-2 py-1 rounded"><i class="fa-solid fa-lock"></i> 無權限</div>' : ''}
                
                <div class="w-12 h-12 rounded-xl flex items-center justify-center text-2xl mb-4 ${iconBg} ${iconColor} ${hasAccess ? 'group-hover:scale-110 transition-transform' : ''}">
                    <i class="fa-solid ${item.icon}"></i>
                </div>
                
                <h3 class="text-lg font-bold mb-2 ${titleColor}">${item.title}</h3>
                <p class="text-sm text-gray-500 flex-grow leading-relaxed">${item.desc}</p>
                
                <div class="mt-4 flex items-center text-xs font-medium ${hasAccess ? 'text-[var(--color-morandi)] opacity-0 group-hover:opacity-100 transition-opacity' : 'hidden'}">
                    進入功能 <i class="fa-solid fa-arrow-right ml-1"></i>
                </div>
            </div>
        `;
    });

    container.innerHTML = html;

    // 添加簡單的 fade-in inline CSS (此部分通常在 Tailwind @layer 或 global css 設定，加在此處方便 DEMO)
    if (!document.getElementById('fadeInStyle')) {
        const style = document.createElement('style');
        style.id = 'fadeInStyle';
        style.innerHTML = `@keyframes fadeIn { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: translateY(0); } }`;
        document.head.appendChild(style);
    }
}

/**
 * 渲染右側儀錶板提醒與公告
 * @param {string} projectId 用於過濾與該專案相關的公告
 */
function renderReminders(projectId) {
    const urgentList = document.getElementById('urgentReminderList');
    const normalList = document.getElementById('announcementList');
    const badge = document.getElementById('announcementCount');

    if (!urgentList || !normalList) return;

    // Filter Reminders
    const urgents = remindersDb.filter(r => r.type === 'urgent' && (r.project === projectId || r.project === 'ALL'));
    const normals = remindersDb.filter(r => r.type === 'normal' && (r.project === projectId || r.project === 'ALL'));

    // Sort Normals: Top first, then date DESC
    normals.sort((a, b) => {
        if (a.isTop && !b.isTop) return -1;
        if (!a.isTop && b.isTop) return 1;
        return new Date(b.date) - new Date(a.date);
    });

    // Render Urgents (凍結區)
    if (urgents.length === 0) {
        urgentList.innerHTML = `<li class="px-4 py-3 text-sm text-gray-500 flex items-center gap-2"><i class="fa-regular fa-face-smile lg"></i> 目前尚無急件。</li>`;
    } else {
        urgentList.innerHTML = urgents.map(u => `
            <li class="px-4 py-3 hover:bg-red-50/50 transition-colors">
                <a href="${u.link}" class="flex items-start gap-2 group">
                    <i class="fa-solid fa-triangle-exclamation text-[var(--color-terracotta)] mt-0.5 animate-pulse"></i>
                    <span class="text-sm text-gray-700 group-hover:text-[var(--color-terracotta)] leading-relaxed">${u.text}</span>
                </a>
            </li>
        `).join('');
    }

    // Render Normals (滾動區)
    badge.textContent = normals.length;

    if (normals.length === 0) {
        normalList.innerHTML = `<div class="p-4 text-sm text-gray-500 text-center">無相關公告。</div>`;
    } else {
        normalList.innerHTML = normals.map((n, i) => `
            <div class="px-4 py-3 border-b border-gray-100 hover:bg-gray-50 transition-colors cursor-pointer notice-item" data-index="${i}">
                <div class="flex items-center gap-2 mb-1">
                    ${n.isTop ? '<span class="bg-red-100 text-red-600 text-[10px] font-bold px-1.5 py-0.5 rounded">置頂</span>' : ''}
                    <span class="text-xs text-gray-400"><i class="fa-regular fa-calendar ml-0.5"></i> ${n.date}</span>
                </div>
                <h4 class="text-sm font-bold text-[var(--color-slate-main)] hover:text-[var(--color-morandi)] mb-1 line-clamp-2">${n.title}</h4>
                <p class="text-xs text-gray-500 line-clamp-2">${n.content}</p>
            </div>
        `).join('');

        // Store sorted normals for modal use
        window._currentAnnouncements = normals;
    }
}

/**
 * 初始化公告 Modal
 */
function initNoticeModal() {
    const modal = document.getElementById('noticeModal');
    const panel = document.getElementById('noticePanel');
    const closeBtns = document.querySelectorAll('.close-modal-btn');
    const titleEl = document.getElementById('noticeModalTitle');
    const dateEl = document.getElementById('noticeModalDate');
    const tagsEl = document.getElementById('noticeModalTags');
    const contentEl = document.getElementById('noticeModalContent');

    if (!modal) return;

    const openModal = (data) => {
        titleEl.textContent = data.title;
        dateEl.textContent = data.date;

        // Tags setup
        tagsEl.innerHTML = '';
        if (data.isTop) {
            tagsEl.innerHTML += '<span class="bg-red-100 text-red-600 text-xs font-bold px-2 py-0.5 rounded">置頂公告</span>';
        }
        if (data.project === 'ALL') {
            tagsEl.innerHTML += '<span class="bg-[var(--color-morandi)]/10 text-[var(--color-morandi)] text-xs font-bold px-2 py-0.5 rounded">全域公告</span>';
        } else {
            tagsEl.innerHTML += `<span class="bg-gray-100 text-gray-600 text-xs font-bold px-2 py-0.5 rounded">專案: ${data.project}</span>`;
        }

        // 簡單將純文字轉換為段落
        const paragraphs = data.content.split('\n').filter(p => p.trim() !== '').map(p => `<p>${p}</p>`).join('');
        contentEl.innerHTML = paragraphs;

        modal.classList.remove('hidden');
        // Transition play
        void modal.offsetWidth;
        panel.classList.remove('scale-95', 'opacity-0');
        panel.classList.add('scale-100', 'opacity-100');
    };

    const closeModal = () => {
        panel.classList.remove('scale-100', 'opacity-100');
        panel.classList.add('scale-95', 'opacity-0');
        setTimeout(() => modal.classList.add('hidden'), 300);
    };

    closeBtns.forEach(btn => btn.addEventListener('click', closeModal));

    // Event delegation for opening notices
    document.getElementById('announcementList').addEventListener('click', (e) => {
        const item = e.target.closest('.notice-item');
        if (item) {
            const index = item.getAttribute('data-index');
            const data = window._currentAnnouncements[index];
            if (data) openModal(data);
        }
    });

    // Close on backdrop click
    document.querySelector('#noticeModal .modal-backdrop').addEventListener('click', closeModal);
}
