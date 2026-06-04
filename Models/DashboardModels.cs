namespace MT.Models;

/// <summary>卡片 3 階段狀態類型，控制 Footer 顯示邏輯。</summary>
public enum PhaseStatusType
{
    NoProject,      // 尚未選擇梯次（ProjectId = null）
    Preparing,      // 準備中（Status=0），命題尚未開始
    InPhase,        // Today 落在某 Phase 的 StartDate~EndDate 區間內
    BetweenPhases,  // 進行中（Status=1）但 Today 不在任何 Phase 區間（階段銜接空窗）
    Closed          // 已結案（Status=2）
}

/// <summary>
/// 卡片 4 右上角 Pill Badge：當前修題階段標籤。
/// 對應 MT_ProjectPhases.PhaseCode：4→Peer、6→Expert、8→Final；其餘 None。已結案優先 Closed。
/// </summary>
public enum RevisionPhaseLabel
{
    None,    // 非修題階段（命題、審題、未啟動）
    Peer,    // 互審修題（PhaseCode=4）
    Expert,  // 專審修題（PhaseCode=6）
    Final,   // 總審修題（PhaseCode=8）
    Closed   // 已結案，凍結修題狀態
}

/// <summary>
/// 卡片 3 右上角 Pill Badge：當前審題階段標籤。
/// 對應 MT_ProjectPhases.PhaseCode：3→Peer(互審)、5→Expert(專審)、7→Final(總召)；其餘 None。已結案優先 Closed。
/// </summary>
public enum ReviewPhaseLabel
{
    None,    // 非審題階段（命題、修題、未啟動）
    Peer,    // 互審（PhaseCode=3）
    Expert,  // 專審（PhaseCode=5）
    Final,   // 總召審題（PhaseCode=7）
    Closed   // 已結案，凍結審題狀態
}

/// <summary>
/// Dashboard KPI DTO（儀表板上方四張統計卡片）。
/// 依當前梯次 ProjectId 篩選，一次查詢取得所有數字。
/// </summary>
public class DashboardKpiDto
{
    // ── 卡片 1：總目標題數 ──
    /// <summary>SUM(MT_ProjectTargets.TargetCount) 此梯次所有題型目標合計。</summary>
    public int TotalTarget { get; set; }
    /// <summary>各題型目標明細（卡片 1 Footer 迷你表格用）。</summary>
    public List<DashboardTargetBreakdown> TargetBreakdown { get; set; } = [];

    // ── 卡片 2：已納入題庫（採用）──
    /// <summary>本梯次採用總數（Status=9 且 IsDeleted=0；含 CWT 母+子、LCT master+聽力題組整組，SQL 端統一彙總）。</summary>
    public int AdoptedCount { get; set; }
    /// <summary>razor 顯示用別名（同 AdoptedCount），歷史欄位保留。</summary>
    public int AdoptedTotal => AdoptedCount;
    /// <summary>採用率 %（AdoptedTotal / TotalTarget × 100，clamp 0~100）。</summary>
    public int AdoptedPercent => TotalTarget > 0 ? Math.Min(100, AdoptedTotal * 100 / TotalTarget) : 0;

    // ── 卡片 3：各階段審題 ──
    /// <summary>卡片 3 Footer 的狀態類型，控制顏色與文案。</summary>
    public PhaseStatusType PhaseStatusType { get; set; } = PhaseStatusType.NoProject;
    /// <summary>卡片 3 Footer 顯示文字（對應 PhaseStatusType）。</summary>
    public string PhaseStatusText { get; set; } = "請選擇梯次";
    /// <summary>目前階段剩餘天數（僅 InPhase 有值；負數=已逾期，Footer 以警示色顯示）。</summary>
    public int? PhaseDaysRemaining { get; set; }
    /// <summary>當前審題階段標籤（卡片 3 右上角 Pill Badge）。</summary>
    public ReviewPhaseLabel CurrentReviewPhase { get; set; } = ReviewPhaseLabel.None;

    // 審題進度（非審題階段皆為 0；母題、子題獨立計數互不影響）
    /// <summary>母題已審數（SubQuestionId IS NULL 且 DecidedAt 非 NULL）。</summary>
    public int ReviewedCount { get; set; }
    /// <summary>母題應審總數（SubQuestionId IS NULL 的全部分配）。</summary>
    public int ReviewTotalCount { get; set; }
    /// <summary>子題已審數（SubQuestionId 非 NULL 且 DecidedAt 非 NULL）。</summary>
    public int ReviewedSubCount { get; set; }
    /// <summary>子題應審總數（SubQuestionId 非 NULL 的全部分配）。</summary>
    public int ReviewTotalSubCount { get; set; }

    /// <summary>梯次結案時間（MT_Projects.ClosedAt）；null=尚未結案。卡片 3 已結案狀態顯示用。</summary>
    public DateTime? ClosedAt { get; set; }

    // ── 卡片 4：各階段修題 ──
    /// <summary>當前修題階段標籤（卡片 4 右上角 Pill Badge）。</summary>
    public RevisionPhaseLabel CurrentRevisionPhase { get; set; } = RevisionPhaseLabel.None;

    // 修題進度（非修題階段皆為 0；母題、子題獨立計數）
    /// <summary>母題已修完數（RevisionReplies.Content 非空、本輪內）。</summary>
    public int RevisedMasterCount { get; set; }
    /// <summary>母題待修總數（ReviewAssignments + SubQuestionId IS NULL）。</summary>
    public int RevisionMasterTotal { get; set; }
    /// <summary>子題已修完數（RevisionReplies.Content 非空、本輪內）。</summary>
    public int RevisedSubCount { get; set; }
    /// <summary>子題待修總數（ReviewAssignments + SubQuestionId IS NOT NULL）。</summary>
    public int RevisionSubTotal { get; set; }

    /// <summary>卡片 4 顯示用：母題 + 子題已修完合計。</summary>
    public int RevisedCount => RevisedMasterCount + RevisedSubCount;
    /// <summary>卡片 4 顯示用：母題 + 子題待修總數合計。</summary>
    public int RevisionTotalCount => RevisionMasterTotal + RevisionSubTotal;

    // ── 圖表區資料 ──
    /// <summary>圖表 1：各題型缺口達成率（LEFT JOIN 保證無資料仍回傳）。</summary>
    public List<DashboardAchievementItem> AchievementByType { get; set; } = [];
    /// <summary>圖表 2：各題型依狀態分佈（LEFT JOIN 保證無資料仍回傳）。</summary>
    public List<DashboardStatusByTypeItem> StatusByType { get; set; } = [];
    /// <summary>逾期與緊急待辦 Top 5（階段倒數 5 天內 + 題型進度低於 30%）。</summary>
    public List<DashboardUrgentItem> UrgentItems { get; set; } = [];

    // RecentLogs 已移到獨立查詢 GetAuditLogsAsync(AuditLogQuery)，KPI 載入不再帶 LOG。
}

// ── 稽核歷程 DTO ──

/// <summary>
/// 命題儀表板 LOG Filter Chip 類別（單選）。僅梯次內活動（試題/審題）；
/// 跨梯次活動（人員/專案/公告/登入）已移至 SystemLogs.razor。
/// </summary>
public enum LogTypeFilter : byte
{
    All = 0,       // 全部梯次內活動
    Question = 1,  // 試題（TargetType=3）
    Review = 5     // 審題（TargetType=6）
}

/// <summary>命題儀表板 LOG 查詢條件（僅梯次內活動）。</summary>
public class AuditLogQuery
{
    public int ProjectId { get; set; }
    public LogTypeFilter TypeFilter { get; set; } = LogTypeFilter.All;
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}

/// <summary>分頁回傳：本次資料 + 總筆數 + 是否還有更多。</summary>
public class AuditLogPage
{
    public List<RecentAuditLog> Logs { get; set; } = [];
    public int TotalCount { get; set; }
    public bool HasMore { get; set; }
}

/// <summary>
/// 最新稽核歷程單筆（儀表板右下 LOG 區塊用）。
/// Action：0=建立 1=修改 2=刪除；TargetType：0=Users 1=Roles 2=Projects 3=Questions 4=Announcements 5=Teachers 6=Reviews。
/// </summary>
public class RecentAuditLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    /// <summary>執行人顯示名稱，刪除帳號時 fallback 為「系統」。</summary>
    public string UserName { get; set; } = "系統";
    public byte Action { get; set; }
    public byte TargetType { get; set; }
    public int TargetId { get; set; }
    /// <summary>目標資料名稱（批次 JOIN 解析）；查無時改用 OldValue/NewValue JSON 解析。</summary>
    public string TargetName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    /// <summary>原始 JSON（Update/Delete 才有）；目標已刪除時用於 fallback 解析名稱。</summary>
    public string? OldValue { get; set; }
    /// <summary>新資料 JSON（Create/Update 才有）；目標已刪除時用於 fallback 解析名稱。</summary>
    public string? NewValue { get; set; }
}

// ── 緊急待辦相關型別 ──

/// <summary>緊急程度：Critical=已逾期、Warning=0-2 天或題型落後、Notice=3-5 天。</summary>
public enum UrgentSeverity { Critical, Warning, Notice }

/// <summary>緊急訊號來源類型。</summary>
public enum UrgentSourceType { PhaseDeadline, TeacherShortage }

/// <summary>單一緊急待辦項目。</summary>
public class DashboardUrgentItem
{
    public UrgentSeverity Severity { get; set; }
    public UrgentSourceType Source { get; set; }
    /// <summary>主標題，例：「命題階段已逾期 3 天」「王大明 命題進度落後」。</summary>
    public string Title { get; set; } = "";
    /// <summary>副標題，例：「截止日：2026-05-01」「目前 3/30（10%）」。</summary>
    public string Subtitle { get; set; } = "";
    /// <summary>截止日。</summary>
    public DateTime? Deadline { get; set; }
    /// <summary>剩餘天數；負數表示已逾期。</summary>
    public int? DaysRemaining { get; set; }
    /// <summary>命題教師 UserId。</summary>
    public int? UserId { get; set; }
    /// <summary>教師 × 題型命題進度明細（TeacherShortage 展開時顯示，預載於 GetKpiAsync）。</summary>
    public List<UrgentTeacherDetail>? TeacherDetails { get; set; }
    /// <summary>點擊跳轉的目的 URL。PhaseDeadline→命題/審題任務頁；TeacherShortage→/overview?creatorId={UserId}。</summary>
    public string? TargetUrl { get; set; }
}

/// <summary>TeacherShortage 展開區塊：該教師各題型的命題進度明細。</summary>
public class UrgentTeacherDetail
{
    public int QuestionTypeId { get; set; }
    /// <summary>母/子題粒度（0=母題或單題，1=子題）。</summary>
    public byte Granularity { get; set; }
    /// <summary>難度等級（LCT 聽力測驗 1~5；其他固定 null）。</summary>
    public byte? Level { get; set; }
    /// <summary>UI 顯示標籤（CWT 帶（母題）/（子題）；LCT「難度 X」或「聽力題組」）。</summary>
    public string TypeName { get; set; } = "";
    public int Assigned { get; set; }
    public int Produced { get; set; }
    /// <summary>達成率 = Produced / Assigned（0~1+）。</summary>
    public decimal Achievement { get; set; }
}

/// <summary>
/// 單一題型目標題數明細（卡片 1 Footer 迷你表格用）。
/// CWT：閱讀/短文題組拆母+子兩筆（Granularity 區分）；LCT：TypeName 為「難度一」等五桶。
/// </summary>
public class DashboardTargetBreakdown
{
    public string TypeName { get; set; } = string.Empty;
    public int TargetCount { get; set; }
    /// <summary>母/子題粒度（0=母題或單題，1=子題）。</summary>
    public byte Granularity { get; set; }
    /// <summary>UI 顯示標籤（Service 端組裝，Razor 端取代 TypeName）。</summary>
    public string DisplayLabel { get; set; } = string.Empty;
    /// <summary>已產出數（Status NOT IN 0,10,11；排除草稿/不採用，比 CwtList 配額卡更保守）。</summary>
    public int Produced { get; set; }
}

/// <summary>
/// 圖表 1：題型缺口達成率（CWT 6 筆 / LCT 5 筆）。
/// Produced=Status 非草稿/不採用計數、Target=MT_ProjectTargets.TargetCount。
/// CWT：閱讀(3)/短文(5)各拆母+子；LCT：依難度一~五分組（TypeId=7 子題計入對應難度桶）。
/// </summary>
public class DashboardAchievementItem
{
    /// <summary>題型 ID（CWT 用；LCT 固定 0）。</summary>
    public int QuestionTypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    /// <summary>已產出數（Status 非 0/10/11）。</summary>
    public int Produced { get; set; }
    /// <summary>目標題數（MT_ProjectTargets.TargetCount）。</summary>
    public int Target { get; set; }
    /// <summary>母/子題粒度（0=母題或單題，1=子題）。</summary>
    public byte Granularity { get; set; }
    /// <summary>難度等級（LCT 1-5；CWT 固定 null）。</summary>
    public byte? Level { get; set; }
    /// <summary>UI 顯示標籤（Service 端組裝，對應 ApexCharts Y 軸；Razor 端取代 TypeName）。</summary>
    public string DisplayLabel { get; set; } = string.Empty;
}

/// <summary>
/// 圖表 2：依題型狀態分佈（CWT 6 軸 / LCT 5 軸，5 個狀態桶）。
/// 階段為推進型，故只分「草稿 / 進行中 / 完成 / 採用 / 不採用」。
/// CWT：閱讀/短文各拆母+子；LCT：依難度一~五分組。
/// </summary>
public class DashboardStatusByTypeItem
{
    public string TypeName { get; set; } = string.Empty;
    /// <summary>母/子題粒度（0=母題或單題，1=子題）。</summary>
    public byte Granularity { get; set; }
    /// <summary>難度等級（LCT 1-5；CWT 固定 null）。</summary>
    public byte? Level { get; set; }
    /// <summary>UI 顯示標籤（Service 端組裝，對應 ApexCharts X 軸；Razor 端取代 TypeName）。</summary>
    public string DisplayLabel { get; set; } = string.Empty;
    /// <summary>命題草稿（Status=0）；進入審題後若仍為草稿視同捨去。</summary>
    public int Drafts { get; set; }
    /// <summary>階段進行中（命題階段→Status 1，PhaseCode 3..8→對應 Status N）。</summary>
    public int InProgress { get; set; }
    /// <summary>階段完成（Status 1~8 中除「進行中」外的歷史狀態）。</summary>
    public int DoneStage { get; set; }
    /// <summary>已採用（Status=9）。</summary>
    public int Adopted { get; set; }
    /// <summary>不採用合計（Status IN 10,11）：10=三審親判不採用、11=結案清盤未採用。警示型退回(1/2 輪)回 FinalEditing(8)，不落此桶。</summary>
    public int Rejected { get; set; }
}
