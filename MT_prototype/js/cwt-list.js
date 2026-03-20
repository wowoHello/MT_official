/**
 * CWT List Module (命題任務)
 * 負責命題教師的試題管理：命題作業區、審修作業區、審核結果與歷史。
 * 包含 7 種題型表單、底部滑入式 Quill 編輯器、修題回覆機制。
 * Version: 1.0 (DEMO)
 *
 * [Blazor Migration Note]
 * - Mock 資料需替換為 API 呼叫
 * - Quill 編輯器需評估 Blazor 相容方案 (Blazored.TextEditor or JS Interop)
 * - localStorage 操作替換為 Server Session
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

/** 試題狀態機 */
const statusMap = {
    'draft': { label: '草稿', color: 'bg-gray-100 text-gray-600', border: 'border-gray-200', tab: 'compose' },
    'completed': { label: '命題完成', color: 'bg-blue-100 text-blue-700', border: 'border-blue-200', tab: 'compose' },
    'pending': { label: '已送審', color: 'bg-yellow-100 text-yellow-700', border: 'border-yellow-200', tab: 'compose' },
    'peer_reviewing': { label: '互審中', color: 'bg-blue-50 text-blue-600', border: 'border-blue-200', tab: 'revision' },
    'peer_editing': { label: '互審修題', color: 'bg-amber-100 text-amber-700', border: 'border-amber-300', tab: 'revision' },
    'expert_reviewing': { label: '專審中', color: 'bg-blue-50 text-blue-600', border: 'border-blue-200', tab: 'revision' },
    'expert_editing': { label: '專審修題', color: 'bg-amber-100 text-amber-700', border: 'border-amber-300', tab: 'revision' },
    'final_reviewing': { label: '總審中', color: 'bg-blue-50 text-blue-600', border: 'border-blue-200', tab: 'revision' },
    'final_editing': { label: '總審修題', color: 'bg-red-100 text-red-700', border: 'border-red-300', tab: 'revision' },
    'adopted': { label: '採用', color: 'bg-emerald-100 text-emerald-700', border: 'border-emerald-300', tab: 'history' },
    'rejected': { label: '不採用', color: 'bg-gray-200 text-gray-500', border: 'border-gray-300', tab: 'history' }
};

/** 審查階段名稱對應 */
const reviewStageLabel = {
    'peer': '互審', 'expert': '專審', 'final': '總審'
};

const revisionReviewingStatuses = ['peer_reviewing', 'expert_reviewing', 'final_reviewing'];
const revisionEditingStatuses = ['peer_editing', 'expert_editing', 'final_editing'];

const tabStatusFilterOptions = {
    compose: [
        { value: 'all', label: '所有狀態' },
        { value: 'draft', label: '命題草稿' },
        { value: 'completed', label: '命題完成' },
        { value: 'pending', label: '已送審' }
    ],
    revision: [
        { value: 'all', label: '所有狀態' },
        { value: 'reviewing', label: '審題鎖定' },
        { value: 'editing', label: '修題中' }
    ],
    history: [
        { value: 'all', label: '所有狀態' },
        { value: 'adopted', label: '已採用' },
        { value: 'rejected', label: '不採用' }
    ]
};

const defaultPageSize = 12;
const normalLevelOptions = ['初級', '中級', '中高級', '高級', '優級'];
const listenLevelOptions = ['難度一', '難度二', '難度三', '難度四', '難度五'];

// 聽力測驗：等級 → 核心能力 / 細目指標 對應
const listenCompetencyMap = {
    '難度一': { competency: '提取訊息', indicator: '提取對話與訊息主旨' },
    '難度二': { competency: '理解訊息', indicator: '理解訊息意圖' },
    '難度三': { competency: '推斷訊息', indicator: '推斷訊息邏輯性' },
    '難度四': { competency: '歸納分析訊息', indicator: '歸納或總結訊息內容' },
    '難度五': { competency: '統整、闡述或評鑑訊息', indicator: '摘要、條列、統整訊息' }
};

const listenAudioTypeOptions = ['對話', '情境', '陳述'];
const listenMaterialOptions = ['生活', '教育', '職場', '專業'];
const listenGroupFixedQuestionConfigs = [
    { level: '難度三', competency: '推斷訊息', indicator: '推斷訊息邏輯性' },
    { level: '難度四', competency: '歸納分析', indicator: '歸納或總結訊息內容' }
];

const typeLevelOptions = {
    all: normalLevelOptions,
    single: ['初級', '中級', '中高級'],
    select: ['初級', '中級', '中高級'],
    readGroup: normalLevelOptions,
    longText: ['初級', '中級', '中高級', '優級'],
    shortGroup: ['高級', '優級'],
    listen: listenLevelOptions,
    listenGroup: listenLevelOptions
};

const singleChoiceCategoryMap = {
    '文字': ['字音', '字型', '造字原則'],
    '語詞': ['辭義辨識', '詞彙辨析', '詞性分辨', '語詞應用'],
    '成語短語': ['短語辨識', '語詞使用', '文義取得'],
    '造句標點': ['句義', '句法辨析', '標點符號'],
    '修辭技巧': ['修辭類型', '語態變化'],
    '語文知識': ['語文知識'],
    '文意判讀': ['段義辨析']
};

const singleChoiceTopics = Object.keys(singleChoiceCategoryMap);
const longTextModeOptions = ['引導寫作', '資訊整合'];
const shortGroupMainCategory = '文義判讀';
const shortGroupSubCategory = '篇章辨析';
const shortGroupGenreOptions = ['文言文', '應用文', '語體文'];
const shortGroupDimensionMap = {
    '條列敘述': [
        '1-1 條列敘述人、事、物特徵與特質',
        '1-2 條列敘述人、事、物起始原因、發生情況、結論等時空先後順序',
        '1-3 條列敘述人、事、物的差異'
    ],
    '歸納統整': [
        '2-1 歸納作者主張',
        '2-2 歸納文章主旨',
        '2-3 歸納共同特點'
    ],
    '分析推理': [
        '3-1 分析線索',
        '3-2 推論緣由',
        '3-3 判斷結果',
        '3-4 判斷詞性、主語',
        '3-5 判斷字句的解釋、文意說明是否正確',
        '3-6 推測行為的原因或用意、說明如何達成行為',
        '3-7 推測寫作手法的目的',
        '3-8 判斷文體、格律、風格'
    ]
};
const shortGroupDimensionOptions = Object.keys(shortGroupDimensionMap);
const shortGroupIndicatorDimensionMap = Object.entries(shortGroupDimensionMap).reduce((acc, [dimension, indicators]) => {
    indicators.forEach((indicator) => {
        acc[indicator] = dimension;
    });
    return acc;
}, {});
const shortGroupLegacyIndicatorMap = {
    '擷取訊息': '1-1 條列敘述人、事、物特徵與特質',
    '歸納段意': '2-2 歸納文章主旨',
    '整合理解': '2-3 歸納共同特點',
    '推論文意': '3-2 推論緣由',
    '辨析修辭': '3-7 推測寫作手法的目的',
    '觀點判讀': '2-1 歸納作者主張',
    '情境回應': '3-3 判斷結果',
    '觀點表述': '2-1 歸納作者主張',
    '書寫組織': '1-2 條列敘述人、事、物起始原因、發生情況、結論等時空先後順序'
};
const shortGroupLegacyDimensionFallbackMap = {
    '文本理解': '條列敘述',
    '文意分析': '分析推理',
    '表達應用': '歸納統整'
};
const getNormalizedShortGroupSelection = (subQuestion = {}) => {
    const rawDimension = subQuestion.dimension || '';
    const rawIndicator = subQuestion.indicator || '';
    const directMatch = shortGroupDimensionMap[rawDimension]?.includes(rawIndicator);

    if (directMatch) {
        return {
            selectedDimension: rawDimension,
            indicatorOptions: shortGroupDimensionMap[rawDimension] || [],
            selectedIndicator: rawIndicator
        };
    }

    const normalizedIndicator = shortGroupIndicatorDimensionMap[rawIndicator]
        ? rawIndicator
        : (shortGroupLegacyIndicatorMap[rawIndicator] || '');
    const selectedDimension = normalizedIndicator
        ? shortGroupIndicatorDimensionMap[normalizedIndicator]
        : (shortGroupLegacyDimensionFallbackMap[rawDimension] || shortGroupDimensionOptions[0]);
    const indicatorOptions = shortGroupDimensionMap[selectedDimension] || [];
    const selectedIndicator = indicatorOptions.includes(normalizedIndicator)
        ? normalizedIndicator
        : (indicatorOptions[0] || '');

    return { selectedDimension, indicatorOptions, selectedIndicator };
};
// ===================================================================
// Mock 資料 — 命題教師 (T1001 劉雅婷) 的配額與試題
// ===================================================================

/** 題型欄位契約 */
const qTypeConfig = {
    single: {
        hasStem: true,
        stemLabel: '題幹',
        hasOptions: true,
        hasPassage: false,
        hasSubQuestions: false,
        subQuestionMode: null,
        hasAudio: false
    },
    select: {
        hasStem: true,
        stemLabel: '題幹',
        hasOptions: true,
        hasPassage: false,
        hasSubQuestions: false,
        subQuestionMode: null,
        hasAudio: false
    },
    longText: {
        hasStem: true,
        stemLabel: '題目',
        hasOptions: false,
        hasPassage: true,
        passageLabel: '文章內容',
        hasSubQuestions: false,
        subQuestionMode: null,
        hasAudio: false,
        analysisLabel: '批閱說明',
        analysisPlaceholder: '請簡要說明長文題目的批閱說明...'
    },
    readGroup: {
        hasStem: true,
        stemLabel: '標題',
        hasOptions: false,
        hasPassage: true,
        passageLabel: '文章內容',
        hasSubQuestions: true,
        subQuestionMode: 'choice',
        hasAudio: false
    },
    shortGroup: {
        hasStem: true,
        stemLabel: '題目',
        hasOptions: false,
        hasPassage: true,
        passageLabel: '文章內容',
        hasSubQuestions: true,
        subQuestionMode: 'freeResponse',
        hasAudio: false
    },
    listen: {
        hasStem: true,
        stemLabel: '題幹',
        hasOptions: true,
        hasPassage: false,
        hasSubQuestions: false,
        subQuestionMode: null,
        hasAudio: true
    },
    listenGroup: {
        hasStem: false,
        stemLabel: '',
        hasOptions: false,
        hasPassage: true,
        passageLabel: '語音內容',
        hasSubQuestions: true,
        subQuestionMode: 'choice',
        hasAudio: true
    }
};

const optionLabels = ['A', 'B', 'C', 'D'];

const getListenGroupQuestionConfig = (index) => listenGroupFixedQuestionConfigs[index] || listenGroupFixedQuestionConfigs[0];
const getNormalizedListenGroupSubQuestions = (subQuestions = []) => (
    listenGroupFixedQuestionConfigs.map((config, index) => {
        const source = subQuestions[index] || {};
        return {
            stem: source.stem || '',
            options: optionLabels.map((label, optionIndex) => {
                const matched = (source.options || []).find((option) => option.label === label);
                return matched || { label, text: '' };
            }),
            answer: source.answer || '',
            level: config.level,
            competency: config.competency,
            indicator: config.indicator
        };
    })
);

const getTypeConfig = (type) => qTypeConfig[type] || qTypeConfig.single;
const shouldUseCommonLevelAndDifficulty = () => true;
const getQuestionMetaLine = (question) => {
    const parts = [];
    if (question.level) parts.push(question.level);
    if (question.difficulty) parts.push('難度：' + (diffMap[question.difficulty] || question.difficulty));
    return parts.join(' / ') || '--';
};

const getDefaultSubQuestion = (mode = 'choice') => (
    mode === 'freeResponse'
        ? {
            stem: '',
            analysis: '',
            dimension: shortGroupDimensionOptions[0],
            indicator: shortGroupDimensionMap[shortGroupDimensionOptions[0]][0]
        }
        : {
            stem: '',
            answer: '',
            options: optionLabels.map(label => ({ label, text: '' }))
        }
);

const getQuestionSearchText = (question) => {
    const segments = [
        question.id,
        question.stem,
        question.passage,
        question.analysis,
        question.reviewComment,
        question.attributes?.topic,
        question.attributes?.subtopic,
        question.attributes?.mode,
        question.attributes?.genre,
        question.attributes?.mainCategory,
        question.attributes?.subCategory,
        ...(question.options || []).map(option => option.text),
        ...(question.subQuestions || []).flatMap((subQuestion) => [
            subQuestion.stem,
            subQuestion.analysis,
            subQuestion.dimension,
            subQuestion.indicator,
            ...(subQuestion.options || []).map(option => option.text)
        ])
    ];

    return segments.filter(Boolean).map(segment => stripHtml(segment)).join(' ').toLowerCase();
};

const getQuestionPreviewMeta = (question) => {
    const config = getTypeConfig(question.type);

    if (config.hasSubQuestions) {
        if (config.subQuestionMode === 'freeResponse') {
            return {
                stemPreview: stripHtml(question.passage || '(尚未輸入文章內容)'),
                optionPreview: '<span class="text-gray-400 text-xs">含 ' + ((question.subQuestions || []).length) + ' 道自由作答子題</span>'
            };
        }

        return {
            stemPreview: stripHtml(question.passage || '(尚未輸入題組內容)'),
            optionPreview: '<span class="text-gray-400 text-xs">含 ' + ((question.subQuestions || []).length) + ' 道選擇子題</span>'
        };
    }

    if (question.type === 'longText') {
        return {
            stemPreview: stripHtml(question.stem || question.passage || '(尚未輸入題目或文章內容)'),
            optionPreview: `<span class="text-gray-400 text-xs">${question.attributes?.mode ? `題型：${question.attributes.mode}` : '長文題，無選項'}</span>`
        };
    }

    if (question.type === 'listen') {
        return {
            stemPreview: stripHtml(question.stem || '(尚未輸入聽力題幹)'),
            optionPreview: '<span class="text-gray-400 text-xs">' + (question.audioUrl ? ('音檔：' + question.audioUrl) : '尚未上傳音檔') + '</span>'
        };
    }

    return {
        stemPreview: stripHtml(question.stem || '(尚未輸入題幹)'),
        optionPreview: (question.options || [])
            .map(option => '<span class="inline-block mr-2">(' + option.label + ') ' + truncate(stripHtml(option.text || ''), 8) + '</span>')
            .join('')
    };
};

/** 教師在此梯次被指派的命題配額 */
const myQuotasDb = {
    'P2026-01': { single: 150, select: 100, readGroup: 25, longText: 10, shortGroup: 10, listen: 25, listenGroup: 10 },
    'P2026-02': { single: 50, select: 30, readGroup: 10, longText: 5, shortGroup: 5, listen: 10, listenGroup: 5 }
};

/** 教師名稱庫 (簡化) */
const teacherNames = {
    'T1001': '劉雅婷', 'T1002': '王健明', 'T1003': '張心怡', 'T1004': '吳家豪',
    'C2001': '李教授', 'C2002': '陳副教授', 'S3001': '林總召', 'S3002': '許編輯'
};

/** 試題假資料庫 */
let myQuestionsDb = [
    // ========== 命題作業區 ==========
    {
        id: 'Q-2602-M001', projectId: 'P2026-01', type: 'single', level: '中級', difficulty: 'medium',
        status: 'draft', stem: '下列何者不是臺灣的原住民族群？',
        options: [
            { label: 'A', text: '阿美族' }, { label: 'B', text: '排灣族' },
            { label: 'C', text: '苗族' }, { label: 'D', text: '布農族' }
        ],
        answer: 'C', analysis: '苗族主要分布於中國大陸西南方，並非臺灣原住民族。',
        createdAt: '2026-03-01 09:30', updatedAt: '2026-03-07 14:20',
        returnCount: 0, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
        history: [{ time: '2026-03-01 09:30', user: '劉雅婷', action: '建立草稿', comment: '' }]
    },
    {
        id: 'Q-2602-M002', projectId: 'P2026-01', type: 'single', level: '初級', difficulty: 'easy',
        status: 'draft', stem: '「學而時習之，不亦說乎」出自哪一本經典？',
        options: [
            { label: 'A', text: '《孟子》' }, { label: 'B', text: '《論語》' },
            { label: 'C', text: '《大學》' }, { label: 'D', text: '《中庸》' }
        ],
        answer: 'B', analysis: '此句出自《論語・學而篇》，為孔子所言。',
        createdAt: '2026-03-02 10:15', updatedAt: '2026-03-02 10:15',
        returnCount: 0, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
        history: [{ time: '2026-03-02 10:15', user: '劉雅婷', action: '建立草稿', comment: '' }]
    },
    {
        id: 'Q-2602-M003', projectId: 'P2026-01', type: 'select', level: '高級', difficulty: 'hard',
        status: 'completed', stem: '下列文句，何者使用了「倒裝」修辭？',
        options: [
            { label: 'A', text: '風蕭蕭兮易水寒，壯士一去兮不復還' },
            { label: 'B', text: '不以物喜，不以己悲' },
            { label: 'C', text: '甚矣，汝之不惠！' },
            { label: 'D', text: '有朋自遠方來，不亦樂乎' }
        ],
        answer: 'C', analysis: '「甚矣，汝之不惠」原句應為「汝之不惠，甚矣」，屬典型的主謂倒裝。',
        createdAt: '2026-03-03 11:00', updatedAt: '2026-03-06 16:45',
        returnCount: 0, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
        history: [
            { time: '2026-03-03 11:00', user: '劉雅婷', action: '建立草稿', comment: '' },
            { time: '2026-03-06 16:45', user: '劉雅婷', action: '命題完成', comment: '已完成所有選項設計與解析撰寫' }
        ]
    },
    {
        id: 'Q-2602-M004', projectId: 'P2026-01', type: 'single', level: '中級', difficulty: 'medium',
        status: 'completed', stem: '下列詞語中，何者屬於「聯綿詞」？',
        options: [
            { label: 'A', text: '蝴蝶' }, { label: 'B', text: '書桌' },
            { label: 'C', text: '紅花' }, { label: 'D', text: '跑步' }
        ],
        answer: 'A', analysis: '蝴蝶為雙聲聯綿詞，不可拆開使用。',
        createdAt: '2026-03-04 09:00', updatedAt: '2026-03-08 10:30',
        returnCount: 0, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
        history: [
            { time: '2026-03-04 09:00', user: '劉雅婷', action: '建立草稿', comment: '' },
            { time: '2026-03-08 10:30', user: '劉雅婷', action: '命題完成', comment: '' }
        ]
    },
    {
        id: 'Q-2602-M005', projectId: 'P2026-01', type: 'readGroup', level: '高級', difficulty: 'hard',
        status: 'pending',
        stem: '〈論語・學而〉節選',
        passage: '子曰：「學而時習之，不亦說乎？有朋自遠方來，不亦樂乎？人不知而不慍，不亦君子乎？」——《論語・學而》',
        subQuestions: [
            {
                stem: '下列何者最能說明「學而時習之」的意涵？',
                options: [
                    { label: 'A', text: '學習後要時常溫習' }, { label: 'B', text: '學習需要有固定時間' },
                    { label: 'C', text: '學習只需一次就好' }, { label: 'D', text: '學習要跟隨潮流' }
                ],
                answer: 'A',
                analysis: '文句中的「時習」強調在學習後反覆溫習，因此 A 最能貼合原意；其餘選項不是縮限成固定時段，就是偏離原句重點。'
            },
            {
                stem: '「人不知而不慍」中的「慍」字意思最接近下列何者？',
                options: [
                    { label: 'A', text: '高興' }, { label: 'B', text: '生氣' },
                    { label: 'C', text: '難過' }, { label: 'D', text: '緊張' }
                ],
                answer: 'B',
                analysis: '依上下文可知，即使別人不了解自己，也不會因此動怒，所以「慍」最接近生氣；其餘選項與情緒方向不符。'
            }
        ],
        options: [], answer: '',
        analysis: '',
        attributes: { genre: '文言文' },
        createdAt: '2026-03-05 13:00', updatedAt: '2026-03-08 15:00',
        returnCount: 0, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
        history: [
            { time: '2026-03-05 13:00', user: '劉雅婷', action: '建立草稿', comment: '' },
            { time: '2026-03-07 10:00', user: '劉雅婷', action: '命題完成', comment: '閱讀題組含 2 子題' },
            { time: '2026-03-08 15:00', user: '劉雅婷', action: '送審', comment: '' }
        ]
    },
    {
        id: 'Q-2602-M006', projectId: 'P2026-01', type: 'longText', level: '中高級', difficulty: 'medium',
        status: 'draft',
        stem: '如果課本外也有教室',
        passage: '請以「如果課本外也有教室」為題，撰寫一篇作文。文章需結合一段你在校園、家庭或社區中的真實觀察，說明你曾在哪裡學到課本之外的重要事情，並寫出這段經驗如何改變你看待學習的方式。字數以 500 至 700 字為原則。',
        options: [],
        answer: '',
        analysis: '本題重點在於檢視學生能否結合具體經驗、清楚敘事並提出反思。批閱時可觀察立意是否明確、材料是否充實，以及段落組織與語言表達是否流暢。',
        attributes: { mode: '引導寫作' },
        createdAt: '2026-03-06 11:00', updatedAt: '2026-03-06 11:00',
        returnCount: 0, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
        history: [{ time: '2026-03-06 11:00', user: '劉雅婷', action: '建立草稿', comment: '' }]
    }, {
        id: 'Q-2602-M007', projectId: 'P2026-01', type: 'listen', level: '難度二', difficulty: 'medium',
        status: 'draft',
        stem: '請聽一段對話，回答下列問題：對話中的男子想要做什麼？',
        audioUrl: 'demo_audio_001.mp3',
        options: [
            { label: 'A', text: '去圖書館借書' }, { label: 'B', text: '去超市買東西' },
            { label: 'C', text: '去公園散步' }, { label: 'D', text: '去餐廳吃飯' }
        ],
        answer: 'B', analysis: '對話中男子說「我們去超市買一些水果吧」，故答案為 B。',
        attributes: { audioType: '對話', material: '生活' },
        createdAt: '2026-03-07 14:00', updatedAt: '2026-03-07 14:00',
        returnCount: 0, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
        history: [{ time: '2026-03-07 14:00', user: '劉雅婷', action: '建立草稿', comment: '' }]
    },
    {
        id: 'Q-2602-M008', projectId: 'P2026-01', type: 'shortGroup', level: '高級', difficulty: 'medium',
        status: 'completed',
        stem: '春日公園即景',
        passage: '春天來了，小鳥在枝頭歌唱，花兒在路旁綻放。孩子們在公園裡奔跑嬉戲，大人們在樹蔭下閒聊。這是一個充滿生機的季節。',
        subQuestions: [
            {
                stem: '請根據短文內容，說明作者如何透過景物描寫呈現春天的氣氛。',
                dimension: '歸納統整',
                indicator: '2-2 歸納文章主旨',
                analysis: '作答時可抓住「小鳥歌唱」、「花兒綻放」、「孩子奔跑」等具體描寫，說明作者如何藉由聲音、色彩與人物活動營造充滿活力的春日景象。'
            },
            {
                stem: '如果你也在這座公園裡，最可能觀察到什麼畫面？請寫出你的想像並說明理由。',
                dimension: '分析推理',
                indicator: '3-1 分析線索',
                analysis: '本題重點在於學生是否能延伸短文情境，提出合理的觀察與感受，並以短文中的線索支持自己的想法。'
            }
        ],
        options: [], answer: '',
        analysis: '',
        attributes: { mainCategory: shortGroupMainCategory, subCategory: shortGroupSubCategory, genre: '語體文' },
        createdAt: '2026-03-08 09:00', updatedAt: '2026-03-09 08:00',
        returnCount: 0, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
        history: [
            { time: '2026-03-08 09:00', user: '劉雅婷', action: '建立草稿', comment: '' },
            { time: '2026-03-09 08:00', user: '劉雅婷', action: '命題完成', comment: '' }
        ]
    },
    {
        id: 'Q-2602-M009', projectId: 'P2026-01', type: 'listenGroup', level: '難度三', difficulty: 'medium',
        status: 'completed',
        stem: '',
        passage: '請先聆聽一段廣播訪談。主持人邀請返鄉創業的青年分享，他如何把家鄉廢棄穀倉改造成社區共學空間，並帶動在地長者與學生一起參與課程。',
        audioUrl: 'demo_audio_group_001.mp3',
        subQuestions: [
            {
                stem: '根據訪談內容，這位青年返鄉後最先進行的工作是什麼？',
                options: [
                    { label: 'A', text: '募集企業贊助' },
                    { label: 'B', text: '整理閒置穀倉空間' },
                    { label: 'C', text: '招募外地講師' },
                    { label: 'D', text: '成立觀光工廠' }
                ],
                answer: 'B',
                level: '難度三',
                competency: '推斷訊息',
                indicator: '推斷訊息邏輯性'
            },
            {
                stem: '主持人認為這個計畫最有價值的地方是什麼？',
                options: [
                    { label: 'A', text: '提高農產品售價' },
                    { label: 'B', text: '吸引大量觀光客' },
                    { label: 'C', text: '讓不同世代在同一空間學習' },
                    { label: 'D', text: '增加地方夜市收入' }
                ],
                answer: 'C',
                level: '難度四',
                competency: '歸納分析',
                indicator: '歸納或總結訊息內容'
            }
        ],
        options: [], answer: '',
        analysis: '',
        attributes: { audioType: '對話', material: '教育' },
        createdAt: '2026-03-08 10:30', updatedAt: '2026-03-09 09:10',
        returnCount: 0, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
        history: [
            { time: '2026-03-08 10:30', user: '劉雅婷', action: '建立草稿', comment: '' },
            { time: '2026-03-09 09:10', user: '劉雅婷', action: '命題完成', comment: '聽力題組含 2 道子題' }
        ]
    },

    // ========== 審修作業區 ==========
    {
        id: 'Q-2602-M010', projectId: 'P2026-01', type: 'single', level: '中級', difficulty: 'medium',
        status: 'peer_editing',
        stem: '下列何者是正確的成語用法？',
        options: [
            { label: 'A', text: '走投無路' }, { label: 'B', text: '按步就班' },
            { label: 'C', text: '破斧沉舟' }, { label: 'D', text: '再乘再勵' }
        ],
        answer: 'A', analysis: '「走投無路」為正確寫法。(B) 應為「按部就班」；(C) 應為「破釜沉舟」；(D) 應為「再接再厲」。',
        createdAt: '2026-02-20 10:00', updatedAt: '2026-03-05 09:15',
        returnCount: 1,
        reviewComment: '選項 (C)「破斧沉舟」的錯誤字建議改為更具迷惑性的寫法，例如「破斧沈舟」，讓學生需要更仔細辨別。另外建議在解析中補充每個成語的正確出處。',
        reviewerName: '王健明', reviewStage: 'peer', revisionReply: '',
        history: [
            { time: '2026-02-20 10:00', user: '劉雅婷', action: '建立草稿', comment: '' },
            { time: '2026-02-25 14:00', user: '劉雅婷', action: '命題完成', comment: '' },
            { time: '2026-02-26 09:00', user: '劉雅婷', action: '送審', comment: '' },
            { time: '2026-03-05 09:15', user: '王健明', action: '互審意見', comment: '選項 (C)「破斧沉舟」的錯誤字建議改為更具迷惑性的寫法。另外建議補充成語出處。' }
        ]
    },
    {
        id: 'Q-2602-M011', projectId: 'P2026-01', type: 'select', level: '高級', difficulty: 'hard',
        status: 'expert_editing',
        stem: '下列何者最能表現「物是人非」的感慨？',
        options: [
            { label: 'A', text: '年年歲歲花相似，歲歲年年人不同' },
            { label: 'B', text: '山重水複疑無路，柳暗花明又一村' },
            { label: 'C', text: '海內存知己，天涯若比鄰' },
            { label: 'D', text: '欲窮千里目，更上一層樓' }
        ],
        answer: 'A', analysis: '「年年歲歲花相似，歲歲年年人不同」正是描寫景物依舊但人事已變的感慨，最能體現「物是人非」。',
        createdAt: '2026-02-18 11:00', updatedAt: '2026-03-04 16:30',
        returnCount: 1,
        reviewComment: '題目設計佳，但選項 (B) 的詩句引用有誤，原文應為「山重水複」而非「山窮水複」。請確認並修正。另外，解析可再補充其他選項的修辭分析。',
        reviewerName: '李教授', reviewStage: 'expert', revisionReply: '',
        history: [
            { time: '2026-02-18 11:00', user: '劉雅婷', action: '建立草稿', comment: '' },
            { time: '2026-02-22 15:00', user: '劉雅婷', action: '命題完成', comment: '' },
            { time: '2026-02-23 09:00', user: '劉雅婷', action: '送審', comment: '' },
            { time: '2026-02-28 10:00', user: '張心怡', action: '互審意見', comment: '題目設計不錯。' },
            { time: '2026-03-04 16:30', user: '李教授', action: '專審意見 (改後再審)', comment: '選項引用有誤，請確認修正。' }
        ]
    },
    {
        id: 'Q-2602-M012', projectId: 'P2026-01', type: 'single', level: '中高級', difficulty: 'medium',
        status: 'final_editing',
        stem: '「亡羊補牢，猶未遲也」這句話的主要寓意為何？',
        options: [
            { label: 'A', text: '牧羊人要注意安全' }, { label: 'B', text: '發現錯誤後及時補救' },
            { label: 'C', text: '羊群走丟了很可惜' }, { label: 'D', text: '圍欄需要定期維修' }
        ],
        answer: 'B', analysis: '此成語出自《戰國策》，意指出了問題後及時補救仍然不晚。',
        createdAt: '2026-02-16 10:00', updatedAt: '2026-03-08 11:00',
        returnCount: 2,
        reviewComment: '解析過於簡略，請補充成語的歷史典故與實際應用情境，讓學生能深入理解。此為第二次退回，請務必修正完善。',
        reviewerName: '林總召', reviewStage: 'final', revisionReply: '',
        history: [
            { time: '2026-02-16 10:00', user: '劉雅婷', action: '建立草稿', comment: '' },
            { time: '2026-02-20 14:00', user: '劉雅婷', action: '命題完成', comment: '' },
            { time: '2026-02-21 09:00', user: '劉雅婷', action: '送審', comment: '' },
            { time: '2026-02-25 10:00', user: '王健明', action: '互審意見', comment: '建議加強解析。' },
            { time: '2026-03-01 14:00', user: '李教授', action: '專審意見 (採用)', comment: '基本合格。' },
            { time: '2026-03-05 10:00', user: '林總召', action: '總召決策 (改後再審)', comment: '解析不夠完整，退回修改。' },
            { time: '2026-03-06 09:00', user: '劉雅婷', action: '修題回覆', comment: '已補充典故出處。' },
            { time: '2026-03-08 11:00', user: '林總召', action: '總召決策 (改後再審)', comment: '解析仍過於簡略，請補充實際應用情境。' }
        ]
    },

    // ========== 審核結果與歷史 ==========
    {
        id: 'Q-2602-M015', projectId: 'P2026-01', type: 'single', level: '中級', difficulty: 'easy',
        status: 'adopted',
        stem: '「千里之行，始於足下」的意思是？',
        options: [
            { label: 'A', text: '走路一千里才能到達目的地' }, { label: 'B', text: '做事要從基礎做起' },
            { label: 'C', text: '腳下的路很長很遠' }, { label: 'D', text: '旅行要穿好鞋子' }
        ],
        answer: 'B', analysis: '此句出自《老子》第六十四章，強調做任何事都要從基礎開始，一步一步來。',
        createdAt: '2026-02-15 10:00', updatedAt: '2026-03-06 14:00',
        returnCount: 0, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
        history: [
            { time: '2026-02-15 10:00', user: '劉雅婷', action: '建立草稿', comment: '' },
            { time: '2026-02-18 14:00', user: '劉雅婷', action: '命題完成', comment: '' },
            { time: '2026-02-19 09:00', user: '劉雅婷', action: '送審', comment: '' },
            { time: '2026-02-24 10:00', user: '張心怡', action: '互審意見', comment: '題目清楚明白，很好。' },
            { time: '2026-03-01 14:00', user: '李教授', action: '專審意見 (採用)', comment: '符合等級，予以採用。' },
            { time: '2026-03-06 14:00', user: '林總召', action: '總召決策 (採用)', comment: '核准入庫。' }
        ]
    },
    {
        id: 'Q-2602-M016', projectId: 'P2026-01', type: 'select', level: '中高級', difficulty: 'hard',
        status: 'adopted',
        stem: '下列何者使用了「對偶」的修辭手法？',
        options: [
            { label: 'A', text: '海記憶體知己，天涯若比鄰' }, { label: 'B', text: '千山鳥飛絕，萬徑人蹤滅' },
            { label: 'C', text: '白日依山盡，黃河入海流' }, { label: 'D', text: '以上皆是' }
        ],
        answer: 'D', analysis: '三個選項皆使用了對偶修辭，上下句字數相同、詞性相對、結構一致。',
        createdAt: '2026-02-16 09:00', updatedAt: '2026-03-07 11:00',
        returnCount: 0, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
        history: [
            { time: '2026-02-16 09:00', user: '劉雅婷', action: '建立草稿', comment: '' },
            { time: '2026-02-20 11:00', user: '劉雅婷', action: '命題完成', comment: '' },
            { time: '2026-02-21 09:00', user: '劉雅婷', action: '送審', comment: '' },
            { time: '2026-02-26 10:00', user: '王健明', action: '互審意見', comment: '精選題型，設計精良。' },
            { time: '2026-03-03 14:00', user: '陳副教授', action: '專審意見 (採用)', comment: '題目優秀。' },
            { time: '2026-03-07 11:00', user: '林總召', action: '總召決策 (採用)', comment: '核准。' }
        ]
    },
    {
        id: 'Q-2602-M017', projectId: 'P2026-01', type: 'single', level: '初級', difficulty: 'easy',
        status: 'rejected',
        stem: '下列何者是水果？',
        options: [
            { label: 'A', text: '蘋果' }, { label: 'B', text: '白菜' },
            { label: 'C', text: '米飯' }, { label: 'D', text: '麵包' }
        ],
        answer: 'A', analysis: '蘋果是水果。',
        createdAt: '2026-02-17 10:00', updatedAt: '2026-03-07 16:00',
        returnCount: 2, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
        history: [
            { time: '2026-02-17 10:00', user: '劉雅婷', action: '建立草稿', comment: '' },
            { time: '2026-02-19 11:00', user: '劉雅婷', action: '命題完成', comment: '' },
            { time: '2026-02-20 09:00', user: '劉雅婷', action: '送審', comment: '' },
            { time: '2026-02-25 10:00', user: '吳家豪', action: '互審意見', comment: '題目過於簡單。' },
            { time: '2026-03-02 14:00', user: '李教授', action: '專審意見 (改後再審)', comment: '需提升鑑別度。' },
            { time: '2026-03-07 16:00', user: '林總召', action: '總召決策 (不採用)', comment: '題目鑑別度過低，不適用於任何等級的正式考試。' }
        ]
    }
];


// ===================================================================
// 狀態管理
// ===================================================================
let currentTab = 'compose';       // 'compose' | 'revision' | 'history'
let filteredQuestions = [];       // 目前篩選後的題目列表
let currentEditingQuestion = null; // 目前正在編輯的題目 (null = 新增模式)
let formMode = 'create';          // 'create' | 'edit' | 'revision' | 'view'
let quillInstance = null;         // Quill 編輯器實例
let activeEditableField = null;   // 目前正在編輯的欄位 DOM 元素
let activeFieldKey = null;        // 目前正在編輯的欄位 key (如 'stem', 'analysis', 'passage')
let isFormSidebarCollapsed = false; // 左側題目屬性欄是否收合
let quillCloseTimer = null;       // Quill 抽屜收合動畫計時器
let currentPage = 1;              // 列表目前頁碼
let pageSize = defaultPageSize;   // 每頁顯示筆數
const quillFontOptions = [
    { value: 'dfkai-sb', label: '標楷體' },
    { value: 'times-new-roman', label: 'Times New Roman' }
];
const registerQuillFormats = () => {
    const Font = Quill.import('formats/font');
    Font.whitelist = quillFontOptions.map((option) => option.value);
    Quill.register(Font, true);
};
const getQuillCharacterCount = (text = '') => Array.from((text || '').replace(/\s/g, '')).length;
const updateQuillWordCount = () => {
    const counter = document.getElementById('quillWordCount');
    if (!counter || !quillInstance) return;
    counter.textContent = `字數：${getQuillCharacterCount(quillInstance.getText())}`;
};


// ===================================================================
// 初始化
// ===================================================================
document.addEventListener('DOMContentLoaded', () => {
    // 權限檢查：僅命題教師可進入
    const userStr = localStorage.getItem('cwt_user');
    if (userStr) {
        const user = JSON.parse(userStr);
        // [DEMO] 為展示方便，暫時允許 ADMIN 也能進入
        if (user.role !== 'ADMIN' && user.role !== 'TEACHER') {
            Swal.fire({
                icon: 'error', title: '權限不足',
                text: '「我的命題任務」為命題教師專屬功能。即將導回首頁。',
                showConfirmButton: false, timer: 2500
            }).then(() => { window.location.href = 'firstpage.html'; });
            return;
        }
    }

    // 初始化 Quill 編輯器
    initQuillEditor();

    // 初始化 Tab 切換
    initTabs();

    // 初始化表單 Modal 事件
    initFormModal();

    // 初始化預覽 Modal
    initPreviewModal();

    // 載入資料
    const projectId = localStorage.getItem('cwt_current_project') || 'P2026-01';
    loadPageData(projectId);

    // Deep Linking: 檢查 URL 參數切換頁籤 (US-008)
    const urlParams = new URLSearchParams(window.location.search);
    const tabParam = urlParams.get('tab');
    if (tabParam && ['compose', 'revision', 'history'].includes(tabParam)) {
        switchTab(tabParam);
    }

    // 監聽專案切換事件
    document.addEventListener('projectChanged', (e) => {
        loadPageData(e.detail.id);
    });
});


// ===================================================================
// 頁面資料載入
// ===================================================================
const loadPageData = (projectId) => {
    renderQuotaCards(projectId);
    renderTabContent();
};


// ===================================================================
// 配額進度卡片
// ===================================================================
const renderQuotaCards = (projectId) => {
    const container = document.getElementById('quotaCardsContainer');
    const quotas = myQuotasDb[projectId] || {};
    const questions = myQuestionsDb.filter(q => q.projectId === projectId);

    let totalTarget = 0;
    let totalDone = 0;

    const typeKeys = Object.keys(qTypeMap);
    let html = '';

    typeKeys.forEach(key => {
        const target = quotas[key] || 0;
        const done = questions.filter(q => q.type === key && q.status !== 'rejected').length;
        totalTarget += target;
        totalDone += done;

        const pct = target > 0 ? Math.min(Math.round((done / target) * 100), 100) : 0;
        const barColor = pct >= 100 ? 'bg-[var(--color-sage)]' : pct >= 60 ? 'bg-[var(--color-morandi)]' : 'bg-[var(--color-terracotta)]';

        html += `
            <div class="bg-white rounded-lg border border-gray-200 p-3 shadow-sm hover:shadow transition-shadow">
                <div class="flex items-center justify-between mb-1">
                    <span class="text-xs font-bold text-gray-500 truncate"><i class="${qTypeIcon[key]} mr-1 opacity-50"></i>${qTypeMap[key]}</span>
                </div>
                <div class="text-lg font-bold text-[var(--color-slate-main)]">${done}<span class="text-sm text-gray-400 font-normal"> / ${target}</span></div>
                <div class="w-full bg-gray-100 rounded-full mt-1.5" style="height:4px;">
                    <div class="quota-bar ${barColor} rounded-full" style="width:${pct}%;"></div>
                </div>
            </div>
        `;
    });

    container.innerHTML = html;
    document.getElementById('quotaTotalProgress').textContent = `總計 ${totalDone} / ${totalTarget} 題`;
};


// ===================================================================
// Tab 切換
// ===================================================================
const initTabs = () => {
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const tab = btn.getAttribute('data-tab');
            switchTab(tab);
        });
    });
};

const switchTab = (tab) => {
    currentTab = tab;
    currentPage = 1;

    // 更新 Tab 按鈕樣式
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.classList.remove('active-compose', 'active-revision', 'active-history');
        if (btn.getAttribute('data-tab') === tab) {
            btn.classList.add(`active-${tab === 'compose' ? 'compose' : tab === 'revision' ? 'revision' : 'history'}`);
        }
    });

    renderTabContent();
};


// ===================================================================
// Tab 內容渲染 (統計 + 篩選 + 列表)
// ===================================================================
const renderTabContent = () => {
    const projectId = localStorage.getItem('cwt_current_project') || 'P2026-01';
    const allQuestions = myQuestionsDb.filter(q => q.projectId === projectId);

    // 計算各 Tab 題數
    const composeQ = allQuestions.filter(q => ['draft', 'completed', 'pending'].includes(q.status));
    const revisionQ = allQuestions.filter(q => ['peer_reviewing', 'peer_editing', 'expert_reviewing', 'expert_editing', 'final_reviewing', 'final_editing'].includes(q.status));
    const historyQ = allQuestions.filter(q => ['adopted', 'rejected'].includes(q.status));

    document.getElementById('tabCountCompose').textContent = composeQ.length;
    document.getElementById('tabCountRevision').textContent = revisionQ.length;
    document.getElementById('tabCountHistory').textContent = historyQ.length;

    // 根據當前 Tab 篩選題目
    let currentQuestions = [];
    if (currentTab === 'compose') currentQuestions = composeQ;
    else if (currentTab === 'revision') currentQuestions = revisionQ;
    else currentQuestions = historyQ;

    renderStatusFilterOptions();
    renderTabStats(applyFiltersWithoutStatus(currentQuestions));
    const pageSizeSelect = document.getElementById('pageSizeSelect');
    if (pageSizeSelect) {
        pageSizeSelect.value = String(pageSize);
    }

    // 套用篩選條件
    filteredQuestions = applyFilters(currentQuestions);

    // 排序
    filteredQuestions = sortQuestions(filteredQuestions);
    currentPage = Math.min(currentPage, getTotalPages(filteredQuestions.length));

    // 渲染列表
    renderQuestionList();
};

const getTabStatsCards = (questions) => {
    if (currentTab === 'compose') {
        return [
            { value: 'all', title: '命題總計', count: questions.length, tone: 'slate' },
            { value: 'draft', title: '命題草稿', count: questions.filter(q => q.status === 'draft').length, tone: 'gray' },
            { value: 'completed', title: '命題完成', count: questions.filter(q => q.status === 'completed').length, tone: 'blue' },
            { value: 'pending', title: '已送審', count: questions.filter(q => q.status === 'pending').length, tone: 'amber' }
        ];
    }

    if (currentTab === 'revision') {
        return [
            { value: 'all', title: '審題總計', count: questions.length, tone: 'slate' },
            { value: 'reviewing', title: '審題鎖定', count: questions.filter(q => revisionReviewingStatuses.includes(q.status)).length, tone: 'blue' },
            { value: 'editing', title: '修題中', count: questions.filter(q => revisionEditingStatuses.includes(q.status)).length, tone: 'amber' }
        ];
    }

    return [
        { value: 'all', title: '全部題目', count: questions.length, tone: 'slate' },
        { value: 'adopted', title: '已採用', count: questions.filter(q => q.status === 'adopted').length, tone: 'emerald' },
        { value: 'rejected', title: '不採用', count: questions.filter(q => q.status === 'rejected').length, tone: 'gray' }
    ];
};

const getStatsCardToneClass = (tone, isActive) => {
    const toneMap = {
        slate: isActive ? 'border-slate-400 bg-slate-50' : 'border-gray-200 bg-white',
        gray: isActive ? 'border-gray-400 bg-gray-50' : 'border-gray-200 bg-white',
        blue: isActive ? 'border-blue-400 bg-blue-50' : 'border-gray-200 bg-white',
        amber: isActive ? 'border-amber-400 bg-amber-50' : 'border-gray-200 bg-white',
        emerald: isActive ? 'border-emerald-400 bg-emerald-50' : 'border-gray-200 bg-white'
    };
    return toneMap[tone] || toneMap.slate;
};

const renderStatsCard = (card, activeFilter) => {
    const isActive = activeFilter === card.value || (card.value === 'all' && activeFilter === 'all');
    return `
        <button type="button" data-status-filter="${card.value}" class="min-w-[150px] flex-1 cursor-pointer rounded-xl border px-4 py-3 text-left transition-colors ${getStatsCardToneClass(card.tone, isActive)} ${isActive ? 'shadow-sm' : ''}">
            <div class="text-sm font-bold text-gray-700">${card.title}</div>
            <div class="mt-3 text-3xl font-bold text-gray-900">${card.count}</div>
            <div class="mt-2 text-xs ${isActive ? 'text-gray-600' : 'text-gray-400'}">${card.value === 'all' ? '顯示全部題目' : '點一下套用篩選'}</div>
        </button>`;
};

/** 渲染各 Tab 的統計卡片 */
const renderTabStats = (questions) => {
    const container = document.getElementById('tabStatsContainer');
    if (!container) return;

    const cards = getTabStatsCards(questions);
    const activeFilter = getStatusFilterValue();
    container.innerHTML = `
        <div class="flex flex-wrap gap-3">
            ${cards.map((card) => renderStatsCard(card, activeFilter)).join('')}
        </div>`;
};


// ===================================================================
// 篩選與排序
// ===================================================================
const getStatusFilterValue = () => document.getElementById('filterStatus')?.value || 'all';

const applyFiltersWithoutStatus = (questions) => {
    const keyword = document.getElementById('filterKeyword').value.trim().toLowerCase();
    const typeVal = document.getElementById('filterType').value;
    const levelVal = document.getElementById('filterLevel').value;

    return questions.filter(q => {
        if (keyword && !getQuestionSearchText(q).includes(keyword)) return false;
        if (typeVal !== 'all' && q.type !== typeVal) return false;
        if (levelVal !== 'all' && q.level !== levelVal) return false;
        return true;
    });
};

const applyFilters = (questions) => {
    const statusVal = getStatusFilterValue();
    return applyFiltersWithoutStatus(questions).filter(q => {
        if (!matchesStatusFilter(q, statusVal)) return false;
        return true;
    });
};

const setStatusFilter = (statusValue = 'all') => {
    const statusSelect = document.getElementById('filterStatus');
    if (!statusSelect) return;

    const nextValue = Array.from(statusSelect.options).some((option) => option.value === statusValue)
        ? statusValue
        : 'all';

    statusSelect.value = nextValue;
    currentPage = 1;
    renderTabContent();
};

const sortQuestions = (questions) => {
    const statusPriority = {
        // 命題作業區排序：草稿 > 完成 > 已送審
        'draft': 0, 'completed': 1, 'pending': 2,
        // 審修作業區排序：審查中 > 修題中
        'peer_reviewing': 0, 'expert_reviewing': 0, 'final_reviewing': 0,
        'peer_editing': 1, 'expert_editing': 1, 'final_editing': 1,
        // 歷史排序
        'adopted': 0, 'rejected': 1
    };

    return [...questions].sort((a, b) => {
        const pa = statusPriority[a.status] ?? 99;
        const pb = statusPriority[b.status] ?? 99;
        if (pa !== pb) return pa - pb;
        return new Date(b.updatedAt) - new Date(a.updatedAt);
    });
};

// 監聽篩選欄位變化
document.getElementById('filterKeyword')?.addEventListener('input', () => {
    currentPage = 1;
    renderTabContent();
});
document.getElementById('filterType')?.addEventListener('change', (e) => {
    currentPage = 1;
    // 依題型切換等級下拉選項（一般單選題 → 初級到中高級；短文題組 → 高級到優級；聽力 → 難度一到五；其餘題型 → 初級到優級）
    syncLevelDropdown(document.getElementById('filterLevel'), e.target.value);
    renderTabContent();
});
document.getElementById('filterLevel')?.addEventListener('change', () => {
    currentPage = 1;
    renderTabContent();
});
document.getElementById('filterStatus')?.addEventListener('change', () => {
    currentPage = 1;
    renderTabContent();
});
document.getElementById('tabStatsContainer')?.addEventListener('click', (e) => {
    const trigger = e.target.closest('[data-status-filter]');
    if (!trigger) return;
    setStatusFilter(trigger.getAttribute('data-status-filter') || 'all');
});
document.getElementById('pageSizeSelect')?.addEventListener('change', (e) => {
    pageSize = Number(e.target.value) || defaultPageSize;
    currentPage = 1;
    renderTabContent();
});

/**
 * 依據題型切換等級下拉選項的顯示
 * 一般單選題 → 顯示「初級 / 中級 / 中高級」
 * 短文題組 → 顯示「高級 / 優級」
 * 聽力題型 → 顯示「難度一～難度五」
 * 其他題型 → 顯示「初級～優級」
 * @param {HTMLSelectElement} levelSelect - 等級下拉元素
 * @param {string} typeValue - 題型值
 */
const syncLevelDropdown = (levelSelect, typeValue) => {
    if (!levelSelect) return;
    const allowedLevels = new Set(typeLevelOptions[typeValue] || typeLevelOptions.all);

    levelSelect.querySelectorAll('option').forEach((opt) => {
        if (opt.value === '' || opt.value === 'all') return;
        const shouldShow = allowedLevels.has(opt.value);
        opt.classList.toggle('hidden', !shouldShow);
        opt.disabled = !shouldShow;
    });

    const currentVal = levelSelect.value;
    if (currentVal !== 'all' && currentVal !== '') {
        const selectedOpt = levelSelect.querySelector(`option[value="${currentVal}"]`);
        if (selectedOpt && selectedOpt.disabled) {
            levelSelect.value = levelSelect.id === 'filterLevel' ? 'all' : '';
        }
    }
};

const matchesStatusFilter = (question, statusValue) => {
    if (statusValue === 'all') return true;

    if (currentTab === 'revision') {
        if (statusValue === 'reviewing') return revisionReviewingStatuses.includes(question.status);
        if (statusValue === 'editing') return revisionEditingStatuses.includes(question.status);
    }

    return question.status === statusValue;
};

const renderStatusFilterOptions = () => {
    const statusSelect = document.getElementById('filterStatus');
    if (!statusSelect) return;

    const currentValue = statusSelect.value || 'all';
    const options = tabStatusFilterOptions[currentTab] || tabStatusFilterOptions.compose;
    statusSelect.innerHTML = options.map(option => `<option value="${option.value}">${option.label}</option>`).join('');
    statusSelect.value = options.some(option => option.value === currentValue) ? currentValue : 'all';
};

const getTotalPages = (totalItems = filteredQuestions.length) => Math.max(Math.ceil(totalItems / pageSize), 1);

const getVisibleQuestions = () => {
    const startIndex = (currentPage - 1) * pageSize;
    return filteredQuestions.slice(startIndex, startIndex + pageSize);
};

const updateListMeta = (totalItems, totalPages) => {
    document.getElementById('listCount').textContent = totalItems;
    const pageMeta = document.getElementById('listPageMeta');
    if (!pageMeta) return;
    pageMeta.textContent = totalItems > 0 ? `・ 第 ${currentPage} / ${totalPages} 頁` : '';
};

const goToListPage = (page) => {
    const totalPages = getTotalPages();
    currentPage = Math.min(Math.max(page, 1), totalPages);
    renderQuestionList();
    document.getElementById('questionListContainer')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
};

const renderPagination = (totalItems, totalPages) => {
    const container = document.getElementById('listPagination');
    if (!container) return;

    if (totalItems === 0 || totalPages <= 1) {
        container.classList.add('hidden');
        container.innerHTML = '';
        return;
    }

    const startIndex = (currentPage - 1) * pageSize + 1;
    const endIndex = Math.min(currentPage * pageSize, totalItems);
    const pageButtons = [];
    const windowStart = Math.max(1, currentPage - 1);
    const windowEnd = Math.min(totalPages, currentPage + 1);

    const buildPageButton = (page) => `
        <button onclick="goToListPage(${page})"
            class="min-w-9 px-3 py-2 text-sm rounded-lg border transition-colors ${page === currentPage ? 'border-[var(--color-morandi)] bg-[var(--color-morandi)] text-white font-bold' : 'border-gray-200 bg-white text-gray-600 hover:border-[var(--color-morandi)] hover:text-[var(--color-morandi)]'}">
            ${page}
        </button>`;

    if (windowStart > 1) {
        pageButtons.push(buildPageButton(1));
        if (windowStart > 2) {
            pageButtons.push('<span class="px-1 text-sm text-gray-300">…</span>');
        }
    }

    for (let page = windowStart; page <= windowEnd; page += 1) {
        pageButtons.push(buildPageButton(page));
    }

    if (windowEnd < totalPages) {
        if (windowEnd < totalPages - 1) {
            pageButtons.push('<span class="px-1 text-sm text-gray-300">…</span>');
        }
        pageButtons.push(buildPageButton(totalPages));
    }

    container.classList.remove('hidden');
    container.innerHTML = `
        <div class="text-xs text-gray-400">顯示第 ${startIndex}-${endIndex} 題，共 ${totalItems} 題</div>
        <div class="flex items-center justify-end gap-1.5 flex-wrap w-full sm:w-auto">
            <button onclick="goToListPage(${currentPage - 1})" ${currentPage === 1 ? 'disabled' : ''}
                class="px-3 py-2 text-sm rounded-lg border border-gray-200 bg-white text-gray-600 transition-colors ${currentPage === 1 ? 'opacity-40 cursor-not-allowed' : 'hover:border-[var(--color-morandi)] hover:text-[var(--color-morandi)]'}">
                上一頁
            </button>
            ${pageButtons.join('')}
            <button onclick="goToListPage(${currentPage + 1})" ${currentPage === totalPages ? 'disabled' : ''}
                class="px-3 py-2 text-sm rounded-lg border border-gray-200 bg-white text-gray-600 transition-colors ${currentPage === totalPages ? 'opacity-40 cursor-not-allowed' : 'hover:border-[var(--color-morandi)] hover:text-[var(--color-morandi)]'}">
                下一頁
            </button>
        </div>`;
};


// ===================================================================
// 題目列表渲染
// ===================================================================
const renderQuestionList = () => {
    const container = document.getElementById('questionListContainer');
    const emptyState = document.getElementById('emptyState');
    const totalPages = getTotalPages(filteredQuestions.length);
    const visibleQuestions = getVisibleQuestions();

    updateListMeta(filteredQuestions.length, totalPages);

    if (filteredQuestions.length === 0) {
        container.innerHTML = '';
        emptyState.classList.remove('hidden');
        renderPagination(0, totalPages);
        const emptyTexts = {
            'compose': '目前沒有命題作業，點擊右上角「新增試題」開始命題。',
            'revision': '目前沒有待修題的試題，您辛苦了！',
            'history': '此梯次尚無已審核的試題紀錄。'
        };
        document.getElementById('emptyStateText').textContent = emptyTexts[currentTab];
        return;
    }

    emptyState.classList.add('hidden');

    let html = '';
    visibleQuestions.forEach(q => {
        const st = statusMap[q.status] || {};
        const isRevision = currentTab === 'revision';
        const isEditable = q.status.endsWith('_editing');
        const { stemPreview, optionPreview } = getQuestionPreviewMeta(q);
        const stemText = truncate(stemPreview, 80);

        let revisionBlock = '';
        if (isRevision && q.reviewComment) {
            const stageName = reviewStageLabel[q.reviewStage] || '審查';
            revisionBlock = `
                <div class="mt-3 p-3 rounded-lg bg-amber-50 border border-amber-200 text-sm">
                    <div class="flex items-center gap-2 mb-1">
                        <span class="text-xs font-bold text-[var(--color-terracotta)] bg-[var(--color-terracotta)]/10 px-1.5 py-0.5 rounded">${stageName}意見</span>
                        <span class="text-xs text-gray-500">${q.reviewerName || '匿名'}</span>
                        ${q.returnCount >= 2 ? '<span class="text-xs text-red-600 font-bold ml-auto">⚠ 第 ' + q.returnCount + ' 次退回</span>' : ''}
                    </div>
                    <p class="text-gray-700 leading-relaxed">${truncate(q.reviewComment, 100)}</p>
                </div>`;
        }

        let actionBtns = '';
        if (currentTab === 'compose') {
            if (q.status === 'draft') {
                actionBtns = `
                    <button onclick="openFormModal('edit', '${q.id}')" class="text-xs px-3 py-1.5 bg-[var(--color-morandi)] text-white rounded-md hover:bg-[#5b7a95] transition-colors cursor-pointer font-medium">編輯</button>
                    <button onclick="deleteQuestion('${q.id}')" class="text-xs px-3 py-1.5 border border-red-200 rounded-md hover:bg-red-50 text-red-500 transition-colors cursor-pointer font-medium"><i class="fa-regular fa-trash-can mr-1"></i>刪除</button>`;
            } else if (q.status === 'completed') {
                actionBtns = `
                    <button onclick="openFormModal('edit', '${q.id}')" class="text-xs px-3 py-1.5 bg-[var(--color-morandi)] text-white rounded-md hover:bg-[#5b7a95] transition-colors cursor-pointer font-medium">編輯</button>
                    <button onclick="submitQuestion('${q.id}')" class="text-xs px-3 py-1.5 bg-[var(--color-sage)] text-white rounded-md hover:bg-[#7a9a82] transition-colors cursor-pointer font-medium">命題送審</button>`;
            } else if (q.status === 'pending') {
                actionBtns = `
                    <button onclick="openFormModal('view', '${q.id}')" class="text-xs px-3 py-1.5 border border-gray-300 rounded-md hover:bg-gray-50 text-gray-600 transition-colors cursor-pointer font-medium">檢視</button>`;
            }
        } else if (currentTab === 'revision') {
            if (isEditable) {
                actionBtns = `
                    <button onclick="openFormModal('revision', '${q.id}')" class="text-xs px-3 py-1.5 bg-[var(--color-terracotta)] text-white rounded-md hover:bg-[#c87a5e] transition-colors cursor-pointer font-bold">進入修題</button>
                    <button onclick="openFormModal('view', '${q.id}')" class="text-xs px-3 py-1.5 border border-gray-300 rounded-md hover:bg-gray-50 text-gray-600 transition-colors cursor-pointer font-medium">檢視</button>`;
            } else {
                actionBtns = '<span class="text-xs text-blue-500 font-medium"><i class="fa-solid fa-lock mr-1"></i>審查中，暫時無法操作</span>';
            }
        } else {
            actionBtns = `
                <button onclick="openFormModal('view', '${q.id}')" class="text-xs px-3 py-1.5 border border-gray-300 rounded-md hover:bg-gray-50 text-gray-600 transition-colors cursor-pointer font-medium">檢視詳情</button>`;
        }

        html += `
            <div class="q-card ${isRevision && isEditable ? 'q-card-revision' : ''} bg-white p-4 sm:p-5 hover:bg-gray-50/50 transition-colors">
                <div class="flex flex-col sm:flex-row sm:items-start gap-3">
                    <div class="flex-shrink-0 sm:w-40">
                        <div class="font-mono text-sm font-bold text-[var(--color-morandi)]">${q.id}</div>
                        <div class="flex flex-wrap items-center gap-1.5 mt-1">
                            <span class="text-xs px-1.5 py-0.5 bg-gray-100 text-gray-600 rounded font-medium"><i class="${qTypeIcon[q.type]} mr-0.5 text-[10px]"></i> ${qTypeMap[q.type]}</span>
                            <span class="text-xs text-gray-400">${getQuestionMetaLine(q)}</span>
                        </div>
                    </div>
                    <div class="flex-grow min-w-0">
                        <p class="text-sm text-gray-700 leading-relaxed mb-1 line-clamp-2">${stemText}</p>
                        <div class="text-xs text-gray-400 leading-relaxed">${optionPreview}</div>
                        ${revisionBlock}
                    </div>
                    <div class="flex-shrink-0 flex flex-col items-end gap-2 sm:w-40">
                        <span class="text-xs px-2.5 py-1 rounded-full font-bold border ${st.color} ${st.border}">${st.label}</span>
                        <div class="text-[10px] text-gray-400">${q.updatedAt}</div>
                        <div class="flex items-center gap-1.5 mt-1">${actionBtns}</div>
                    </div>
                </div>
            </div>`;
    });

    container.innerHTML = html;
    renderPagination(filteredQuestions.length, totalPages);
};


// ===================================================================
// 表單 Modal
// ===================================================================
const initFormModal = () => {
    // 返回列表按鈕
    document.getElementById('formBackBtn').addEventListener('click', closeFormModal);

    // 點擊背景：新增/編輯模式存為草稿並關閉；檢視模式直接關閉
    document.getElementById('formBackdrop').addEventListener('click', () => {
        if (formMode === 'view') {
            closeFormModal();
        } else {
            saveAsDraft();
            closeFormModal();
        }
    });

    // 存為草稿
    document.getElementById('formDraftBtn').addEventListener('click', () => {
        saveAsDraft();
        closeFormModal();
    });

    // 命題完成 / 完成修題 / 送審
    document.getElementById('formSubmitBtn').addEventListener('click', handleFormSubmit);

    // 預覽按鈕
    document.getElementById('formPreviewBtn').addEventListener('click', showExamPreview);

    // 題目屬性欄收合
    document.getElementById('formSidebarToggleBtn').addEventListener('click', () => {
        setFormSidebarCollapsed(!isFormSidebarCollapsed);
    });
    window.addEventListener('resize', () => setFormSidebarCollapsed(isFormSidebarCollapsed));
    setFormSidebarCollapsed(isFormSidebarCollapsed);

    // 題型切換 → 連動等級下拉 + 重渲染編輯區
    document.getElementById('formType').addEventListener('change', (e) => {
        const nextType = e.target.value;
        syncLevelDropdown(document.getElementById('formLevel'), nextType);
        updateFormCommonAttributeVisibility(nextType);
        renderTypeSpecificAttributes(nextType);
        renderFormEditorContent(nextType);
    });

    // 新增試題按鈕
    document.getElementById('newQuestionBtn').addEventListener('click', () => {
        openFormModal('create');
    });
};

/** 開啟表單 Modal */
const openFormModal = (mode, questionId = null) => {
    formMode = mode;
    currentEditingQuestion = questionId ? myQuestionsDb.find(q => q.id === questionId) : null;

    const modal = document.getElementById('formModal');
    const panel = document.getElementById('formPanel');
    const backdrop = document.getElementById('formBackdrop');
    const revBanner = document.getElementById('revisionBanner');
    const revReplyArea = document.getElementById('revisionReplyArea');
    const title = document.getElementById('formTitle');
    const statusBadge = document.getElementById('formStatusBadge');
    const submitLabel = document.getElementById('formSubmitLabel');
    const submitBtn = document.getElementById('formSubmitBtn');
    const draftBtn = document.getElementById('formDraftBtn');

    modal.classList.remove('hidden');
    requestAnimationFrame(() => {
        backdrop.classList.remove('opacity-0');
        panel.classList.remove('opacity-0');
        panel.classList.add('modal-animate-in');
    });

    enableFormInputs();

    // 設定 Modal 標題與按鈕
    if (mode === 'create') {
        title.textContent = '新增試題';
        statusBadge.textContent = '草稿';
        statusBadge.className = 'text-xs px-2 py-0.5 rounded-full bg-gray-100 text-gray-500 font-bold border border-gray-200';
        submitLabel.textContent = '命題完成';
        submitBtn.classList.remove('hidden');
        draftBtn.classList.remove('hidden');
        revBanner.classList.add('hidden');
        revReplyArea.classList.add('hidden');
    } else if (mode === 'edit') {
        title.textContent = '編輯試題';
        const st = statusMap[currentEditingQuestion.status];
        statusBadge.textContent = st.label;
        statusBadge.className = `text-xs px-2 py-0.5 rounded-full font-bold border ${st.color} ${st.border}`;
        submitLabel.textContent = '命題完成';
        submitBtn.classList.remove('hidden');
        draftBtn.classList.remove('hidden');
        revBanner.classList.add('hidden');
        revReplyArea.classList.add('hidden');
    } else if (mode === 'revision') {
        title.textContent = '修題作業';
        const st = statusMap[currentEditingQuestion.status];
        statusBadge.textContent = st.label;
        statusBadge.className = `text-xs px-2 py-0.5 rounded-full font-bold border ${st.color} ${st.border}`;
        submitLabel.textContent = '完成修題';
        submitBtn.classList.remove('hidden');
        submitBtn.className = 'px-4 py-1.5 text-sm bg-[var(--color-terracotta)] hover:bg-[#c87a5e] text-white rounded-lg font-bold transition-colors cursor-pointer';
        draftBtn.classList.remove('hidden');
        // 顯示修題意見橫幅
        revBanner.classList.remove('hidden');
        const stageName = reviewStageLabel[currentEditingQuestion.reviewStage] || '審查';
        document.getElementById('revBannerStage').textContent = `${stageName}意見`;
        document.getElementById('revBannerReviewer').textContent = `審查人：${currentEditingQuestion.reviewerName || '匿名'}`;
        document.getElementById('revBannerCount').textContent = `退回次數：${currentEditingQuestion.returnCount}/2`;
        document.getElementById('revBannerComment').textContent = currentEditingQuestion.reviewComment || '';
        // 顯示修題回覆區
        revReplyArea.classList.remove('hidden');
        document.getElementById('revisionReplyInput').value = currentEditingQuestion.revisionReply || '';
    } else {
        // view mode
        title.textContent = '檢視試題';
        const st = statusMap[currentEditingQuestion.status];
        statusBadge.textContent = st.label;
        statusBadge.className = `text-xs px-2 py-0.5 rounded-full font-bold border ${st.color} ${st.border}`;
        submitBtn.classList.add('hidden');
        draftBtn.classList.add('hidden');
        revBanner.classList.add('hidden');
        revReplyArea.classList.add('hidden');
    }

    // 填充左側表單
    populateFormSidebar();

    // 渲染右側編輯區域，並同步等級下拉選項
    const typeValue = currentEditingQuestion ? currentEditingQuestion.type : 'single';
    document.getElementById('formType').value = typeValue;
    syncLevelDropdown(document.getElementById('formLevel'), typeValue);
    updateFormCommonAttributeVisibility(typeValue);
    renderTypeSpecificAttributes(typeValue);
    renderFormEditorContent(typeValue);
    setFormSidebarCollapsed(isFormSidebarCollapsed);

    // 若為檢視模式，禁用所有輸入
    if (mode === 'view') {
        disableFormInputs();
    }
};

/** 關閉表單 Modal */
const closeFormModal = () => {
    // 先關閉 Quill 編輯器
    closeQuillEditor();

    const modal = document.getElementById('formModal');
    const panel = document.getElementById('formPanel');
    const backdrop = document.getElementById('formBackdrop');

    backdrop.classList.add('opacity-0');
    panel.classList.remove('modal-animate-in');
    panel.classList.add('opacity-0');

    setTimeout(() => {
        modal.classList.add('hidden');
        // 恢復送審按鈕樣式
        document.getElementById('formSubmitBtn').className = 'px-4 py-1.5 text-sm bg-[var(--color-sage)] hover:bg-[#7a9a82] text-white rounded-lg font-bold transition-colors cursor-pointer';
        currentEditingQuestion = null;
        formMode = 'create';
    }, 300);
};

/** 填充左側屬性表單 */
const populateFormSidebar = () => {
    const q = currentEditingQuestion;
    document.getElementById('formType').value = q?.type || 'single';
    document.getElementById('formLevel').value = q?.level || '';
    document.getElementById('formDifficulty').value = q?.difficulty || '';
};

const getChoiceAttributeFieldConfig = (type = 'single') => (
    type === 'select'
        ? {
            title: '精選單選題分類',
            levelOptions: typeLevelOptions.select,
            topicId: 'formSelectTopic',
            subtopicId: 'formSelectSubtopic',
            hintId: 'selectSubtopicHint'
        }
        : {
            title: '一般單選題分類',
            levelOptions: typeLevelOptions.single,
            topicId: 'formSingleTopic',
            subtopicId: 'formSingleSubtopic',
            hintId: 'singleSubtopicHint'
        }
);

const getChoiceAttributeDefaults = () => ({
    topic: currentEditingQuestion?.attributes?.topic || '',
    subtopic: currentEditingQuestion?.attributes?.subtopic || ''
});

const getChoiceSelection = (type = 'single') => {
    const defaults = getChoiceAttributeDefaults();
    const fieldConfig = getChoiceAttributeFieldConfig(type);
    return {
        topic: document.getElementById(fieldConfig.topicId)?.value ?? defaults.topic,
        subtopic: document.getElementById(fieldConfig.subtopicId)?.value ?? defaults.subtopic
    };
};
const getLongTextAttributeDefaults = () => ({
    mode: currentEditingQuestion?.attributes?.mode || ''
});

const getLongTextSelection = () => {
    const defaults = getLongTextAttributeDefaults();
    return {
        mode: document.getElementById('formLongTextMode')?.value ?? defaults.mode
    };
};

const getReadGroupAttributeDefaults = () => ({
    genre: currentEditingQuestion?.attributes?.genre || ''
});

const getReadGroupSelection = () => {
    const defaults = getReadGroupAttributeDefaults();
    return {
        genre: document.getElementById('formReadGroupGenre')?.value ?? defaults.genre
    };
};

const getShortGroupAttributeDefaults = () => ({
    mainCategory: shortGroupMainCategory,
    subCategory: shortGroupSubCategory,
    genre: currentEditingQuestion?.attributes?.genre || ''
});

const getShortGroupSelection = () => {
    const defaults = getShortGroupAttributeDefaults();
    return {
        mainCategory: shortGroupMainCategory,
        subCategory: shortGroupSubCategory,
        genre: document.getElementById('formShortGroupGenre')?.value ?? defaults.genre
    };
};

const getListenAttributeDefaults = () => ({
    audioType: currentEditingQuestion?.attributes?.audioType || '',
    material: currentEditingQuestion?.attributes?.material || ''
});

const getListenSelection = () => {
    const defaults = getListenAttributeDefaults();
    return {
        audioType: document.getElementById('formListenAudioType')?.value ?? defaults.audioType,
        material: document.getElementById('formListenMaterial')?.value ?? defaults.material
    };
};

const syncListenCompetency = () => {
    const level = document.getElementById('formLevel')?.value || '';
    const mapping = listenCompetencyMap[level] || { competency: '', indicator: '' };
    const compEl = document.getElementById('formListenCompetency');
    const indEl = document.getElementById('formListenIndicator');
    if (compEl) compEl.value = mapping.competency;
    if (indEl) indEl.value = mapping.indicator;
};

const syncChoiceSubtopicOptions = (type, topic, selectedSubtopic = '') => {
    const fieldConfig = getChoiceAttributeFieldConfig(type);
    const subtopicSelect = document.getElementById(fieldConfig.subtopicId);
    const hintContainer = document.getElementById(fieldConfig.hintId);
    if (!subtopicSelect || !hintContainer) return;

    const subtopics = singleChoiceCategoryMap[topic] || [];
    subtopicSelect.innerHTML = `
        <option value="">${topic ? '請選擇次類' : '請先選擇主題'}</option>
        ${subtopics.map((subtopic) => `<option value="${subtopic}">${subtopic}</option>`).join('')}
    `;

    const normalizedSubtopic = subtopics.includes(selectedSubtopic) ? selectedSubtopic : '';
    subtopicSelect.disabled = !topic;
    subtopicSelect.classList.toggle('opacity-60', !topic);
    subtopicSelect.classList.toggle('cursor-not-allowed', !topic);
    subtopicSelect.value = normalizedSubtopic;
    subtopicSelect.onchange = () => {
        syncChoiceSubtopicOptions(type, topic, subtopicSelect.value);
    };

    hintContainer.className = subtopics.length ? 'grid grid-cols-2 gap-1.5' : 'flex flex-wrap gap-2';
    hintContainer.innerHTML = subtopics.length
        ? subtopics.map((subtopic) => `
            <span class="rounded-lg border px-2 py-1 text-[11px] leading-4 transition-colors ${subtopic === normalizedSubtopic ? 'border-[var(--color-sage)] bg-[var(--color-sage)]/12 text-[var(--color-sage)] font-semibold' : 'border-gray-200 bg-white/80 text-gray-500'}">
                ${subtopic}
            </span>`).join('')
        : '<span class="text-[11px] text-gray-400">先選主題，次類才會跟著出來。</span>';
};

const renderTypeSpecificAttributes = (type, presetSelection = null) => {
    const container = document.getElementById('formTypeSpecificAttributes');
    if (!container) return;

    if (type === 'single' || type === 'select') {
        const selection = presetSelection || getChoiceSelection(type);
        const selectedTopic = selection.topic || '';
        const selectedSubtopic = selection.subtopic || '';
        const fieldConfig = getChoiceAttributeFieldConfig(type);

        container.innerHTML = `
            <section class="rounded-2xl border border-[var(--color-morandi)]/15 bg-gradient-to-br from-[var(--color-morandi)]/10 via-white to-[var(--color-sage)]/10 p-3 space-y-3 shadow-sm">
                <div class="space-y-1">
                    <p class="text-xs font-bold uppercase tracking-[0.16em] text-[var(--color-morandi)]">${fieldConfig.title}</p>
                </div>
                <div class="rounded-xl bg-white/75 px-3 py-2 text-[11px] leading-4 text-gray-500 border border-white/70">
                    <span class="font-semibold text-gray-600">適用等級：</span>${fieldConfig.levelOptions.join(' / ')}
                </div>
                <div class="grid grid-cols-2 gap-2">
                    <div>
                        <label class="block text-xs font-bold text-gray-700 mb-1">主題 <span class="text-red-400">*</span></label>
                        <select id="${fieldConfig.topicId}" class="w-full px-2.5 py-2 text-[13px] border border-white/70 bg-white/90 rounded-xl focus:outline-none focus:ring-1 focus:ring-[var(--color-morandi)] cursor-pointer shadow-sm">
                            <option value="">請選擇主題</option>
                            ${singleChoiceTopics.map((topic) => `<option value="${topic}" ${selectedTopic === topic ? 'selected' : ''}>${topic}</option>`).join('')}
                        </select>
                    </div>
                    <div>
                        <label class="block text-xs font-bold text-gray-700 mb-1">次類 <span class="text-red-400">*</span></label>
                        <select id="${fieldConfig.subtopicId}" class="w-full px-2.5 py-2 text-[13px] border border-white/70 bg-white/90 rounded-xl focus:outline-none focus:ring-1 focus:ring-[var(--color-morandi)] cursor-pointer shadow-sm"></select>
                    </div>
                </div>
                <div class="space-y-1.5">
                    <p class="text-[11px] font-bold text-gray-500">次類總覽</p>
                    <div id="${fieldConfig.hintId}"></div>
                </div>
            </section>`;

        document.getElementById(fieldConfig.topicId)?.addEventListener('change', (e) => {
            syncChoiceSubtopicOptions(type, e.target.value, '');
        });
        syncChoiceSubtopicOptions(type, selectedTopic, selectedSubtopic);
        return;
    }
    if (type === 'longText') {
        const selection = presetSelection || getLongTextSelection();
        const selectedMode = selection.mode || '';

        container.innerHTML = `
            <section class="rounded-2xl border border-[var(--color-terracotta)]/18 bg-gradient-to-br from-[var(--color-terracotta)]/10 via-white to-[var(--color-oatmeal)] p-3 space-y-3 shadow-sm">
                <div class="space-y-1">
                    <p class="text-xs font-bold uppercase tracking-[0.16em] text-[var(--color-terracotta)]">長文題目設定</p>
                </div>
                <div class="rounded-xl bg-white/80 px-3 py-2 text-[11px] leading-4 text-gray-500 border border-white/70">
                    <span class="font-semibold text-gray-600">適用等級：</span>${typeLevelOptions.longText.join(' / ')}
                </div>
                <div>
                    <label class="block text-xs font-bold text-gray-700 mb-1">題型 <span class="text-red-400">*</span></label>
                    <select id="formLongTextMode" class="w-full px-2.5 py-2 text-[13px] border border-white/70 bg-white/90 rounded-xl focus:outline-none focus:ring-1 focus:ring-[var(--color-terracotta)] cursor-pointer shadow-sm">
                        <option value="">請選擇題型</option>
                        ${longTextModeOptions.map((mode) => `<option value="${mode}" ${selectedMode === mode ? 'selected' : ''}>${mode}</option>`).join('')}
                    </select>
                </div>
            </section>`;
        return;
    }

    if (type === 'readGroup') {
        const selection = presetSelection || getReadGroupSelection();
        const selectedGenre = selection.genre || '';

        container.innerHTML = `
            <section class="rounded-2xl border border-[var(--color-oatmeal)] bg-gradient-to-br from-[var(--color-oatmeal)] via-white to-[var(--color-sage)]/10 p-3 space-y-3 shadow-sm">
                <div class="space-y-1">
                    <p class="text-xs font-bold uppercase tracking-[0.16em] text-[var(--color-terracotta)]">閱讀題組設定</p>
                </div>
                <div class="rounded-xl bg-white/80 px-3 py-2 text-[11px] leading-4 text-gray-500 border border-white/70">
                    <span class="font-semibold text-gray-600">適用等級：</span>${typeLevelOptions.readGroup.join(' / ')}
                </div>
                <div>
                    <label class="block text-xs font-bold text-gray-700 mb-1">文體 <span class="text-red-400">*</span></label>
                    <select id="formReadGroupGenre" class="w-full px-2.5 py-2 text-[13px] border border-white/70 bg-white/90 rounded-xl focus:outline-none focus:ring-1 focus:ring-[var(--color-terracotta)] cursor-pointer shadow-sm">
                        <option value="">請選擇文體</option>
                        ${shortGroupGenreOptions.map((genre) => `<option value="${genre}" ${selectedGenre === genre ? 'selected' : ''}>${genre}</option>`).join('')}
                    </select>
                </div>
            </section>`;
        return;
    }

    if (type === 'shortGroup') {
        const selection = presetSelection || getShortGroupSelection();
        const selectedGenre = selection.genre || '';

        container.innerHTML = `
            <section class="rounded-2xl border border-[var(--color-sage)]/20 bg-gradient-to-br from-[var(--color-sage)]/10 via-white to-[var(--color-morandi)]/10 p-3 space-y-3 shadow-sm">
                <div class="space-y-1">
                    <p class="text-xs font-bold uppercase tracking-[0.16em] text-[var(--color-sage)]">短文題組設定</p>
                </div>
                <div class="rounded-xl bg-white/80 px-3 py-2 text-[11px] leading-4 text-gray-500 border border-white/70">
                    <span class="font-semibold text-gray-600">主類／次類：</span>${shortGroupMainCategory} / ${shortGroupSubCategory}
                </div>
                <div class="rounded-xl bg-white/80 px-3 py-2 text-[11px] leading-4 text-gray-500 border border-white/70">
                    <span class="font-semibold text-gray-600">適用等級：</span>${typeLevelOptions.shortGroup.join(' / ')}
                </div>
                <div class="grid grid-cols-2 gap-2">
                    <div>
                        <label class="block text-xs font-bold text-gray-700 mb-1">主類</label>
                        <input type="text" value="${shortGroupMainCategory}" disabled class="w-full px-2.5 py-2 text-[13px] border border-white/70 bg-gray-100 text-gray-500 rounded-xl cursor-not-allowed">
                    </div>
                    <div>
                        <label class="block text-xs font-bold text-gray-700 mb-1">次類</label>
                        <input type="text" value="${shortGroupSubCategory}" disabled class="w-full px-2.5 py-2 text-[13px] border border-white/70 bg-gray-100 text-gray-500 rounded-xl cursor-not-allowed">
                    </div>
                </div>
                <div>
                    <label class="block text-xs font-bold text-gray-700 mb-1">文體 <span class="text-red-400">*</span></label>
                    <select id="formShortGroupGenre" class="w-full px-2.5 py-2 text-[13px] border border-white/70 bg-white/90 rounded-xl focus:outline-none focus:ring-1 focus:ring-[var(--color-sage)] cursor-pointer shadow-sm">
                        <option value="">請選擇文體</option>
                        ${shortGroupGenreOptions.map((genre) => `<option value="${genre}" ${selectedGenre === genre ? 'selected' : ''}>${genre}</option>`).join('')}
                    </select>
                </div>
            </section>`;
        return;
    }

    if (type === 'listenGroup') {
        const selection = presetSelection || getListenSelection();
        const selectedAudioType = selection.audioType || '';
        const selectedMaterial = selection.material || '';
        container.innerHTML = `
            <section class="rounded-2xl border border-[var(--color-morandi)]/15 bg-gradient-to-br from-[var(--color-morandi)]/10 via-white to-[var(--color-sage)]/10 p-3 space-y-3 shadow-sm">
                <div class="space-y-1">
                    <p class="text-xs font-bold uppercase tracking-[0.16em] text-[var(--color-morandi)]">聽力題組設定</p>
                </div>
                <div class="rounded-xl bg-white/75 px-3 py-2 text-[11px] leading-4 text-gray-500 border border-white/70 space-y-1.5">
                    <div><span class="font-semibold text-gray-600">固定子題：</span>只有 2 題，不可新增或刪除</div>
                    <div>第 1 題：難度三 / 推斷訊息</div>
                    <div>第 2 題：難度四 / 歸納分析</div>
                </div>
                <div>
                    <label class="block text-xs font-bold text-gray-700 mb-1">語音類型 <span class="text-red-400">*</span></label>
                    <select id="formListenAudioType" class="w-full px-2.5 py-2 text-[13px] border border-white/70 bg-white/90 rounded-xl focus:outline-none focus:ring-1 focus:ring-[var(--color-morandi)] cursor-pointer shadow-sm">
                        <option value="">請選擇語音類型</option>
                        ${listenAudioTypeOptions.map(t => `<option value="${t}" ${selectedAudioType === t ? 'selected' : ''}>${t}</option>`).join('')}
                    </select>
                </div>
                <div>
                    <label class="block text-xs font-bold text-gray-700 mb-1">素材分類 <span class="text-red-400">*</span></label>
                    <select id="formListenMaterial" class="w-full px-2.5 py-2 text-[13px] border border-white/70 bg-white/90 rounded-xl focus:outline-none focus:ring-1 focus:ring-[var(--color-morandi)] cursor-pointer shadow-sm">
                        <option value="">請選擇素材分類</option>
                        ${listenMaterialOptions.map(m => `<option value="${m}" ${selectedMaterial === m ? 'selected' : ''}>${m}</option>`).join('')}
                    </select>
                </div>
            </section>`;
        return;
    }

    if (type === 'listen') {
        const selection = presetSelection || getListenSelection();
        const selectedAudioType = selection.audioType || '';
        const selectedMaterial = selection.material || '';
        const currentLevel = document.getElementById('formLevel')?.value || '';
        const mapping = listenCompetencyMap[currentLevel] || { competency: '', indicator: '' };
        container.innerHTML = `
            <section class="rounded-2xl border border-[var(--color-morandi)]/15 bg-gradient-to-br from-[var(--color-morandi)]/10 via-white to-[var(--color-sage)]/10 p-3 space-y-3 shadow-sm">
                <div class="space-y-1">
                    <p class="text-xs font-bold uppercase tracking-[0.16em] text-[var(--color-morandi)]">聽力測驗設定</p>
                </div>
                <div class="rounded-xl bg-white/75 px-3 py-2 text-[11px] leading-4 text-gray-500 border border-white/70">
                    <span class="font-semibold text-gray-600">適用等級：</span>${listenLevelOptions.join(' / ')}
                </div>
                <div>
                    <label class="block text-xs font-bold text-gray-700 mb-1">核心能力</label>
                    <input type="text" id="formListenCompetency" value="${mapping.competency}" readonly class="w-full px-2.5 py-2 text-[13px] border border-white/70 bg-gray-100 text-gray-500 rounded-xl cursor-not-allowed">
                </div>
                <div>
                    <label class="block text-xs font-bold text-gray-700 mb-1">細目指標</label>
                    <input type="text" id="formListenIndicator" value="${mapping.indicator}" readonly class="w-full px-2.5 py-2 text-[13px] border border-white/70 bg-gray-100 text-gray-500 rounded-xl cursor-not-allowed">
                </div>
                <div>
                    <label class="block text-xs font-bold text-gray-700 mb-1">語音類型 <span class="text-red-400">*</span></label>
                    <select id="formListenAudioType" class="w-full px-2.5 py-2 text-[13px] border border-white/70 bg-white/90 rounded-xl focus:outline-none focus:ring-1 focus:ring-[var(--color-morandi)] cursor-pointer shadow-sm">
                        <option value="">請選擇語音類型</option>
                        ${listenAudioTypeOptions.map(t => `<option value="${t}" ${selectedAudioType === t ? 'selected' : ''}>${t}</option>`).join('')}
                    </select>
                </div>
                <div>
                    <label class="block text-xs font-bold text-gray-700 mb-1">素材分類 <span class="text-red-400">*</span></label>
                    <select id="formListenMaterial" class="w-full px-2.5 py-2 text-[13px] border border-white/70 bg-white/90 rounded-xl focus:outline-none focus:ring-1 focus:ring-[var(--color-morandi)] cursor-pointer shadow-sm">
                        <option value="">請選擇素材分類</option>
                        ${listenMaterialOptions.map(m => `<option value="${m}" ${selectedMaterial === m ? 'selected' : ''}>${m}</option>`).join('')}
                    </select>
                </div>
            </section>`;

        // 等級連動：formLevel 改變時更新核心能力與細目指標
        const levelEl = document.getElementById('formLevel');
        if (levelEl) {
            levelEl._listenHandler = () => syncListenCompetency();
            levelEl.addEventListener('change', levelEl._listenHandler);
        }

        return;
    }

    container.innerHTML = '';
};

const getFormSidebarExpandedWidth = () => (window.matchMedia('(min-width: 1024px)').matches ? 304 : 256);

const updateFormCommonAttributeVisibility = (type) => {
    const levelWrap = document.getElementById('formLevelWrap');
    const difficultyWrap = document.getElementById('formDifficultyWrap');
    const divider = document.getElementById('formCommonAttributeDivider');
    const shouldShow = shouldUseCommonLevelAndDifficulty(type);

    [levelWrap, difficultyWrap, divider].forEach((el) => {
        if (!el) return;
        el.classList.toggle('hidden', !shouldShow);
    });

    if (!shouldShow) {
        const levelEl = document.getElementById('formLevel');
        const difficultyEl = document.getElementById('formDifficulty');
        if (levelEl) levelEl.value = '';
        if (difficultyEl) difficultyEl.value = '';
    }
};

const setFormSidebarCollapsed = (collapsed) => {
    const sidebar = document.getElementById('formSidebar');
    const sidebarBody = document.getElementById('formSidebarBody');
    const sidebarFooter = document.getElementById('formSidebarFooter');
    const toggleBtn = document.getElementById('formSidebarToggleBtn');
    const toggleLabel = document.getElementById('formSidebarToggleLabel');
    const toggleIcon = document.getElementById('formSidebarToggleIcon');
    if (!sidebar || !sidebarBody || !sidebarFooter || !toggleBtn || !toggleLabel || !toggleIcon) return;

    const expandedWidth = getFormSidebarExpandedWidth();
    const toggleOpenLeft = Math.max(expandedWidth - 1, 0);

    isFormSidebarCollapsed = collapsed;
    sidebar.style.width = collapsed ? '0px' : `${expandedWidth}px`;
    sidebar.style.minWidth = collapsed ? '0px' : `${expandedWidth}px`;
    sidebar.style.flexBasis = collapsed ? '0px' : `${expandedWidth}px`;
    sidebar.style.borderRightWidth = collapsed ? '0px' : '1px';
    sidebar.style.transform = collapsed ? 'translateX(-22px)' : 'translateX(0)';
    sidebar.style.boxShadow = collapsed ? 'none' : 'inset -1px 0 0 rgba(226, 232, 240, 0.9)';

    sidebarBody.style.opacity = collapsed ? '0' : '1';
    sidebarBody.style.transform = collapsed ? 'translateX(-32px)' : 'translateX(0)';
    sidebarBody.style.pointerEvents = collapsed ? 'none' : 'auto';

    sidebarFooter.style.opacity = collapsed ? '0' : '1';
    sidebarFooter.style.transform = collapsed ? 'translateX(-32px)' : 'translateX(0)';
    sidebarFooter.style.pointerEvents = collapsed ? 'none' : 'auto';
    toggleBtn.style.left = collapsed ? '0px' : `${toggleOpenLeft}px`;
    toggleBtn.style.boxShadow = collapsed
        ? '0 16px 30px rgba(15, 23, 42, 0.14)'
        : '0 12px 24px rgba(15, 23, 42, 0.08)';
    toggleBtn.style.backgroundColor = collapsed ? 'rgba(255, 255, 255, 0.98)' : 'rgba(255, 255, 255, 0.94)';
    toggleLabel.textContent = collapsed ? '展開題目屬性' : '收合題目屬性';
    toggleBtn.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
    toggleBtn.setAttribute('aria-label', toggleLabel.textContent);
    toggleBtn.setAttribute('title', toggleLabel.textContent);
    toggleIcon.className = collapsed ? 'fa-solid fa-chevron-right text-sm' : 'fa-solid fa-chevron-left text-sm';
};
/** 依題型渲染右側編輯區內容 */
const renderFormEditorContent = (type) => {
    const container = document.getElementById('formEditorArea');
    const q = currentEditingQuestion;
    const config = getTypeConfig(type);
    const answerSection = document.getElementById('formAnswerSection');
    answerSection.style.display = 'none';

    let html = '<div class="p-6 lg:p-8 space-y-6 max-w-4xl mx-auto">';

    if (config.hasAudio) {
        html += `
            <div>
                <label class="block text-sm font-bold text-gray-700 mb-2"><i class="fa-solid fa-headphones mr-1 text-[var(--color-morandi)]"></i> 聽力音檔</label>
                <div class="border-2 border-dashed border-gray-300 rounded-xl p-6 text-center bg-gray-50 hover:border-[var(--color-morandi)] transition-colors cursor-pointer">
                    <i class="fa-solid fa-cloud-arrow-up text-3xl text-gray-300 mb-2"></i>
                    <p class="text-sm text-gray-500">點擊或拖曳上傳音檔 (MP3/WAV)</p>
                    ${q?.audioUrl ? `<p class="text-xs text-[var(--color-sage)] mt-2"><i class="fa-solid fa-check-circle mr-1"></i> 已上傳: ${q.audioUrl}</p>` : '<p class="text-xs text-gray-400 mt-2">尚未上傳音檔</p>'}
                </div>
            </div>`;
    }

    const renderStemField = (label, placeholder = `點擊此處開始編輯${label}...`, required = false) => {
        const stemContent = q?.stem || '';
        html += `
            <div class="space-y-3">
                <label class="block text-sm font-bold text-gray-700 mb-2"><i class="fa-solid fa-pen mr-1 text-[var(--color-morandi)]"></i> ${label}${required ? ' <span class="text-red-400">*</span>' : ''}</label>
                <div class="editable-field bg-white text-sm text-gray-700 leading-relaxed" data-field="stem" onclick="activateQuillField(this, 'stem', '${label}')">
                    ${stemContent || `<span class="text-gray-400 italic">${placeholder}</span>`}
                </div>
            </div>`;
    };

    const renderPassageField = (label, required = false, hintHtml = '') => {
        const passageContent = q?.passage || '';
        html += `
            <div class="space-y-3">
                <label class="block text-sm font-bold text-gray-700 mb-2"><i class="fa-solid fa-book-open mr-1 text-[var(--color-morandi)]"></i> ${label}${required ? ' <span class="text-red-400">*</span>' : ''}</label>
                ${hintHtml}
                <div class="editable-field bg-white text-sm text-gray-700 leading-relaxed" data-field="passage" onclick="activateQuillField(this, 'passage', '${label}')">
                    ${passageContent || `<span class="text-gray-400 italic">點擊此處開始輸入${label}...</span>`}
                </div>
            </div>`;
    };

    const typesWithStemFirst = new Set(['longText', 'readGroup', 'shortGroup']);
    const requiredStemTypes = new Set(['readGroup', 'shortGroup']);

    if (config.hasStem && typesWithStemFirst.has(type)) {
        const placeholder = type === 'longText' ? '點擊此處開始編輯題目...' : `點擊此處開始編輯${config.stemLabel}...`;
        renderStemField(config.stemLabel, placeholder, requiredStemTypes.has(type));
    }

    if (config.hasPassage) {
        const longTextPassageHint = type === 'longText'
            ? `
            <div class="rounded-xl border border-amber-100 bg-amber-50 px-4 py-3 text-sm text-amber-800 leading-relaxed">
                <div class="font-bold mb-1"><i class="fa-solid fa-image mr-1"></i> 文章內容可附圖</div>
                <p>點擊文章內容後，可用底部編輯器工具列插入圖片，適合放題幹情境圖或資料圖表。</p>
            </div>`
            : '';
        const shortGroupHint = type === 'shortGroup'
            ? `
            <div class="rounded-xl border border-emerald-100 bg-emerald-50 px-4 py-3 text-sm text-emerald-800 leading-relaxed">
                <div class="font-bold mb-1"><i class="fa-solid fa-circle-info mr-1"></i> 短文題組母題提醒</div>
                <p>母題包含「題目」與「文章內容」，兩者皆為必填；文章內容可附圖。子題請補上主向度與能力指標，方便後續審題判讀。</p>
            </div>`
            : longTextPassageHint;
        const readGroupHint = type === 'readGroup'
            ? `
            <div class="rounded-xl border border-amber-100 bg-amber-50 px-4 py-3 text-sm text-amber-800 leading-relaxed">
                <div class="font-bold mb-1"><i class="fa-solid fa-circle-info mr-1"></i> 閱讀題組母題提醒</div>
                <p>母題包含「標題」與「文章內容」，兩者皆為必填；文章內容可附圖。子題再補上選項、答案與解析即可。</p>
            </div>`
            : shortGroupHint;
        renderPassageField(config.passageLabel, ['longText', 'readGroup', 'shortGroup', 'listenGroup'].includes(type), readGroupHint);
    }

    if (config.hasStem && !typesWithStemFirst.has(type)) {
        renderStemField(config.stemLabel);
    }

    if (config.hasOptions) {
        const optionWritingHint = `
            <div class="mb-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800 leading-relaxed flex items-start gap-3">
                <i class="fa-solid fa-circle-exclamation mt-0.5"></i>
                <p>請避免選項長短、語氣明顯差異，以免影響鑑別度。</p>
            </div>`;
        html += `
            <div>
                <div class="flex items-center justify-between mb-2">
                    <label class="block text-sm font-bold text-gray-700"><i class="fa-solid fa-list-ol mr-1 text-[var(--color-morandi)]"></i> 選項與答案</label>
                    <span class="text-xs text-gray-400">請勾選正確答案，可點選內容插入圖片</span>
                </div>
                ${optionWritingHint}
                <div class="space-y-3" id="formOptionsContainer">`;

        optionLabels.forEach((label, i) => {
            const optText = q?.options?.[i]?.text || '';
            const isCorrect = q?.answer === label;
            html += `
                    <div class="flex items-start gap-3 rounded-xl border border-gray-200 bg-white px-4 py-3">
                        <label class="flex items-center gap-2 text-sm font-bold text-gray-600 cursor-pointer pt-2">
                            <input type="radio" name="formAnswer" value="${label}" ${isCorrect ? 'checked' : ''} class="text-[var(--color-sage)] focus:ring-[var(--color-sage)]">
                            <span class="w-7 h-7 rounded-full flex items-center justify-center ${isCorrect ? 'bg-[var(--color-sage)] text-white' : 'bg-gray-100 text-gray-500'}">${label}</span>
                        </label>
                        <div class="editable-field bg-white text-sm text-gray-700 leading-relaxed flex-grow min-h-[76px]" data-option-index="${i}" data-option-label="${label}" onclick="activateQuillField(this, 'option-${i}', '選項 (${label})')">
                            ${optText || `<span class="text-gray-400 italic">點擊此處開始編輯選項 (${label})，可插入圖片...</span>`}
                        </div>
                    </div>`;
        });

        html += '</div></div>';
    }

    if (config.hasSubQuestions) {
        const subQuestions = type === 'listenGroup'
            ? getNormalizedListenGroupSubQuestions(q?.subQuestions || [])
            : (q?.subQuestions?.length ? q.subQuestions : [getDefaultSubQuestion(config.subQuestionMode)]);
        const subQuestionLabel = config.subQuestionMode === 'freeResponse'
            ? '子題區（自由作答）'
            : (type === 'readGroup' ? '子題區' : (type === 'listenGroup' ? '子題列表（固定 2 題）' : '子題列表'));
        const canManageSubQuestions = type !== 'listenGroup';
        html += `
            <div>
                <div class="flex items-center justify-between mb-3">
                    <label class="text-sm font-bold text-gray-700"><i class="fa-solid fa-layer-group mr-1 text-[var(--color-morandi)]"></i> ${subQuestionLabel}</label>
                    ${canManageSubQuestions
                ? `<button onclick="addSubQuestion()" class="text-xs px-3 py-1.5 bg-[var(--color-morandi)] text-white rounded-md hover:bg-[#5b7a95] transition-colors cursor-pointer font-medium">
                        <i class="fa-solid fa-plus mr-1"></i> 新增子題
                    </button>`
                : `<span class="text-xs font-medium text-gray-500">固定 2 題，不可新增或刪除</span>`}
                </div>
                <div class="space-y-4" id="subQuestionsContainer">`;

        subQuestions.forEach((subQuestion, index) => {
            html += renderSubQuestionBlock(subQuestion, index, config.subQuestionMode, type);
        });

        html += '</div></div>';
    }

    if (!['shortGroup', 'readGroup', 'listenGroup'].includes(type)) {
        const analysisContent = q?.analysis || '';
        const analysisLabel = config.analysisLabel || '解析';
        const analysisPlaceholder = config.analysisPlaceholder || '點擊此處編輯解析說明...';
        html += `
            <div>
                <label class="block text-sm font-bold text-gray-700 mb-2"><i class="fa-regular fa-lightbulb mr-1 text-yellow-500"></i> ${analysisLabel}</label>
                <div class="editable-field bg-white text-sm text-gray-700 leading-relaxed" data-field="analysis" onclick="activateQuillField(this, 'analysis', '${analysisLabel}')">
                    ${analysisContent || `<span class="text-gray-400 italic">${analysisPlaceholder}</span>`}
                </div>
            </div>`;
    }

    if (q && q.history && q.history.length > 0 && (formMode === 'view' || formMode === 'revision')) {
        html += `
            <div class="border-t border-gray-200 pt-6">
                <h3 class="text-sm font-bold text-gray-700 mb-4"><i class="fa-solid fa-clock-rotate-left mr-1 text-[var(--color-sage)]"></i> 歷程軌跡</h3>
                <div class="relative border-l-2 border-gray-200 ml-3 pl-5 space-y-4">`;

        [...q.history].reverse().forEach((h, i) => {
            const isLatest = i === 0;
            html += `
                    <div class="relative">
                        <div class="absolute -left-[1.625rem] top-1 w-3 h-3 rounded-full border-2 ${isLatest ? 'bg-[var(--color-morandi)] border-[var(--color-morandi)]' : 'bg-white border-gray-300'}"></div>
                        <div class="text-xs text-gray-400 mb-0.5">${h.time}</div>
                        <div class="text-sm"><span class="font-bold text-gray-700">${h.user}</span> <span class="text-gray-500">${h.action}</span></div>
                        ${h.comment ? `<p class="text-xs text-gray-500 mt-0.5 bg-gray-50 p-2 rounded">${h.comment}</p>` : ''}
                    </div>`;
        });

        html += '</div></div>';
    }

    html += '</div>';
    container.innerHTML = html;
};

/** 渲染子題區塊 */
const renderSubQuestionBlock = (sq, idx, mode = 'choice', type = '') => {
    const listenGroupConfig = type === 'listenGroup' ? getListenGroupQuestionConfig(idx) : null;
    const isReadGroup = type === 'readGroup';
    const isListenGroup = type === 'listenGroup';
    const stemContent = sq.stem || '';
    let html = `
        <div class="bg-white border border-gray-200 rounded-xl p-4 shadow-sm" data-sub-index="${idx}">
            <div class="flex items-center justify-between mb-3">
                <span class="text-sm font-bold text-[var(--color-morandi)]" data-sub-title>第 ${idx + 1} 題</span>
                ${isListenGroup ? '' : `<button data-sub-remove onclick="removeSubQuestion(${idx})" class="text-xs text-red-400 hover:text-red-600 transition-colors cursor-pointer">
                    <i class="fa-solid fa-trash-can mr-0.5"></i> 刪除
                </button>`}
            </div>
            ${listenGroupConfig ? `<div class="mb-3 flex flex-wrap items-center gap-2 text-xs">
                <span class="inline-flex items-center rounded-full border border-[var(--color-morandi)]/20 bg-[var(--color-morandi)]/10 px-3 py-1 font-medium text-[var(--color-morandi)]">${listenGroupConfig.level}</span>
                <span class="inline-flex items-center rounded-full border border-[var(--color-sage)]/20 bg-[var(--color-sage)]/10 px-3 py-1 font-medium text-[var(--color-sage)]">核心能力：${listenGroupConfig.competency}</span>
                <span class="inline-flex items-center rounded-full border border-[var(--color-oatmeal)] bg-[var(--color-oatmeal)]/70 px-3 py-1 font-medium text-[var(--color-slate-main)]">指標：${listenGroupConfig.indicator}</span>
            </div>` : ''}
            <div class="mb-3 space-y-2">
                <label class="block text-xs font-bold text-gray-600">題目內容 <span class="text-red-400">*</span></label>
                ${isReadGroup
            ? `<div class="editable-field bg-white text-sm text-gray-700 leading-relaxed min-h-[92px]" data-sub-stem="${idx}" onclick="activateQuillField(this, 'subStem-${idx}', '第 ${idx + 1} 題題目內容')">
                        ${stemContent || '<span class="text-gray-400 italic">點擊此處開始編輯題目內容，可插入圖片...</span>'}
                    </div>`
            : `<input type="text" class="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-[var(--color-morandi)]"
                    data-sub-stem="${idx}" value="${escapeHtml(sq.stem || '')}" placeholder="輸入子題內容...">`}
            </div>`;

    if (mode === 'freeResponse') {
        const { selectedDimension, indicatorOptions, selectedIndicator } = getNormalizedShortGroupSelection(sq);

        html += `
            <div class="mb-3 grid grid-cols-1 md:grid-cols-2 gap-3">
                <div>
                    <label class="block text-xs font-bold text-gray-600 mb-1">主向度 <span class="text-red-400">*</span></label>
                    <select data-sub-dimension="${idx}" onchange="syncShortGroupIndicator(this, ${idx})" class="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-[var(--color-morandi)]">
                        ${shortGroupDimensionOptions.map((dimension) => `<option value="${dimension}" ${dimension === selectedDimension ? 'selected' : ''}>${dimension}</option>`).join('')}
                    </select>
                </div>
                <div>
                    <label class="block text-xs font-bold text-gray-600 mb-1">能力指標 <span class="text-red-400">*</span></label>
                    <select data-sub-indicator="${idx}" class="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-[var(--color-morandi)]">
                        ${indicatorOptions.map((indicator) => `<option value="${indicator}" ${indicator === selectedIndicator ? 'selected' : ''}>${indicator}</option>`).join('')}
                    </select>
                </div>
            </div>
            <div class="mb-3 px-3 py-2 bg-gray-50 border border-dashed border-gray-200 rounded-lg text-sm text-gray-500 flex items-center gap-2 w-max">
                <i class="fa-solid fa-pen-to-square"></i> 自由作答
            </div>
            <div>
                <label class="block text-xs font-bold text-gray-600 mb-1">試題解析</label>
                <textarea class="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-[var(--color-morandi)] resize-none"
                    rows="3" data-sub-analysis="${idx}" placeholder="請簡要說明本題的評分重點或作答方向...">${escapeHtml(sq.analysis || '')}</textarea>
            </div>`;
    } else {
        const choiceOptionHint = (isReadGroup || isListenGroup)
            ? `
                <div class="mb-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800 leading-relaxed flex items-start gap-3">
                    <i class="fa-solid fa-circle-exclamation mt-0.5"></i>
                    <p>請避免選項長短、語氣明顯差異，以免影響鑑別度。</p>
                </div>`
            : '';
        html += `
            <div class="mb-3">
                <div class="flex items-center justify-between mb-2">
                    <span class="text-xs font-bold text-gray-500">選項與答案</span>
                    <span class="text-xs text-gray-400">請勾選正確答案，可點選內容插入圖片</span>
                </div>
                ${choiceOptionHint}
                <div class="grid grid-cols-2 gap-3">`;
        optionLabels.forEach((label, optionIndex) => {
            const optText = sq.options?.[optionIndex]?.text || '';
            const isCorrect = sq.answer === label;
            html += `
                <div class="flex items-start gap-3 rounded-xl border border-gray-200 bg-white px-4 py-3 min-w-0">
                    <label class="flex items-center gap-2 text-sm font-bold text-gray-600 cursor-pointer flex-shrink-0 pt-2">
                        <input type="radio" name="subAnswer-${idx}" value="${label}" ${isCorrect ? 'checked' : ''} class="text-[var(--color-sage)] focus:ring-[var(--color-sage)]">
                        <span class="w-7 h-7 rounded-full flex items-center justify-center ${isCorrect ? 'bg-[var(--color-sage)] text-white' : 'bg-gray-100 text-gray-500'}">${label}</span>
                    </label>
                    <div class="editable-field bg-white text-sm text-gray-700 leading-relaxed flex-grow min-h-[76px] min-w-0" data-sub-option="${idx}-${optionIndex}" data-option-label="${label}" onclick="activateQuillField(this, 'subOption-${idx}-${optionIndex}', '第 ${idx + 1} 題選項 (${label})')">
                        ${optText || `<span class="text-gray-400 italic">點擊此處開始編輯選項 (${label})，可插入圖片...</span>`}
                    </div>
                </div>`;
        });

        html += `
                </div>
            </div>`;

        if (isReadGroup || isListenGroup) {
            const analysisLabel = isListenGroup ? '試題解析 <span class="text-red-400">*</span>' : '試題解析（紀錄答案理由） <span class="text-red-400">*</span>';
            const analysisPlaceholder = isListenGroup
                ? '請說明為什麼選這個答案，可補充關鍵聽力線索或判斷依據...'
                : '點擊此處開始編輯解析，可說明正確答案依據與其他選項錯誤原因...';
            html += isReadGroup
                ? `
            <div class="space-y-2">
                <label class="block text-xs font-bold text-gray-600">${analysisLabel}</label>
                <div class="editable-field bg-white text-sm text-gray-700 leading-relaxed min-h-[120px]" data-sub-analysis="${idx}" onclick="activateQuillField(this, 'subAnalysis-${idx}', '第 ${idx + 1} 題試題解析')">
                    ${sq.analysis || `<span class="text-gray-400 italic">${analysisPlaceholder}</span>`}
                </div>
            </div>`
                : `
            <div>
                <label class="block text-xs font-bold text-gray-600 mb-1">${analysisLabel}</label>
                <textarea class="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-[var(--color-morandi)] resize-y"
                    rows="4" data-sub-analysis="${idx}" placeholder="${analysisPlaceholder}">${escapeHtml(sq.analysis || '')}</textarea>
            </div>`;
        }
    }

    html += '</div>';
    return html;
};

const syncSubQuestionIndices = () => {
    const container = document.getElementById('subQuestionsContainer');
    if (!container) return;

    Array.from(container.children).forEach((el, idx) => {
        el.setAttribute('data-sub-index', idx);
        const titleEl = el.querySelector('[data-sub-title]');
        if (titleEl) titleEl.textContent = `第 ${idx + 1} 題`;
        el.querySelector('[data-sub-remove]')?.setAttribute('onclick', `removeSubQuestion(${idx})`);

        const stemField = el.querySelector('[data-sub-stem]');
        if (stemField) {
            stemField.setAttribute('data-sub-stem', idx);
            if (stemField.classList.contains('editable-field')) {
                stemField.setAttribute('onclick', `activateQuillField(this, 'subStem-${idx}', '第 ${idx + 1} 題題目內容')`);
            }
        }

        const analysisField = el.querySelector('[data-sub-analysis]');
        if (analysisField) {
            analysisField.setAttribute('data-sub-analysis', idx);
            if (analysisField.classList.contains('editable-field')) {
                analysisField.setAttribute('onclick', `activateQuillField(this, 'subAnalysis-${idx}', '第 ${idx + 1} 題試題解析')`);
            }
        }

        const isFreeResponseBlock = Boolean(el.querySelector('[data-sub-dimension]'));
        if (isFreeResponseBlock) {
            el.querySelector('[data-sub-dimension]')?.setAttribute('data-sub-dimension', idx);
            el.querySelector('[data-sub-indicator]')?.setAttribute('data-sub-indicator', idx);
            el.querySelector('[data-sub-dimension]')?.setAttribute('onchange', `syncShortGroupIndicator(this, ${idx})`);
            return;
        }

        el.querySelectorAll('[data-sub-option]').forEach((input, optionIndex) => {
            const label = input.getAttribute('data-option-label') || optionLabels[optionIndex];
            input.setAttribute('data-sub-option', `${idx}-${optionIndex}`);
            input.setAttribute('onclick', `activateQuillField(this, 'subOption-${idx}-${optionIndex}', '第 ${idx + 1} 題選項 (${label})')`);
        });

        el.querySelectorAll('input[type="radio"]').forEach((radio) => {
            radio.name = `subAnswer-${idx}`;
        });
    });
};

/** 新增子題 */
const addSubQuestion = () => {
    const container = document.getElementById('subQuestionsContainer');
    const type = document.getElementById('formType').value;
    const mode = getTypeConfig(type).subQuestionMode;
    if (!container || type === 'listenGroup') return;
    const currentCount = container.children.length;
    container.insertAdjacentHTML('beforeend', renderSubQuestionBlock(getDefaultSubQuestion(mode), currentCount, mode, type));
    syncSubQuestionIndices();
};

const syncShortGroupIndicator = (dimensionSelect, subIndex) => {
    const indicatorSelect = document.querySelector(`[data-sub-indicator="${subIndex}"]`);
    if (!dimensionSelect || !indicatorSelect) return;

    const indicators = shortGroupDimensionMap[dimensionSelect.value] || [];
    const fallback = indicators[0] || '';
    const previous = indicatorSelect.value;

    indicatorSelect.innerHTML = indicators
        .map((indicator) => `<option value="${indicator}">${indicator}</option>`)
        .join('');
    indicatorSelect.value = indicators.includes(previous) ? previous : fallback;
};

/** 刪除子題 */
const removeSubQuestion = (idx) => {
    const type = document.getElementById('formType')?.value;
    if (type === 'listenGroup') return;
    const container = document.getElementById('subQuestionsContainer');
    if (!container || container.children.length <= 1) {
        Swal.fire({ icon: 'warning', title: '至少需保留一道子題', toast: true, position: 'top-end', showConfirmButton: false, timer: 1500 });
        return;
    }
    container.children[idx]?.remove();
    syncSubQuestionIndices();
};

const enableFormInputs = () => {
    const formArea = document.getElementById('formPanel');
    if (!formArea) return;

    formArea.querySelectorAll('input, select, textarea').forEach(el => {
        el.disabled = false;
        el.classList.remove('opacity-60', 'cursor-not-allowed');
    });
    formArea.querySelectorAll('.editable-field').forEach(el => {
        el.style.pointerEvents = 'auto';
        el.classList.remove('opacity-60');
    });
};

/** 禁用所有表單輸入 (檢視模式) */
const disableFormInputs = () => {
    const formArea = document.getElementById('formPanel');
    formArea.querySelectorAll('input, select, textarea').forEach(el => {
        el.disabled = true;
        el.classList.add('opacity-60', 'cursor-not-allowed');
    });
    formArea.querySelectorAll('.editable-field').forEach(el => {
        el.style.pointerEvents = 'none';
        el.classList.add('opacity-60');
    });
    formArea.querySelectorAll('button').forEach(btn => {
        if (!['formBackBtn', 'formPreviewBtn', 'previewCloseBtn'].includes(btn.id) && !btn.closest('#formPanel > div:first-child')) {
            // 只保留返回和預覽按鈕可用
        }
    });
};


// ===================================================================
// 表單資料收集與儲存
// ===================================================================

/** 收集題型專屬屬性 */
const collectTypeSpecificAttributes = (type) => {
    if (type === 'single' || type === 'select') {
        const fieldConfig = getChoiceAttributeFieldConfig(type);
        return {
            topic: document.getElementById(fieldConfig.topicId)?.value || '',
            subtopic: document.getElementById(fieldConfig.subtopicId)?.value || ''
        };
    }
    if (type === 'longText') {
        return {
            mode: document.getElementById('formLongTextMode')?.value || ''
        };
    }

    if (type === 'readGroup') {
        return {
            genre: document.getElementById('formReadGroupGenre')?.value || ''
        };
    }

    if (type === 'shortGroup') {
        return {
            mainCategory: shortGroupMainCategory,
            subCategory: shortGroupSubCategory,
            genre: document.getElementById('formShortGroupGenre')?.value || ''
        };
    }

    if (type === 'listen' || type === 'listenGroup') {
        return {
            audioType: document.getElementById('formListenAudioType')?.value || '',
            material: document.getElementById('formListenMaterial')?.value || ''
        };
    }

    return {};
};

/** 判斷 Quill HTML 是否真的有內容 */
const hasMeaningfulHtmlContent = (html = '') => {
    const normalizedHtml = html || '';
    return Boolean(stripHtml(normalizedHtml).trim() || /<img[\s>]/i.test(normalizedHtml));
};

/** 取得表單欄位內容 */
const getFormControlContent = (element) => {
    if (!element) return '';

    if (element.classList?.contains('editable-field')) {
        if (element.querySelector('.text-gray-400.italic')) {
            return '';
        }
        const html = element.innerHTML || '';
        return hasMeaningfulHtmlContent(html) ? html : '';
    }

    if (typeof element.value === 'string') {
        return element.value || '';
    }

    return '';
};

/** 從表單收集當前資料 */
const collectFormData = () => {
    const type = document.getElementById('formType').value;
    const config = getTypeConfig(type);
    const useCommonLevelAndDifficulty = shouldUseCommonLevelAndDifficulty(type);
    const level = useCommonLevelAndDifficulty ? document.getElementById('formLevel').value : '';
    const difficulty = useCommonLevelAndDifficulty ? document.getElementById('formDifficulty').value : '';
    const data = { type, level, difficulty, attributes: collectTypeSpecificAttributes(type) };

    document.querySelectorAll('#formEditorArea .editable-field[data-field]').forEach((el) => {
        const field = el.getAttribute('data-field');
        if (field) data[field] = getFormControlContent(el);
    });

    if (!config.hasPassage) data.passage = '';
    if (!config.hasStem) data.stem = '';

    if (config.hasOptions) {
        data.options = [];
        document.querySelectorAll('#formOptionsContainer .editable-field[data-option-index]').forEach((input) => {
            data.options.push({
                label: input.getAttribute('data-option-label'),
                text: getFormControlContent(input)
            });
        });
        const checkedRadio = document.querySelector('input[name="formAnswer"]:checked');
        data.answer = checkedRadio ? checkedRadio.value : '';
    } else {
        data.options = [];
        data.answer = '';
    }

    if (config.hasSubQuestions) {
        data.subQuestions = [];
        document.querySelectorAll('#subQuestionsContainer > div').forEach((block, idx) => {
            const subQuestion = {
                stem: getFormControlContent(block.querySelector(`[data-sub-stem="${idx}"]`))
            };

            if (config.subQuestionMode === 'freeResponse') {
                subQuestion.dimension = block.querySelector(`[data-sub-dimension="${idx}"]`)?.value || '';
                subQuestion.indicator = block.querySelector(`[data-sub-indicator="${idx}"]`)?.value || '';
                subQuestion.analysis = getFormControlContent(block.querySelector(`[data-sub-analysis="${idx}"]`));
            } else {
                subQuestion.options = optionLabels.map((label, optionIndex) => ({
                    label,
                    text: getFormControlContent(block.querySelector(`[data-sub-option="${idx}-${optionIndex}"]`))
                }));
                const checkedRadio = block.querySelector(`input[name="subAnswer-${idx}"]:checked`);
                subQuestion.answer = checkedRadio ? checkedRadio.value : '';
                if (type === 'readGroup' || type === 'listenGroup') {
                    subQuestion.analysis = getFormControlContent(block.querySelector(`[data-sub-analysis="${idx}"]`));
                }
                if (type === 'listenGroup') {
                    const fixedConfig = getListenGroupQuestionConfig(idx);
                    subQuestion.level = fixedConfig.level;
                    subQuestion.competency = fixedConfig.competency;
                    subQuestion.indicator = fixedConfig.indicator;
                }
            }

            data.subQuestions.push(subQuestion);
        });
    } else {
        data.subQuestions = [];
    }

    if (['shortGroup', 'readGroup', 'listenGroup'].includes(type)) {
        data.analysis = '';
    }

    if (formMode === 'revision') {
        data.revisionReply = document.getElementById('revisionReplyInput')?.value || '';
    }

    return data;
};

/** 存為草稿 */
const saveAsDraft = () => {
    const data = collectFormData();
    const now = new Date().toISOString().replace('T', ' ').substring(0, 16);

    if (currentEditingQuestion) {
        // 更新現有題目
        Object.assign(currentEditingQuestion, data);
        currentEditingQuestion.updatedAt = now;
        if (formMode === 'revision') {
            currentEditingQuestion.revisionReply = data.revisionReply;
        }
    } else {
        // 新增題目
        const newId = `Q-2602-M${String(myQuestionsDb.length + 100).padStart(3, '0')}`;
        const newQ = {
            id: newId,
            projectId: localStorage.getItem('cwt_current_project') || 'P2026-01',
            ...data,
            status: 'draft',
            passage: data.passage || '',
            createdAt: now, updatedAt: now,
            returnCount: 0, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
            history: [{ time: now, user: '劉雅婷', action: '建立草稿', comment: '' }]
        };
        myQuestionsDb.push(newQ);
    }

    renderTabContent();
    showAutoSaveToast();
};

/** 命題完成 / 完成修題 */
const handleFormSubmit = () => {
    const data = collectFormData();
    const now = new Date().toISOString().replace('T', ' ').substring(0, 16);

    // 基本驗證
    if (shouldUseCommonLevelAndDifficulty(data.type) && (!data.level || !data.difficulty)) {
        Swal.fire({ icon: 'warning', title: '請填寫完整', text: '等級與難易度為必填欄位。' });
        return;
    }

    if (['single', 'select'].includes(data.type) && (!data.attributes?.topic || !data.attributes?.subtopic)) {
        const questionTypeLabel = data.type === 'select' ? '精選單選題' : '一般單選題';
        Swal.fire({ icon: 'warning', title: '請選擇題目分類', text: `${questionTypeLabel}需先選擇主題與次類。` });
        return;
    }
    if (data.type === 'longText' && !data.attributes?.mode) {
        Swal.fire({ icon: 'warning', title: '請選擇長文題型', text: '長文題目需先選擇「引導寫作」或「資訊整合」。' });
        return;
    }

    if (data.type === 'longText' && !stripHtml(data.passage || '').trim()) {
        Swal.fire({ icon: 'warning', title: '請填寫文章內容', text: '長文題目的文章內容為必填欄位。' });
        return;
    }

    if (data.type === 'listenGroup' && (!data.attributes?.audioType || !data.attributes?.material)) {
        Swal.fire({ icon: 'warning', title: '請補齊題目屬性', text: '聽力題組需先選擇語音類型與素材分類。' });
        return;
    }

    if (data.type === 'listenGroup' && !stripHtml(data.passage || '').trim()) {
        Swal.fire({ icon: 'warning', title: '請填寫語音內容', text: '聽力題組的母題需先填寫語音內容。' });
        return;
    }

    if (data.type === 'listenGroup') {
        const hasInvalidListenGroupSubQuestion = (data.subQuestions || []).length !== listenGroupFixedQuestionConfigs.length
            || (data.subQuestions || []).some((subQuestion) => {
                const hasEmptyOption = !(subQuestion.options || []).every((option) => hasMeaningfulHtmlContent(option.text || ''));
                return !subQuestion.stem?.trim() || hasEmptyOption || !subQuestion.answer || !subQuestion.analysis?.trim();
            });
        if (hasInvalidListenGroupSubQuestion) {
            Swal.fire({
                icon: 'warning',
                title: '請補齊子題資料',
                text: '聽力題組固定 2 題，每題都需要題目、完整 ABCD 選項、答案與試題解析。'
            });
            return;
        }
    }

    if (data.type === 'readGroup' && !data.attributes?.genre) {
        Swal.fire({ icon: 'warning', title: '請選擇文體', text: '閱讀題組需先選擇文體（文言文 / 應用文 / 語體文）。' });
        return;
    }

    if (data.type === 'readGroup' && !stripHtml(data.stem || '').trim()) {
        Swal.fire({ icon: 'warning', title: '請填寫標題', text: '閱讀題組的母題區需先輸入標題。' });
        return;
    }

    if (data.type === 'readGroup' && !stripHtml(data.passage || '').trim()) {
        Swal.fire({ icon: 'warning', title: '請填寫文章內容', text: '閱讀題組的文章內容為必填欄位。' });
        return;
    }

    if (data.type === 'readGroup') {
        const hasInvalidReadGroupSubQuestion = (data.subQuestions || []).some((subQuestion) => {
            const hasEmptyOption = !(subQuestion.options || []).every((option) => hasMeaningfulHtmlContent(option.text || ''));
            return !hasMeaningfulHtmlContent(subQuestion.stem || '') || hasEmptyOption || !subQuestion.answer || !hasMeaningfulHtmlContent(subQuestion.analysis || '');
        });
        if (hasInvalidReadGroupSubQuestion) {
            Swal.fire({
                icon: 'warning',
                title: '請補齊子題資料',
                text: '每道子題都需要題目、完整 ABCD 選項、答案與試題解析。'
            });
            return;
        }
    }

    if (data.type === 'shortGroup' && !data.attributes?.genre) {
        Swal.fire({ icon: 'warning', title: '請選擇文體', text: '短文題組需先選擇文體（文言文 / 應用文 / 語體文）。' });
        return;
    }

    if (data.type === 'shortGroup' && !stripHtml(data.stem || '').trim()) {
        Swal.fire({ icon: 'warning', title: '請填寫題目', text: '短文題組的題目為必填欄位。' });
        return;
    }

    if (data.type === 'shortGroup' && !stripHtml(data.passage || '').trim()) {
        Swal.fire({ icon: 'warning', title: '請填寫文章內容', text: '短文題組的文章內容為必填欄位。' });
        return;
    }

    if (data.type === 'shortGroup') {
        const hasInvalidSubQuestion = (data.subQuestions || []).some((subQuestion) => (
            !stripHtml(subQuestion.stem || '').trim() || !subQuestion.dimension || !subQuestion.indicator
        ));
        if (hasInvalidSubQuestion) {
            Swal.fire({
                icon: 'warning',
                title: '請補齊子題資料',
                text: '每道子題都需要題目、主向度與能力指標。'
            });
            return;
        }
    }

    if (formMode === 'revision') {
        // 修題完成
        if (!data.revisionReply?.trim()) {
            Swal.fire({ icon: 'warning', title: '請填寫修題回覆', text: '請說明本次修改的內容。' });
            return;
        }

        Object.assign(currentEditingQuestion, data);
        currentEditingQuestion.updatedAt = now;
        currentEditingQuestion.revisionReply = data.revisionReply;

        // 修題完成後，狀態改為下一階段
        // peer_editing → pending (回到待審)
        // expert_editing → pending
        // final_editing → pending
        currentEditingQuestion.status = 'pending';
        currentEditingQuestion.history.push({
            time: now, user: '劉雅婷', action: '修題回覆',
            comment: data.revisionReply
        });

        Swal.fire({
            icon: 'success', title: '修題完成',
            text: '已提交修題回覆，試題將進入下一階段審查。',
            toast: true, position: 'top-end', showConfirmButton: false, timer: 2500, timerProgressBar: true
        });
    } else {
        // 命題完成
        if (currentEditingQuestion) {
            Object.assign(currentEditingQuestion, data);
            currentEditingQuestion.status = 'completed';
            currentEditingQuestion.updatedAt = now;
            currentEditingQuestion.history.push({
                time: now, user: '劉雅婷', action: '命題完成', comment: ''
            });
        } else {
            const newId = `Q-2602-M${String(myQuestionsDb.length + 100).padStart(3, '0')}`;
            const newQ = {
                id: newId,
                projectId: localStorage.getItem('cwt_current_project') || 'P2026-01',
                ...data,
                status: 'completed',
                passage: data.passage || '',
                createdAt: now, updatedAt: now,
                returnCount: 0, reviewComment: null, reviewerName: null, reviewStage: null, revisionReply: '',
                history: [
                    { time: now, user: '劉雅婷', action: '建立草稿', comment: '' },
                    { time: now, user: '劉雅婷', action: '命題完成', comment: '' }
                ]
            };
            myQuestionsDb.push(newQ);
        }

        Swal.fire({
            icon: 'success', title: '命題完成',
            text: '試題已儲存為「命題完成」狀態，可隨時送審。',
            toast: true, position: 'top-end', showConfirmButton: false, timer: 2500, timerProgressBar: true
        });
    }

    closeFormModal();
    renderTabContent();
};

/** 送審 */
const submitQuestion = (questionId) => {
    const q = myQuestionsDb.find(q => q.id === questionId);
    if (!q) return;

    if (q.status === 'draft') {
        Swal.fire({ icon: 'warning', title: '尚未命題完成', text: '請先完成命題後再送審。' });
        return;
    }

    Swal.fire({
        title: '確認送審？',
        text: `將「${q.id}」送出審查，送審後將無法再編輯。`,
        icon: 'question',
        showCancelButton: true,
        confirmButtonColor: '#8EAB94',
        cancelButtonColor: '#9ca3af',
        confirmButtonText: '確認送審',
        cancelButtonText: '取消'
    }).then((result) => {
        if (result.isConfirmed) {
            const now = new Date().toISOString().replace('T', ' ').substring(0, 16);
            q.status = 'pending';
            q.updatedAt = now;
            q.history.push({ time: now, user: '劉雅婷', action: '送審', comment: '' });

            Swal.fire({
                icon: 'success', title: '已送審',
                toast: true, position: 'top-end', showConfirmButton: false, timer: 2000, timerProgressBar: true
            });

            renderTabContent();
        }
    });
};

/** 刪除試題 (軟刪除) */
const deleteQuestion = (questionId) => {
    const qIndex = myQuestionsDb.findIndex(q => q.id === questionId);
    if (qIndex === -1) return;

    const q = myQuestionsDb[qIndex];

    Swal.fire({
        title: '確認刪除？',
        text: `確定要刪除試題「${q.id}」嗎？此動作將無法復原。`,
        icon: 'warning',
        showCancelButton: true,
        confirmButtonColor: '#D98A6C',
        cancelButtonColor: '#9ca3af',
        confirmButtonText: '確認刪除',
        cancelButtonText: '取消',
        reverseButtons: true
    }).then((result) => {
        if (result.isConfirmed) {
            // 目前 Demo 採用直接從陣列移除。
            // 實務上可能會是變更 status 為 'deleted'
            myQuestionsDb.splice(qIndex, 1);

            Swal.fire({
                icon: 'success',
                title: '已刪除',
                toast: true,
                position: 'top-end',
                showConfirmButton: false,
                timer: 2000,
                timerProgressBar: true
            });

            renderTabContent();
        }
    });
};


// ===================================================================
// Quill 底部滑入式編輯器
// ===================================================================
const initQuillEditor = () => {
    registerQuillFormats();

    quillInstance = new Quill('#quillEditorContainer', {
        theme: 'snow',
        placeholder: '在此輸入內容...',
        modules: {
            toolbar: {
                container: [
                    [{ 'size': ['small', false, 'large'] }, { 'header': [2, 3, false] }, { 'font': quillFontOptions.map((option) => option.value) }],
                    [{ 'color': [] }, { 'background': [] }, { 'align': [] }],
                    ['bold', 'underline', 'strike', 'link'],
                    [{ 'list': 'ordered' }, { 'list': 'bullet' }, { 'indent': '-1' }, { 'indent': '+1' }],
                    [{ 'script': 'sub' }, { 'script': 'super' }],
                    ['image', 'clean']
                ],
                handlers: {
                    /** 自訂圖片上傳：觸發 file input 讀取為 Base64 嵌入 */
                    image: function () {
                        const input = document.createElement('input');
                        input.setAttribute('type', 'file');
                        input.setAttribute('accept', 'image/png, image/jpeg, image/gif, image/webp');
                        input.click();
                        input.onchange = () => {
                            const file = input.files?.[0];
                            if (!file) return;
                            // 限制 5 MB
                            if (file.size > 5 * 1024 * 1024) {
                                Swal.fire({ icon: 'warning', title: '圖片過大', text: '請上傳 5 MB 以內的圖片。' });
                                return;
                            }
                            const reader = new FileReader();
                            reader.onload = (e) => {
                                const range = quillInstance.getSelection(true);
                                quillInstance.insertEmbed(range.index, 'image', e.target.result);
                                quillInstance.setSelection(range.index + 1);
                            };
                            reader.readAsDataURL(file);
                        };
                    }
                }
            }
        }
    });

    updateQuillWordCount();

    // 中文標點按鈕事件（含括弧配對插入邏輯）
    document.querySelectorAll('.punct-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const char = btn.getAttribute('data-char');
            const isPair = btn.hasAttribute('data-pair'); // 括弧類：「」『』（）
            if (char && quillInstance) {
                const range = quillInstance.getSelection(true);
                quillInstance.insertText(range.index, char);
                if (isPair) {
                    // 括弧配對：左右同時插入，游標自動移到中間
                    quillInstance.setSelection(range.index + 1);
                } else {
                    quillInstance.setSelection(range.index + char.length);
                }
            }
        });
    });

    // 收起按鈕 / 點擊外側收合
    document.getElementById('quillCloseBtn').addEventListener('click', closeQuillEditor);
    document.getElementById('quillBackdrop').addEventListener('click', closeQuillEditor);

    // 監聽 Quill 內容變化，即時同步回欄位
    quillInstance.on('text-change', () => {
        if (activeEditableField && activeFieldKey) {
            const html = quillInstance.root.innerHTML;
            activeEditableField.innerHTML = html;
        }
        updateQuillWordCount();
    });
};

/** 啟動 Quill 編輯特定欄位 */
const activateQuillField = (element, fieldKey, label) => {
    if (formMode === 'view') return;

    // 移除前一個欄位的 editing 樣式
    if (activeEditableField) {
        activeEditableField.classList.remove('editing');
    }

    activeEditableField = element;
    activeFieldKey = fieldKey;

    // 加上 editing 樣式
    element.classList.add('editing');

    // 更新 Quill 編輯器內容
    const currentHtml = element.innerHTML;
    // 如果是佔位文字，清空
    const isPlaceholder = element.querySelector('.text-gray-400.italic');
    quillInstance.root.innerHTML = isPlaceholder ? '' : currentHtml;

    // 更新標籤與字數
    document.getElementById('quillTargetLabel').textContent = label;
    updateQuillWordCount();

    // 滑出編輯器
    const panel = document.getElementById('quillPanel');
    const sheet = document.getElementById('quillSheet');
    const backdrop = document.getElementById('quillBackdrop');
    clearTimeout(quillCloseTimer);
    panel.classList.remove('pointer-events-none');
    sheet.classList.remove('translate-y-full');
    sheet.classList.add('translate-y-0');
    backdrop.classList.remove('opacity-0');

    // 聚焦 Quill
    quillInstance.focus();
    const selectionIndex = Math.max(quillInstance.getLength() - 1, 0);
    quillInstance.setSelection(selectionIndex, 0, 'silent');
    if (isPlaceholder || !stripHtml(currentHtml || '').trim()) {
        quillInstance.format('font', 'dfkai-sb');
    }
    updateQuillWordCount();
};

/** 關閉 Quill 編輯器 */
const closeQuillEditor = () => {
    const panel = document.getElementById('quillPanel');
    const sheet = document.getElementById('quillSheet');
    const backdrop = document.getElementById('quillBackdrop');
    sheet.classList.remove('translate-y-0');
    sheet.classList.add('translate-y-full');
    backdrop.classList.add('opacity-0');

    if (activeEditableField) {
        activeEditableField.classList.remove('editing');
    }
    activeEditableField = null;
    activeFieldKey = null;
    const counter = document.getElementById('quillWordCount');
    if (counter) {
        counter.textContent = '字數：0';
    }

    clearTimeout(quillCloseTimer);
    quillCloseTimer = setTimeout(() => {
        if (sheet.classList.contains('translate-y-full')) {
            panel.classList.add('pointer-events-none');
        }
    }, 300);
};


// ===================================================================
// 預覽 Modal (考卷樣式)
// ===================================================================
const initPreviewModal = () => {
    document.getElementById('previewCloseBtn').addEventListener('click', closePreviewModal);
    document.getElementById('previewBackdrop').addEventListener('click', closePreviewModal);
};

const renderPreviewChoiceOptions = (options = [], answer = '') => {
    const normalizedOptions = optionLabels.map((label) => {
        const matched = options.find((option) => option.label === label);
        return matched || { label, text: '' };
    });

    return `
        <div class="grid grid-cols-1 md:grid-cols-2 gap-3 text-sm mb-3">
            ${normalizedOptions.map((option) => `
                <div class="flex items-start gap-3 rounded-xl border px-4 py-3 min-w-0 ${option.label === answer ? 'border-[var(--color-sage)] bg-[var(--color-sage)]/10' : 'border-gray-200 bg-white'}">
                    <span class="w-7 h-7 rounded-full flex items-center justify-center flex-shrink-0 ${option.label === answer ? 'bg-[var(--color-sage)] text-white' : 'bg-gray-100 text-gray-500'}">${option.label}</span>
                    <div class="min-w-0 flex-grow leading-relaxed">${option.text || '<p class="text-gray-400 italic">尚未輸入選項內容</p>'}</div>
                </div>`).join('')}
        </div>`;
};

const showExamPreview = () => {
    const data = collectFormData();
    const content = document.getElementById('previewContent');
    const config = getTypeConfig(data.type);
    const previewMetaTags = [];

    if (['single', 'select'].includes(data.type) && data.attributes?.topic) {
        previewMetaTags.push(`<span class="inline-flex items-center rounded-full border border-[var(--color-morandi)]/20 bg-[var(--color-morandi)]/10 px-3 py-1 text-xs font-medium text-[var(--color-morandi)]">主題：${escapeHtml(data.attributes.topic)}</span>`);
    }
    if (['single', 'select'].includes(data.type) && data.attributes?.subtopic) {
        previewMetaTags.push(`<span class="inline-flex items-center rounded-full border border-[var(--color-sage)]/20 bg-[var(--color-sage)]/12 px-3 py-1 text-xs font-medium text-[var(--color-sage)]">次類：${escapeHtml(data.attributes.subtopic)}</span>`);
    }
    if (data.type === 'longText' && data.attributes?.mode) {
        previewMetaTags.push(`<span class="inline-flex items-center rounded-full border border-[var(--color-terracotta)]/20 bg-[var(--color-terracotta)]/10 px-3 py-1 text-xs font-medium text-[var(--color-terracotta)]">題型：${escapeHtml(data.attributes.mode)}</span>`);
    }
    if (data.type === 'listenGroup') {
        if (data.attributes?.audioType) {
            previewMetaTags.push(`<span class="inline-flex items-center rounded-full border border-[var(--color-morandi)]/20 bg-[var(--color-morandi)]/10 px-3 py-1 text-xs font-medium text-[var(--color-morandi)]">語音類型：${escapeHtml(data.attributes.audioType)}</span>`);
        }
        if (data.attributes?.material) {
            previewMetaTags.push(`<span class="inline-flex items-center rounded-full border border-[var(--color-sage)]/20 bg-[var(--color-sage)]/10 px-3 py-1 text-xs font-medium text-[var(--color-sage)]">素材分類：${escapeHtml(data.attributes.material)}</span>`);
        }
    }
    if (data.type === 'readGroup' && data.attributes?.genre) {
        previewMetaTags.push(`<span class="inline-flex items-center rounded-full border border-[var(--color-terracotta)]/20 bg-[var(--color-terracotta)]/10 px-3 py-1 text-xs font-medium text-[var(--color-terracotta)]">文體：${escapeHtml(data.attributes.genre)}</span>`);
    }
    if (data.type === 'shortGroup') {
        previewMetaTags.push(`<span class="inline-flex items-center rounded-full border border-[var(--color-sage)]/20 bg-[var(--color-sage)]/10 px-3 py-1 text-xs font-medium text-[var(--color-sage)]">主類／次類：${shortGroupMainCategory} / ${shortGroupSubCategory}</span>`);
        if (data.attributes?.genre) {
            previewMetaTags.push(`<span class="inline-flex items-center rounded-full border border-[var(--color-morandi)]/20 bg-[var(--color-morandi)]/10 px-3 py-1 text-xs font-medium text-[var(--color-morandi)]">文體：${escapeHtml(data.attributes.genre)}</span>`);
        }
    }

    let html = `
        <div class="font-serif max-w-2xl mx-auto">
            <div class="text-center mb-6 pb-4 border-b-2 border-gray-800">
                <h2 class="text-xl font-bold mb-1">全民中文檢定 - 模擬試卷</h2>
                <p class="text-sm text-gray-500">${qTypeMap[data.type]} ・ ${getQuestionMetaLine(data)}</p>
                ${previewMetaTags.length ? `<div class="mt-3 flex flex-wrap items-center justify-center gap-2">${previewMetaTags.join('')}</div>` : ''}
            </div>`;

    if (config.hasAudio) {
        html += `
            <div class="mb-6 p-4 bg-blue-50 rounded-xl border border-blue-100 text-sm text-blue-800">
                <div class="font-bold mb-2"><i class="fa-solid fa-headphones mr-1"></i> 聽力音檔</div>
                <div>${data.audioUrl || 'DEMO 顯示區：正式版將於此載入音檔。'}</div>
            </div>`;
    }

    if (config.hasPassage && data.type !== 'longText') {
        const passageHeader = data.type === 'shortGroup'
            ? `題目：${escapeHtml(stripHtml(data.stem || '未填寫題目'))}`
            : (data.type === 'readGroup' ? `標題：${escapeHtml(stripHtml(data.stem || '未填寫標題'))}` : '');
        html += `
            <div class="mb-6 p-5 bg-gray-50 border border-gray-200 rounded-xl text-sm leading-relaxed">
                ${passageHeader ? `<div class="mb-3 text-sm font-bold text-gray-600">${passageHeader}</div>` : ''}
                <div class="prose prose-sm max-w-none">${data.passage || '<p class="text-gray-400">內容尚未填寫</p>'}</div>
            </div>`;
    }

    if (data.type === 'longText') {
        html += `
            <div class="mb-6 space-y-4">
                <div class="rounded-xl border border-gray-200 bg-white px-5 py-4 shadow-sm">
                    <div class="text-xs font-bold tracking-wide text-gray-500 mb-2">題目</div>
                    <div class="text-lg font-bold text-[var(--color-slate-main)]">${data.stem || '（可略）'}</div>
                </div>
                <div>
                    <div class="text-xs font-bold tracking-wide text-gray-500 mb-2">文章內容</div>
                    <div class="p-5 bg-white border border-gray-200 rounded-xl shadow-sm leading-relaxed prose prose-sm max-w-none">${data.passage || '<p class="text-gray-400">文章內容尚未填寫</p>'}</div>
                </div>
            </div>`;
    } else if (config.hasSubQuestions) {
        (data.subQuestions || []).forEach((sq, i) => {
            const readGroupStemHtml = hasMeaningfulHtmlContent(sq.stem || '')
                ? sq.stem
                : '<p class="text-gray-400 italic">題幹尚未填寫</p>';
            const readGroupAnalysisHtml = hasMeaningfulHtmlContent(sq.analysis || '')
                ? sq.analysis
                : '<p class="text-gray-400 italic">尚未填寫解析</p>';

            html += `
                <div class="mb-6 border-t border-gray-200 pt-5">
                    ${data.type === 'readGroup'
                    ? `<div class="mb-3 flex items-start gap-2"><span class="font-bold shrink-0">${i + 1}.</span><div class="min-w-0 flex-1 leading-relaxed prose prose-sm max-w-none">${readGroupStemHtml}</div></div>`
                    : `<p class="font-bold mb-2">${i + 1}. ${escapeHtml(sq.stem || '(題幹尚未填寫)')}</p>`}`;

            if (config.subQuestionMode === 'freeResponse') {
                html += `
                    <div class="mb-3 p-3 border border-dashed border-gray-300 rounded-lg text-sm text-gray-500">本題為自由作答，請依題意作答。</div>
                    <div class="mb-3 flex flex-wrap items-center gap-2 text-xs">
                        <span class="inline-flex items-center rounded-full border border-[var(--color-morandi)]/20 bg-[var(--color-morandi)]/10 px-3 py-1 font-medium text-[var(--color-morandi)]">主向度：${escapeHtml(sq.dimension || '未設定')}</span>
                        <span class="inline-flex items-center rounded-full border border-[var(--color-sage)]/20 bg-[var(--color-sage)]/10 px-3 py-1 font-medium text-[var(--color-sage)]">能力指標：${escapeHtml(sq.indicator || '未設定')}</span>
                    </div>
                    <div class="p-3 bg-yellow-50 border border-yellow-100 rounded-lg text-sm text-gray-700 whitespace-pre-line"><span class="font-bold text-yellow-700">解析：</span>${escapeHtml(sq.analysis || '尚未填寫解析')}</div>`;
            } else {
                html += `
                    ${data.type === 'listenGroup' ? `<div class="mb-3 flex flex-wrap items-center gap-2 text-xs">
                        <span class="inline-flex items-center rounded-full border border-[var(--color-morandi)]/20 bg-[var(--color-morandi)]/10 px-3 py-1 font-medium text-[var(--color-morandi)]">${escapeHtml(sq.level || getListenGroupQuestionConfig(i).level)}</span>
                        <span class="inline-flex items-center rounded-full border border-[var(--color-sage)]/20 bg-[var(--color-sage)]/10 px-3 py-1 font-medium text-[var(--color-sage)]">核心能力：${escapeHtml(sq.competency || getListenGroupQuestionConfig(i).competency)}</span>
                        <span class="inline-flex items-center rounded-full border border-[var(--color-oatmeal)] bg-[var(--color-oatmeal)]/70 px-3 py-1 font-medium text-[var(--color-slate-main)]">指標：${escapeHtml(sq.indicator || getListenGroupQuestionConfig(i).indicator)}</span>
                    </div>` : ''}
                    ${renderPreviewChoiceOptions(sq.options || [], sq.answer)}
                    <div class="text-xs text-gray-500">正確答案：${sq.answer || '未設定'}</div>
                    ${data.type === 'readGroup'
                        ? `<div class="mt-3 p-3 bg-yellow-50 border border-yellow-100 rounded-lg text-sm text-gray-700"><div class="font-bold text-yellow-700 mb-2">解析</div><div class="leading-relaxed prose prose-sm max-w-none">${readGroupAnalysisHtml}</div></div>`
                        : (data.type === 'listenGroup' ? `<div class="mt-3 p-3 bg-yellow-50 border border-yellow-100 rounded-lg text-sm text-gray-700 whitespace-pre-line"><span class="font-bold text-yellow-700">解析：</span>${escapeHtml(sq.analysis || '尚未填寫解析')}</div>` : '')}`;
            }

            html += '</div>';
        });

        if (data.analysis && !['shortGroup', 'readGroup', 'listenGroup'].includes(data.type)) {
            html += `
                <div class="p-4 bg-yellow-50 border border-yellow-100 rounded-lg text-sm text-gray-700">
                    <div class="font-bold text-yellow-700 mb-2">題組解析</div>
                    <div class="leading-relaxed prose prose-sm max-w-none">${data.analysis}</div>
                </div>`;
        }
    } else {
        html += `
            <div class="mb-6">
                <div class="font-bold mb-3 leading-relaxed prose prose-sm max-w-none">${data.stem || '<p class="text-gray-400">題幹尚未填寫</p>'}</div>
                ${renderPreviewChoiceOptions(data.options || [], data.answer)}
                <div class="text-xs text-gray-500">正確答案：${data.answer || '未設定'}</div>
            </div>`;
    }

    if (data.analysis && !config.hasSubQuestions) {
        const analysisTitle = data.type === 'longText' ? '批閱說明' : '解析';
        html += `
            <div class="p-4 bg-yellow-50 border border-yellow-100 rounded-lg text-sm text-gray-700">
                <div class="font-bold text-yellow-700 mb-2">${analysisTitle}</div>
                <div class="leading-relaxed prose prose-sm max-w-none">${data.analysis}</div>
            </div>`;
    }

    html += '</div>';
    content.innerHTML = html;

    document.getElementById('previewModal').classList.remove('hidden');
};

const closePreviewModal = () => {
    document.getElementById('previewModal').classList.add('hidden');
};


// ===================================================================
// 工具函式
// ===================================================================

/** 去除 HTML 標籤 */
const stripHtml = (html) => {
    const tmp = document.createElement('div');
    tmp.innerHTML = html;
    return tmp.textContent || tmp.innerText || '';
};

/** 截斷文字 */
const truncate = (text, maxLen) => {
    if (!text) return '';
    return text.length > maxLen ? text.substring(0, maxLen) + '...' : text;
};

/** HTML 轉義 */
const escapeHtml = (str) => {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
};

/** 自動儲存 Toast */
const showAutoSaveToast = () => {
    const indicator = document.getElementById('autosaveIndicator');
    if (indicator) {
        indicator.classList.remove('hidden');
        indicator.classList.add('autosave-flash');
        setTimeout(() => {
            indicator.classList.add('hidden');
            indicator.classList.remove('autosave-flash');
        }, 2200);
    }
};

// 讓函式可在 HTML onclick 中使用 (全域掛載)
window.openFormModal = openFormModal;
window.submitQuestion = submitQuestion;
window.deleteQuestion = deleteQuestion;
window.activateQuillField = activateQuillField;
window.addSubQuestion = addSubQuestion;
window.removeSubQuestion = removeSubQuestion;
window.goToListPage = goToListPage;
