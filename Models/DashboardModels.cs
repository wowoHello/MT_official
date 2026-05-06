namespace MT.Models;

// ======================================================================
//  Dashboard KPI DTO（儀表板上方四張統計卡片）
//  依「當前梯次 ProjectId」篩選，一次查詢取得所有數字
// ======================================================================

/// <summary>
/// 卡片 3 的階段狀態類型，控制 Footer 顯示邏輯。
/// 比對舊版直接用 string 判斷，此列舉讓 Razor 的 switch 更清晰。
/// </summary>
public enum PhaseStatusType
{
    /// <summary>尚未選擇梯次（ProjectId = null）。</summary>
    NoProject,
    /// <summary>梯次存在但狀態為 0（準備中），命題尚未開始。</summary>
    Preparing,
    /// <summary>Today 落在某 Phase 的 StartDate ~ EndDate 區間內。</summary>
    InPhase,
    /// <summary>梯次進行中（Status=1）但 Today 不在任何 Phase 區間（階段銜接空窗）。</summary>
    BetweenPhases,
    /// <summary>梯次已結案（Status=2）。</summary>
    Closed
}

/// <summary>
/// 卡片 4 右上角 Pill Badge 顯示用：當前修題階段標籤。
/// 對應 MT_ProjectPhases.PhaseCode：4→Peer(互修)、6→Expert(專修)、8→Final(總修)；其餘 = None。
/// 已結案專案優先顯示 Closed，覆蓋修題階段判定。
/// </summary>
public enum RevisionPhaseLabel
{
    /// <summary>非修題階段（命題、審題、未啟動）。</summary>
    None,
    /// <summary>互審修題（PhaseCode = 4，對應 RevisionStage = 1）。</summary>
    Peer,
    /// <summary>專審修題（PhaseCode = 6，對應 RevisionStage = 2）。</summary>
    Expert,
    /// <summary>總審修題（PhaseCode = 8，對應 RevisionStage = 3）。</summary>
    Final,
    /// <summary>已結案（含中途結案）— 凍結所有修題狀態。</summary>
    Closed
}

/// <summary>
/// 卡片 3 右上角 Pill Badge 顯示用：當前審題階段標籤。
/// 對應 MT_ProjectPhases.PhaseCode：3→Peer(互審)、5→Expert(專審)、7→Final(總召)；其餘 = None。
/// 已結案專案優先顯示 Closed，覆蓋審題階段判定。
/// </summary>
public enum ReviewPhaseLabel
{
    /// <summary>非審題階段（命題、修題、未啟動）。</summary>
    None,
    /// <summary>互審階段（PhaseCode = 3，對應 ReviewStage = 1）。</summary>
    Peer,
    /// <summary>專審階段（PhaseCode = 5，對應 ReviewStage = 2）。</summary>
    Expert,
    /// <summary>總召審題（PhaseCode = 7，對應 ReviewStage = 3）。</summary>
    Final,
    /// <summary>已結案（含中途結案）— 凍結所有審題狀態。</summary>
    Closed
}

/// <summary>
/// 四張 KPI 卡片的彙整資料。
/// </summary>
public class DashboardKpiDto
{
    // ── 卡片 1：總目標題數 ──────────────────────────────────────
    /// <summary>SUM(MT_ProjectTargets.TargetCount) 此梯次所有題型目標合計。</summary>
    public int TotalTarget { get; set; }

    /// <summary>各題型目標明細（卡片 1 Footer 迷你表格用）。</summary>
    public List<DashboardTargetBreakdown> TargetBreakdown { get; set; } = [];

    // ── 卡片 2：已納入題庫（採用）──────────────────────────────
    /// <summary>STATUS = 9（採用）且 IsDeleted = 0 的總數。</summary>
    public int AdoptedCount { get; set; }

    /// <summary>採用率百分比（AdoptedCount / TotalTarget * 100），已 clamp 0~100。</summary>
    public int AdoptedPercent => TotalTarget > 0
        ? Math.Min(100, AdoptedCount * 100 / TotalTarget)
        : 0;

    // ── 卡片 3：各階段審題 ──────────────────────────────────────
    /// <summary>STATUS IN (2,3,4,5,6,7,8) 且 IsDeleted = 0 的總數（送審至總審修題，尚未判決）。</summary>
    public int InReviewCount { get; set; }

    /// <summary>卡片 3 Footer 的狀態類型，控制顏色與文案。</summary>
    public PhaseStatusType PhaseStatusType { get; set; } = PhaseStatusType.NoProject;

    /// <summary>卡片 3 Footer 顯示的文字（對應 PhaseStatusType）。</summary>
    public string PhaseStatusText { get; set; } = "請選擇梯次";

    /// <summary>
    /// 目前階段剩餘天數，僅 PhaseStatusType = InPhase 時有值。
    /// 負數代表已逾期，Footer 應以警示色顯示。
    /// </summary>
    public int? PhaseDaysRemaining { get; set; }

    /// <summary>當前審題階段標籤（卡片 3 右上角 Pill Badge）。</summary>
    public ReviewPhaseLabel CurrentReviewPhase { get; set; } = ReviewPhaseLabel.None;

    /// <summary>
    /// 當前審題階段「已完成審題」的數量（MT_ReviewAssignments 中 Comment 不為空）。
    /// 非審題階段時為 0。
    /// </summary>
    public int ReviewedCount { get; set; }

    /// <summary>
    /// 當前審題階段「應審題目總數」（MT_ReviewAssignments 中對應 ReviewStage 的全部分配筆數）。
    /// 非審題階段時為 0。
    /// </summary>
    public int ReviewTotalCount { get; set; }

    /// <summary>梯次結案時間（MT_Projects.ClosedAt），用於卡片 3 已結案狀態顯示。null 代表尚未結案。</summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>當前修題階段標籤（卡片 4 右上角 Pill Badge）。</summary>
    public RevisionPhaseLabel CurrentRevisionPhase { get; set; } = RevisionPhaseLabel.None;

    /// <summary>
    /// 當前修題階段「已修完」題目數（已寫過 MT_RevisionReplies.Content 的 distinct QuestionId）。
    /// 非修題階段時為 0。
    /// </summary>
    public int RevisedCount { get; set; }

    /// <summary>
    /// 當前修題階段「待修總數」（對應 ReviewStage 有 Comment 的 distinct QuestionId）。
    /// 非修題階段時為 0。
    /// </summary>
    public int RevisionTotalCount { get; set; }

    // ── 卡片 4：退回修題 ────────────────────────────────────────
    /// <summary>STATUS IN (4,6,8)（互審修題中、專審修題中、總審修題中）合計。</summary>
    public int ReturnEditCount { get; set; }

    /// <summary>互審修題中（Status=4）數量。</summary>
    public int PeerEditCount { get; set; }

    /// <summary>專審修題中（Status=6）數量。</summary>
    public int ExpertEditCount { get; set; }

    /// <summary>總審修題中（Status=8）數量。</summary>
    public int FinalEditCount { get; set; }

    // ── 圖表區資料 ───────────────────────────────────────────────
    /// <summary>圖表 1：各題型缺口達成率（7 筆，LEFT JOIN 保證即使無資料仍回傳）。</summary>
    public List<DashboardAchievementItem> AchievementByType { get; set; } = [];

    /// <summary>圖表 2：各題型依狀態分佈（7 筆，LEFT JOIN 保證即使無資料仍回傳）。</summary>
    public List<DashboardStatusByTypeItem> StatusByType { get; set; } = [];

    /// <summary>逾期與緊急待辦 Top 5（階段倒數 ≤ 5 天 + 題型進度嚴重落後 < 30%）。</summary>
    public List<DashboardUrgentItem> UrgentItems { get; set; } = [];

    // 注意：RecentLogs 已移到獨立查詢 GetAuditLogsAsync(AuditLogQuery)，KPI 載入不再帶 LOG。
}

// ======================================================================
//  稽核歷程 DTO
// ======================================================================

/// <summary>LOG Filter Chip 對應類別（單選）。</summary>
public enum LogTypeFilter : byte
{
    /// <summary>全部（不過濾）。</summary>
    All = 0,
    /// <summary>試題（TargetType=3）。</summary>
    Question = 1,
    /// <summary>公告（TargetType=4）。</summary>
    Announcement = 2,
    /// <summary>人員（TargetType IN (1,5)，涵蓋角色與教師）。</summary>
    Members = 3,
    /// <summary>專案（TargetType=2）。</summary>
    Project = 4,
    /// <summary>審題（TargetType=6）。</summary>
    Review = 5,
    /// <summary>登入（特例：以 Action 過濾，含 Login=3 / Logout=4）。</summary>
    Login = 6
}

/// <summary>LOG 查詢條件（單一 DTO 同時支援切換梯次/Toggle/Chip/分頁）。</summary>
public class AuditLogQuery
{
    public int  ProjectId     { get; set; }
    /// <summary>含全站事件（ProjectId IS NULL）。</summary>
    public bool IncludeGlobal { get; set; }
    public LogTypeFilter TypeFilter { get; set; } = LogTypeFilter.All;
    public int  Skip           { get; set; }
    public int  Take           { get; set; } = 50;
}

/// <summary>分頁回傳：本次資料 + 總筆數 + 是否還有更多。</summary>
public class AuditLogPage
{
    public List<RecentAuditLog> Logs       { get; set; } = [];
    public int                  TotalCount { get; set; }
    public bool                 HasMore    { get; set; }
}

/// <summary>
/// 最新稽核歷程單筆資料（儀表板右下 LOG 區塊用）。
/// Action：0=建立 1=修改 2=刪除；TargetType：0=Users 1=Roles 2=Projects 3=Questions 4=Announcements 5=Teachers 6=Reviews。
/// </summary>
public class RecentAuditLog
{
    public int      Id         { get; set; }
    public int?     UserId     { get; set; }
    /// <summary>執行人顯示名稱，刪除帳號時 Fallback 為「系統」。</summary>
    public string   UserName   { get; set; } = "系統";
    public byte     Action     { get; set; }
    public byte     TargetType { get; set; }
    public int      TargetId   { get; set; }
    /// <summary>目標資料名稱（批次 JOIN 解析），目標資料表查無時改用 OldValue/NewValue JSON 解析。</summary>
    public string   TargetName { get; set; } = "";
    public DateTime CreatedAt  { get; set; }

    /// <summary>原始資料 JSON（Update/Delete 才有）— 目標已刪除時用於 Fallback 解析名稱。</summary>
    public string?  OldValue   { get; set; }

    /// <summary>新資料 JSON（Create/Update 才有）— 目標已刪除時用於 Fallback 解析名稱。</summary>
    public string?  NewValue   { get; set; }
}

// ======================================================================
//  緊急待辦相關型別
// ======================================================================

/// <summary>緊急程度：Critical = 已逾期、Warning = 0-2 天或題型落後、Notice = 3-5 天。</summary>
public enum UrgentSeverity { Critical, Warning, Notice }

/// <summary>緊急訊號來源類型。</summary>
public enum UrgentSourceType { PhaseDeadline, TeacherShortage }

/// <summary>單一緊急待辦項目。</summary>
public class DashboardUrgentItem
{
    public UrgentSeverity Severity { get; set; }
    public UrgentSourceType Source { get; set; }

    /// <summary>主標題，例：「命題階段已逾期 3 天」或「王大明 命題進度落後」。</summary>
    public string Title { get; set; } = "";

    /// <summary>副標題，例：「截止日：2026-05-01」或「目前 3/30（10%）」。</summary>
    public string Subtitle { get; set; } = "";

    /// <summary>截止日（PhaseDeadline 才有）。</summary>
    public DateTime? Deadline { get; set; }

    /// <summary>剩餘天數；負數表示已逾期（PhaseDeadline 才有）。</summary>
    public int? DaysRemaining { get; set; }

    /// <summary>命題教師 UserId（TeacherShortage 才有）。</summary>
    public int? UserId { get; set; }

    /// <summary>教師×題型命題進度明細（TeacherShortage 展開時顯示，預載於 GetKpiAsync）。</summary>
    public List<UrgentTeacherDetail>? TeacherDetails { get; set; }

    /// <summary>
    /// 點擊後跳轉的目的 URL。
    /// PhaseDeadline：依階段碼對映至命題任務或審題任務頁；
    /// TeacherShortage：/overview?creatorId={UserId}。
    /// </summary>
    public string? TargetUrl { get; set; }
}

/// <summary>TeacherShortage 展開區塊：該教師各題型的命題進度明細。</summary>
public class UrgentTeacherDetail
{
    public int QuestionTypeId { get; set; }
    public string TypeName { get; set; } = "";
    public int Assigned { get; set; }
    public int Produced { get; set; }

    /// <summary>達成率 = Produced / Assigned（0~1+）。</summary>
    public decimal Achievement { get; set; }
}

/// <summary>
/// 單一題型的目標題數明細（卡片 1 Footer 迷你表格用）。
/// </summary>
public class DashboardTargetBreakdown
{
    public string TypeName { get; set; } = string.Empty;
    public int TargetCount { get; set; }
}

/// <summary>
/// 圖表 1：題型缺口達成率（每題型 1 筆）。
/// Produced = Status 2~9（送審後所有進度中的題目）
/// Target   = MT_ProjectTargets.TargetCount
/// </summary>
public class DashboardAchievementItem
{
    /// <summary>題型 ID（MT_QuestionTypes.Id），供 TypeShortage 批次查詢教師使用。</summary>
    public int QuestionTypeId { get; set; }

    public string TypeName { get; set; } = string.Empty;

    /// <summary>已產出數（Status 2~9，含採用、審修中，不含草稿）。</summary>
    public int Produced { get; set; }

    /// <summary>目標題數（MT_ProjectTargets.TargetCount）。</summary>
    public int Target { get; set; }
}

/// <summary>
/// 圖表 2：依題型狀態分佈（每題型 1 筆，5 個狀態桶，依當前階段動態分組）。
/// 規則：階段為推進型，故只區分「草稿 / 進行中 / 完成 / 採用 / 不採用」。
/// </summary>
public class DashboardStatusByTypeItem
{
    public string TypeName { get; set; } = string.Empty;

    /// <summary>命題草稿（Status = 0）；進入審題後若仍為草稿視同捨去。</summary>
    public int Drafts { get; set; }

    /// <summary>
    /// 階段進行中（Status = 對應當前 PhaseCode 的 in-progress 狀態）。
    /// PhaseCode 2(命題)→Status 1；PhaseCode 3..8 → Status N。
    /// </summary>
    public int InProgress { get; set; }

    /// <summary>階段完成（Status 1~8 中除「進行中」之外的所有歷史狀態）。</summary>
    public int DoneStage { get; set; }

    /// <summary>已採用（Status = 9）。</summary>
    public int Adopted { get; set; }

    /// <summary>不採用（Status = 10）。</summary>
    public int Rejected { get; set; }
}
