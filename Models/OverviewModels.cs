namespace MT.Models;

// ======================================================================
//  命題總覽（Overview.razor）專屬 DTO
// ======================================================================

/// <summary>命題總覽頁的篩選條件（razor 端 UI state ↔ Service 端組裝 SQL）。</summary>
public class OverviewFilter
{
    public byte? StatusFilter { get; set; }
    public int? QuestionTypeId { get; set; }
    public int? CreatorId { get; set; }       // 命題教師篩選（null = 全部）
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;   // Hope 規格：每頁最多 10 筆

    /// <summary>轉成共用的 QuestionListFilter（CreatorId 採用 Overview 端設定，Tab="all" 不限狀態範圍）。</summary>
    public QuestionListFilter ToListFilter(int projectId) => new()
    {
        ProjectId         = projectId,
        CreatorId         = CreatorId,
        Tab               = "all",
        StatusFilter      = StatusFilter,
        QuestionTypeId    = QuestionTypeId,
        Keyword           = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
        SearchCreatorName = true,             // 命題總覽：跨教師搜尋（CwtList/Reviews 不啟用）
        Page              = Page,
        PageSize          = PageSize
    };
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
}
