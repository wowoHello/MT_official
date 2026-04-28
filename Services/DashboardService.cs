using Dapper;
using MT.Models;

namespace MT.Services;

/// <summary>
/// 命題儀表板服務契約。
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// 依梯次取得四張 KPI 卡片資料，一次查詢完成，避免 N+1。
    /// </summary>
    Task<DashboardKpiDto> GetKpiAsync(int projectId);
}

/// <summary>
/// 命題儀表板的資料查詢與統計計算。
/// 所有統計皆依傳入的 projectId 過濾，不混用其他梯次資料。
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly IDatabaseService _db;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(IDatabaseService db, ILogger<DashboardService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DashboardKpiDto> GetKpiAsync(int projectId)
    {
        using var conn = _db.CreateConnection();

        // ──────────────────────────────────────────────────────────────
        // 1. 各題型目標題數（含明細）
        // ──────────────────────────────────────────────────────────────
        const string sqlTargets = """
            SELECT qt.Name AS TypeName, ISNULL(SUM(pt.TargetCount), 0) AS TargetCount
            FROM   dbo.MT_QuestionTypes qt
            LEFT JOIN dbo.MT_ProjectTargets pt
                   ON pt.QuestionTypeId = qt.Id AND pt.ProjectId = @pid
            GROUP BY qt.Id, qt.Name
            ORDER BY qt.Id
            """;

        var targetRows = (await conn.QueryAsync<DashboardTargetBreakdown>(
            sqlTargets, new { pid = projectId })).ToList();

        // ──────────────────────────────────────────────────────────────
        // 2. 各題目狀態計數（一次掃表，按 Status 分組）
        //    IsDeleted = 0：只計算未軟刪除的題目
        //    Status 採用 = 9；審修中 = 2~8；退回修題 = 4,6,8
        // ──────────────────────────────────────────────────────────────
        const string sqlStatusCounts = """
            SELECT
                SUM(CASE WHEN Status = 9                          THEN 1 ELSE 0 END) AS AdoptedCount,
                SUM(CASE WHEN Status BETWEEN 2 AND 8              THEN 1 ELSE 0 END) AS InReviewCount,
                SUM(CASE WHEN Status IN (4, 6, 8)                 THEN 1 ELSE 0 END) AS ReturnEditCount,
                SUM(CASE WHEN Status = 4                          THEN 1 ELSE 0 END) AS PeerEditCount,
                SUM(CASE WHEN Status = 6                          THEN 1 ELSE 0 END) AS ExpertEditCount,
                SUM(CASE WHEN Status = 8                          THEN 1 ELSE 0 END) AS FinalEditCount
            FROM dbo.MT_Questions
            WHERE ProjectId = @pid AND IsDeleted = 0
            """;

        var counts = await conn.QuerySingleOrDefaultAsync<StatusCountRow>(
            sqlStatusCounts, new { pid = projectId });

        // ──────────────────────────────────────────────────────────────
        // 3. 梯次狀態 + 目前所在階段
        //    MT_Projects 無 Status 欄位，需用 ClosedAt / StartDate 計算：
        //      ClosedAt 非 NULL          → 2 已結案
        //      StartDate > 今日           → 0 準備中
        //      其餘                       → 1 進行中
        //    再查 MT_ProjectPhases 確認 Today 是否落在某個 Phase 區間
        // ──────────────────────────────────────────────────────────────
        const string sqlProjectStatus = """
            SELECT
                CASE
                    WHEN ClosedAt IS NOT NULL                       THEN 2
                    WHEN StartDate > CAST(GETDATE() AS DATE)        THEN 0
                    ELSE 1
                END AS Status
            FROM dbo.MT_Projects
            WHERE Id = @pid AND IsDeleted = 0
            """;

        var projectStatus = await conn.QuerySingleOrDefaultAsync<int?>(
            sqlProjectStatus, new { pid = projectId });

        const string sqlCurrentPhase = """
            SELECT TOP 1
                PhaseName,
                DATEDIFF(DAY, SYSDATETIME(), CAST(EndDate AS DATETIME2)) AS DaysLeft
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @pid
              AND StartDate <= CAST(GETDATE() AS DATE)
              AND EndDate   >= CAST(GETDATE() AS DATE)
            ORDER BY SortOrder ASC
            """;

        var phaseRow = await conn.QuerySingleOrDefaultAsync<PhaseRow>(
            sqlCurrentPhase, new { pid = projectId });

        // 計算卡片 3 的狀態類型與顯示文字
        var (phaseStatusType, phaseStatusText, phaseDaysRemaining) =
            ResolvePhaseStatus(projectStatus, phaseRow);

        // ──────────────────────────────────────────────────────────────
        // 組裝 DTO
        // ──────────────────────────────────────────────────────────────
        return new DashboardKpiDto
        {
            TotalTarget       = targetRows.Sum(r => r.TargetCount),
            TargetBreakdown   = targetRows,
            AdoptedCount      = counts?.AdoptedCount    ?? 0,
            InReviewCount     = counts?.InReviewCount   ?? 0,
            ReturnEditCount   = counts?.ReturnEditCount ?? 0,
            PeerEditCount     = counts?.PeerEditCount   ?? 0,
            ExpertEditCount   = counts?.ExpertEditCount ?? 0,
            FinalEditCount    = counts?.FinalEditCount  ?? 0,
            PhaseStatusType   = phaseStatusType,
            PhaseStatusText   = phaseStatusText,
            PhaseDaysRemaining = phaseDaysRemaining
        };
    }

    /// <summary>
    /// 依梯次狀態碼與 Phase 查詢結果，決定卡片 3 Footer 要顯示的狀態。
    /// 抽出成獨立方法以便日後維護，純運算不查 DB。
    /// </summary>
    private static (PhaseStatusType type, string text, int? days) ResolvePhaseStatus(
        int? projectStatus, PhaseRow? phaseRow)
    {
        // 梯次狀態 0 = 準備中（尚未開始命題）
        if (projectStatus == 0)
            return (PhaseStatusType.Preparing, "命題尚未開始", null);

        // 梯次狀態 2 = 已結案
        if (projectStatus == 2)
            return (PhaseStatusType.Closed, "已結案", null);

        // 梯次進行中（Status=1），判斷是否有當前 Phase
        if (phaseRow is not null)
        {
            // Today 落在某 Phase 區間內
            return (PhaseStatusType.InPhase, phaseRow.PhaseName, phaseRow.DaysLeft);
        }

        // 梯次進行中但 Today 不在任何 Phase 區間（階段空窗期）
        return (PhaseStatusType.BetweenPhases, "階段銜接中", null);
    }

    // ──── 內部 mapping 型別（僅此 Service 使用，無需暴露）────────────
    private sealed class StatusCountRow
    {
        public int AdoptedCount    { get; init; }
        public int InReviewCount   { get; init; }
        public int ReturnEditCount { get; init; }
        public int PeerEditCount   { get; init; }
        public int ExpertEditCount { get; init; }
        public int FinalEditCount  { get; init; }
    }

    private sealed class PhaseRow
    {
        public string PhaseName { get; init; } = string.Empty;
        public int    DaysLeft  { get; init; }
    }
}
