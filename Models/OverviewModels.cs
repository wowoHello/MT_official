namespace MT.Models;

// ======================================================================
//  命題總覽（Overview.razor）專屬 DTO
// ======================================================================

/// <summary>命題總覽頁的篩選條件（razor 端 UI state ↔ Service 端組裝 SQL）。</summary>
public class OverviewFilter
{
    /// <summary>
    /// 狀態識別碼：與 Overview 列表「當前狀態」Badge 100% 對齊（見 OverviewStatusKey）。
    /// 由 OverviewService 翻譯成 StatusesOverride / HasReplied / 額外條件，避免雙寫 ResolveDisplayStatus 邏輯。
    /// null/"" = 不篩選。
    /// </summary>
    public string? StatusKey { get; set; }
    public int? QuestionTypeId { get; set; }
    public int? CreatorId { get; set; }       // 命題教師篩選（null = 全部）
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;   // Hope 規格：每頁最多 10 筆

    /// <summary>
    /// 轉成共用的 QuestionListFilter；StatusesOverride / HasReplied 等狀態相關欄位由 OverviewService 端依 StatusKey 設定。
    /// </summary>
    public QuestionListFilter ToListFilter(int projectId) => new()
    {
        ProjectId         = projectId,
        CreatorId         = CreatorId,
        Tab               = "all",
        QuestionTypeId    = QuestionTypeId,
        Keyword           = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
        SearchCreatorName = true,             // 命題總覽：跨教師搜尋（CwtList/Reviews 不啟用）
        IncludeSubRows    = true,             // 命題總覽：題組類母題與子題各自獨立成列、各自顯示燈號與當前狀態
        Page              = Page,
        PageSize          = PageSize
    };
}

/// <summary>
/// 命題總覽詳情 SlideOver 的劃記評語卡片資料（管理員視角，不匿名）。
/// 一張卡 = 一筆 MT_ReviewAnnotations。Hover 卡片時觸發螢光筆高亮預覽對應位置。
/// </summary>
public class OverviewAnnotationCard
{
    public int     AnnotationId    { get; set; }
    public int?    SubQuestionId   { get; set; }       // NULL = 母題單元；非 NULL = 該子題單元

    /// <summary>原始 fieldKey（如 "stem"、"subOptionA.0"），供 annotation.js 比對；卡片顯示請用 FieldLabel</summary>
    public string  FieldKey        { get; set; } = "";

    /// <summary>翻譯後的友善欄位名稱（透過 AnnotationFieldLabel.Describe）</summary>
    public string  FieldLabel      { get; set; } = "";

    public byte    Stage           { get; set; }       // 1=互審 / 2=專審 / 3=總審
    public string  StageLabel      { get; set; } = ""; // 管理員視角顯示真實名稱（互審 / 專審 / 總審）

    public int     AnchorStart     { get; set; }
    public int     AnchorEnd       { get; set; }
    public string  SelectedText    { get; set; } = "";
    public string  Comment         { get; set; } = "";

    public byte?   ResponseState   { get; set; }       // 1=Accepted / 2=Rejected / null=未回應
    public string? NoChangeReason  { get; set; }
    public string? ResponseByName  { get; set; }

    public string  CreatorName     { get; set; } = "";
    public DateTime CreatedAt      { get; set; }
}

/// <summary>
/// Overview 篩選下拉的識別碼集合：每個鍵對應一種 Badge 顯示語意。
/// 後端會依 PhaseCode + AllReviewersResponded 翻譯成具體的 SQL 條件。
/// </summary>
public static class OverviewStatusKey
{
    // 命題
    public const string Draft               = "draft";                // 命題草稿
    public const string Completed           = "completed";            // 命題完成
    public const string FailedComposition   = "failed-composition";   // 未完成命題（草稿落隊，R1）

    // 審題
    public const string AwaitingReview      = "awaiting-review";      // 待審（R2 warning）
    public const string Reviewed            = "reviewed";             // 已給意見（R2 success）

    // 修題
    public const string InRevision          = "in-revision";          // 修題中（C 沿用 Labels）
    public const string RevisionSubmitted   = "revision-submitted";   // 修題已送出（B success）
    public const string AwaitingNext        = "awaiting-next";        // OO 完成（A info 藍）

    // 結果
    public const string Adopted             = "adopted";              // 採用（含結案入庫）
    public const string NotAdopted          = "not-adopted";          // 不採用（含結案未採用）

    // 其他
    public const string Deleted             = "deleted";              // 命題刪除（IsDeleted=1）

    /// <summary>下拉選單顯示用：分組 → [(識別碼, 顯示文字)]，razor 端 foreach 渲染。</summary>
    public static readonly (string GroupLabel, (string Key, string Label)[] Options)[] Groups =
    [
        ("命題", [(Draft, "命題草稿"), (Completed, "命題完成"), (FailedComposition, "未完成命題")]),
        ("審題", [(AwaitingReview, "待審"), (Reviewed, "已給意見")]),
        ("修題", [(InRevision, "修題中"), (RevisionSubmitted, "修題已送出"), (AwaitingNext, "OO 完成（待下一階段）")]),
        ("結果", [(Adopted, "採用"), (NotAdopted, "不採用")]),
        ("其他", [(Deleted, "命題刪除")])
    ];
}

/// <summary>命題總覽教師下拉選項（命題人才庫該專案的命題教師清單）。</summary>
public class OverviewCreatorOption
{
    public int    Id          { get; set; }
    public string DisplayName { get; set; } = "";
    public int    QuestionCount { get; set; }   // 該老師於此專案已建立的題目數（含已刪）
}

/// <summary>
/// 命題總覽頁一次回傳的彙整結果：
/// 列表分頁項目 + status 分桶計數（給統計卡片用）
/// </summary>
public class OverviewListResult
{
    public List<QuestionListItem> Items { get; set; } = [];
    public Dictionary<byte, int> StatusCounts { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int PageCount => PageSize > 0 ? (TotalCount + PageSize - 1) / PageSize : 0;

    /// <summary>
    /// 待修編真實數量（修題階段 4/6/8 且命題者尚未送出該階段修題說明）。
    /// 用於「待修編」卡：StatusCounts 會包含全部修題狀態題目（含已送出者），此處扣除已送出。
    /// </summary>
    public int PendingRevisionCount { get; set; }

    /// <summary>
    /// 梯次當前實際 PhaseCode（2-8 對應命題階段~總審修題；null = 梯次不在任何進行中階段）。
    /// 給進度球與當前狀態 Badge 做「梯次階段感知」判定：
    /// 例如題目 Status=ExpertEditing(6) 但梯次 PhaseCode 仍 < 6（專審中），
    /// 表示該題「已被審題人按下改後採用」但梯次尚未推進到專修，畫面應顯示「專審完成」而非「專審修題中」。
    /// </summary>
    public byte? CurrentPhaseCode { get; set; }

    /// <summary>
    /// 「該題在當前審題階段是否所有被指派的審題者皆已給意見」。
    /// 僅在 PhaseCode ∈ {3,5,7}（互審 / 專審 / 總審） 時有意義；其他階段為空 dict。
    /// 判定條件：MT_ReviewAssignments 對應 ReviewStage 的全部分配筆數 = 已填 Comment 的筆數（且筆數 > 0）。
    /// 用途：當前狀態 Badge 在「待審」與「已給意見」之間切換，與儀表板 Reviewed 計算邏輯一致。
    /// </summary>
    public Dictionary<int, bool> AllReviewersResponded { get; set; } = new();

    /// <summary>
    /// 此梯次每個 OverviewStatusKey 對應的題數（key 不存在 = 該狀態無題）。
    /// 用於下拉動態渲染：避免列出 0 筆狀態造成使用者選了卻篩到空畫面。
    /// 計算範圍：忽略 OverviewFilter 的所有篩選（StatusKey/CreatorId/Keyword/QuestionTypeId），
    /// 以梯次全題目為基底——確保切換其他篩選時下拉選項不會無預警消失。
    /// 規則表與 OverviewService.TranslateStatusKey + Overview.razor::ResolveDisplayStatus 同源。
    /// </summary>
    public Dictionary<string, int> StatusKeyCounts { get; set; } = new();
}
