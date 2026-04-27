namespace MT.Models;

// ======================================================================
//  命題總覽（Overview.razor）專屬 DTO
// ======================================================================

/// <summary>命題總覽頁的篩選條件（razor 端 UI state ↔ Service 端組裝 SQL）。</summary>
public class OverviewFilter
{
    public byte? StatusFilter { get; set; }
    public int? QuestionTypeId { get; set; }
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>轉成共用的 QuestionListFilter（CreatorId=null 取整個專案，Tab="all" 不限狀態範圍）。</summary>
    public QuestionListFilter ToListFilter(int projectId) => new()
    {
        ProjectId      = projectId,
        CreatorId      = null,
        Tab            = "all",
        StatusFilter   = StatusFilter,
        QuestionTypeId = QuestionTypeId,
        Keyword        = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
        Page           = Page,
        PageSize       = PageSize
    };
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
}
