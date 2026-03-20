/**
 * Announcements Module (系統公告 / 使用說明)
 * 負責公告管理的 CRUD、分類篩選、置頂排序、Quill 編輯器、與首頁連動。
 * Version: 1.0 (DEMO)
 */

// --- 假資料：專案對照 ---
const projectsRef = [
    { id: 'P2026-01', name: '115年度 春季全民中檢', status: 'active', year: '115' },
    { id: 'P2026-02', name: '115年度 秋季全民中檢', status: 'preparing', year: '115' },
    { id: 'P2025-02', name: '114年度 秋季全民中檢', status: 'closed', year: '114' },
    { id: 'P2025-01', name: '114年度 春季全民中檢', status: 'closed', year: '114' }
];

// --- 假資料：公告列表 ---
let announcementsDb = [
    {
        id: 'ANN-001',
        category: 'system',
        status: 'published',
        project: 'ALL',
        publishDate: '2026-03-08',
        unpublishDate: '',
        pinned: true,
        title: '題型規格異動通知',
        content: '<p>自115年度秋季起，長文閱讀題將新增「跨領域素養」指標，請各位教師在命題時於表單右側屬性欄位正確勾選。</p><p>詳情請參考最新的使用說明手冊，如有疑問歡迎來電或來信洽詢。</p>',
        author: '系統管理員',
        createdAt: '2026-03-08T09:00:00'
    },
    {
        id: 'ANN-002',
        category: 'system',
        status: 'published',
        project: 'ALL',
        publishDate: '2026-03-05',
        unpublishDate: '2026-03-12',
        pinned: false,
        title: '系統維護提前公告',
        content: '<p>資料庫將於本週末凌晨 02:00~04:00 進行搬遷與擴容作業，屆時命題平台將暫時無法登入與存檔，請預先將草稿儲存。</p><p>維護完成後將發送通知，感謝各位配合。</p>',
        author: '系統管理員',
        createdAt: '2026-03-05T10:30:00'
    },
    {
        id: 'ANN-003',
        category: 'compose',
        status: 'published',
        project: 'P2026-01',
        publishDate: '2026-02-28',
        unpublishDate: '',
        pinned: false,
        title: 'P2026-01 梯次準備期程啟動',
        content: '<p>各位教師好，<strong>115年度春季全民中檢</strong>命題專案已建檔，請負責此梯次的老師登入後確認左方倒數時程，並開始準備素材。</p><p>命題階段將於 <u>2026-02-15</u> 正式開始，請提前備妥參考資料。</p>',
        author: '系統管理員',
        createdAt: '2026-02-28T08:00:00'
    },
    {
        id: 'ANN-004',
        category: 'compose',
        status: 'published',
        project: 'P2026-01',
        publishDate: '2026-03-10',
        unpublishDate: '',
        pinned: false,
        title: '命題階段倒數提醒：剩餘 5 天',
        content: '<p>提醒各位命題教師，<strong>115年度春季全民中檢</strong>的命題階段將於 2026-03-15 截止。</p><p>目前仍有部分教師尚未完成配額數量，請儘速完成命題作業。若有特殊狀況無法如期完成，請主動聯繫專案負責人。</p>',
        author: '系統管理員',
        createdAt: '2026-03-10T09:00:00'
    },
    {
        id: 'ANN-005',
        category: 'review',
        status: 'published',
        project: 'P2026-01',
        publishDate: '2026-03-09',
        unpublishDate: '',
        pinned: false,
        title: '交互審題注意事項公告',
        content: '<p>各位互審教師您好，交互審題階段將於命題截止後 <strong>3/16</strong> 正式開始。</p><p>提醒您：</p><ul><li>互審階段僅提供「給予意見」功能，無法直接採用或不採用</li><li>請針對題幹、選項、答案正確性三方面提出建設性意見</li><li>自己命題的題目不會出現在您的審題清單中</li></ul>',
        author: '林淑華',
        createdAt: '2026-03-09T14:00:00'
    },
    {
        id: 'ANN-006',
        category: 'system',
        status: 'draft',
        project: 'ALL',
        publishDate: '2026-03-15',
        unpublishDate: '',
        pinned: false,
        title: '平臺功能更新預告 (v2.1)',
        content: '<p>即將推出的功能更新包含：</p><ol><li>試題重複比對功能優化</li><li>批次匯出 PDF 考卷預覽</li><li>新增聽力題型音檔上傳支援</li></ol><p>詳細更新日誌將於正式上線後公告。</p>',
        author: '系統管理員',
        createdAt: '2026-03-11T11:00:00'
    },
    {
        id: 'ANN-007',
        category: 'other',
        status: 'published',
        project: 'ALL',
        publishDate: '2026-02-20',
        unpublishDate: '',
        pinned: false,
        title: '使用說明手冊更新通知',
        content: '<p>使用說明手冊已更新至 v3.2 版本，主要修訂內容包含：</p><ul><li>新增「聽力題組」命題操作指引</li><li>修訂「閱讀題組」子題建立流程圖</li><li>補充「總召審題」退回次數限制說明</li></ul><p>請至「使用說明手冊」按鈕下載最新版本。</p>',
        author: '許志豪',
        createdAt: '2026-02-20T16:00:00'
    },
    {
        id: 'ANN-008',
        category: 'review',
        status: 'archived',
        project: 'P2025-02',
        publishDate: '2025-09-01',
        unpublishDate: '2025-10-01',
        pinned: false,
        title: '114年度秋季中檢 專審階段開始通知',
        content: '<p>114年度秋季全民中檢之專家審題階段已於今日開始，請各位專審委員登入系統進行審題作業。</p>',
        author: '系統管理員',
        createdAt: '2025-09-01T08:00:00'
    },
    {
        id: 'ANN-009',
        category: 'compose',
        status: 'archived',
        project: 'P2025-02',
        publishDate: '2025-08-10',
        unpublishDate: '2025-09-15',
        pinned: false,
        title: '114年度秋季中檢 命題階段啟動',
        content: '<p>各位教師，114年度秋季命題作業已開始。本次命題數量目標：一般單選題 600 題、精選單選題 200 題、閱讀題組 150 組。</p>',
        author: '系統管理員',
        createdAt: '2025-08-10T09:00:00'
    },
    {
        id: 'ANN-010',
        category: 'other',
        status: 'draft',
        project: 'P2026-02',
        publishDate: '2026-07-01',
        unpublishDate: '',
        pinned: false,
        title: '115年度秋季中檢 產學合作說明 (草稿)',
        content: '<p>本次秋季梯次將與南臺科大合作辦理，相關產學合作細節待定中。</p>',
        author: '系統管理員',
        createdAt: '2026-03-11T10:00:00'
    }
];

// --- 常數對照 ---
const categoryMap = {
    'system': { label: '系統公告', color: 'bg-blue-100 text-blue-700', icon: 'fa-solid fa-gear', sortOrder: 1 },
    'compose': { label: '命題公告', color: 'bg-emerald-100 text-emerald-700', icon: 'fa-solid fa-pen-nib', sortOrder: 2 },
    'review': { label: '審題公告', color: 'bg-purple-100 text-purple-700', icon: 'fa-solid fa-magnifying-glass', sortOrder: 3 },
    'other': { label: '其它', color: 'bg-gray-100 text-gray-600', icon: 'fa-solid fa-ellipsis', sortOrder: 4 }
};

const statusDisplayMap = {
    'draft': { label: '草稿', color: 'bg-gray-100 text-gray-600', dot: 'bg-gray-400' },
    'published': { label: '已發佈', color: 'bg-green-100 text-green-700', dot: 'bg-green-500' },
    'archived': { label: '已下架', color: 'bg-orange-100 text-orange-600', dot: 'bg-orange-400' }
};

// --- 狀態管理 ---
let quillInstance = null;
let editingAnnouncementId = null;

// ==================== INIT ====================
document.addEventListener('DOMContentLoaded', () => {
    autoCheckUnpublishDates();
    renderStats();
    renderAnnouncementList();
    initFilters();
    initSlideOverPanel();
    initQuillEditor();
    initViewModal();
    initUserGuideButton();

    document.addEventListener('projectChanged', () => {
        renderStats();
    });
});

// ==================== 自動下架檢查 ====================
function autoCheckUnpublishDates() {
    const today = new Date().toISOString().split('T')[0];
    announcementsDb.forEach(ann => {
        if (ann.status === 'published' && ann.unpublishDate && ann.unpublishDate <= today) {
            ann.status = 'archived';
        }
    });
}

// ==================== 統計卡片 ====================
function renderStats() {
    const total = announcementsDb.length;
    const published = announcementsDb.filter(a => a.status === 'published').length;
    const draft = announcementsDb.filter(a => a.status === 'draft').length;
    const archived = announcementsDb.filter(a => a.status === 'archived').length;
    const pinned = announcementsDb.filter(a => a.pinned && a.status === 'published').length;

    animateCounter('statTotal', total);
    animateCounter('statPublished', published);
    animateCounter('statDraft', draft);
    animateCounter('statArchived', archived);
    animateCounter('statPinned', pinned);
}

function animateCounter(elementId, target) {
    const el = document.getElementById(elementId);
    if (!el) return;
    let current = 0;
    const increment = Math.max(1, Math.ceil(target / 12));
    const timer = setInterval(() => {
        current += increment;
        if (current >= target) { current = target; clearInterval(timer); }
        el.textContent = current;
    }, 40);
}

// ==================== 排序邏輯 ====================
/**
 * 排序優先順序：
 * 1. 置頂 (pinned) 最優先
 * 2. 分類順序：系統公告 > 命題公告 > 審題公告 > 其它
 * 3. 發佈日期由新到舊
 */
function sortAnnouncements(list) {
    return list.sort((a, b) => {
        // 置頂優先
        if (a.pinned && !b.pinned) return -1;
        if (!a.pinned && b.pinned) return 1;

        // 分類排序
        const catOrderA = categoryMap[a.category]?.sortOrder || 99;
        const catOrderB = categoryMap[b.category]?.sortOrder || 99;
        if (catOrderA !== catOrderB) return catOrderA - catOrderB;

        // 日期由新到舊
        return new Date(b.publishDate) - new Date(a.publishDate);
    });
}

// ==================== 公告列表渲染 ====================
function renderAnnouncementList() {
    const container = document.getElementById('announcementListContainer');
    if (!container) return;

    const keyword = document.getElementById('filterKeyword')?.value.trim().toLowerCase() || '';
    const catFilter = document.getElementById('filterCategory')?.value || 'all';
    const statusFilter = document.getElementById('filterStatus')?.value || 'all';

    let filtered = announcementsDb.filter(a => {
        if (catFilter !== 'all' && a.category !== catFilter) return false;
        if (statusFilter !== 'all' && a.status !== statusFilter) return false;
        if (keyword) {
            const searchStr = [a.title, a.content, a.author].join(' ').toLowerCase();
            if (!searchStr.includes(keyword)) return false;
        }
        return true;
    });

    const sorted = sortAnnouncements([...filtered]);

    document.getElementById('listCount').textContent = sorted.length;

    if (sorted.length === 0) {
        container.innerHTML = `
            <div class="p-12 text-center text-gray-400">
                <i class="fa-solid fa-inbox text-4xl mb-3 text-gray-300"></i>
                <p class="text-sm">查無相符公告</p>
            </div>
        `;
        return;
    }

    container.innerHTML = sorted.map(ann => {
        const cat = categoryMap[ann.category] || categoryMap['other'];
        const st = statusDisplayMap[ann.status] || statusDisplayMap['draft'];
        const projLabel = ann.project === 'ALL' ? '全站廣播' : (projectsRef.find(p => p.id === ann.project)?.name || ann.project);

        return `
            <div class="ann-row grid grid-cols-12 gap-2 px-6 py-3.5 border-b border-gray-50 hover:bg-gray-50/50 transition-colors items-center cursor-pointer"
                 data-id="${ann.id}">
                <!-- 置頂 -->
                <div class="col-span-1 flex justify-center">
                    ${ann.pinned ? '<i class="fa-solid fa-thumbtack text-red-500 text-sm" title="置頂中"></i>' : '<span class="text-gray-300">—</span>'}
                </div>
                <!-- 分類 -->
                <div class="col-span-1">
                    <span class="text-[10px] font-bold px-1.5 py-0.5 rounded ${cat.color}">${cat.label}</span>
                </div>
                <!-- 標題 -->
                <div class="col-span-4 lg:col-span-5 min-w-0">
                    <div class="flex items-center gap-2">
                        ${ann.pinned ? '<span class="bg-red-50 text-red-500 text-[10px] font-bold px-1 py-0 rounded flex-shrink-0">TOP</span>' : ''}
                        <span class="text-sm font-bold text-[var(--color-slate-main)] truncate">${ann.title}</span>
                    </div>
                    <p class="text-xs text-gray-400 mt-0.5 truncate">${stripHtml(ann.content).substring(0, 60)}...</p>
                </div>
                <!-- 綁定梯次 -->
                <div class="col-span-2 hidden lg:block">
                    <span class="text-xs ${ann.project === 'ALL' ? 'text-[var(--color-morandi)] font-bold' : 'text-gray-500'} truncate block">${projLabel}</span>
                </div>
                <!-- 狀態 -->
                <div class="col-span-2 lg:col-span-1">
                    <span class="inline-flex items-center gap-1 text-xs font-bold px-2 py-0.5 rounded-full ${st.color}">
                        <span class="w-1.5 h-1.5 rounded-full ${st.dot}"></span>${st.label}
                    </span>
                </div>
                <!-- 發佈日 -->
                <div class="col-span-1 hidden md:block">
                    <span class="text-xs text-gray-500">${ann.publishDate}</span>
                </div>
                <!-- 操作 -->
                <div class="col-span-1 flex justify-center gap-1">
                    <button class="btn-edit-ann w-7 h-7 rounded-full hover:bg-blue-50 text-gray-400 hover:text-[var(--color-morandi)] flex items-center justify-center transition-colors" data-id="${ann.id}" title="編輯">
                        <i class="fa-solid fa-pen text-xs"></i>
                    </button>
                    <button class="btn-pin-ann w-7 h-7 rounded-full hover:bg-red-50 ${ann.pinned ? 'text-red-500' : 'text-gray-400 hover:text-red-500'} flex items-center justify-center transition-colors" data-id="${ann.id}" title="${ann.pinned ? '取消置頂' : '置頂'}">
                        <i class="fa-solid fa-thumbtack text-xs"></i>
                    </button>
                    <button class="btn-delete-ann w-7 h-7 rounded-full hover:bg-red-50 text-gray-400 hover:text-red-500 flex items-center justify-center transition-colors" data-id="${ann.id}" title="刪除">
                        <i class="fa-solid fa-trash-can text-xs"></i>
                    </button>
                </div>
            </div>
        `;
    }).join('');

    // 綁定事件：行點擊 → 檢視
    container.querySelectorAll('.ann-row').forEach(row => {
        row.addEventListener('click', (e) => {
            // 如果點擊的是操作按鈕區域，不開啟檢視
            if (e.target.closest('.btn-edit-ann') || e.target.closest('.btn-pin-ann') || e.target.closest('.btn-delete-ann')) return;
            const id = row.getAttribute('data-id');
            openViewModal(id);
        });
    });

    // 綁定事件：編輯
    container.querySelectorAll('.btn-edit-ann').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            const id = btn.getAttribute('data-id');
            openEditPanel(id);
        });
    });

    // 綁定事件：置頂切換
    container.querySelectorAll('.btn-pin-ann').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            const id = btn.getAttribute('data-id');
            togglePin(id);
        });
    });

    // 綁定事件：刪除
    container.querySelectorAll('.btn-delete-ann').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            const id = btn.getAttribute('data-id');
            deleteAnnouncement(id);
        });
    });
}

function stripHtml(html) {
    const tmp = document.createElement('div');
    tmp.innerHTML = html;
    return tmp.textContent || tmp.innerText || '';
}

// ==================== 篩選 ====================
function initFilters() {
    const keyword = document.getElementById('filterKeyword');
    const catFilter = document.getElementById('filterCategory');
    const statusFilter = document.getElementById('filterStatus');

    let debounceTimer;
    keyword?.addEventListener('input', () => {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(() => renderAnnouncementList(), 200);
    });
    catFilter?.addEventListener('change', () => renderAnnouncementList());
    statusFilter?.addEventListener('change', () => renderAnnouncementList());
}

// ==================== 置頂切換 ====================
function togglePin(id) {
    const ann = announcementsDb.find(a => a.id === id);
    if (!ann) return;
    ann.pinned = !ann.pinned;
    renderAnnouncementList();
    renderStats();
    Swal.fire({
        icon: 'success',
        title: ann.pinned ? '已置頂' : '已取消置頂',
        toast: true,
        position: 'top-end',
        showConfirmButton: false,
        timer: 1500
    });
}

// ==================== 刪除 ====================
function deleteAnnouncement(id) {
    const ann = announcementsDb.find(a => a.id === id);
    if (!ann) return;
    Swal.fire({
        title: '確認刪除?',
        html: `將刪除公告「<b>${ann.title}</b>」，此操作無法復原。`,
        icon: 'warning',
        showCancelButton: true,
        confirmButtonColor: '#D98A6C',
        cancelButtonColor: '#6B8EAD',
        confirmButtonText: '確認刪除',
        cancelButtonText: '取消'
    }).then(result => {
        if (result.isConfirmed) {
            announcementsDb = announcementsDb.filter(a => a.id !== id);
            renderAnnouncementList();
            renderStats();
            Swal.fire({ icon: 'success', title: '已刪除', toast: true, position: 'top-end', showConfirmButton: false, timer: 1500 });
        }
    });
}

// ==================== Quill Editor ====================
function initQuillEditor() {
    quillInstance = new Quill('#quillEditorContainer', {
        theme: 'snow',
        placeholder: '在此輸入公告內容...',
        modules: {
            toolbar: [
                [{ 'size': ['small', false, 'large'] }, { 'header': [2, 3, false] }],
                [{ 'color': [] }, { 'background': [] }, { 'align': [] }],
                ['bold', 'underline', 'strike', 'link'],
                [{ 'list': 'ordered' }, { 'list': 'bullet' }, { 'indent': '-1' }, { 'indent': '+1' }],
                ['image', 'clean']
            ]
        }
    });

    // 中文標點按鈕事件
    document.querySelectorAll('.punct-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const char = btn.getAttribute('data-char');
            const isPair = btn.hasAttribute('data-pair');
            if (char && quillInstance) {
                const range = quillInstance.getSelection(true);
                quillInstance.insertText(range.index, char);
                if (isPair) {
                    quillInstance.setSelection(range.index + 1);
                } else {
                    quillInstance.setSelection(range.index + char.length);
                }
            }
        });
    });
}

// ==================== Slide-over Panel ====================
function initSlideOverPanel() {
    const wrapper = document.getElementById('slideOverWrapper');
    const backdrop = document.getElementById('slideOverBackdrop');
    const panel = document.getElementById('slideOverPanel');
    const closeBtns = document.querySelectorAll('.close-panel-btn');

    // 新增公告按鈕
    document.getElementById('btnNewAnnouncement')?.addEventListener('click', () => {
        editingAnnouncementId = null;
        resetForm();
        document.querySelector('#panelTitle span').textContent = '新增公告';
        openPanel();
    });

    // 儲存公告
    document.getElementById('btnSaveAnnouncement')?.addEventListener('click', () => saveAnnouncement('auto'));
    document.getElementById('btnSaveDraft')?.addEventListener('click', () => saveAnnouncement('draft'));

    // 關閉按鈕
    closeBtns.forEach(btn => btn.addEventListener('click', closePanel));
    backdrop?.addEventListener('click', closePanel);

    // 動態注入梯次選項
    populateProjectDropdown();
}

function populateProjectDropdown() {
    const select = document.getElementById('frmProject');
    if (!select) return;
    select.innerHTML = '<option value="ALL">全站廣播 (所有梯次)</option>' +
        projectsRef.map(p => `<option value="${p.id}">${p.name}</option>`).join('');
}

function openPanel() {
    const wrapper = document.getElementById('slideOverWrapper');
    const backdrop = document.getElementById('slideOverBackdrop');
    const panel = document.getElementById('slideOverPanel');
    wrapper.classList.remove('hidden');
    void wrapper.offsetWidth;
    backdrop.classList.remove('opacity-0');
    backdrop.classList.add('opacity-100');
    panel.classList.remove('translate-x-full');
    panel.classList.add('translate-x-0');
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

function openEditPanel(id) {
    editingAnnouncementId = id;
    populateForm(id);
    document.querySelector('#panelTitle span').textContent = '編輯公告';
    openPanel();
}

function resetForm() {
    document.getElementById('frmCategory').value = 'system';
    document.getElementById('frmStatus').value = 'draft';
    document.getElementById('frmProject').value = 'ALL';
    document.getElementById('frmPublishDate').value = new Date().toISOString().split('T')[0];
    document.getElementById('frmUnpublishDate').value = '';
    document.getElementById('frmPinned').checked = false;
    document.getElementById('frmTitle').value = '';
    if (quillInstance) quillInstance.root.innerHTML = '';
}

function populateForm(id) {
    const ann = announcementsDb.find(a => a.id === id);
    if (!ann) return;
    document.getElementById('frmCategory').value = ann.category;
    document.getElementById('frmStatus').value = ann.status;
    document.getElementById('frmProject').value = ann.project;
    document.getElementById('frmPublishDate').value = ann.publishDate;
    document.getElementById('frmUnpublishDate').value = ann.unpublishDate || '';
    document.getElementById('frmPinned').checked = ann.pinned;
    document.getElementById('frmTitle').value = ann.title;
    if (quillInstance) quillInstance.root.innerHTML = ann.content || '';
}

function saveAnnouncement(mode) {
    const title = document.getElementById('frmTitle').value.trim();
    const publishDate = document.getElementById('frmPublishDate').value;

    if (!title) {
        Swal.fire({ icon: 'warning', title: '請填寫公告標題', confirmButtonColor: '#6B8EAD' });
        return;
    }
    if (!publishDate) {
        Swal.fire({ icon: 'warning', title: '請設定上架日期', confirmButtonColor: '#6B8EAD' });
        return;
    }

    const content = quillInstance ? quillInstance.root.innerHTML : '';
    if (!content || content === '<p><br></p>') {
        Swal.fire({ icon: 'warning', title: '請填寫公告內容', confirmButtonColor: '#6B8EAD' });
        return;
    }

    let status = document.getElementById('frmStatus').value;
    if (mode === 'draft') status = 'draft';

    const formData = {
        category: document.getElementById('frmCategory').value,
        status,
        project: document.getElementById('frmProject').value,
        publishDate,
        unpublishDate: document.getElementById('frmUnpublishDate').value || '',
        pinned: document.getElementById('frmPinned').checked,
        title,
        content,
        author: JSON.parse(localStorage.getItem('cwt_user'))?.name || '系統管理員'
    };

    if (editingAnnouncementId) {
        const idx = announcementsDb.findIndex(a => a.id === editingAnnouncementId);
        if (idx >= 0) {
            announcementsDb[idx] = { ...announcementsDb[idx], ...formData };
        }
        Swal.fire({ icon: 'success', title: '公告已更新', toast: true, position: 'top-end', showConfirmButton: false, timer: 2000, timerProgressBar: true });
    } else {
        const newId = `ANN-${String(announcementsDb.length + 1).padStart(3, '0')}`;
        announcementsDb.push({
            id: newId,
            ...formData,
            createdAt: new Date().toISOString()
        });
        Swal.fire({ icon: 'success', title: mode === 'draft' ? '已存為草稿' : '公告已新增', toast: true, position: 'top-end', showConfirmButton: false, timer: 2000, timerProgressBar: true });
    }

    closePanel();
    renderAnnouncementList();
    renderStats();
}

// ==================== 檢視 Modal ====================
function initViewModal() {
    const modal = document.getElementById('viewModal');
    const panel = document.getElementById('viewModalPanel');
    const closeBtns = document.querySelectorAll('.close-view-modal');

    closeBtns.forEach(btn => btn.addEventListener('click', closeViewModal));
    modal?.querySelector('.modal-backdrop')?.addEventListener('click', closeViewModal);

    // 檢視 Modal 中的編輯按鈕
    document.getElementById('viewModalEditBtn')?.addEventListener('click', () => {
        const id = modal.getAttribute('data-current-id');
        closeViewModal();
        setTimeout(() => openEditPanel(id), 350);
    });
}

function openViewModal(id) {
    const ann = announcementsDb.find(a => a.id === id);
    if (!ann) return;

    const modal = document.getElementById('viewModal');
    const panel = document.getElementById('viewModalPanel');
    modal.setAttribute('data-current-id', id);

    // 標籤
    const tagsEl = document.getElementById('viewModalTags');
    const cat = categoryMap[ann.category] || categoryMap['other'];
    const st = statusDisplayMap[ann.status] || statusDisplayMap['draft'];
    tagsEl.innerHTML = `
        <span class="text-xs font-bold px-2 py-0.5 rounded ${cat.color}">${cat.label}</span>
        <span class="text-xs font-bold px-2 py-0.5 rounded ${st.color}">${st.label}</span>
        ${ann.pinned ? '<span class="bg-red-100 text-red-500 text-xs font-bold px-2 py-0.5 rounded"><i class="fa-solid fa-thumbtack mr-0.5"></i>置頂</span>' : ''}
    `;

    document.getElementById('viewModalTitle').textContent = ann.title;
    document.getElementById('viewModalPublishDate').textContent = ann.publishDate;

    const unpubWrap = document.getElementById('viewModalUnpublishWrap');
    if (ann.unpublishDate) {
        unpubWrap.classList.remove('hidden');
        document.getElementById('viewModalUnpublishDate').textContent = ann.unpublishDate;
    } else {
        unpubWrap.classList.add('hidden');
    }

    const projLabel = ann.project === 'ALL' ? '全站廣播' : (projectsRef.find(p => p.id === ann.project)?.name || ann.project);
    document.getElementById('viewModalProject').textContent = projLabel;

    document.getElementById('viewModalContent').innerHTML = ann.content;

    modal.classList.remove('hidden');
    void modal.offsetWidth;
    panel.classList.remove('scale-95', 'opacity-0');
    panel.classList.add('scale-100', 'opacity-100');
}

function closeViewModal() {
    const modal = document.getElementById('viewModal');
    const panel = document.getElementById('viewModalPanel');
    panel.classList.remove('scale-100', 'opacity-100');
    panel.classList.add('scale-95', 'opacity-0');
    setTimeout(() => modal.classList.add('hidden'), 300);
}

// ==================== 使用說明按鈕 ====================
function initUserGuideButton() {
    document.getElementById('btnUserGuide')?.addEventListener('click', (e) => {
        e.preventDefault();
        Swal.fire({
            icon: 'info',
            title: '使用說明手冊 (DEMO)',
            html: `
                <p class="text-sm text-gray-600 mb-4">此為模擬功能，實際上線後將提供 PDF 文件下載連結。</p>
                <div class="text-left text-sm space-y-2 border-t border-gray-100 pt-4">
                    <div class="flex items-center gap-2 text-gray-700">
                        <i class="fa-solid fa-file-pdf text-red-500"></i>
                        <span>CWT 命題工作平臺 使用說明手冊 v3.2.pdf</span>
                    </div>
                    <div class="flex items-center gap-2 text-gray-700">
                        <i class="fa-solid fa-file-pdf text-red-500"></i>
                        <span>聽力題組命題操作指引 v1.0.pdf</span>
                    </div>
                    <div class="flex items-center gap-2 text-gray-700">
                        <i class="fa-solid fa-file-pdf text-red-500"></i>
                        <span>三審流程與迴避規則說明.pdf</span>
                    </div>
                </div>
            `,
            confirmButtonColor: '#6B8EAD',
            confirmButtonText: '瞭解'
        });
    });
}
