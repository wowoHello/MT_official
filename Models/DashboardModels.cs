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

    // ── 卡片 3：各階段審修中 ────────────────────────────────────
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

    // ── 卡片 4：退回修題 ────────────────────────────────────────
    /// <summary>STATUS IN (4,6,8)（互審修題中、專審修題中、總審修題中）合計。</summary>
    public int ReturnEditCount { get; set; }

    /// <summary>互審修題中（Status=4）數量。</summary>
    public int PeerEditCount { get; set; }

    /// <summary>專審修題中（Status=6）數量。</summary>
    public int ExpertEditCount { get; set; }

    /// <summary>總審修題中（Status=8）數量。</summary>
    public int FinalEditCount { get; set; }
}

/// <summary>
/// 單一題型的目標題數明細（卡片 1 Footer 迷你表格用）。
/// </summary>
public class DashboardTargetBreakdown
{
    public string TypeName { get; set; } = string.Empty;
    public int TargetCount { get; set; }
}
