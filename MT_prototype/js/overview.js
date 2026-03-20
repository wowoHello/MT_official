/**
 * Overview Module
 * 負責命題總覽的狀態過濾、統計卡片、三審進度燈號呈現與試題詳情。
 * Version: 1.0 (DEMO)
 */

// 試題狀態機對應文字 (協助渲染標籤顏色)
const statusMap = {
    'draft': { label: '草稿', color: 'bg-gray-100 text-gray-600', border: 'border-gray-200' },
    'completed': { label: '命題完成', color: 'bg-blue-100 text-blue-700', border: 'border-blue-200' },
    'pending': { label: '待審', color: 'bg-yellow-100 text-yellow-700', border: 'border-yellow-200' },
    'peer_reviewing': { label: '互審中', color: 'bg-blue-100 text-[var(--color-morandi)]', border: 'border-[var(--color-morandi)]/30' },
    'peer_reviewed': { label: '互審完成', color: 'bg-green-100 text-green-700', border: 'border-green-200' },
    'peer_editing': { label: '互審修題', color: 'bg-red-100 text-[var(--color-terracotta)]', border: 'border-[var(--color-terracotta)]/30' },
    'expert_reviewing': { label: '專審中', color: 'bg-blue-100 text-[var(--color-morandi)]', border: 'border-[var(--color-morandi)]/30' },
    'expert_reviewed': { label: '專審完成', color: 'bg-green-100 text-green-700', border: 'border-green-200' },
    'expert_editing': { label: '專審修題', color: 'bg-red-100 text-[var(--color-terracotta)]', border: 'border-[var(--color-terracotta)]/30' },
    'final_reviewing': { label: '總審中', color: 'bg-blue-100 text-[var(--color-morandi)]', border: 'border-[var(--color-morandi)]/30' },
    'final_reviewed': { label: '總審完成', color: 'bg-red-100 text-red-700', border: 'border-red-200' },
    'final_editing': { label: '總審修題', color: 'bg-red-100 text-[var(--color-terracotta)]', border: 'border-[var(--color-terracotta)]/30' },
    'adopted': { label: '採用', color: 'bg-[var(--color-sage)]/20 text-[var(--color-sage)]', border: 'border-[var(--color-sage)]/50' },
    'rejected': { label: '不採用', color: 'bg-gray-200 text-gray-500', border: 'border-gray-300' }
};

const qTypeMap = {
    'single': '一般單選題', 'select': '精選單選題', 'readGroup': '閱讀題組',
    'longText': '長文題目', 'shortGroup': '短文題組', 'listen': '聽力測驗', 'listenGroup': '聽力題組'
};

const diffMap = { 'easy': '易', 'medium': '中', 'hard': '難' };

// 模擬人才庫名稱比對用 (整合自 projects.js)
const teacherMap = {
    'T1001': '劉雅婷', 'T1002': '王健明', 'T1003': '張心怡', 'T1004': '吳家豪',
    'C2001': '李教授', 'C2002': '陳副教授', 'S3001': '林總召', 'S3002': '許編輯'
};

const qTypeConfig = {
    single: { subQuestionMode: null, hasAudio: false, hasPassage: false },
    select: { subQuestionMode: null, hasAudio: false, hasPassage: false },
    longText: { subQuestionMode: null, hasAudio: false, hasPassage: false },
    readGroup: { subQuestionMode: 'choice', hasAudio: false, hasPassage: true, passageLabel: '閱讀文章' },
    shortGroup: { subQuestionMode: 'freeResponse', hasAudio: false, hasPassage: true, passageLabel: '短文內容' },
    listen: { subQuestionMode: null, hasAudio: true, hasPassage: false },
    listenGroup: { subQuestionMode: 'choice', hasAudio: true, hasPassage: true, passageLabel: '聽力腳本' }
};

const renderChoiceOptions = (options = [], answer = '') => `
    <div class="space-y-2 mb-4">
        ${options.map(option => `
            <div class="flex gap-2 ${option.label === answer ? 'bg-green-50 border border-green-200 text-green-700 p-2 rounded items-center justify-between font-bold' : 'text-gray-600'}">
                <div><span>(${option.label})</span> ${option.text}</div>
                ${option.label === answer ? '<i class="fa-solid fa-circle-check"></i>' : ''}
            </div>`).join('')}
    </div>`;

const renderSubQuestionDetail = (subQuestion, index, mode) => {
    if (mode === 'freeResponse') {
        return `
            <div class="border-t border-gray-100 pt-6">
                <div class="flex items-start gap-4">
                    <div class="bg-blue-500 text-white px-3 py-1 rounded text-sm font-bold flex-shrink-0">第${index + 1}題</div>
                    <div class="flex-grow">
                        <p class="mb-4 text-gray-800 font-medium">${subQuestion.stem}</p>
                        <div class="mb-4 px-3 py-2 bg-gray-50 border border-gray-200 border-dashed rounded text-gray-500 text-sm flex items-center gap-2 w-max">
                            <i class="fa-solid fa-pen-to-square"></i> 論述題 (自由作答)
                        </div>
                        <div class="p-3 bg-yellow-50 rounded-lg border border-yellow-100 text-sm">
                            <p class="text-yellow-700 font-bold inline-block mr-2"><i class="fa-regular fa-lightbulb"></i> 解析</p>
                            <span class="text-gray-700">${subQuestion.analysis || '尚未填寫解析'}</span>
                        </div>
                    </div>
                </div>
            </div>`;
    }

    return `
        <div class="border-t border-gray-100 pt-6">
            <div class="flex items-start gap-4">
                <div class="bg-blue-500 text-white px-3 py-1 rounded text-sm font-bold flex-shrink-0">第${index + 1}題</div>
                <div class="flex-grow">
                    <p class="mb-4 text-gray-800 font-medium">${subQuestion.stem}</p>
                    ${renderChoiceOptions(subQuestion.options, subQuestion.answer)}
                </div>
            </div>
        </div>`;
};

const renderOverviewQuestionContent = (question) => {
    const config = qTypeConfig[question.type] || qTypeConfig.single;
    let html = '';

    if (config.hasAudio) {
        html += `
            <div class="mb-6 p-4 bg-blue-50 rounded-lg border border-blue-100 flex items-center gap-4">
                <div class="w-12 h-12 bg-blue-500 text-white rounded-full flex items-center justify-center flex-shrink-0 text-xl shadow-sm">
                    <i class="fa-solid fa-volume-high"></i>
                </div>
                <div class="flex-grow">
                    <div class="text-sm font-bold text-blue-800 mb-2">${question.audioUrl || 'DEMO 聽力音檔'}</div>
                    <div class="text-xs text-blue-700">正式版將於此處載入實際音檔。</div>
                </div>
            </div>`;
    }

    if (config.hasPassage) {
        html += `
            <div class="mb-6">
                <span class="inline-block px-2 py-1 ${question.type === 'listenGroup' ? 'bg-pink-50 border-pink-200 text-pink-600' : question.type === 'shortGroup' ? 'bg-green-50 border-green-200 text-green-600' : 'bg-orange-50 border-orange-200 text-orange-600'} border rounded text-xs font-bold mb-3">${qTypeMap[question.type]}</span>
                <div class="bg-gray-50 border border-gray-200 rounded-lg p-5">
                    <p class="text-gray-700 whitespace-pre-wrap leading-relaxed">${question.passage}</p>
                </div>
            </div>`;
    }

    if (question.type === 'longText') {
        html += `
            <div class="space-y-4">
                <div class="rounded-2xl border border-amber-200 bg-amber-50 p-5">
                    <div class="text-xs font-bold tracking-[0.2em] text-amber-700 mb-2">作文題幹</div>
                    <p class="text-lg font-semibold text-gray-800 leading-relaxed">${question.stem}</p>
                </div>
                <div class="rounded-2xl border border-dashed border-gray-300 bg-white p-5 text-sm text-gray-500">本題為作文題，作答時不提供選項，考生需於答案卷自由書寫。</div>
                <div class="p-4 bg-yellow-50 rounded-lg border border-yellow-100 text-sm text-gray-700">
                    <p class="text-yellow-700 font-bold inline-block mr-2"><i class="fa-regular fa-lightbulb"></i> 解析</p>
                    <span>${question.analysis}</span>
                </div>
            </div>`;
        return html;
    }

    if (!config.hasPassage) {
        html += `
            <p class="font-medium text-gray-800 mb-4">${question.stem}</p>`;
    }

    if (config.subQuestionMode) {
        html += `<div class="space-y-6">${question.subQuestions.map((subQuestion, index) => renderSubQuestionDetail(subQuestion, index, config.subQuestionMode)).join('')}</div>`;
        if (question.analysis) {
            html += `
                <div class="mt-6 p-4 bg-yellow-50 rounded-lg border border-yellow-100 text-sm text-gray-700">
                    <p class="text-yellow-700 font-bold inline-block mr-2"><i class="fa-regular fa-lightbulb"></i> 題組解析</p>
                    <span>${question.analysis}</span>
                </div>`;
        }
        return html;
    }

    html += `${renderChoiceOptions(question.options, question.answer)}
        <div class="p-3 bg-yellow-50 rounded-lg border border-yellow-100 text-sm text-gray-700">
            <p class="text-yellow-700 font-bold inline-block mr-2"><i class="fa-regular fa-lightbulb"></i> 解析</p>
            <span>${question.analysis}</span>
        </div>`;

    return html;
};

// 產生假資料庫 (針對當前 P2026-01 梯次)
const mockQuestionsDb = [
    {
        id: 'Q-2602-001', project_id: 'P2026-01', type: 'single', level: '中級', difficulty: 'medium', author_id: 'T1001',
        stage: 6, status: 'adopted', returnCount: 0,
        stem: '下列何者最能表現「飲水思源」的意思？',
        options: [
            { label: 'A', text: '吃飯時要慢慢品嘗' },
            { label: 'B', text: '獲得成果後不忘感念來源' },
            { label: 'C', text: '做事前要先找水源' },
            { label: 'D', text: '旅行時要準備飲水' }
        ],
        answer: 'B', analysis: '「飲水思源」比喻人在享受成果時，不忘感謝本源與幫助自己的人。',
        history: [
            { time: '2026-08-11 10:00', user: 'T1001', action: '命題完成', comment: '初稿建立完畢' },
            { time: '2026-08-15 14:20', user: 'T1002', action: '互審意見', comment: '誘答選項設計良好。' },
            { time: '2026-08-20 09:15', user: 'C2001', action: '專審意見 (採用)', comment: '符合等級。' },
            { time: '2026-08-25 11:30', user: 'S3001', action: '總召決策 (採用)', comment: '核准入庫。' }
        ]
    },
    {
        id: 'Q-2602-002', project_id: 'P2026-01', type: 'select', level: '中高級', difficulty: 'hard', author_id: 'T1003',
        stage: 6, status: 'final_reviewing', returnCount: 0,
        stem: '下列何者使用了「對偶」修辭？',
        options: [
            { label: 'A', text: '千山鳥飛絕，萬徑人蹤滅' },
            { label: 'B', text: '月落烏啼霜滿天' },
            { label: 'C', text: '夕陽無限好，只是近黃昏' },
            { label: 'D', text: '春眠不覺曉' }
        ],
        answer: 'A', analysis: '選項 A 句式整齊、詞性對稱，屬於典型對偶。',
        history: [
            { time: '2026-08-05 10:00', user: 'T1003', action: '命題完成', comment: '修辭鑑別題' },
            { time: '2026-08-08 11:20', user: 'T1002', action: '互審意見', comment: '選項鑑別度佳。' },
            { time: '2026-08-12 14:00', user: 'C2001', action: '專審意見 (採用)', comment: '同意進入總審。' }
        ]
    },
    {
        id: 'Q-2602-003', project_id: 'P2026-01', type: 'readGroup', level: '高級', difficulty: 'hard', author_id: 'T1002',
        stage: 4, status: 'expert_reviewing', returnCount: 0,
        passage: '閱讀以下古文節選，回答第 1～2 題。\n\n「庖丁為文惠君解牛，手之所觸，肩之所倚，足之所履，膝之所踦，砉然響然，奏刀騞然，莫不中音。」',
        subQuestions: [
            {
                stem: '文中「手之所觸，肩之所倚，足之所履，膝之所踦」使用了何種修辭手法？',
                options: [
                    { label: 'A', text: '譬喻' },
                    { label: 'B', text: '排比' },
                    { label: 'C', text: '誇飾' },
                    { label: 'D', text: '設問' }
                ],
                answer: 'B'
            },
            {
                stem: '這段文字主要凸顯庖丁具備何種特質？',
                options: [
                    { label: 'A', text: '勇敢' },
                    { label: 'B', text: '細心' },
                    { label: 'C', text: '技藝純熟' },
                    { label: 'D', text: '善於辯論' }
                ],
                answer: 'C'
            }
        ],
        analysis: '本題組重點在於理解古文描寫技巧，以及從細節歸納人物特質。',
        history: [
            { time: '2026-08-12 11:00', user: 'T1002', action: '命題完成', comment: '古文閱讀題組初稿' },
            { time: '2026-08-16 16:00', user: 'T1001', action: '互審意見', comment: '語氣可再順一些。' }
        ]
    },
    {
        id: 'Q-2602-004', project_id: 'P2026-01', type: 'longText', level: '優級', difficulty: 'hard', author_id: 'T1003',
        stage: 7, status: 'final_editing', returnCount: 3,
        stem: '請以「如果時間可以倒流」為題，寫一篇作文。文章需描述你想重新面對的一件往事，說明你會如何做出不同選擇，並反思這個選擇可能帶來的改變。字數以 600 字左右為原則。',
        analysis: '本題著重在敘事完整性、情感真實度與反思深度，評閱時應特別觀察轉折安排與立意是否清楚。',
        history: [
            { time: '2026-08-01 09:00', user: 'T1003', action: '命題完成', comment: '' },
            { time: '2026-08-15 09:00', user: 'S3001', action: '總召決策 (改後再審)', comment: '請補充評分重點。' },
            { time: '2026-08-26 15:00', user: 'S3001', action: '總召決策 (改後再審)', comment: '觸發三次退回底線，由總召收回處理。' }
        ]
    },
    {
        id: 'Q-2602-005', project_id: 'P2026-01', type: 'shortGroup', level: '中級', difficulty: 'medium', author_id: 'T1001',
        stage: 1, status: 'completed', returnCount: 0,
        passage: '閱讀下面短文，回答第 1～2 題。\n\n「一個人的價值不在於他擁有什麼，而在於他貢獻了什麼。真正有意義的人生，是不斷為他人創造價值的過程。」',
        subQuestions: [
            {
                stem: '作者認為衡量一個人價值的標準是什麼？請根據短文內容加以說明。',
                analysis: '重點在於說明「貢獻」而非「擁有」才是價值核心。'
            },
            {
                stem: '你是否同意作者觀點？請舉生活實例說明你的看法。',
                analysis: '可從生活經驗出發，結合短文觀點表達個人立場。'
            }
        ],
        history: [
            { time: '2026-09-01 10:00', user: 'T1001', action: '命題完成', comment: '生活情境題組' }
        ]
    },
    {
        id: 'Q-2602-006', project_id: 'P2026-01', type: 'listen', level: '難度三', difficulty: 'medium', author_id: 'T1004',
        stage: 5, status: 'expert_editing', returnCount: 1,
        audioUrl: 'demo_audio_001.mp3',
        stem: '請問講者在對話中主要想表達什麼？',
        options: [
            { label: 'A', text: '工作進度落後' },
            { label: 'B', text: '需要增加預算' },
            { label: 'C', text: '團隊溝通出問題' },
            { label: 'D', text: '客戶反應不佳' }
        ],
        answer: 'C', analysis: '講者反覆提到訊息沒有同步、會議結論未被執行，可判斷問題核心在於溝通。',
        history: [
            { time: '2026-08-10 09:00', user: 'T1004', action: '命題完成', comment: '職場情境聽力' },
            { time: '2026-08-22 15:00', user: 'C2002', action: '專審意見 (改後再審)', comment: '選項 C 與 D 可再拉開差距。' }
        ]
    },
    {
        id: 'Q-2602-007', project_id: 'P2026-01', type: 'listenGroup', level: '難度四', difficulty: 'hard', author_id: 'T1002',
        stage: 2, status: 'peer_reviewing', returnCount: 0,
        audioUrl: 'demo_audio_group_001.mp3',
        passage: '【聽力題組】請先聆聽一段訪談，了解青年返鄉後如何將閒置穀倉改造成社區共學空間，再回答以下兩道子題。',
        subQuestions: [
            {
                stem: '根據內容，青年返鄉後最先處理的是哪一件事？',
                options: [
                    { label: 'A', text: '募集企業贊助' },
                    { label: 'B', text: '整理閒置穀倉空間' },
                    { label: 'C', text: '成立觀光工廠' },
                    { label: 'D', text: '大量招募外地講師' }
                ],
                answer: 'B'
            },
            {
                stem: '主持人認為這個計畫最重要的價值是什麼？',
                options: [
                    { label: 'A', text: '提高地方收入' },
                    { label: 'B', text: '推動夜間經濟' },
                    { label: 'C', text: '讓不同世代在同一空間學習' },
                    { label: 'D', text: '增加觀光打卡點' }
                ],
                answer: 'C'
            }
        ],
        analysis: '題組重點在於掌握事件順序與主持人總結的核心價值。',
        history: [
            { time: '2026-08-05 09:00', user: 'T1002', action: '命題完成', comment: '地方創生主題' },
            { time: '2026-08-09 14:00', user: 'T1004', action: '互審意見', comment: '內容真實，題組完整。' }
        ]
    }
];

let currentProjId = 'P2026-01'; // default fallback

document.addEventListener('DOMContentLoaded', () => {
    // Determine current project
    const pId = localStorage.getItem('cwt_current_project');
    if (pId) currentProjId = pId;

    // Export Button
    const btnExport = document.getElementById('exportCsvBtn');
    if (btnExport) btnExport.addEventListener('click', exportToCsv);

    // Listen to Project Switcher
    document.addEventListener('projectChanged', (e) => {
        currentProjId = e.detail.id;
        renderOverviewList();
    });

    // Bind filters
    document.getElementById('filterKeyword').addEventListener('input', renderOverviewList);
    document.getElementById('filterType').addEventListener('change', handleTypeChange);
    document.getElementById('filterLevel').addEventListener('change', renderOverviewList);
    document.getElementById('filterStatus').addEventListener('change', renderOverviewList);

    // Initial Render
    renderOverviewList();

    // Setup slide over closing via backdrop
    document.getElementById('slideOverBackdrop').addEventListener('click', closePanel);
});

// 當題型改變時，連動切換適用的等級選項
function handleTypeChange() {
    const fType = document.getElementById('filterType').value;
    const fLevelSel = document.getElementById('filterLevel');
    const optNormal = fLevelSel.querySelectorAll('.opt-normal');
    const optListen = fLevelSel.querySelectorAll('.opt-listen');

    const isListenType = fType === 'listen' || fType === 'listenGroup';

    if (fType === 'all') {
        // 若為所有題型，全開
        optNormal.forEach(el => el.classList.remove('hidden'));
        optListen.forEach(el => el.classList.remove('hidden'));
    } else if (isListenType) {
        // 聽力題型：隱藏一般，顯示難度
        optNormal.forEach(el => el.classList.add('hidden'));
        optListen.forEach(el => el.classList.remove('hidden'));

        // 防呆：若目前選中一般等級，重置為 all
        if (fLevelSel.options[fLevelSel.selectedIndex].classList.contains('opt-normal')) {
            fLevelSel.value = 'all';
        }
    } else {
        // 非聽力題型：顯示一般，隱藏難度
        optNormal.forEach(el => el.classList.remove('hidden'));
        optListen.forEach(el => el.classList.add('hidden'));

        // 防呆：若目前選中聽力等級，重置為 all
        if (fLevelSel.options[fLevelSel.selectedIndex].classList.contains('opt-listen')) {
            fLevelSel.value = 'all';
        }
    }

    // 觸發重新渲染
    renderOverviewList();
}

function renderOverviewList() {
    const listContainer = document.getElementById('overviewListContainer');
    const kw = document.getElementById('filterKeyword').value.toLowerCase();
    const fType = document.getElementById('filterType').value;
    const fLevel = document.getElementById('filterLevel').value;
    const fStatus = document.getElementById('filterStatus').value;

    let filtered = mockQuestionsDb.filter(q => q.project_id === currentProjId);

    // Stats variables
    let stTotal = filtered.length;
    let stDraft = 0, stAdopted = 0, stEditing = 0, stPending = 0, stPeer = 0, stExpert = 0;

    // Filter Logic & Stats Acc
    const finalList = [];
    filtered.forEach(q => {
        const authorName = teacherMap[q.author_id] || q.author_id;
        let matchKw = q.id.toLowerCase().includes(kw) || authorName.toLowerCase().includes(kw);
        let matchType = fType === 'all' || q.type === fType;

        // Detailed Stats
        if (q.status === 'draft' || q.status === 'completed') stDraft++;
        if (q.status === 'adopted') stAdopted++;
        if (['peer_editing', 'expert_editing', 'final_editing'].includes(q.status)) stEditing++;
        if (['pending'].includes(q.status) && q.stage !== 6) stPending++; // avoid double counting if adopted mapped wrong
        if (q.status === 'peer_reviewing') stPeer++;
        if (q.status === 'expert_reviewing') stExpert++;

        let matchStatus = true;
        if (fStatus === 'working') {
            matchStatus = ['draft', 'completed', 'peer_reviewing', 'expert_reviewing', 'final_reviewing'].includes(q.status);
        } else if (fStatus === 'editing') {
            matchStatus = ['peer_editing', 'expert_editing', 'final_editing'].includes(q.status);
        } else if (fStatus === 'adopted') {
            matchStatus = q.status === 'adopted';
        } else if (fStatus === 'rejected') {
            matchStatus = q.status === 'rejected';
        }

        if (matchKw && matchType && matchStatus) {
            // Level Filtering
            let matchLevel = fLevel === 'all' || q.level === fLevel;

            if (matchLevel) {
                finalList.push(q);
            }
        }
    });

    // Update Stats DOM
    document.getElementById('statTotal').innerText = stTotal;
    document.getElementById('statDraft').innerText = stDraft;
    document.getElementById('statAdopted').innerText = stAdopted;
    document.getElementById('statEditing').innerText = stEditing;
    document.getElementById('statPending').innerText = stPending;
    document.getElementById('statPeerReviewing').innerText = stPeer;
    document.getElementById('statExpertReviewing').innerText = stExpert;
    document.getElementById('listCount').innerText = finalList.length;

    if (finalList.length === 0) {
        listContainer.innerHTML = '<div class="p-10 text-center text-gray-400 font-medium">該梯次目前無相符的試題。</div>';
        return;
    }

    let html = '';
    finalList.forEach(q => {
        const author = teacherMap[q.author_id] || q.author_id;
        const sMeta = statusMap[q.status] || { label: q.status, color: 'bg-gray-100 text-gray-500' };

        // 渲染 7 階段燈號
        const stepperHtml = renderProgressStepper(q);

        // 特殊處理第3次退回警告
        let warningBadge = '';
        if (q.returnCount >= 3) {
            warningBadge = `<span class="bg-red-100 text-red-600 text-[10px] px-1.5 py-0.5 rounded font-bold ml-2 animate-pulse" title="觸發退回底線，總召強制收回"><i class="fa-solid fa-triangle-exclamation"></i> 強制</span`;
        }

        html += `
            <div class="grid grid-cols-12 gap-4 px-6 py-4 border-b border-gray-100 hover:bg-blue-50/50 hover:shadow-[inset_4px_0_0_var(--color-morandi)] transition-all items-center cursor-pointer group" onclick="openPanel('${q.id}')">
                <div class="col-span-12 lg:col-span-2 flex flex-col">
                    <span class="font-bold text-[var(--color-slate-main)] text-sm group-hover:text-[var(--color-morandi)] transition-colors">${q.id}</span>
                    <span class="text-xs text-gray-500 mt-0.5"><i class="fa-solid fa-user text-[var(--color-sage)] mr-1"></i>${author} ${warningBadge}</span>
                </div>
                <div class="col-span-12 lg:col-span-1">
                    <span class="inline-block px-2 py-1 bg-gray-100 text-gray-600 rounded text-[11px] font-bold border border-gray-200">${qTypeMap[q.type]}</span>
                </div>
                <div class="col-span-12 lg:col-span-1 flex flex-col items-start gap-1">
                    <span class="inline-block px-1.5 py-0.5 bg-blue-50 text-[var(--color-morandi)] rounded text-[10px] font-bold border border-blue-100">${q.level}</span>
                    <span class="inline-block px-1.5 py-0.5 bg-orange-50 text-[var(--color-terracotta)] rounded text-[10px] font-bold border border-orange-100">${diffMap[q.difficulty]}</span>
                </div>
                <div class="col-span-12 lg:col-span-7 overflow-hidden">
                    ${stepperHtml}
                </div>
                <div class="col-span-12 lg:col-span-1 text-center">
                    <span class="${sMeta.color} ${sMeta.border} border px-2 py-1 text-[11px] font-bold rounded shadow-sm whitespace-nowrap">${sMeta.label}</span>
                </div>
            </div>
        `;
    });

    listContainer.innerHTML = html;
}

/**
 * 渲染 7 階段燈號 SVG
 * 階段: 1命題 2互審 3互修 4專審 5專修 6總審 7總修
 */
function renderProgressStepper(q) {
    const steps = ['命題', '互審', '互修', '專審', '專修', '總審', '總修'];

    // 計算每盞燈的狀態 (green=通過, blue=進行中, red=卡關/退回修題, gray=未到)
    const getLightStatus = (stepIdx, qStage, qStatus) => {
        // 特別處理：如果已被採用入庫，全部轉綠
        if (qStatus === 'adopted') return 'green';
        // 如果被不採用，停留在那關變紅，後面全灰
        if (qStatus === 'rejected') {
            return stepIdx < qStage ? 'green' : (stepIdx === qStage ? 'red' : 'gray');
        }

        if (stepIdx < qStage) return 'green';
        if (stepIdx > qStage) return 'gray';

        // 當前階段 stepIdx === qStage
        if (['draft', 'completed', 'peer_reviewing', 'expert_reviewing', 'final_reviewing'].includes(qStatus)) {
            return 'blue'; // 一般執行中
        }
        if (['peer_editing', 'expert_editing', 'final_editing'].includes(qStatus)) {
            return 'red'; // 修題中 (注意)
        }
        if (['pending', 'peer_reviewed', 'expert_reviewed', 'final_reviewed'].includes(qStatus)) {
            return 'green'; // 剛完成，等待下一個 stage 接手 (過渡狀態)
        }
        return 'gray';
    };

    let sHtml = '<div class="flex items-center justify-between relative w-full px-2 max-w-[400px] mx-auto">';

    // 繪製連線底層
    sHtml += '<div class="absolute top-1/2 left-4 right-4 h-0.5 bg-gray-200 -translate-y-1/2 z-0"></div>';

    // 繪製 7 顆燈泡
    steps.forEach((lbl, idx) => {
        let st = getLightStatus(idx + 1, q.stage, q.status);

        // Style variables
        let bg = 'bg-gray-100', border = 'border-gray-300', icon = '', text = 'text-gray-400', anim = '';

        if (st === 'green') {
            bg = 'bg-[var(--color-sage)]'; border = 'border-[var(--color-sage)]'; text = 'text-white';
            icon = '<i class="fa-solid fa-check text-[10px]"></i>';
        } else if (st === 'blue') {
            bg = 'bg-[var(--color-morandi)]'; border = 'border-[var(--color-morandi)]'; text = 'text-white';
            anim = 'animate-pulse-blue rounded-full shadow-lg';
            icon = '<i class="fa-solid fa-spinner fa-spin text-[10px]"></i>';
        } else if (st === 'red') {
            bg = 'bg-[var(--color-terracotta)]'; border = 'border-[var(--color-terracotta)]'; text = 'text-white';
            anim = 'animate-pulse-red rounded-full shadow-lg';
            icon = '<i class="fa-solid fa-pen text-[10px]"></i>';
        }

        if (q.status === 'rejected' && idx + 1 === q.stage) {
            icon = '<i class="fa-solid fa-xmark text-[10px]"></i>'; // 報廢打叉
        }

        // 當前節點點亮下方的連線
        let lineActive = st === 'green' ? 'w-full' : (st !== 'gray' ? 'w-1/2' : 'w-0');
        let lineDiv = idx < 6 ? `<div class="absolute top-1/2 left-full h-0.5 bg-[var(--color-sage)] transition-all z-10 -translate-y-1/2" style="width: calc(100% * flex-grow); ${lineActive !== 'w-0' ? 'width: ' + (st === 'green' ? '100%' : '50%') : 'display:none;'}"></div>` : '';

        sHtml += `
            <div class="flex flex-col items-center relative z-10" title="${lbl}">
                <div class="w-6 h-6 rounded-full border-2 flex items-center justify-center transition-all ${bg} ${border} ${text} ${anim} text-xs">
                    ${icon || (idx + 1)}
                </div>
                <div class="text-[9px] mt-1 font-bold ${st !== 'gray' ? 'text-gray-700' : 'text-gray-400'}">${lbl}</div>
                <!--${lineDiv}-->
            </div>
        `;
    });
    sHtml += '</div>';
    return sHtml;
}

// ----------------------------------------
// Slide-over 詳細面版控制
// ----------------------------------------

function openPanel(id) {
    const q = mockQuestionsDb.find(x => x.id === id);
    if (!q) return;

    // 填入基本資料
    document.getElementById('dtlQid').innerText = q.id;
    document.getElementById('dtlAuthor').innerText = teacherMap[q.author_id] || q.author_id;
    document.getElementById('dtlType').innerText = qTypeMap[q.type];
    document.getElementById('dtlLevel').innerText = q.level;
    document.getElementById('dtlDiff').innerText = diffMap[q.difficulty];
    document.getElementById('dtlContent').innerHTML = renderOverviewQuestionContent(q);

    // 狀態 Badge
    const sMeta = statusMap[q.status];
    const badge = document.getElementById('dtlStatusBadge');
    badge.className = `text-xs px-2 py-0.5 rounded-full font-bold border ${sMeta.color} ${sMeta.border}`;
    badge.innerText = sMeta.label;

    // 退回警告標籤
    const warnBadge = document.getElementById('dtlWarningBadge');
    if (q.returnCount >= 3) {
        warnBadge.classList.remove('hidden');
    } else {
        warnBadge.classList.add('hidden');
    }

    // 渲染歷史軌跡 (最新在上)
    const timelineContainer = document.getElementById('dtlTimelineLogs');
    if (q.history.length === 0) {
        timelineContainer.innerHTML = '<div class="text-sm text-gray-400 py-4">尚無任何歷史軌跡。</div>';
    } else {
        let tHtml = '';
        [...q.history].reverse().forEach((log, i) => {
            const isLatest = i === 0;
            const userIcon = log.user.startsWith('C') ? 'fa-user-tie text-[var(--color-morandi)]' : (log.user.startsWith('S') ? 'fa-user-shield text-[var(--color-terracotta)]' : 'fa-user text-[var(--color-sage)]');

            // 判斷決策給予特殊顏色標籤
            let actionBadgeColor = 'bg-white border-gray-200 text-gray-500';
            let actionIcon = '';

            if (log.action.includes('採用') && !log.action.includes('不採用')) {
                actionBadgeColor = 'bg-green-50 border-green-200 text-green-700 font-bold';
                actionIcon = '<i class="fa-solid fa-circle-check text-green-600 mr-1"></i>';
            } else if (log.action.includes('不採用')) {
                actionBadgeColor = 'bg-gray-100 border-gray-300 text-gray-700 font-bold';
                actionIcon = '<i class="fa-solid fa-ban text-gray-500 mr-1"></i>';
            } else if (log.action.includes('改後再審') || log.action.includes('退回')) {
                actionBadgeColor = 'bg-orange-50 border-orange-200 text-orange-700 font-bold';
                actionIcon = '<i class="fa-solid fa-rotate-left text-orange-600 mr-1"></i>';
            }

            tHtml += `
                <div class="relative">
                    <div class="absolute -left-[31px] top-1 w-4 h-4 rounded-full border-2 border-white ${isLatest ? 'bg-[var(--color-morandi)] shadow-sm' : 'bg-gray-300'} z-10 flex items-center justify-center"></div>
                    <div class="bg-gray-50 rounded-lg p-3 border border-gray-100 ${isLatest ? 'shadow-sm border-[var(--color-morandi)]/30' : ''}">
                        <div class="flex justify-between items-start mb-1">
                            <div class="text-xs font-bold text-gray-700 flex items-center gap-1">
                                <i class="fa-solid gap-1 ${userIcon}"></i>
                                ${teacherMap[log.user] || log.user}
                                <span class="border px-1.5 py-0.5 rounded text-[10px] ml-1 shadow-sm ${actionBadgeColor}">${actionIcon}${log.action}</span>
                            </div>
                            <div class="text-[10px] text-gray-400 font-mono">${log.time}</div>
                        </div>
                        ${log.comment ? `<div class="mt-2 text-sm text-gray-800 bg-white p-2 rounded border border-gray-100 italic">" ${log.comment} "</div>` : ''}
                    </div>
                </div>
            `;
        });
        timelineContainer.innerHTML = tHtml;
    }

    // 動畫開展
    document.getElementById('slideOverWrapper').classList.remove('hidden');
    // slight delay to allow display block to apply before transition
    setTimeout(() => {
        document.getElementById('slideOverBackdrop').classList.remove('opacity-0');
        document.getElementById('slideOverBackdrop').classList.add('opacity-100');
        document.getElementById('slideOverPanel').classList.remove('translate-x-full');
    }, 10);
}

function closePanel() {
    document.getElementById('slideOverBackdrop').classList.remove('opacity-100');
    document.getElementById('slideOverBackdrop').classList.add('opacity-0');
    document.getElementById('slideOverPanel').classList.add('translate-x-full');

    // wait for transition to end before hiding
    setTimeout(() => {
        document.getElementById('slideOverWrapper').classList.add('hidden');
    }, 300);
}

// ----------------------------------------
// 匯出 CSV 報表
// ----------------------------------------
function exportToCsv() {
    // 取得當前專案資料
    const targetData = mockQuestionsDb.filter(q => q.project_id === currentProjId);

    if (targetData.length === 0) {
        Swal.fire({
            icon: 'info',
            title: '目前尚無資料可匯出',
            confirmButtonColor: 'var(--color-morandi)'
        });
        return;
    }

    // CSV 表頭
    let csvContent = "專案(梯次)代碼,試題編號,題型,等級,難易度,命題教師,當前所在階段(1~7),審核狀態,退回次數\n";

    // 填入資料列
    targetData.forEach(q => {
        const row = [
            q.project_id,
            q.id,
            qTypeMap[q.type] || q.type,
            q.level || '',
            diffMap[q.difficulty] || q.difficulty,
            teacherMap[q.author_id] || q.author_id,
            q.stage,
            statusMap[q.status] ? statusMap[q.status].label : q.status,
            q.returnCount
        ];

        // 為了避免內容有逗號破壞 csv 格式，用雙引號包起來
        const rowStr = row.map(item => `"${String(item).replace(/"/g, '""')}"`).join(',');
        csvContent += rowStr + "\n";
    });

    // 處理 BOM (讓 Excel 開啟不亂碼)
    const BOM = "\uFEFF";
    const blob = new Blob([BOM + csvContent], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);

    // 建立一個隱藏的 <a> 標籤觸發下載
    const a = document.createElement('a');
    a.href = url;
    a.download = `CWT_試題報表_${currentProjId}_${new Date().getTime()}.csv`;
    a.style.display = 'none';

    document.body.appendChild(a);
    a.click();

    // 清理資源
    document.body.removeChild(a);
    URL.revokeObjectURL(url);

    // 提示成功
    Swal.fire({
        icon: 'success',
        title: '匯出成功',
        text: '報表已下載至您的電腦',
        timer: 1500,
        showConfirmButton: false
    });
}



