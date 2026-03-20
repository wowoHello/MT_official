/**
 * CWT Review Module (審題任務)
 * 負責審題委員的三審管控：審題作業區、審核結果與歷史。
 * 包含互審/專審/總審三階段決策機制、Quill 富文本意見輸入、罐頭訊息。
 * Version: 1.0 (DEMO)
 *
 * [Blazor Migration Note]
 * - Mock 資料需替換為 API 呼叫
 * - Quill 編輯器需評估 Blazor 相容方案 (Blazored.TextEditor or JS Interop)
 * - localStorage 操作替換為 Server Session
 * - 審題分配邏輯由後端處理（迴避規則、配額分配）
 */

// ===================================================================
// 常數定義
// ===================================================================

/** 題型中文對應 */
const qTypeMap = {
    'single': '一般單選題', 'select': '精選單選題', 'readGroup': '閱讀題組',
    'longText': '長文題目', 'shortGroup': '短文題組', 'listen': '聽力測驗', 'listenGroup': '聽力題組'
};

/** 題型圖示 */
const qTypeIcon = {
    'single': 'fa-solid fa-circle-dot', 'select': 'fa-solid fa-star',
    'readGroup': 'fa-solid fa-book-open', 'longText': 'fa-solid fa-file-lines',
    'shortGroup': 'fa-solid fa-layer-group', 'listen': 'fa-solid fa-headphones',
    'listenGroup': 'fa-solid fa-headphones'
};

/** 難易度中文 */
const diffMap = { 'easy': '易', 'medium': '中', 'hard': '難' };

/** 審查階段定義 */
const reviewStageMap = {
    'peer': { label: '互審', color: 'bg-blue-100 text-blue-700', border: 'border-blue-200', icon: 'fa-solid fa-people-arrows' },
    'expert': { label: '專審', color: 'bg-purple-100 text-purple-700', border: 'border-purple-200', icon: 'fa-solid fa-user-tie' },
    'final': { label: '總審', color: 'bg-red-100 text-red-700', border: 'border-red-200', icon: 'fa-solid fa-crown' }
};

/** 審查決策類型 */
const decisionMap = {
    'comment': { label: '已給意見', color: 'bg-blue-50 text-blue-600' },
    'adopt': { label: '採用', color: 'bg-emerald-100 text-emerald-700' },
    'revise': { label: '改後再審', color: 'bg-amber-100 text-amber-700' },
    'reject': { label: '不採用', color: 'bg-gray-200 text-gray-500' }
};

/** 審題作業區狀態篩選 */
const reviewTabStatusOptions = [
    { value: 'all', label: '所有狀態' },
    { value: 'peer_pending', label: '互審待審' },
    { value: 'expert_pending', label: '專審待審' },
    { value: 'final_pending', label: '總審待審' },
    { value: 'decided', label: '已決策' }
];

/** 歷史 Tab 狀態篩選 */
const historyTabStatusOptions = [
    { value: 'all', label: '所有狀態' },
    { value: 'adopted', label: '已採用' },
    { value: 'rejected', label: '不採用' }
];

/** 罐頭訊息列表 */
const cannedMessages = [
    '題目設計清晰，符合等級標準。',
    '建議調整選項鑑別度，增加迷惑選項難度。',
    '解析內容過於簡略，建議補充說明。',
    '題幹語意模糊，建議修正敘述方式。',
    '正確答案可能有爭議，請確認並補充理據。',
    '選項排列建議依照邏輯或筆畫順序。',
    '文章篇幅適中，與子題配合良好。'
];

const defaultPageSize = 12;

/** 教師名稱庫 */
const teacherNames = {
    'T1001': '劉雅婷', 'T1002': '王健明', 'T1003': '張心怡', 'T1004': '吳家豪',
    'T1005': '陳美玲', 'T1006': '林志偉', 'C2001': '李教授', 'C2002': '陳副教授',
    'S3001': '林總召', 'S3002': '許編輯'
};

/**
 * 模擬當前梯次的七階段時程（DEMO 用）
 * 未來搬家到 Blazor 後，此資料由後端 API 的專案管理模組提供
 */
const projectStageTimeline = [
    { key: 'proposing', label: '命題', icon: 'fa-solid fa-pen-nib', start: '2026-02-01', end: '2026-02-28' },
    { key: 'peerReview', label: '互審', icon: 'fa-solid fa-people-arrows', start: '2026-03-01', end: '2026-03-10' },
    { key: 'peerEdit', label: '互修', icon: 'fa-solid fa-pen-to-square', start: '2026-03-11', end: '2026-03-17' },
    { key: 'expertReview', label: '專審', icon: 'fa-solid fa-user-tie', start: '2026-03-18', end: '2026-03-27' },
    { key: 'expertEdit', label: '專修', icon: 'fa-solid fa-pen-to-square', start: '2026-03-28', end: '2026-04-03' },
    { key: 'finalReview', label: '總審', icon: 'fa-solid fa-crown', start: '2026-04-04', end: '2026-04-13' },
    { key: 'finalEdit', label: '總修', icon: 'fa-solid fa-pen-to-square', start: '2026-04-14', end: '2026-04-20' }
];

/**
 * 根據當前日期判定各階段的狀態（done / active / upcoming）
 * 同時回傳當前進行中的階段索引與剩餘天數
 */
const getCurrentStageInfo = () => {
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    let activeIndex = -1;
    let daysRemaining = 0;

    const stages = projectStageTimeline.map((stage, i) => {
        const start = new Date(stage.start + 'T00:00:00');
        const end = new Date(stage.end + 'T23:59:59');
        let state = 'upcoming';

        if (today > end) {
            state = 'done';
        } else if (today >= start && today <= end) {
            state = 'active';
            activeIndex = i;
            // 計算剩餘天數（含當天）
            const endDate = new Date(stage.end + 'T00:00:00');
            daysRemaining = Math.ceil((endDate - today) / (1000 * 60 * 60 * 24));
        }

        return { ...stage, state };
    });

    // 若所有階段都已結束，標記最後一個為 active（展示用）
    if (activeIndex === -1 && stages.every(s => s.state === 'done')) {
        activeIndex = stages.length - 1;
    }

    return { stages, activeIndex, daysRemaining };
};


// ===================================================================
// Mock 資料 — 分配給當前審題人員的題目
// ===================================================================

let reviewQuestionsDb = [
    // ===================== 互審待審 =====================
    {
        id: 'Q-2602-R001', projectId: 'P2026-01', type: 'single', level: '中級', difficulty: 'medium',
        stem: '下列何者是「形聲字」？',
        options: [
            { label: 'A', text: '日' }, { label: 'B', text: '江' },
            { label: 'C', text: '上' }, { label: 'D', text: '本' }
        ],
        answer: 'B', analysis: '「江」由「水（氵）」為義符、「工」為聲符組成，屬於形聲字。',
        passage: '', subQuestions: [],
        authorId: 'T1002', authorName: '王健明',
        reviewStage: 'peer', reviewStatus: 'pending', myDecision: null, myComment: '',
        finalResult: null,
        history: [
            { time: '2026-02-20 10:00', user: '王健明', action: '建立草稿', comment: '' },
            { time: '2026-02-25 14:00', user: '王健明', action: '命題完成', comment: '' },
            { time: '2026-02-26 09:00', user: '王健明', action: '送審', comment: '' }
        ]
    },
    {
        id: 'Q-2602-R002', projectId: 'P2026-01', type: 'single', level: '初級', difficulty: 'easy',
        stem: '「他跑得很＿＿」，空格中應填入下列哪個詞？',
        options: [
            { label: 'A', text: '慢' }, { label: 'B', text: '快' },
            { label: 'C', text: '大' }, { label: 'D', text: '高' }
        ],
        answer: 'B', analysis: '根據語境，「跑得很快」符合語法邏輯。',
        passage: '', subQuestions: [],
        authorId: 'T1003', authorName: '張心怡',
        reviewStage: 'peer', reviewStatus: 'pending', myDecision: null, myComment: '',
        finalResult: null,
        history: [
            { time: '2026-02-22 11:00', user: '張心怡', action: '建立草稿', comment: '' },
            { time: '2026-02-27 09:00', user: '張心怡', action: '送審', comment: '' }
        ]
    },
    {
        id: 'Q-2602-R003', projectId: 'P2026-01', type: 'select', level: '中高級', difficulty: 'hard',
        stem: '下列哪一組詞語全部都是「雙聲聯綿詞」？',
        options: [
            { label: 'A', text: '參差、伶俐' }, { label: 'B', text: '蝴蝶、徘徊' },
            { label: 'C', text: '玲瓏、蜻蜓' }, { label: 'D', text: '匍匐、崎嶇' }
        ],
        answer: 'A', analysis: '「參差」（ㄘ-ㄘ）、「伶俐」（ㄌ-ㄌ）皆為雙聲聯綿詞。B 含疊韻，C、D 各有混合。',
        passage: '', subQuestions: [],
        authorId: 'T1004', authorName: '吳家豪',
        reviewStage: 'peer', reviewStatus: 'pending', myDecision: null, myComment: '',
        finalResult: null,
        history: [
            { time: '2026-02-23 10:00', user: '吳家豪', action: '建立草稿', comment: '' },
            { time: '2026-02-28 15:00', user: '吳家豪', action: '送審', comment: '' }
        ]
    },
    {
        id: 'Q-2602-R004', projectId: 'P2026-01', type: 'readGroup', level: '高級', difficulty: 'hard',
        stem: '〈蘭亭集序〉節選',
        passage: '永和九年，歲在癸丑，暮春之初，會於會稽山陰之蘭亭，修禊事也。群賢畢至，少長咸集。此地有崇山峻嶺，茂林修竹，又有清流激湍，映帶左右。',
        subQuestions: [
            {
                stem: '下列何者最能說明「修禊事」的目的？',
                options: [
                    { label: 'A', text: '祈求豐收' }, { label: 'B', text: '驅除不祥' },
                    { label: 'C', text: '紀念先人' }, { label: 'D', text: '迎接新年' }
                ],
                answer: 'B'
            },
            {
                stem: '「群賢畢至，少長咸集」中「咸」字的意思是？',
                options: [
                    { label: 'A', text: '全部' }, { label: 'B', text: '鹹味' },
                    { label: 'C', text: '減少' }, { label: 'D', text: '以前' }
                ],
                answer: 'A'
            }
        ],
        options: [], answer: '', analysis: '',
        authorId: 'T1002', authorName: '王健明',
        reviewStage: 'peer', reviewStatus: 'pending', myDecision: null, myComment: '',
        finalResult: null,
        history: [
            { time: '2026-02-24 09:00', user: '王健明', action: '建立草稿', comment: '' },
            { time: '2026-03-01 10:00', user: '王健明', action: '送審', comment: '' }
        ]
    },
    {
        id: 'Q-2602-R005', projectId: 'P2026-01', type: 'listen', level: '難度二', difficulty: 'medium',
        stem: '請聽一段廣播，回答下列問題：說話者建議聽眾做什麼？',
        options: [
            { label: 'A', text: '多喝水' }, { label: 'B', text: '早點睡' },
            { label: 'C', text: '多運動' }, { label: 'D', text: '少吃糖' }
        ],
        answer: 'C', analysis: '廣播中提到「每天運動三十分鐘」，故答案為 C。',
        passage: '', subQuestions: [],
        audioUrl: 'demo_audio_review_001.mp3',
        attributes: { audioType: '陳述', material: '生活' },
        authorId: 'T1005', authorName: '陳美玲',
        reviewStage: 'peer', reviewStatus: 'pending', myDecision: null, myComment: '',
        finalResult: null,
        history: [
            { time: '2026-02-25 14:00', user: '陳美玲', action: '建立草稿', comment: '' },
            { time: '2026-03-02 09:00', user: '陳美玲', action: '送審', comment: '' }
        ]
    },

    // ===================== 互審已決策 =====================
    {
        id: 'Q-2602-R006', projectId: 'P2026-01', type: 'single', level: '中級', difficulty: 'easy',
        stem: '下列何者是「量詞」的正確使用？',
        options: [
            { label: 'A', text: '一匹布' }, { label: 'B', text: '一匹書' },
            { label: 'C', text: '一匹花' }, { label: 'D', text: '一匹水' }
        ],
        answer: 'A', analysis: '「匹」用於布料為正確量詞搭配。',
        passage: '', subQuestions: [],
        authorId: 'T1004', authorName: '吳家豪',
        reviewStage: 'peer', reviewStatus: 'decided', myDecision: 'comment',
        myComment: '題目設計清晰，量詞搭配的選項具有鑑別度。建議解析可再補充其他選項為何錯誤。',
        finalResult: null,
        history: [
            { time: '2026-02-21 10:00', user: '吳家豪', action: '建立草稿', comment: '' },
            { time: '2026-02-26 09:00', user: '吳家豪', action: '送審', comment: '' },
            { time: '2026-03-05 11:00', user: '劉雅婷', action: '互審意見', comment: '題目設計清晰，量詞搭配的選項具有鑑別度。建議解析可再補充其他選項為何錯誤。' }
        ]
    },
    {
        id: 'Q-2602-R007', projectId: 'P2026-01', type: 'select', level: '中級', difficulty: 'medium',
        stem: '下列何者「不是」疊韻詞？',
        options: [
            { label: 'A', text: '徘徊' }, { label: 'B', text: '從容' },
            { label: 'C', text: '參差' }, { label: 'D', text: '窈窕' }
        ],
        answer: 'C', analysis: '「參差」為雙聲詞而非疊韻詞。',
        passage: '', subQuestions: [],
        authorId: 'T1003', authorName: '張心怡',
        reviewStage: 'peer', reviewStatus: 'decided', myDecision: 'comment',
        myComment: '建議在解析中加入每個選項的韻母分析，讓學生更容易理解雙聲與疊韻的差異。',
        finalResult: null,
        history: [
            { time: '2026-02-22 09:00', user: '張心怡', action: '建立草稿', comment: '' },
            { time: '2026-02-27 14:00', user: '張心怡', action: '送審', comment: '' },
            { time: '2026-03-06 10:00', user: '劉雅婷', action: '互審意見', comment: '建議在解析中加入韻母分析。' }
        ]
    },
    {
        id: 'Q-2602-R008', projectId: 'P2026-01', type: 'longText', level: '中高級', difficulty: 'medium',
        stem: '我心目中的好老師',
        passage: '請以「我心目中的好老師」為題，撰寫一篇作文。需舉出至少一位你印象深刻的老師，說明這位老師對你的影響，以及你認為好老師應該具備哪些特質。字數以 500 至 700 字為原則。',
        options: [], answer: '',
        analysis: '本題著重檢測學生的敘事能力與議論能力，需能結合個人經驗提出具體事例，並歸納好老師的特質。',
        subQuestions: [],
        attributes: { mode: '引導寫作' },
        authorId: 'T1006', authorName: '林志偉',
        reviewStage: 'peer', reviewStatus: 'decided', myDecision: 'comment',
        myComment: '題目方向明確，引導語設計佳。建議批閱說明可再補充「段落組織」的評分要點。',
        finalResult: null,
        history: [
            { time: '2026-02-23 11:00', user: '林志偉', action: '建立草稿', comment: '' },
            { time: '2026-02-28 16:00', user: '林志偉', action: '送審', comment: '' },
            { time: '2026-03-07 09:00', user: '劉雅婷', action: '互審意見', comment: '題目方向明確，引導語設計佳。' }
        ]
    },

    // ===================== 專審待審 =====================
    {
        id: 'Q-2602-R009', projectId: 'P2026-01', type: 'single', level: '中級', difficulty: 'medium',
        stem: '「不恥下問」的「恥」字在此句中的意思是？',
        options: [
            { label: 'A', text: '感到丟臉' }, { label: 'B', text: '認為可恥' },
            { label: 'C', text: '害怕困難' }, { label: 'D', text: '不願嘗試' }
        ],
        answer: 'B', analysis: '「恥」在此為意動用法，意為「以……為恥」，整句意為不以向學識不如自己的人請教為恥辱。',
        passage: '', subQuestions: [],
        authorId: 'T1003', authorName: '張心怡',
        reviewStage: 'expert', reviewStatus: 'pending', myDecision: null, myComment: '',
        finalResult: null,
        history: [
            { time: '2026-02-18 10:00', user: '張心怡', action: '建立草稿', comment: '' },
            { time: '2026-02-22 09:00', user: '張心怡', action: '送審', comment: '' },
            { time: '2026-02-28 10:00', user: '吳家豪', action: '互審意見', comment: '題目品質佳。' },
            { time: '2026-03-02 14:00', user: '張心怡', action: '修題完成', comment: '已微調選項用語。' }
        ]
    },
    {
        id: 'Q-2602-R010', projectId: 'P2026-01', type: 'select', level: '高級', difficulty: 'hard',
        stem: '下列何者使用了「借代」修辭？',
        options: [
            { label: 'A', text: '朱門酒肉臭，路有凍死骨' }, { label: 'B', text: '白髮三千丈，緣愁似箇長' },
            { label: 'C', text: '春蠶到死絲方盡' }, { label: 'D', text: '千山鳥飛絕，萬徑人蹤滅' }
        ],
        answer: 'A', analysis: '「朱門」借代富貴人家，「凍死骨」借代窮苦百姓。B 為誇飾，C 為雙關，D 為對偶。',
        passage: '', subQuestions: [],
        authorId: 'T1004', authorName: '吳家豪',
        reviewStage: 'expert', reviewStatus: 'pending', myDecision: null, myComment: '',
        finalResult: null,
        history: [
            { time: '2026-02-19 09:00', user: '吳家豪', action: '建立草稿', comment: '' },
            { time: '2026-02-24 14:00', user: '吳家豪', action: '送審', comment: '' },
            { time: '2026-03-01 10:00', user: '張心怡', action: '互審意見', comment: '精選題設計精良。' },
            { time: '2026-03-03 09:00', user: '吳家豪', action: '修題完成', comment: '' }
        ]
    },
    {
        id: 'Q-2602-R011', projectId: 'P2026-01', type: 'shortGroup', level: '高級', difficulty: 'hard',
        stem: '秋日散記',
        passage: '秋風拂過稻田，金黃色的稻穗低垂著頭，像是在向大地致敬。田埂上，一位老農望著豐收的景象，臉上露出滿足的微笑。遠處山頭已被楓紅染上一層暖意，炊煙從村落裊裊升起。這是農忙後難得的靜謐時光。',
        subQuestions: [
            {
                stem: '本文主要透過哪些感官描寫來呈現秋天的氛圍？請舉例說明。',
                dimension: '條列敘述',
                indicator: '1-1 條列敘述人、事、物特徵與特質',
                analysis: '學生需辨識視覺（金黃稻穗、楓紅）、觸覺（秋風）等感官描寫，並條列說明各自呈現的秋意。'
            },
            {
                stem: '作者寫「稻穗低垂著頭，像是在向大地致敬」運用了什麼修辭手法？其效果為何？',
                dimension: '分析推理',
                indicator: '3-7 推測寫作手法的目的',
                analysis: '學生需指出擬人修辭，並分析此修辭讓稻穗具有感恩、謙遜等人性特質，使畫面更溫馨。'
            }
        ],
        options: [], answer: '', analysis: '',
        attributes: { mainCategory: '文義判讀', subCategory: '篇章辨析', genre: '語體文' },
        authorId: 'T1002', authorName: '王健明',
        reviewStage: 'expert', reviewStatus: 'pending', myDecision: null, myComment: '',
        finalResult: null,
        history: [
            { time: '2026-02-20 11:00', user: '王健明', action: '建立草稿', comment: '' },
            { time: '2026-02-25 09:00', user: '王健明', action: '送審', comment: '' },
            { time: '2026-03-02 10:00', user: '陳美玲', action: '互審意見', comment: '短文題組佳。' },
            { time: '2026-03-04 14:00', user: '王健明', action: '修題完成', comment: '' }
        ]
    },
    {
        id: 'Q-2602-R012', projectId: 'P2026-01', type: 'listenGroup', level: '難度三', difficulty: 'hard',
        stem: '',
        passage: '請聆聽一段關於校園永續發展的討論。兩位學生就「校園是否應全面禁用一次性餐具」展開辯論，分別提出贊成與反對的理由。',
        audioUrl: 'demo_audio_review_group.mp3',
        subQuestions: [
            {
                stem: '贊成方認為禁用一次性餐具最主要的好處是什麼？',
                options: [
                    { label: 'A', text: '節省學校預算' }, { label: 'B', text: '減少垃圾量' },
                    { label: 'C', text: '提高學生成績' }, { label: 'D', text: '增加校園美感' }
                ],
                answer: 'B',
                level: '難度三', competency: '推斷訊息', indicator: '推斷訊息邏輯性'
            },
            {
                stem: '反對方提出最主要的顧慮是什麼？',
                options: [
                    { label: 'A', text: '清洗費用太高' }, { label: 'B', text: '衛生難以保證' },
                    { label: 'C', text: '學生不習慣' }, { label: 'D', text: '老師反對' }
                ],
                answer: 'B',
                level: '難度四', competency: '歸納分析', indicator: '歸納或總結訊息內容'
            }
        ],
        options: [], answer: '', analysis: '',
        attributes: { audioType: '對話', material: '教育' },
        authorId: 'T1005', authorName: '陳美玲',
        reviewStage: 'expert', reviewStatus: 'pending', myDecision: null, myComment: '',
        finalResult: null,
        history: [
            { time: '2026-02-21 15:00', user: '陳美玲', action: '建立草稿', comment: '' },
            { time: '2026-02-26 10:00', user: '陳美玲', action: '送審', comment: '' },
            { time: '2026-03-03 14:00', user: '王健明', action: '互審意見', comment: '聽力題組設計佳。' },
            { time: '2026-03-05 09:00', user: '陳美玲', action: '修題完成', comment: '' }
        ]
    },

    // ===================== 專審已決策 =====================
    {
        id: 'Q-2602-R013', projectId: 'P2026-01', type: 'single', level: '中級', difficulty: 'easy',
        stem: '「畫蛇添足」這個成語比喻什麼？',
        options: [
            { label: 'A', text: '做多餘的事反而壞事' }, { label: 'B', text: '做事要有創意' },
            { label: 'C', text: '畫畫要認真' }, { label: 'D', text: '蛇是可怕的動物' }
        ],
        answer: 'A', analysis: '此成語出自《戰國策》，比喻做了多餘的事反而把事情弄壞。',
        passage: '', subQuestions: [],
        authorId: 'T1006', authorName: '林志偉',
        reviewStage: 'expert', reviewStatus: 'decided', myDecision: 'adopt',
        myComment: '題目設計符合等級，解析清楚，予以採用。',
        finalResult: null,
        history: [
            { time: '2026-02-17 10:00', user: '林志偉', action: '建立草稿', comment: '' },
            { time: '2026-02-22 14:00', user: '林志偉', action: '送審', comment: '' },
            { time: '2026-02-28 10:00', user: '吳家豪', action: '互審意見', comment: '題目簡潔明瞭。' },
            { time: '2026-03-04 15:00', user: '劉雅婷', action: '專審決策 (採用)', comment: '題目設計符合等級，解析清楚。' }
        ]
    },
    {
        id: 'Q-2602-R014', projectId: 'P2026-01', type: 'single', level: '中高級', difficulty: 'medium',
        stem: '下列何者的「之」字用法為「代詞」？',
        options: [
            { label: 'A', text: '學而時習之' }, { label: 'B', text: '赤子之心' },
            { label: 'C', text: '之乎者也' }, { label: 'D', text: '久而久之' }
        ],
        answer: 'A', analysis: '「學而時習之」的「之」代替前面學過的內容，為代詞用法。B 為「的」，C 為語助詞，D 為句尾助詞。',
        passage: '', subQuestions: [],
        authorId: 'T1003', authorName: '張心怡',
        reviewStage: 'expert', reviewStatus: 'decided', myDecision: 'revise',
        myComment: '選項 C「之乎者也」建議替換為更有語境的例句，目前過於口語化不夠正式。另外解析中「B 為的」表述可更精確。',
        finalResult: null,
        history: [
            { time: '2026-02-18 11:00', user: '張心怡', action: '建立草稿', comment: '' },
            { time: '2026-02-23 09:00', user: '張心怡', action: '送審', comment: '' },
            { time: '2026-03-01 10:00', user: '陳美玲', action: '互審意見', comment: '虛詞辨析題設計佳。' },
            { time: '2026-03-05 16:00', user: '劉雅婷', action: '專審決策 (改後再審)', comment: '選項 C 建議替換。' }
        ]
    },

    // ===================== 總審待審 =====================
    {
        id: 'Q-2602-R015', projectId: 'P2026-01', type: 'single', level: '初級', difficulty: 'easy',
        stem: '「恭喜發財」中的「恭」字，下列何者是正確的注音？',
        options: [
            { label: 'A', text: 'ㄍㄨㄥ' }, { label: 'B', text: 'ㄍㄨㄥˇ' },
            { label: 'C', text: 'ㄍㄨㄥˋ' }, { label: 'D', text: 'ㄎㄨㄥ' }
        ],
        answer: 'A', analysis: '「恭」字的正確注音為一聲「ㄍㄨㄥ」。',
        passage: '', subQuestions: [],
        authorId: 'T1004', authorName: '吳家豪',
        reviewStage: 'final', reviewStatus: 'pending', myDecision: null, myComment: '',
        finalResult: null,
        history: [
            { time: '2026-02-15 10:00', user: '吳家豪', action: '建立草稿', comment: '' },
            { time: '2026-02-19 09:00', user: '吳家豪', action: '送審', comment: '' },
            { time: '2026-02-24 10:00', user: '張心怡', action: '互審意見', comment: '初級注音題設計得宜。' },
            { time: '2026-02-28 14:00', user: '李教授', action: '專審決策 (採用)', comment: '符合初級水準。' }
        ]
    },
    {
        id: 'Q-2602-R016', projectId: 'P2026-01', type: 'select', level: '中高級', difficulty: 'hard',
        stem: '下列何者使用了「映襯」修辭？',
        options: [
            { label: 'A', text: '親賢臣，遠小人' }, { label: 'B', text: '白髮三千丈' },
            { label: 'C', text: '春蠶到死絲方盡' }, { label: 'D', text: '明月幾時有' }
        ],
        answer: 'A', analysis: '「親賢臣」與「遠小人」為反襯手法，透過對比突顯主張。B 為誇飾，C 為雙關，D 為設問。',
        passage: '', subQuestions: [],
        authorId: 'T1002', authorName: '王健明',
        reviewStage: 'final', reviewStatus: 'pending', myDecision: null, myComment: '',
        finalResult: null,
        history: [
            { time: '2026-02-16 11:00', user: '王健明', action: '建立草稿', comment: '' },
            { time: '2026-02-20 09:00', user: '王健明', action: '送審', comment: '' },
            { time: '2026-02-25 10:00', user: '陳美玲', action: '互審意見', comment: '精選題目品質佳。' },
            { time: '2026-03-01 14:00', user: '陳副教授', action: '專審決策 (採用)', comment: '修辭分析精準。' }
        ]
    },
    {
        id: 'Q-2602-R017', projectId: 'P2026-01', type: 'readGroup', level: '中級', difficulty: 'medium',
        stem: '〈世說新語〉節選',
        passage: '徐孺子年九歲，嘗月下戲，人語之曰：「若令月中無物，當極明邪？」徐曰：「不然。譬如人眼中有瞳子，無此必不明。」',
        subQuestions: [
            {
                stem: '徐孺子用什麼來比喻月中之物？',
                options: [
                    { label: 'A', text: '星星' }, { label: 'B', text: '眼睛中的瞳孔' },
                    { label: 'C', text: '太陽' }, { label: 'D', text: '蠟燭的火焰' }
                ],
                answer: 'B'
            },
            {
                stem: '本文主要表現徐孺子什麼特質？',
                options: [
                    { label: 'A', text: '勤奮好學' }, { label: 'B', text: '聰明機智' },
                    { label: 'C', text: '謙虛有禮' }, { label: 'D', text: '膽大心細' }
                ],
                answer: 'B'
            }
        ],
        options: [], answer: '', analysis: '',
        authorId: 'T1005', authorName: '陳美玲',
        reviewStage: 'final', reviewStatus: 'pending', myDecision: null, myComment: '',
        finalResult: null,
        history: [
            { time: '2026-02-17 09:00', user: '陳美玲', action: '建立草稿', comment: '' },
            { time: '2026-02-21 14:00', user: '陳美玲', action: '送審', comment: '' },
            { time: '2026-02-26 10:00', user: '王健明', action: '互審意見', comment: '古文選材適當。' },
            { time: '2026-03-02 16:00', user: '李教授', action: '專審決策 (採用)', comment: '閱讀題組設計良好。' }
        ]
    },

    // ===================== 總審已決策 =====================
    {
        id: 'Q-2602-R018', projectId: 'P2026-01', type: 'single', level: '中級', difficulty: 'medium',
        stem: '「塞翁失馬，焉知非福」的寓意為何？',
        options: [
            { label: 'A', text: '養馬很危險' }, { label: 'B', text: '禍福相倚，不可預知' },
            { label: 'C', text: '老人很有智慧' }, { label: 'D', text: '馬是重要財產' }
        ],
        answer: 'B', analysis: '此成語出自《淮南子》，說明禍與福之間是相互轉化的，不能只看眼前。',
        passage: '', subQuestions: [],
        authorId: 'T1006', authorName: '林志偉',
        reviewStage: 'final', reviewStatus: 'decided', myDecision: 'adopt',
        myComment: '題目設計符合等級標準，成語典故的考查方向正確，核准入庫。',
        finalResult: 'adopted',
        history: [
            { time: '2026-02-14 10:00', user: '林志偉', action: '建立草稿', comment: '' },
            { time: '2026-02-18 14:00', user: '林志偉', action: '送審', comment: '' },
            { time: '2026-02-23 10:00', user: '王健明', action: '互審意見', comment: '佳。' },
            { time: '2026-02-28 14:00', user: '李教授', action: '專審決策 (採用)', comment: '合格。' },
            { time: '2026-03-06 10:00', user: '劉雅婷', action: '總召決策 (採用)', comment: '核准入庫。' }
        ]
    },

    // ===================== 審核結果與歷史 — 採用 =====================
    {
        id: 'Q-2602-R019', projectId: 'P2026-01', type: 'single', level: '初級', difficulty: 'easy',
        stem: '下列何者是「水果」？',
        options: [
            { label: 'A', text: '蘋果' }, { label: 'B', text: '白菜' },
            { label: 'C', text: '米飯' }, { label: 'D', text: '麵包' }
        ],
        answer: 'A', analysis: '蘋果是水果類，其餘為蔬菜或主食。',
        passage: '', subQuestions: [],
        authorId: 'T1003', authorName: '張心怡',
        reviewStage: 'final', reviewStatus: 'decided', myDecision: 'adopt',
        myComment: '初級題目設計適當，核准。',
        finalResult: 'adopted',
        history: [
            { time: '2026-02-10 10:00', user: '張心怡', action: '建立草稿', comment: '' },
            { time: '2026-02-14 09:00', user: '張心怡', action: '送審', comment: '' },
            { time: '2026-02-19 10:00', user: '吳家豪', action: '互審意見', comment: '太簡單了吧。' },
            { time: '2026-02-24 14:00', user: '陳副教授', action: '專審決策 (採用)', comment: '初級水準適合。' },
            { time: '2026-03-02 10:00', user: '劉雅婷', action: '總召決策 (採用)', comment: '核准入庫。' }
        ]
    },
    {
        id: 'Q-2602-R020', projectId: 'P2026-01', type: 'select', level: '高級', difficulty: 'hard',
        stem: '下列詩句，何者屬於「送別」主題？',
        options: [
            { label: 'A', text: '海內存知己，天涯若比鄰' }, { label: 'B', text: '床前明月光' },
            { label: 'C', text: '鋤禾日當午' }, { label: 'D', text: '碧玉妝成一樹高' }
        ],
        answer: 'A', analysis: '「海內存知己，天涯若比鄰」出自王勃〈送杜少府之任蜀州〉，為著名送別詩。',
        passage: '', subQuestions: [],
        authorId: 'T1004', authorName: '吳家豪',
        reviewStage: 'final', reviewStatus: 'decided', myDecision: 'adopt',
        myComment: '精選題設計優良，選項涵蓋不同主題，鑑別度高。核准。',
        finalResult: 'adopted',
        history: [
            { time: '2026-02-11 11:00', user: '吳家豪', action: '建立草稿', comment: '' },
            { time: '2026-02-15 09:00', user: '吳家豪', action: '送審', comment: '' },
            { time: '2026-02-20 10:00', user: '張心怡', action: '互審意見', comment: '精選題佳。' },
            { time: '2026-02-25 14:00', user: '李教授', action: '專審決策 (採用)', comment: '鑑別度高。' },
            { time: '2026-03-03 10:00', user: '劉雅婷', action: '總召決策 (採用)', comment: '核准入庫。' }
        ]
    },
    {
        id: 'Q-2602-R021', projectId: 'P2026-01', type: 'readGroup', level: '中高級', difficulty: 'medium',
        stem: '〈陋室銘〉節選',
        passage: '山不在高，有仙則名。水不在深，有龍則靈。斯是陋室，惟吾德馨。',
        subQuestions: [
            {
                stem: '作者用「山」和「水」來類比什麼？',
                options: [
                    { label: 'A', text: '自然風景' }, { label: 'B', text: '居住的房屋' },
                    { label: 'C', text: '文學作品' }, { label: 'D', text: '歷史人物' }
                ],
                answer: 'B'
            },
            {
                stem: '「惟吾德馨」表達了作者什麼態度？',
                options: [
                    { label: 'A', text: '自卑' }, { label: 'B', text: '自信與安貧樂道' },
                    { label: 'C', text: '炫耀' }, { label: 'D', text: '懷疑' }
                ],
                answer: 'B'
            }
        ],
        options: [], answer: '', analysis: '',
        authorId: 'T1002', authorName: '王健明',
        reviewStage: 'final', reviewStatus: 'decided', myDecision: 'adopt',
        myComment: '閱讀題組選材經典，子題設計由表及裡，核准入庫。',
        finalResult: 'adopted',
        history: [
            { time: '2026-02-12 10:00', user: '王健明', action: '建立草稿', comment: '' },
            { time: '2026-02-16 09:00', user: '王健明', action: '送審', comment: '' },
            { time: '2026-02-21 10:00', user: '陳美玲', action: '互審意見', comment: '選材佳。' },
            { time: '2026-02-26 14:00', user: '陳副教授', action: '專審決策 (採用)', comment: '閱讀題組優秀。' },
            { time: '2026-03-04 10:00', user: '劉雅婷', action: '總召決策 (採用)', comment: '核准入庫。' }
        ]
    },

    // ===================== 審核結果與歷史 — 不採用 =====================
    {
        id: 'Q-2602-R022', projectId: 'P2026-01', type: 'single', level: '初級', difficulty: 'easy',
        stem: '1 + 1 = ?',
        options: [
            { label: 'A', text: '1' }, { label: 'B', text: '2' },
            { label: 'C', text: '3' }, { label: 'D', text: '4' }
        ],
        answer: 'B', analysis: '簡單加法。',
        passage: '', subQuestions: [],
        authorId: 'T1006', authorName: '林志偉',
        reviewStage: 'final', reviewStatus: 'decided', myDecision: 'reject',
        myComment: '此為數學題，不屬於國語文命題範疇，且鑑別度為零，不採用。',
        finalResult: 'rejected',
        history: [
            { time: '2026-02-13 10:00', user: '林志偉', action: '建立草稿', comment: '' },
            { time: '2026-02-17 09:00', user: '林志偉', action: '送審', comment: '' },
            { time: '2026-02-22 10:00', user: '王健明', action: '互審意見', comment: '這不是國語文題目……' },
            { time: '2026-02-27 14:00', user: '李教授', action: '專審決策 (改後再審)', comment: '題目不符命題範疇。' },
            { time: '2026-03-05 10:00', user: '劉雅婷', action: '總召決策 (不採用)', comment: '非國語文範疇。' }
        ]
    },
    {
        id: 'Q-2602-R023', projectId: 'P2026-01', type: 'single', level: '中級', difficulty: 'medium',
        stem: '台灣最高的山是哪一座？',
        options: [
            { label: 'A', text: '玉山' }, { label: 'B', text: '阿里山' },
            { label: 'C', text: '合歡山' }, { label: 'D', text: '雪山' }
        ],
        answer: 'A', analysis: '玉山為台灣最高峰，海拔 3952 公尺。',
        passage: '', subQuestions: [],
        authorId: 'T1005', authorName: '陳美玲',
        reviewStage: 'final', reviewStatus: 'decided', myDecision: 'reject',
        myComment: '此為地理常識題，不屬於國語文閱讀理解或語文知識範疇，建議改為地理科命題。不採用。',
        finalResult: 'rejected',
        history: [
            { time: '2026-02-14 11:00', user: '陳美玲', action: '建立草稿', comment: '' },
            { time: '2026-02-18 09:00', user: '陳美玲', action: '送審', comment: '' },
            { time: '2026-02-23 10:00', user: '吳家豪', action: '互審意見', comment: '似乎偏離語文範疇。' },
            { time: '2026-02-28 14:00', user: '陳副教授', action: '專審決策 (改後再審)', comment: '建議調整方向。' },
            { time: '2026-03-06 16:00', user: '劉雅婷', action: '總召決策 (不採用)', comment: '非語文範疇。' }
        ]
    }
];


// ===================================================================
// 工具函式
// ===================================================================

const stripHtml = (html) => {
    if (!html) return '';
    const tmp = document.createElement('div');
    tmp.innerHTML = html;
    return tmp.textContent || tmp.innerText || '';
};

const truncate = (str, len = 50) => (str && str.length > len) ? str.substring(0, len) + '…' : (str || '');

const getSearchText = (q) => {
    const parts = [q.id, q.stem, q.passage, q.analysis, q.authorName];
    (q.options || []).forEach(o => parts.push(o.text));
    (q.subQuestions || []).forEach(sq => {
        parts.push(sq.stem);
        (sq.options || []).forEach(o => parts.push(o.text));
    });
    return parts.filter(Boolean).map(s => stripHtml(s)).join(' ').toLowerCase();
};

const getStemPreview = (q) => {
    if (q.passage && (q.type === 'readGroup' || q.type === 'shortGroup' || q.type === 'listenGroup')) {
        return truncate(stripHtml(q.passage), 60);
    }
    return truncate(stripHtml(q.stem || ''), 60);
};


// ===================================================================
// 狀態管理
// ===================================================================

let currentTab = 'review';          // 'review' | 'history'
let filteredQuestions = [];
let currentPage = 1;
let pageSize = defaultPageSize;
let currentReviewQuestion = null;   // 目前正在審閱的題目
let reviewQuillInstance = null;     // Quill 實例


// ===================================================================
// 初始化
// ===================================================================

document.addEventListener('DOMContentLoaded', () => {
    const userStr = localStorage.getItem('cwt_user');
    if (userStr) {
        const user = JSON.parse(userStr);
        if (user.role !== 'ADMIN' && user.role !== 'TEACHER') {
            Swal.fire({
                icon: 'error', title: '權限不足',
                text: '「審題任務」需有審題相關權限。即將導回首頁。',
                showConfirmButton: false, timer: 2500
            }).then(() => { window.location.href = 'firstpage.html'; });
            return;
        }
    }

    initQuillEditor();
    initTabs();
    initFilters();
    initReviewModal();
    initCannedMessages();

    const projectId = localStorage.getItem('cwt_current_project') || 'P2026-01';
    loadPageData(projectId);

    // Deep Linking: 檢查 URL 參數切換頁籤 (US-009)
    const urlParams = new URLSearchParams(window.location.search);
    const tabParam = urlParams.get('tab');
    if (tabParam && ['review', 'history'].includes(tabParam)) {
        switchTab(tabParam);
    }

    document.addEventListener('projectChanged', (e) => {
        loadPageData(e.detail.id);
    });
});


// ===================================================================
// Quill 編輯器初始化
// ===================================================================

const initQuillEditor = () => {
    const container = document.getElementById('reviewQuillContainer');
    if (!container || reviewQuillInstance) return;

    // 註冊自訂字體
    const Font = Quill.import('formats/font');
    Font.whitelist = [false, 'kaiti', 'times-new-roman'];
    Quill.register(Font, true);

    reviewQuillInstance = new Quill(container, {
        theme: 'snow',
        placeholder: '請輸入審查意見...',
        modules: {
            toolbar: [
                [{ 'font': [false, 'kaiti', 'times-new-roman'] }],
                [{ 'size': ['small', false, 'large', 'huge'] }],
                ['bold', 'underline', 'strike'],
                [{ 'color': [] }, { 'background': [] }],
                [{ 'align': [] }],
                [{ 'list': 'ordered' }, { 'list': 'bullet' }],
                ['blockquote'],
                ['link'],
                ['clean']
            ]
        }
    });

    // 中文標點按鈕事件（含括弧配對插入邏輯）
    document.querySelectorAll('.punct-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const char = btn.getAttribute('data-char');
            const isPair = btn.hasAttribute('data-pair'); // 括弧類：「」『』（）
            if (char && reviewQuillInstance) {
                const range = reviewQuillInstance.getSelection(true);
                reviewQuillInstance.insertText(range.index, char);
                if (isPair) {
                    // 括弧配對：左右同時插入，游標自動移到中間
                    reviewQuillInstance.setSelection(range.index + 1);
                } else {
                    reviewQuillInstance.setSelection(range.index + char.length);
                }
            }
        });
    });
};


// ===================================================================
// 罐頭訊息
// ===================================================================

const initCannedMessages = () => {
    const container = document.getElementById('cannedMessagesContainer');
    if (!container) return;

    container.innerHTML = cannedMessages.map((msg, i) =>
        `<button type="button" class="canned-btn" data-index="${i}">${truncate(msg, 16)}</button>`
    ).join('');

    container.addEventListener('click', (e) => {
        const btn = e.target.closest('.canned-btn');
        if (!btn || !reviewQuillInstance) return;
        const idx = parseInt(btn.dataset.index);
        const msg = cannedMessages[idx];
        if (!msg) return;

        const length = reviewQuillInstance.getLength();
        const prefix = length > 1 ? '\n' : '';
        reviewQuillInstance.insertText(length - 1, prefix + msg);
        reviewQuillInstance.setSelection(reviewQuillInstance.getLength());
    });
};


// ===================================================================
// Tab 切換
// ===================================================================

const initTabs = () => {
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            switchTab(btn.getAttribute('data-tab'));
        });
    });
};

const switchTab = (tab) => {
    currentTab = tab;
    currentPage = 1;

    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.classList.remove('active-review', 'active-history');
        if (btn.getAttribute('data-tab') === tab) {
            btn.classList.add(tab === 'review' ? 'active-review' : 'active-history');
        }
    });

    renderTabContent();
};


// ===================================================================
// 篩選
// ===================================================================

const initFilters = () => {
    ['filterKeyword', 'filterType', 'filterLevel', 'filterStatus'].forEach(id => {
        const el = document.getElementById(id);
        if (el) {
            el.addEventListener(id === 'filterKeyword' ? 'input' : 'change', () => {
                currentPage = 1;
                renderTabContent();
            });
        }
    });

    const pageSizeSelect = document.getElementById('pageSizeSelect');
    if (pageSizeSelect) {
        pageSizeSelect.addEventListener('change', (e) => {
            pageSize = parseInt(e.target.value) || defaultPageSize;
            currentPage = 1;
            renderTabContent();
        });
    }
};

const renderStatusFilterOptions = () => {
    const statusSelect = document.getElementById('filterStatus');
    if (!statusSelect) return;

    const currentVal = statusSelect.value;
    const options = currentTab === 'review' ? reviewTabStatusOptions : historyTabStatusOptions;

    statusSelect.innerHTML = options.map(o => `<option value="${o.value}">${o.label}</option>`).join('');

    if (Array.from(statusSelect.options).some(o => o.value === currentVal)) {
        statusSelect.value = currentVal;
    } else {
        statusSelect.value = 'all';
    }
};

const getStatusFilterValue = () => document.getElementById('filterStatus')?.value || 'all';

const matchesStatusFilter = (q, statusVal) => {
    if (statusVal === 'all') return true;

    if (currentTab === 'review') {
        if (statusVal === 'peer_pending') return q.reviewStage === 'peer' && q.reviewStatus === 'pending';
        if (statusVal === 'expert_pending') return q.reviewStage === 'expert' && q.reviewStatus === 'pending';
        if (statusVal === 'final_pending') return q.reviewStage === 'final' && q.reviewStatus === 'pending';
        if (statusVal === 'decided') return q.reviewStatus === 'decided';
    } else {
        if (statusVal === 'adopted') return q.finalResult === 'adopted';
        if (statusVal === 'rejected') return q.finalResult === 'rejected';
    }
    return true;
};

const getFilteredQuestions = (questions) => {
    const keyword = (document.getElementById('filterKeyword')?.value || '').trim().toLowerCase();
    const typeVal = document.getElementById('filterType')?.value || 'all';
    const levelVal = document.getElementById('filterLevel')?.value || 'all';
    const statusVal = getStatusFilterValue();

    return questions.filter(q => {
        if (keyword && !getSearchText(q).includes(keyword)) return false;
        if (typeVal !== 'all' && q.type !== typeVal) return false;
        if (levelVal !== 'all' && q.level !== levelVal) return false;
        if (!matchesStatusFilter(q, statusVal)) return false;
        return true;
    });
};

const getFilteredQuestionsWithoutStatus = (questions) => {
    const keyword = (document.getElementById('filterKeyword')?.value || '').trim().toLowerCase();
    const typeVal = document.getElementById('filterType')?.value || 'all';
    const levelVal = document.getElementById('filterLevel')?.value || 'all';

    return questions.filter(q => {
        if (keyword && !getSearchText(q).includes(keyword)) return false;
        if (typeVal !== 'all' && q.type !== typeVal) return false;
        if (levelVal !== 'all' && q.level !== levelVal) return false;
        return true;
    });
};


// ===================================================================
// 排序
// ===================================================================

const sortQuestions = (questions) => {
    const stagePriority = { 'final': 0, 'expert': 1, 'peer': 2 };
    const statusPriority = { 'pending': 0, 'decided': 1 };

    return [...questions].sort((a, b) => {
        const aSP = statusPriority[a.reviewStatus] ?? 2;
        const bSP = statusPriority[b.reviewStatus] ?? 2;
        if (aSP !== bSP) return aSP - bSP;

        const aStage = stagePriority[a.reviewStage] ?? 3;
        const bStage = stagePriority[b.reviewStage] ?? 3;
        if (aStage !== bStage) return aStage - bStage;

        return (b.history?.[b.history.length - 1]?.time || '').localeCompare(
            a.history?.[a.history.length - 1]?.time || '');
    });
};


// ===================================================================
// 頁面資料載入與渲染
// ===================================================================

const loadPageData = (projectId) => {
    // 動態更新標題旁的階段指示燈
    updateStageIndicator();
    renderTabContent();
};

/**
 * 動態更新頁面標題旁的階段指示燈文字
 */
const updateStageIndicator = () => {
    const indicator = document.getElementById('stageIndicator');
    if (!indicator) return;

    const { stages, activeIndex, daysRemaining } = getCurrentStageInfo();
    if (activeIndex >= 0) {
        const active = stages[activeIndex];
        const suffix = active.state === 'done' ? '已完成' : `進行中`;
        indicator.innerHTML = `
            <span class="w-2 h-2 rounded-full bg-[var(--color-terracotta)] stage-active"></span>
            ${active.label}階段${suffix}
        `;
    }
};

const getCurrentQuestions = () => {
    const projectId = localStorage.getItem('cwt_current_project') || 'P2026-01';
    const allQ = reviewQuestionsDb.filter(q => q.projectId === projectId);

    if (currentTab === 'review') {
        return allQ.filter(q => !q.finalResult);
    }
    return allQ.filter(q => q.finalResult);
};

const renderTabContent = () => {
    const projectId = localStorage.getItem('cwt_current_project') || 'P2026-01';
    const allQ = reviewQuestionsDb.filter(q => q.projectId === projectId);

    const reviewQ = allQ.filter(q => !q.finalResult);
    const historyQ = allQ.filter(q => q.finalResult);

    document.getElementById('tabCountReview').textContent = reviewQ.length;
    document.getElementById('tabCountHistory').textContent = historyQ.length;

    renderStatusFilterOptions();

    const baseQuestions = currentTab === 'review' ? reviewQ : historyQ;
    const noStatusFiltered = getFilteredQuestionsWithoutStatus(baseQuestions);
    renderTabStats(noStatusFiltered);

    const pageSizeSelect = document.getElementById('pageSizeSelect');
    if (pageSizeSelect) pageSizeSelect.value = String(pageSize);

    filteredQuestions = getFilteredQuestions(baseQuestions);
    filteredQuestions = sortQuestions(filteredQuestions);

    const totalPages = Math.max(1, Math.ceil(filteredQuestions.length / pageSize));
    currentPage = Math.min(currentPage, totalPages);

    renderQuestionList();
};


// ===================================================================
// 統計卡片 / 時程燈號條
// ===================================================================

/** 歷史 Tab 的統計卡片（維持原設計） */
const getHistoryStatsCards = (questions) => [
    { value: 'all', title: '全部題目', count: questions.length, tone: 'slate' },
    { value: 'adopted', title: '已採用', count: questions.filter(q => q.finalResult === 'adopted').length, tone: 'emerald' },
    { value: 'rejected', title: '不採用', count: questions.filter(q => q.finalResult === 'rejected').length, tone: 'gray' }
];

const getStatsCardToneClass = (tone, isActive) => {
    const map = {
        slate: isActive ? 'border-slate-400 bg-slate-50' : 'border-gray-200 bg-white',
        emerald: isActive ? 'border-emerald-400 bg-emerald-50' : 'border-gray-200 bg-white',
        gray: isActive ? 'border-gray-400 bg-gray-50' : 'border-gray-200 bg-white'
    };
    return map[tone] || map.slate;
};

/**
 * 渲染審題作業區的七階段時程燈號條 + 統計摘要條
 */
const renderReviewTimeline = (container, questions) => {
    const { stages, activeIndex, daysRemaining } = getCurrentStageInfo();

    // 統計數據
    const totalCount = questions.length;
    const pendingCount = questions.filter(q => q.reviewStatus === 'pending').length;
    const decidedCount = questions.filter(q => q.reviewStatus === 'decided').length;

    // 當前階段資訊提示文字
    let stageHint = '';
    if (activeIndex >= 0) {
        const active = stages[activeIndex];
        if (active.state === 'active') {
            stageHint = `<i class="fa-solid fa-location-dot mr-1"></i> 目前階段：<strong>${active.label}</strong>（剩餘 <strong class="text-[var(--color-terracotta)]">${daysRemaining}</strong> 天）`;
        } else {
            stageHint = `<i class="fa-solid fa-circle-check mr-1 text-[var(--color-sage)]"></i> 所有審題階段已完成`;
        }
    }

    container.innerHTML = `
        <!-- 七階段時程燈號條 -->
        <div class="mb-4">
            <div class="stage-timeline relative" style="padding: 0 20px;">
                ${stages.map((s, i) => {
        const icon = s.state === 'done' ? 'fa-solid fa-check'
            : s.state === 'active' ? s.icon
                : 'fa-solid fa-ellipsis';
        // 連接線（前一個到當前之間）
        let lineHtml = '';
        if (i > 0) {
            const prevState = stages[i - 1].state;
            const lineClass = (prevState === 'done' && (s.state === 'done' || s.state === 'active')) ? 'done' : 'upcoming';
            lineHtml = `<div class="stage-line ${lineClass}" style="left: calc(-50% + 14px); right: calc(50% + 14px);"></div>`;
        }
        return `
                        <div class="stage-node">
                            ${lineHtml}
                            <div class="stage-dot ${s.state}" title="${s.label}：${s.start} ~ ${s.end}">
                                <i class="${icon} text-[11px]"></i>
                            </div>
                            <div class="stage-label ${s.state}">${s.label}</div>
                        </div>`;
    }).join('')}
            </div>
            <div class="text-center text-xs text-gray-500 mt-3 font-medium">
                ${stageHint}
            </div>
        </div>

        <!-- 統計摘要條 -->
        <div class="flex flex-wrap items-center justify-center gap-x-6 gap-y-2 bg-white rounded-xl border border-gray-200 px-5 py-3 text-sm font-medium shadow-sm">
            <div class="flex items-center gap-1.5 text-gray-700">
                <i class="fa-solid fa-list-check text-[var(--color-morandi)]"></i>
                本區題目
                <span class="text-lg font-bold text-[var(--color-slate-main)] ml-0.5">${totalCount}</span>
            </div>
            <span class="text-gray-300">|</span>
            <div class="flex items-center gap-1.5 text-gray-700">
                <i class="fa-solid fa-hourglass-half text-[var(--color-terracotta)]"></i>
                待處理
                <span class="text-lg font-bold text-[var(--color-terracotta)] ml-0.5">${pendingCount}</span>
            </div>
            <span class="text-gray-300">|</span>
            <div class="flex items-center gap-1.5 text-gray-700">
                <i class="fa-solid fa-circle-check text-[var(--color-sage)]"></i>
                已決策
                <span class="text-lg font-bold text-[var(--color-sage)] ml-0.5">${decidedCount}</span>
            </div>
        </div>
    `;
};

/**
 * 渲染歷史 Tab 的統計卡片（維持原設計）
 */
const renderHistoryCards = (container, questions) => {
    const cards = getHistoryStatsCards(questions);
    const activeFilter = getStatusFilterValue();

    container.innerHTML = `
        <div class="flex flex-wrap gap-3">
            ${cards.map(card => {
        const isActive = activeFilter === card.value || (card.value === 'all' && activeFilter === 'all');
        return `
                    <button type="button" data-status-filter="${card.value}" class="min-w-[150px] flex-1 cursor-pointer rounded-xl border px-4 py-3 text-left transition-colors ${getStatsCardToneClass(card.tone, isActive)} ${isActive ? 'shadow-sm' : ''}">
                        <div class="text-sm font-bold text-gray-700">${card.title}</div>
                        <div class="mt-3 text-3xl font-bold text-gray-900">${card.count}</div>
                        <div class="mt-2 text-xs ${isActive ? 'text-gray-600' : 'text-gray-400'}">${card.value === 'all' ? '顯示全部題目' : '點一下套用篩選'}</div>
                    </button>`;
    }).join('')}
        </div>`;

    // 卡片點擊篩選
    container.querySelectorAll('[data-status-filter]').forEach(btn => {
        btn.addEventListener('click', () => {
            const val = btn.dataset.statusFilter;
            const statusSelect = document.getElementById('filterStatus');
            if (statusSelect) {
                statusSelect.value = Array.from(statusSelect.options).some(o => o.value === val) ? val : 'all';
            }
            currentPage = 1;
            renderTabContent();
        });
    });
};

/**
 * 主要統計區渲染分發：依當前 Tab 決定渲染方式
 */
const renderTabStats = (questions) => {
    const container = document.getElementById('tabStatsContainer');
    if (!container) return;

    if (currentTab === 'review') {
        renderReviewTimeline(container, questions);
    } else {
        renderHistoryCards(container, questions);
    }
};


// ===================================================================
// 題目列表渲染
// ===================================================================

const renderQuestionList = () => {
    const container = document.getElementById('questionListContainer');
    const emptyState = document.getElementById('emptyState');
    const emptyText = document.getElementById('emptyStateText');
    const paginationEl = document.getElementById('listPagination');
    const listCount = document.getElementById('listCount');
    const listPageMeta = document.getElementById('listPageMeta');

    if (filteredQuestions.length === 0) {
        container.innerHTML = '';
        emptyState.classList.remove('hidden');
        emptyText.textContent = currentTab === 'review' ? '目前沒有待審題目' : '尚無審核結果';
        paginationEl.classList.add('hidden');
        listCount.textContent = '0';
        listPageMeta.textContent = '';
        return;
    }

    emptyState.classList.add('hidden');
    listCount.textContent = filteredQuestions.length;

    const totalPages = Math.max(1, Math.ceil(filteredQuestions.length / pageSize));
    const startIdx = (currentPage - 1) * pageSize;
    const endIdx = Math.min(startIdx + pageSize, filteredQuestions.length);
    const pageItems = filteredQuestions.slice(startIdx, endIdx);
    listPageMeta.textContent = `(第 ${currentPage}/${totalPages} 頁)`;

    let html = '';
    pageItems.forEach(q => {
        const typeInfo = qTypeMap[q.type] || q.type;
        const typeIcon = qTypeIcon[q.type] || 'fa-solid fa-question';
        const diff = diffMap[q.difficulty] || q.difficulty;
        const preview = getStemPreview(q);
        const stageInfo = reviewStageMap[q.reviewStage] || {};

        let stageBadge = '';
        let actionBtn = '';

        if (currentTab === 'review') {
            if (q.reviewStatus === 'decided') {
                const decision = decisionMap[q.myDecision] || {};
                stageBadge = `<span class="text-xs px-2 py-0.5 rounded-full bg-gray-200 text-gray-500 font-bold border border-gray-300">
                    <i class="fa-solid fa-check mr-1"></i>已決策 (${decision.label || '--'})
                </span>`;
                actionBtn = `<button class="px-3 py-1.5 text-xs border border-gray-300 rounded-lg text-gray-400 font-medium bg-gray-50 cursor-pointer hover:bg-gray-100 hover:text-gray-600 transition-colors" data-action="view" data-id="${q.id}">
                    <i class="fa-solid fa-eye mr-1"></i>檢視
                </button>`;
            } else {
                stageBadge = `<span class="text-xs px-2 py-0.5 rounded-full ${stageInfo.color || ''} font-bold border ${stageInfo.border || ''}">
                    <i class="${stageInfo.icon || ''} mr-1"></i>${stageInfo.label || ''}
                </span>`;
                actionBtn = `<button class="px-4 py-1.5 text-xs bg-[var(--color-terracotta)] hover:bg-[#c47a5e] text-white rounded-lg font-bold transition-colors cursor-pointer" data-action="review" data-id="${q.id}">
                    <i class="fa-solid fa-pen-to-square mr-1"></i>審題
                </button>`;
            }
        } else {
            const resultColor = q.finalResult === 'adopted'
                ? 'bg-emerald-100 text-emerald-700 border-emerald-300'
                : 'bg-gray-200 text-gray-500 border-gray-300';
            const resultLabel = q.finalResult === 'adopted' ? '採用' : '不採用';
            const resultIcon = q.finalResult === 'adopted' ? 'fa-solid fa-circle-check' : 'fa-solid fa-circle-xmark';

            stageBadge = `<span class="text-xs px-2 py-0.5 rounded-full ${resultColor} font-bold border">
                <i class="${resultIcon} mr-1"></i>${resultLabel}
            </span>`;
            actionBtn = `<button class="px-3 py-1.5 text-xs border border-[var(--color-morandi)] rounded-lg text-[var(--color-morandi)] font-medium hover:bg-[var(--color-morandi)]/10 transition-colors cursor-pointer" data-action="view" data-id="${q.id}">
                <i class="fa-solid fa-eye mr-1"></i>檢視
            </button>`;
        }

        html += `
            <div class="q-card flex items-center gap-4 px-5 py-4 bg-white hover:bg-gray-50/50 transition-colors cursor-pointer" data-qid="${q.id}">
                <div class="flex-shrink-0 w-10 h-10 rounded-lg bg-gray-100 flex items-center justify-center text-gray-400">
                    <i class="${typeIcon}"></i>
                </div>
                <div class="flex-grow min-w-0">
                    <div class="flex items-center gap-2 mb-1">
                        <span class="text-xs font-mono font-bold text-[var(--color-morandi)]">${q.id}</span>
                        <span class="text-xs text-gray-400">${typeInfo}</span>
                        <span class="text-xs text-gray-400">${q.level} / ${diff}</span>
                        ${stageBadge}
                    </div>
                    <p class="text-sm text-gray-700 truncate">${preview || '<span class="text-gray-400">(尚未輸入)</span>'}</p>
                    <div class="text-xs text-gray-400 mt-1">
                        命題教師：${q.authorName || '--'}
                    </div>
                </div>
                <div class="flex-shrink-0">
                    ${actionBtn}
                </div>
            </div>`;
    });

    container.innerHTML = html;

    // 綁定按鈕事件
    container.querySelectorAll('[data-action]').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            const action = btn.dataset.action;
            const id = btn.dataset.id;
            const q = reviewQuestionsDb.find(item => item.id === id);
            if (!q) return;

            if (action === 'review') {
                openReviewModal(q, 'review');
            } else {
                openReviewModal(q, 'view');
            }
        });
    });

    // 整行點擊
    container.querySelectorAll('.q-card').forEach(card => {
        card.addEventListener('click', () => {
            const qid = card.dataset.qid;
            const q = reviewQuestionsDb.find(item => item.id === qid);
            if (!q) return;
            const mode = (currentTab === 'review' && q.reviewStatus === 'pending') ? 'review' : 'view';
            openReviewModal(q, mode);
        });
    });

    // 分頁
    renderPagination(totalPages);
};


// ===================================================================
// 分頁器
// ===================================================================

const renderPagination = (totalPages) => {
    const el = document.getElementById('listPagination');
    if (!el) return;

    if (totalPages <= 1) {
        el.classList.add('hidden');
        return;
    }

    el.classList.remove('hidden');

    const prevDisabled = currentPage <= 1;
    const nextDisabled = currentPage >= totalPages;

    let pageButtons = '';
    const maxVisible = 5;
    let startPage = Math.max(1, currentPage - Math.floor(maxVisible / 2));
    let endPage = Math.min(totalPages, startPage + maxVisible - 1);
    if (endPage - startPage < maxVisible - 1) startPage = Math.max(1, endPage - maxVisible + 1);

    if (startPage > 1) pageButtons += `<button class="page-btn px-3 py-1 text-sm rounded border border-gray-200 hover:bg-gray-50 cursor-pointer" data-page="1">1</button>`;
    if (startPage > 2) pageButtons += `<span class="text-gray-400 px-1">...</span>`;

    for (let i = startPage; i <= endPage; i++) {
        const isActive = i === currentPage;
        pageButtons += `<button class="page-btn px-3 py-1 text-sm rounded border cursor-pointer ${isActive ? 'bg-[var(--color-morandi)] text-white border-[var(--color-morandi)]' : 'border-gray-200 hover:bg-gray-50'}" data-page="${i}">${i}</button>`;
    }

    if (endPage < totalPages - 1) pageButtons += `<span class="text-gray-400 px-1">...</span>`;
    if (endPage < totalPages) pageButtons += `<button class="page-btn px-3 py-1 text-sm rounded border border-gray-200 hover:bg-gray-50 cursor-pointer" data-page="${totalPages}">${totalPages}</button>`;

    el.innerHTML = `
        <div class="text-xs text-gray-400">第 ${(currentPage - 1) * pageSize + 1}-${Math.min(currentPage * pageSize, filteredQuestions.length)} 筆，共 ${filteredQuestions.length} 筆</div>
        <div class="flex items-center gap-1">
            <button class="page-btn px-2 py-1 text-sm rounded border border-gray-200 ${prevDisabled ? 'text-gray-300 cursor-not-allowed' : 'hover:bg-gray-50 cursor-pointer'}" data-page="${currentPage - 1}" ${prevDisabled ? 'disabled' : ''}><i class="fa-solid fa-chevron-left"></i></button>
            ${pageButtons}
            <button class="page-btn px-2 py-1 text-sm rounded border border-gray-200 ${nextDisabled ? 'text-gray-300 cursor-not-allowed' : 'hover:bg-gray-50 cursor-pointer'}" data-page="${currentPage + 1}" ${nextDisabled ? 'disabled' : ''}><i class="fa-solid fa-chevron-right"></i></button>
        </div>`;

    el.querySelectorAll('.page-btn:not([disabled])').forEach(btn => {
        btn.addEventListener('click', () => {
            currentPage = parseInt(btn.dataset.page);
            renderTabContent();
        });
    });
};


// ===================================================================
// 審題 Modal
// ===================================================================

const initReviewModal = () => {
    document.getElementById('reviewBackBtn')?.addEventListener('click', closeReviewModal);
    document.getElementById('reviewBackdrop')?.addEventListener('click', closeReviewModal);

    document.getElementById('duplicateCheckBtn')?.addEventListener('click', showDuplicateCheck);

    // 審查意見抽屜展收合
    const drawerToggleBar = document.getElementById('drawerToggleBar');
    if (drawerToggleBar) {
        drawerToggleBar.addEventListener('click', (e) => {
            // 避免點擊「試題重複比對」按鈕時觸發 toggle
            if (e.target.closest('#duplicateCheckBtn')) return;
            toggleReviewDrawer();
        });
    }
};

/** 切換審查意見抽屜展收合 */
const toggleReviewDrawer = () => {
    const body = document.getElementById('drawerBody');
    const chevron = document.getElementById('drawerChevron');
    const hint = document.getElementById('drawerHint');
    if (!body) return;

    const isOpen = body.classList.toggle('open');
    chevron?.classList.toggle('open', isOpen);
    if (hint) hint.textContent = isOpen ? '點擊收合' : '點擊展開';
};

const openReviewModal = (question, mode = 'review') => {
    currentReviewQuestion = question;

    const modal = document.getElementById('reviewModal');
    const backdrop = document.getElementById('reviewBackdrop');
    const panel = document.getElementById('reviewPanel');
    const actionArea = document.getElementById('reviewActionArea');

    modal.classList.remove('hidden');
    requestAnimationFrame(() => {
        backdrop.classList.remove('opacity-0');
        panel.classList.remove('opacity-0');
        panel.classList.add('modal-animate-in');
    });

    // 標題
    document.getElementById('reviewModalTitle').textContent = question.id;
    document.getElementById('reviewModalAuthor').textContent = question.authorName || '--';

    // 階段 badge
    const stageInfo = reviewStageMap[question.reviewStage] || {};
    const stageBadgeEl = document.getElementById('reviewModalStageBadge');
    stageBadgeEl.className = `text-xs px-2 py-0.5 rounded-full font-bold border ${stageInfo.color || 'bg-gray-100 text-gray-500'} ${stageInfo.border || 'border-gray-200'}`;
    stageBadgeEl.innerHTML = `<i class="${stageInfo.icon || ''} mr-1"></i>${stageInfo.label || '--'}`;

    // 渲染題目內容
    renderQuestionContent(question);

    // 渲染歷程
    renderHistoryTimeline(question);

    // 決策按鈕
    if (mode === 'review' && question.reviewStatus === 'pending') {
        actionArea.classList.remove('hidden');
        renderDecisionButtons(question);
        if (reviewQuillInstance) {
            reviewQuillInstance.setText('');
            reviewQuillInstance.enable();
        }
        // 預設抽屜收合，讓審題人員先專注看題目
        const drawerBody = document.getElementById('drawerBody');
        const drawerChevron = document.getElementById('drawerChevron');
        const drawerHint = document.getElementById('drawerHint');
        if (drawerBody) {
            drawerBody.classList.remove('open');
            drawerChevron?.classList.remove('open');
            if (drawerHint) drawerHint.textContent = '點擊展開';
        }
    } else {
        actionArea.classList.add('hidden');
    }
};

const closeReviewModal = () => {
    const modal = document.getElementById('reviewModal');
    const backdrop = document.getElementById('reviewBackdrop');
    const panel = document.getElementById('reviewPanel');

    backdrop.classList.add('opacity-0');
    panel.classList.add('opacity-0');
    panel.classList.remove('modal-animate-in');

    setTimeout(() => {
        modal.classList.add('hidden');
        currentReviewQuestion = null;
    }, 300);
};


// ===================================================================
// 題目內容渲染（唯讀）
// ===================================================================

const renderQuestionContent = (q) => {
    const body = document.getElementById('reviewModalBody');
    if (!body) return;

    let contentHtml = '';

    // 基本資訊卡片
    contentHtml += `
        <div class="p-6">
            <div class="bg-white p-6 rounded-xl shadow-sm border border-gray-200 mb-6">
                <div class="flex flex-wrap gap-4 border-b border-gray-100 pb-4 mb-4">
                    <div>
                        <div class="text-xs text-gray-400 mb-0.5">題型</div>
                        <div class="text-sm font-bold"><i class="${qTypeIcon[q.type] || ''} mr-1 text-[var(--color-morandi)]"></i>${qTypeMap[q.type] || q.type}</div>
                    </div>
                    <div>
                        <div class="text-xs text-gray-400 mb-0.5">等級</div>
                        <div class="text-sm font-bold text-[var(--color-morandi)]">${q.level || '--'}</div>
                    </div>
                    <div>
                        <div class="text-xs text-gray-400 mb-0.5">難易度</div>
                        <div class="text-sm font-bold text-[var(--color-terracotta)]">${diffMap[q.difficulty] || q.difficulty || '--'}</div>
                    </div>
                    ${q.attributes?.audioType ? `<div><div class="text-xs text-gray-400 mb-0.5">音檔類型</div><div class="text-sm font-bold">${q.attributes.audioType}</div></div>` : ''}
                    ${q.attributes?.material ? `<div><div class="text-xs text-gray-400 mb-0.5">素材</div><div class="text-sm font-bold">${q.attributes.material}</div></div>` : ''}
                    ${q.attributes?.mode ? `<div><div class="text-xs text-gray-400 mb-0.5">模式</div><div class="text-sm font-bold">${q.attributes.mode}</div></div>` : ''}
                    ${q.attributes?.genre ? `<div><div class="text-xs text-gray-400 mb-0.5">文體</div><div class="text-sm font-bold">${q.attributes.genre}</div></div>` : ''}
                </div>`;

    // 音檔（聽力題）
    if (q.audioUrl) {
        contentHtml += `
            <div class="mb-4 p-3 bg-blue-50 rounded-lg border border-blue-100 flex items-center gap-3">
                <i class="fa-solid fa-headphones text-blue-400 text-lg"></i>
                <div>
                    <div class="text-xs text-blue-500 font-bold">音檔</div>
                    <div class="text-sm text-blue-700">${q.audioUrl} <span class="text-blue-400">(DEMO 模擬)</span></div>
                </div>
            </div>`;
    }

    // 文章/語音內容（題組類）
    if (q.passage) {
        const passageLabel = q.type === 'listenGroup' ? '語音內容' : (q.type === 'longText' ? '題目內容' : '文章內容');
        contentHtml += `
            <div class="mb-4">
                <h4 class="text-xs font-bold text-[var(--color-morandi)] uppercase tracking-wider mb-2">${passageLabel}</h4>
                <div class="text-sm leading-relaxed text-gray-800 p-4 bg-gray-50 rounded-lg border border-gray-100">${q.passage}</div>
            </div>`;
    }

    // 題幹
    if (q.stem) {
        const stemLabel = q.type === 'readGroup' ? '標題' : (q.type === 'longText' ? '題目' : '題幹');
        contentHtml += `
            <div class="mb-4">
                <h4 class="text-xs font-bold text-[var(--color-morandi)] uppercase tracking-wider mb-2">${stemLabel}</h4>
                <div class="text-sm leading-relaxed text-gray-800 p-4 bg-gray-50 rounded-lg border border-gray-100">${q.stem}</div>
            </div>`;
    }

    // 選項（一般選擇題）
    if (q.options && q.options.length > 0 && !q.subQuestions?.length) {
        contentHtml += `
            <div class="mb-4">
                <h4 class="text-xs font-bold text-[var(--color-morandi)] uppercase tracking-wider mb-2">選項</h4>
                <div class="space-y-2">
                    ${q.options.map(opt => {
            const isAnswer = opt.label === q.answer;
            return `<div class="flex items-start gap-2 p-3 rounded-lg ${isAnswer ? 'bg-emerald-50 border border-emerald-200' : 'bg-gray-50 border border-gray-100'}">
                            <span class="flex-shrink-0 w-7 h-7 rounded-full ${isAnswer ? 'bg-emerald-500 text-white' : 'bg-gray-200 text-gray-500'} flex items-center justify-center text-xs font-bold">${opt.label}</span>
                            <span class="text-sm text-gray-800 pt-1">${opt.text || ''}</span>
                            ${isAnswer ? '<span class="ml-auto text-xs text-emerald-600 font-bold flex-shrink-0"><i class="fa-solid fa-check mr-1"></i>正確答案</span>' : ''}
                        </div>`;
        }).join('')}
                </div>
            </div>`;
    }

    // 子題（題組類）
    if (q.subQuestions && q.subQuestions.length > 0) {
        contentHtml += `
            <div class="mb-4">
                <h4 class="text-xs font-bold text-[var(--color-morandi)] uppercase tracking-wider mb-3">子題 (${q.subQuestions.length} 題)</h4>
                <div class="space-y-4">
                    ${q.subQuestions.map((sq, i) => {
            let sqHtml = `<div class="p-4 bg-gray-50 rounded-lg border border-gray-100">
                            <div class="text-xs font-bold text-gray-500 mb-2">第 ${i + 1} 題${sq.level ? ` — ${sq.level}` : ''}${sq.competency ? ` / ${sq.competency}` : ''}</div>
                            <div class="text-sm text-gray-800 mb-2">${sq.stem || ''}</div>`;

            if (sq.options && sq.options.length > 0) {
                sqHtml += `<div class="space-y-1.5 ml-2">
                                ${sq.options.map(opt => {
                    const isAns = opt.label === sq.answer;
                    return `<div class="flex items-center gap-2 text-sm ${isAns ? 'text-emerald-700 font-bold' : 'text-gray-600'}">
                                        <span class="w-5 h-5 rounded-full ${isAns ? 'bg-emerald-500 text-white' : 'bg-gray-200 text-gray-500'} flex items-center justify-center text-[10px] font-bold flex-shrink-0">${opt.label}</span>
                                        ${opt.text || ''}
                                        ${isAns ? ' <i class="fa-solid fa-check text-emerald-500 ml-1"></i>' : ''}
                                    </div>`;
                }).join('')}
                            </div>`;
            }

            if (sq.dimension) {
                sqHtml += `<div class="mt-2 text-xs text-gray-400">向度：${sq.dimension} / 指標：${sq.indicator || '--'}</div>`;
            }

            if (sq.analysis) {
                sqHtml += `<div class="mt-2 text-xs text-gray-500 bg-white p-2 rounded border border-gray-100"><strong>參考解析：</strong>${sq.analysis}</div>`;
            }

            sqHtml += `</div>`;
            return sqHtml;
        }).join('')}
                </div>
            </div>`;
    }

    // 解析
    if (q.analysis) {
        contentHtml += `
            <div class="mb-2">
                <h4 class="text-xs font-bold text-[var(--color-morandi)] uppercase tracking-wider mb-2">${q.type === 'longText' ? '批閱說明' : '解析'}</h4>
                <div class="text-sm leading-relaxed text-gray-700 p-4 bg-amber-50/50 rounded-lg border border-amber-100">${q.analysis}</div>
            </div>`;
    }

    contentHtml += `</div>`;

    // 歷程時間軸容器
    contentHtml += `
        <div class="bg-white p-6 rounded-xl shadow-sm border border-gray-200 mb-6 mx-6">
            <h3 class="text-base font-bold text-[var(--color-slate-main)] mb-4 flex items-center gap-2 border-b border-gray-100 pb-3">
                <i class="fa-solid fa-clock-rotate-left text-[var(--color-sage)]"></i> 歷程軌跡與審查意見
            </h3>
            <div class="relative border-l-2 border-gray-200 ml-4 pl-6 space-y-5 py-2" id="reviewTimelineContainer">
            </div>
        </div>
    </div>`;

    body.innerHTML = contentHtml;
};


// ===================================================================
// 歷程時間軸
// ===================================================================

const renderHistoryTimeline = (q) => {
    const container = document.getElementById('reviewTimelineContainer');
    if (!container || !q.history) return;

    // 統一排序：新 → 舊（與 overview.html 一致）
    container.innerHTML = [...q.history].reverse().map(entry => {
        const isReview = entry.action.includes('審') || entry.action.includes('決策');
        const dotColor = entry.action.includes('採用') && !entry.action.includes('不採用')
            ? 'bg-emerald-500'
            : entry.action.includes('不採用')
                ? 'bg-gray-400'
                : entry.action.includes('改後再審')
                    ? 'bg-amber-500'
                    : isReview ? 'bg-blue-500' : 'bg-gray-300';

        return `
            <div class="relative">
                <div class="absolute -left-[31px] top-1 w-4 h-4 rounded-full ${dotColor} border-2 border-white shadow-sm"></div>
                <div class="text-xs text-gray-400 mb-1">${entry.time}</div>
                <div class="text-sm">
                    <span class="font-bold text-gray-700">${entry.user}</span>
                    <span class="text-gray-500 ml-1">${entry.action}</span>
                </div>
                ${entry.comment ? `<div class="mt-1 text-sm text-gray-600 bg-gray-50 p-2.5 rounded-lg border border-gray-100">${entry.comment}</div>` : ''}
            </div>`;
    }).join('');
};


// ===================================================================
// 決策按鈕
// ===================================================================

const renderDecisionButtons = (q) => {
    const container = document.getElementById('decisionButtonsContainer');
    if (!container) return;

    let btns = '';

    if (q.reviewStage === 'peer') {
        btns = `
            <button type="button" class="decision-btn px-5 py-2 text-sm bg-[var(--color-morandi)] hover:bg-[#5a7d9c] text-white rounded-lg font-bold transition-colors cursor-pointer" data-decision="comment">
                <i class="fa-solid fa-paper-plane mr-1"></i> 送出意見
            </button>`;
    } else if (q.reviewStage === 'expert') {
        btns = `
            <button type="button" class="decision-btn px-5 py-2 text-sm bg-[var(--color-sage)] hover:bg-[#7a9a82] text-white rounded-lg font-bold transition-colors cursor-pointer" data-decision="adopt">
                <i class="fa-solid fa-circle-check mr-1"></i> 採用
            </button>
            <button type="button" class="decision-btn px-5 py-2 text-sm bg-[var(--color-terracotta)] hover:bg-[#c47a5e] text-white rounded-lg font-bold transition-colors cursor-pointer" data-decision="revise">
                <i class="fa-solid fa-rotate-left mr-1"></i> 改後再審
            </button>`;
    } else if (q.reviewStage === 'final') {
        btns = `
            <button type="button" class="decision-btn px-5 py-2 text-sm bg-[var(--color-sage)] hover:bg-[#7a9a82] text-white rounded-lg font-bold transition-colors cursor-pointer" data-decision="adopt">
                <i class="fa-solid fa-circle-check mr-1"></i> 採用
            </button>
            <button type="button" class="decision-btn px-5 py-2 text-sm bg-[var(--color-terracotta)] hover:bg-[#c47a5e] text-white rounded-lg font-bold transition-colors cursor-pointer" data-decision="revise">
                <i class="fa-solid fa-rotate-left mr-1"></i> 改後再審
            </button>
            <button type="button" class="decision-btn px-5 py-2 text-sm bg-gray-500 hover:bg-gray-600 text-white rounded-lg font-bold transition-colors cursor-pointer" data-decision="reject">
                <i class="fa-solid fa-circle-xmark mr-1"></i> 不採用
            </button>`;
    }

    container.innerHTML = btns;

    container.querySelectorAll('.decision-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            executeDecision(btn.dataset.decision);
        });
    });
};

const executeDecision = (decision) => {
    if (!currentReviewQuestion) return;

    const q = currentReviewQuestion;
    const comment = reviewQuillInstance ? reviewQuillInstance.root.innerHTML : '';
    const plainComment = reviewQuillInstance ? reviewQuillInstance.getText().trim() : '';

    const stageLabel = reviewStageMap[q.reviewStage]?.label || '';
    const decisionLabels = {
        'comment': '送出意見',
        'adopt': '採用',
        'revise': '改後再審',
        'reject': '不採用'
    };

    // 互審必須填寫意見
    if (decision === 'comment' && !plainComment) {
        Swal.fire({ icon: 'warning', title: '請填寫審查意見', text: '互審階段必須提供審查意見。', confirmButtonColor: '#6B8EAD' });
        return;
    }

    Swal.fire({
        title: `確認${decisionLabels[decision]}？`,
        html: `<div class="text-left text-sm">
            <p class="mb-2">題號：<strong>${q.id}</strong></p>
            <p class="mb-2">階段：<strong>${stageLabel}</strong></p>
            <p>決策：<strong>${decisionLabels[decision]}</strong></p>
            ${plainComment ? `<p class="mt-2 text-gray-500">意見摘要：${truncate(plainComment, 50)}</p>` : ''}
        </div>`,
        icon: 'question',
        showCancelButton: true,
        confirmButtonColor: decision === 'reject' ? '#6b7280' : (decision === 'adopt' ? '#8EAB94' : '#D98A6C'),
        cancelButtonColor: '#d1d5db',
        confirmButtonText: `確認${decisionLabels[decision]}`,
        cancelButtonText: '取消'
    }).then((result) => {
        if (!result.isConfirmed) return;

        // 更新資料
        q.reviewStatus = 'decided';
        q.myDecision = decision;
        q.myComment = comment;

        // 加入歷程記錄
        const userStr = localStorage.getItem('cwt_user');
        const userName = userStr ? JSON.parse(userStr).name : '劉雅婷';
        const now = new Date();
        const timeStr = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')} ${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`;

        let actionLabel = '';
        if (q.reviewStage === 'peer') actionLabel = '互審意見';
        else if (q.reviewStage === 'expert') actionLabel = `專審決策 (${decisionLabels[decision]})`;
        else actionLabel = `總召決策 (${decisionLabels[decision]})`;

        q.history.push({
            time: timeStr,
            user: userName,
            action: actionLabel,
            comment: plainComment
        });

        // 總審採用/不採用 → 移至歷史
        if (q.reviewStage === 'final' && (decision === 'adopt' || decision === 'reject')) {
            q.finalResult = decision === 'adopt' ? 'adopted' : 'rejected';
        }

        closeReviewModal();

        Swal.fire({
            icon: 'success',
            title: `已${decisionLabels[decision]}`,
            text: `題目 ${q.id} ${decisionLabels[decision]}完成。`,
            toast: true,
            position: 'top-end',
            showConfirmButton: false,
            timer: 2500,
            timerProgressBar: true
        });

        renderTabContent();
    });
};


// ===================================================================
// 試題重複比對（模擬）
// ===================================================================

const showDuplicateCheck = () => {
    if (!currentReviewQuestion) return;

    Swal.fire({
        title: '<i class="fa-solid fa-clone mr-2 text-[#6B8EAD]"></i>試題重複比對',
        html: `
            <div class="text-left text-sm">
                <p class="text-gray-500 mb-3">正在比對題目 <strong>${currentReviewQuestion.id}</strong> 與題庫既有試題的相似度...</p>
                <div class="overflow-hidden rounded-lg border border-gray-200">
                    <table class="w-full text-sm">
                        <thead class="bg-gray-50">
                            <tr>
                                <th class="px-3 py-2 text-left font-bold text-gray-600">比對題號</th>
                                <th class="px-3 py-2 text-left font-bold text-gray-600">相似度</th>
                                <th class="px-3 py-2 text-left font-bold text-gray-600">判定</th>
                            </tr>
                        </thead>
                        <tbody class="divide-y divide-gray-100">
                            <tr>
                                <td class="px-3 py-2 font-mono text-gray-700">Q-2501-H032</td>
                                <td class="px-3 py-2"><span class="text-amber-600 font-bold">42%</span></td>
                                <td class="px-3 py-2"><span class="text-xs px-2 py-0.5 rounded-full bg-amber-100 text-amber-700 font-bold">低度相似</span></td>
                            </tr>
                            <tr>
                                <td class="px-3 py-2 font-mono text-gray-700">Q-2501-H089</td>
                                <td class="px-3 py-2"><span class="text-green-600 font-bold">18%</span></td>
                                <td class="px-3 py-2"><span class="text-xs px-2 py-0.5 rounded-full bg-green-100 text-green-700 font-bold">無疑慮</span></td>
                            </tr>
                            <tr>
                                <td class="px-3 py-2 font-mono text-gray-700">Q-2502-H015</td>
                                <td class="px-3 py-2"><span class="text-green-600 font-bold">12%</span></td>
                                <td class="px-3 py-2"><span class="text-xs px-2 py-0.5 rounded-full bg-green-100 text-green-700 font-bold">無疑慮</span></td>
                            </tr>
                        </tbody>
                    </table>
                </div>
                <p class="text-gray-400 text-xs mt-3"><i class="fa-solid fa-circle-info mr-1"></i>此為 DEMO 模擬結果，實際比對功能將由後端 AI 引擎執行。</p>
            </div>`,
        confirmButtonText: '關閉',
        confirmButtonColor: '#6B8EAD',
        width: 520
    });
};
