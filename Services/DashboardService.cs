using System.Text.Json;
using Dapper;
using MT.Models;

namespace MT.Services;

/// <summary>
/// 命題儀表板服務契約。
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// 依梯次取得 KPI 卡片資料、圖表資料、緊急待辦，一次查詢完成。
    /// LOG 已抽離到 <see cref="GetAuditLogsAsync"/>，因為支援 toggle/chip/分頁。
    /// </summary>
    Task<DashboardKpiDto> GetKpiAsync(int projectId);

    /// <summary>
    /// 依條件取得稽核歷程分頁資料（支援 toggle 含全站事件、類別 chip 過濾、Skip/Take 分頁）。
    /// </summary>
    Task<AuditLogPage> GetAuditLogsAsync(AuditLogQuery query);
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
        // 結案判定（與 HomeService.cs L60 規則一致）：
        //   - 手動結案：ClosedAt 有值
        //   - 自然結案：EndDate 已過
        // ClosedAt 對外顯示：手動結案用 ClosedAt；自然結案 fallback 用 EndDate
        const string sqlProjectStatus = """
            SELECT
                CASE
                    WHEN ClosedAt IS NOT NULL                          THEN 2
                    WHEN EndDate   < CAST(GETDATE() AS DATE)           THEN 2
                    WHEN StartDate > CAST(GETDATE() AS DATE)           THEN 0
                    ELSE 1
                END AS Status,
                COALESCE(
                    ClosedAt,
                    CASE WHEN EndDate < CAST(GETDATE() AS DATE)
                         THEN CAST(EndDate AS DATETIME2) END
                ) AS ClosedAt
            FROM dbo.MT_Projects
            WHERE Id = @pid AND IsDeleted = 0
            """;

        var projectInfo = await conn.QuerySingleOrDefaultAsync<ProjectStatusRow>(
            sqlProjectStatus, new { pid = projectId });
        var projectStatus = projectInfo?.Status;
        var closedAt      = projectInfo?.ClosedAt;

        // 排除 PhaseCode=1（產學計畫區間框架），讓 Footer 顯示真正的工作階段名稱。
        // 否則由於產學區間涵蓋整個專案期間，SortOrder ASC 會先抓到它而誤導使用者。
        const string sqlCurrentPhase = """
            SELECT TOP 1
                PhaseName,
                DATEDIFF(DAY, SYSDATETIME(), CAST(EndDate AS DATETIME2)) AS DaysLeft
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @pid
              AND PhaseCode > 1
              AND StartDate <= CAST(GETDATE() AS DATE)
              AND EndDate   >= CAST(GETDATE() AS DATE)
            ORDER BY SortOrder ASC
            """;

        var phaseRow = await conn.QuerySingleOrDefaultAsync<PhaseRow>(
            sqlCurrentPhase, new { pid = projectId });

        // 若 today 不在任何工作階段內（階段銜接空窗），查最接近的鄰居：
        //   1. 優先：下一個尚未開始的階段（StartDate > today，最早的）
        //   2. 次之：剛結束的階段（EndDate < today，最近的）
        // 這樣 Footer 與「非審題未啟動」說明文字才能呈現有意義的階段名稱。
        NeighborPhase? neighborPhase = null;
        if (phaseRow is null)
        {
            const string sqlNeighbor = """
                SELECT TOP 1 PhaseCode, PhaseName, StartDate, EndDate, IsUpcoming
                FROM (
                    SELECT PhaseCode, PhaseName, StartDate, EndDate, 1 AS IsUpcoming,
                           DATEDIFF(DAY, CAST(GETDATE() AS DATE), StartDate) AS DistanceDays
                    FROM   dbo.MT_ProjectPhases
                    WHERE  ProjectId = @pid AND PhaseCode > 1
                      AND  StartDate > CAST(GETDATE() AS DATE)
                    UNION ALL
                    SELECT PhaseCode, PhaseName, StartDate, EndDate, 0 AS IsUpcoming,
                           DATEDIFF(DAY, EndDate, CAST(GETDATE() AS DATE)) AS DistanceDays
                    FROM   dbo.MT_ProjectPhases
                    WHERE  ProjectId = @pid AND PhaseCode > 1
                      AND  EndDate   < CAST(GETDATE() AS DATE)
                ) n
                ORDER BY IsUpcoming DESC, DistanceDays ASC
                """;
            neighborPhase = await conn.QuerySingleOrDefaultAsync<NeighborPhase>(
                sqlNeighbor, new { pid = projectId });
        }

        // ──────────────────────────────────────────────────────────────
        // 4. 圖表 1：各題型缺口達成率
        //    Produced = Status 2~9（送審後所有進度，不含草稿 0,1）
        //    Target   = MT_ProjectTargets.TargetCount
        //    使用 LEFT JOIN 確保即使無題目也回傳 7 筆（供圖表顯示 0 軸）
        // ──────────────────────────────────────────────────────────────
        const string sqlAchievement = """
            SELECT
                qt.Id   AS QuestionTypeId,
                qt.Name AS TypeName,
                ISNULL(SUM(CASE WHEN q.Status BETWEEN 2 AND 9 THEN 1 ELSE 0 END), 0) AS Produced,
                ISNULL((
                    SELECT SUM(pt2.TargetCount)
                    FROM dbo.MT_ProjectTargets pt2
                    WHERE pt2.QuestionTypeId = qt.Id AND pt2.ProjectId = @pid
                ), 0) AS Target
            FROM dbo.MT_QuestionTypes qt
            LEFT JOIN dbo.MT_Questions q
                   ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
            GROUP BY qt.Id, qt.Name
            ORDER BY qt.Id
            """;

        var achievementRows = (await conn.QueryAsync<DashboardAchievementItem>(
            sqlAchievement, new { pid = projectId })).ToList();

        // ──────────────────────────────────────────────────────────────
        // 5. 圖表 2：各題型依狀態分佈（依當前 PhaseCode 動態分桶）
        //    草稿(0) / 進行中 / 階段完成 / 採用(9) / 不採用(10)
        //
        //    判定差異：
        //    - 審題階段（PhaseCode 3/5/7）：依 MT_ReviewAssignments.Comment 是否填寫
        //         任一 assignment 未填 Comment → 進行中；全填 → 階段完成
        //    - 其他階段（命題/修題）：依 Question.Status 推進
        // ──────────────────────────────────────────────────────────────
        var currentPhaseForChart = await GetCurrentPhaseCodeAsync(conn, projectId);

        List<DashboardStatusByTypeItem> statusByTypeRows;
        if (currentPhaseForChart is 3 or 5 or 7)
        {
            // 審題階段：依 ReviewAssignments.Comment 區分（與卡片 3 同口徑）
            //   PhaseCode 3 → ReviewStage 1、5 → 2、7 → 3
            byte reviewStage = currentPhaseForChart.Value switch { 3 => 1, 5 => 2, 7 => 3, _ => 0 };

            const string sqlReviewBased = """
                WITH QuestionStageStatus AS (
                    SELECT  ra.QuestionId,
                            SUM(CASE WHEN ra.Comment IS NULL
                                       OR LEN(LTRIM(RTRIM(ra.Comment))) = 0
                                     THEN 1 ELSE 0 END) AS PendingCount
                    FROM    dbo.MT_ReviewAssignments ra
                    WHERE   ra.ProjectId = @pid AND ra.ReviewStage = @stage
                    GROUP BY ra.QuestionId
                )
                SELECT
                    qt.Name AS TypeName,
                    ISNULL(SUM(CASE WHEN q.Status = 0 THEN 1 ELSE 0 END), 0) AS Drafts,
                    ISNULL(SUM(CASE
                        WHEN q.Status BETWEEN 1 AND 8
                         AND (qss.PendingCount IS NULL OR qss.PendingCount > 0)
                        THEN 1 ELSE 0 END), 0) AS InProgress,
                    ISNULL(SUM(CASE
                        WHEN q.Status BETWEEN 1 AND 8
                         AND qss.PendingCount = 0
                        THEN 1 ELSE 0 END), 0) AS DoneStage,
                    ISNULL(SUM(CASE WHEN q.Status = 9  THEN 1 ELSE 0 END), 0) AS Adopted,
                    ISNULL(SUM(CASE WHEN q.Status = 10 THEN 1 ELSE 0 END), 0) AS Rejected
                FROM      dbo.MT_QuestionTypes qt
                LEFT JOIN dbo.MT_Questions q
                       ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
                LEFT JOIN QuestionStageStatus qss ON qss.QuestionId = q.Id
                GROUP BY qt.Id, qt.Name
                ORDER BY qt.Id
                """;

            statusByTypeRows = (await conn.QueryAsync<DashboardStatusByTypeItem>(
                sqlReviewBased, new { pid = projectId, stage = reviewStage })).ToList();
        }
        else
        {
            // 命題 / 修題 / 結案 / 未啟動：依 Question.Status 推進
            //   PhaseCode 2 → Status 1 為進行中；PhaseCode 4/6/8 → Status N 為進行中
            //   其餘 → -1，所有 1~8 都歸到「階段完成」
            int inProgressStatus = currentPhaseForChart switch
            {
                2          => 1,
                4 or 6 or 8 => currentPhaseForChart.Value,
                _          => -1
            };

            const string sqlStatusBased = """
                SELECT
                    qt.Name AS TypeName,
                    ISNULL(SUM(CASE WHEN q.Status = 0  THEN 1 ELSE 0 END), 0) AS Drafts,
                    ISNULL(SUM(CASE WHEN q.Status BETWEEN 1 AND 8
                                     AND (q.Status = @inProgress OR q.Status = 2)
                                     THEN 1 ELSE 0 END), 0) AS InProgress,
                    ISNULL(SUM(CASE WHEN q.Status BETWEEN 1 AND 8
                                     AND q.Status <> @inProgress AND q.Status <> 2
                                     THEN 1 ELSE 0 END), 0) AS DoneStage,
                    ISNULL(SUM(CASE WHEN q.Status = 9  THEN 1 ELSE 0 END), 0) AS Adopted,
                    ISNULL(SUM(CASE WHEN q.Status = 10 THEN 1 ELSE 0 END), 0) AS Rejected
                FROM dbo.MT_QuestionTypes qt
                LEFT JOIN dbo.MT_Questions q
                       ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
                GROUP BY qt.Id, qt.Name
                ORDER BY qt.Id
                """;

            statusByTypeRows = (await conn.QueryAsync<DashboardStatusByTypeItem>(
                sqlStatusBased, new { pid = projectId, inProgress = inProgressStatus })).ToList();
        }

        // 計算卡片 3 的狀態類型與顯示文字
        var (phaseStatusType, phaseStatusText, phaseDaysRemaining) =
            ResolvePhaseStatus(projectStatus, phaseRow, neighborPhase);

        // ──────────────────────────────────────────────────────────────
        // 5b. 卡片 3 審題進度（依當前 PhaseCode 對應 ReviewStage）
        //     僅在 PhaseCode ∈ {2,4,6} 時下 SQL，避免不必要查詢
        // ──────────────────────────────────────────────────────────────
        var currentPhaseCode = await GetCurrentPhaseCodeAsync(conn, projectId);
        var (reviewLabel, reviewedCount, reviewTotalCount) =
            await GetReviewProgressAsync(conn, projectId, currentPhaseCode);

        // 已結案專案：覆蓋審題階段判定為 Closed（即使中途結案也顯示為「已結案」）
        if (projectStatus == 2)
        {
            reviewLabel      = ReviewPhaseLabel.Closed;
            reviewedCount    = 0;
            reviewTotalCount = 0;
        }

        // ──────────────────────────────────────────────────────────────
        // 6. 逾期與緊急待辦 Top 5
        //    需在 achievementRows 產出後才可呼叫（TypeShortage 依賴達成率資料）
        // ──────────────────────────────────────────────────────────────
        var urgentItems = await BuildUrgentItemsAsync(conn, projectId, achievementRows);

        // ──────────────────────────────────────────────────────────────
        // 組裝 DTO（LOG 已獨立至 GetAuditLogsAsync，此處不再帶入）
        // ──────────────────────────────────────────────────────────────
        return new DashboardKpiDto
        {
            TotalTarget         = targetRows.Sum(r => r.TargetCount),
            TargetBreakdown     = targetRows,
            AdoptedCount        = counts?.AdoptedCount    ?? 0,
            InReviewCount       = counts?.InReviewCount   ?? 0,
            ReturnEditCount     = counts?.ReturnEditCount ?? 0,
            PeerEditCount       = counts?.PeerEditCount   ?? 0,
            ExpertEditCount     = counts?.ExpertEditCount ?? 0,
            FinalEditCount      = counts?.FinalEditCount  ?? 0,
            PhaseStatusType     = phaseStatusType,
            PhaseStatusText     = phaseStatusText,
            PhaseDaysRemaining  = phaseDaysRemaining,
            CurrentReviewPhase  = reviewLabel,
            ReviewedCount       = reviewedCount,
            ReviewTotalCount    = reviewTotalCount,
            ClosedAt            = closedAt,
            AchievementByType   = achievementRows,
            StatusByType        = statusByTypeRows,
            UrgentItems         = urgentItems
        };
    }

    /// <summary>
    /// 依當前 PhaseCode 對應 ReviewStage，查 MT_ReviewAssignments 統計審題完成度。
    /// 對應規則：PhaseCode 2→Stage 1（互審）/ 4→Stage 2（專審）/ 6→Stage 3（總召）。
    /// 完成判定：Comment 去頭尾空白後不為空字串。
    /// 非審題階段（PhaseCode 為 1/3/5/7 或 null）直接回傳 (None, 0, 0)，不下 SQL。
    /// </summary>
    private static async Task<(ReviewPhaseLabel label, int reviewed, int total)> GetReviewProgressAsync(
        System.Data.IDbConnection conn, int projectId, int? currentPhaseCode)
    {
        // PhaseCode 對應 ReviewStage（DB 實際定義 1=產學區間, 2=命題, 3=互審, 4=互修...）：
        //   3=交互審題 → ReviewStage 1（Peer 互審）
        //   5=專家審題 → ReviewStage 2（Expert 專審）
        //   7=總召審題 → ReviewStage 3（Final 總召）
        var (label, stage) = currentPhaseCode switch
        {
            3 => (ReviewPhaseLabel.Peer,   (byte)1),
            5 => (ReviewPhaseLabel.Expert, (byte)2),
            7 => (ReviewPhaseLabel.Final,  (byte)3),
            _ => (ReviewPhaseLabel.None,   (byte)0)
        };

        if (label == ReviewPhaseLabel.None) return (label, 0, 0);

        const string sql = """
            SELECT
                SUM(CASE WHEN Comment IS NOT NULL
                          AND LEN(LTRIM(RTRIM(Comment))) > 0 THEN 1 ELSE 0 END) AS Reviewed,
                COUNT(*)                                                        AS TotalCount
            FROM   dbo.MT_ReviewAssignments
            WHERE  ProjectId = @pid AND ReviewStage = @stage
            """;

        var row = await conn.QuerySingleOrDefaultAsync<ReviewProgressRow>(
            sql, new { pid = projectId, stage });

        return (label, row?.Reviewed ?? 0, row?.TotalCount ?? 0);
    }

    /// <summary>
    /// 建立「逾期與緊急待辦 Top 5」清單。
    /// 整合兩種訊號：A. 階段倒數（僅當前 + 即將到來階段）、B. 教師命題落後（命題/修題階段才觸發）。
    /// 排序：Severity DESC（Critical=0 最優先）→ DaysRemaining ASC → Take(5)。
    /// 對 Top 5 中的 TeacherShortage 批次撈教師×題型明細，避免 N+1。
    /// </summary>
    private async Task<List<DashboardUrgentItem>> BuildUrgentItemsAsync(
        System.Data.IDbConnection conn,
        int projectId,
        List<DashboardAchievementItem> achievement)
    {
        var items = new List<DashboardUrgentItem>();

        // ── 1. 取得「當前 PhaseCode」：StartDate ≤ 今天，取最大的 ──────
        // 此處無法直接共用 GetKpiAsync 中查過的 currentPhaseCode（避免方法簽名擴張），
        // 重新查詢一次，成本極低（< 5ms）。
        var currentPhaseCode = await GetCurrentPhaseCodeAsync(conn, projectId);

        // ── 2. 撈各 Status 計數，供階段抑制邏輯使用（一次查詢）─────────
        const string sqlStatusCounts2 = """
            SELECT
                SUM(CASE WHEN Status = 3 THEN 1 ELSE 0 END) AS Status3,
                SUM(CASE WHEN Status = 4 THEN 1 ELSE 0 END) AS Status4,
                SUM(CASE WHEN Status = 5 THEN 1 ELSE 0 END) AS Status5,
                SUM(CASE WHEN Status = 6 THEN 1 ELSE 0 END) AS Status6,
                SUM(CASE WHEN Status = 7 THEN 1 ELSE 0 END) AS Status7,
                SUM(CASE WHEN Status = 8 THEN 1 ELSE 0 END) AS Status8
            FROM dbo.MT_Questions
            WHERE ProjectId = @pid AND IsDeleted = 0
            """;

        var statusCounts2 = await conn.QuerySingleOrDefaultAsync<PhaseStatusCounts>(
            sqlStatusCounts2, new { pid = projectId }) ?? new PhaseStatusCounts();

        // ── 3. A. 查 MT_ProjectPhases：所有階段（含已過）──────────────
        //    使用 EndDate ≤ 今天 + 5 天 過濾，再依 currentPhaseCode 精確過濾
        const string sqlPhases = """
            SELECT PhaseCode, PhaseName, StartDate, EndDate,
                   DATEDIFF(DAY, CAST(GETDATE() AS DATE), EndDate)    AS DaysRemaining,
                   DATEDIFF(DAY, CAST(GETDATE() AS DATE), StartDate)  AS DaysToStart
            FROM   dbo.MT_ProjectPhases
            WHERE  ProjectId = @pid
            ORDER  BY PhaseCode ASC
            """;

        var phaseRows = (await conn.QueryAsync<UrgentPhaseRow>(sqlPhases, new { pid = projectId })).ToList();

        // Phase 2（命題階段）抑制條件：所有題型達成率 ≥ 100%
        // （DB 中 PhaseCode=1 是「產學區間」框架；PhaseCode=2 才是命題階段）
        bool propositionCompleted = achievement.All(x => x.Target == 0 || x.Produced >= x.Target);

        foreach (var p in phaseRows)
        {
            // 跳過 PhaseCode=1（產學計畫區間框架），不視為待辦來源
            if (p.PhaseCode == 1) continue;

            // ── 新：依 currentPhaseCode 過濾 ────────────────────────────
            // 已過階段（PhaseCode < currentPhase）→ 不警示
            if (currentPhaseCode.HasValue && p.PhaseCode < currentPhaseCode.Value)
                continue;

            // 當前階段（PhaseCode == currentPhase）→ 依 EndDate 計算倒數
            if (currentPhaseCode.HasValue && p.PhaseCode == currentPhaseCode.Value)
            {
                // 只有 EndDate ≤ 今天 + 5 天 才列入警示（即使已逾期也只在 DaysRemaining < 0 時警示）
                // 但逾期（DaysRemaining < 0）不受 +5 天限制，直接列入
                if (p.DaysRemaining > 5) continue;
            }
            // 下一階段（PhaseCode == currentPhase + 1）→ 距 StartDate ≤ 5 天才觸發 Notice
            else if (currentPhaseCode.HasValue && p.PhaseCode == currentPhaseCode.Value + 1)
            {
                if (p.DaysToStart > 5) continue;
            }
            // 更遠的未來階段 → 不警示
            else if (currentPhaseCode.HasValue && p.PhaseCode > currentPhaseCode.Value + 1)
            {
                continue;
            }
            // currentPhaseCode 未取得（梯次尚未啟動）→ 比照舊邏輯 EndDate ≤ 今天 + 5 天
            else if (!currentPhaseCode.HasValue && p.DaysRemaining > 5)
            {
                continue;
            }

            // 依階段碼判斷是否已完成（滿足條件則跳過）
            // PhaseCode N（互審以後）抑制：對應 Status=N 計數 = 0（沒有題目卡在此狀態）
            bool suppress = p.PhaseCode switch
            {
                2 => propositionCompleted,        // 命題階段：所有題型達成率 ≥ 100%
                3 => statusCounts2.Status3 == 0,  // 交互審題
                4 => statusCounts2.Status4 == 0,  // 互審修題
                5 => statusCounts2.Status5 == 0,  // 專家審題
                6 => statusCounts2.Status6 == 0,  // 專審修題
                7 => statusCounts2.Status7 == 0,  // 總召審題
                8 => statusCounts2.Status8 == 0,  // 總召修題
                _ => false
            };

            if (suppress) continue;

            // 即將到來的下一階段（DaysToStart > 0）顯示 Notice 且文案不同
            if (p.DaysToStart > 0)
            {
                items.Add(new DashboardUrgentItem
                {
                    Severity      = UrgentSeverity.Notice,
                    Source        = UrgentSourceType.PhaseDeadline,
                    Title         = $"{p.PhaseName}預計 {p.DaysToStart} 天後開始",
                    Subtitle      = $"開始日：{p.StartDate:yyyy-MM-dd}",
                    Deadline      = p.StartDate,
                    DaysRemaining = p.DaysToStart,
                    TargetUrl     = ResolvePhaseUrl(p.PhaseCode)
                });
            }
            else
            {
                // 依剩餘天數決定嚴重度
                var severity = p.DaysRemaining < 0  ? UrgentSeverity.Critical
                             : p.DaysRemaining <= 2  ? UrgentSeverity.Warning
                             :                         UrgentSeverity.Notice;

                var title = p.DaysRemaining < 0
                    ? $"{p.PhaseName}已逾期 {Math.Abs(p.DaysRemaining)} 天"
                    : $"{p.PhaseName}倒數 {p.DaysRemaining} 天";

                items.Add(new DashboardUrgentItem
                {
                    Severity      = severity,
                    Source        = UrgentSourceType.PhaseDeadline,
                    Title         = title,
                    Subtitle      = $"截止日：{p.EndDate:yyyy-MM-dd}",
                    Deadline      = p.EndDate,
                    DaysRemaining = p.DaysRemaining,
                    TargetUrl     = ResolvePhaseUrl(p.PhaseCode)
                });
            }
        }

        // ── 4. B. 教師命題落後（TeacherShortage）────────────────────────
        // 觸發條件：命題階段「倒數 5 天內」才警示，與 PhaseDeadline 警示對齊
        //   - 命題未開始 → ✗ 不警示
        //   - 命題進行中且距 EndDate > 5 天 → ✗ 不警示（提早警示反而打擾）
        //   - 命題進行中且距 EndDate ≤ 5 天（含當天）→ ✓ 警示（最後衝刺期）
        //   - 已過 EndDate → ✗ 警示自動下架（補不了，無意義）
        var today         = DateTime.Today;
        var proposalPhase = phaseRows.FirstOrDefault(p => p.PhaseCode == 2);

        bool isProposingPhase =
                proposalPhase is not null
             && proposalPhase.StartDate    <= today
             && proposalPhase.DaysRemaining >= 0   // 尚未過 EndDate
             && proposalPhase.DaysRemaining <= 5;  // 在 5 天倒數窗口內

        if (isProposingPhase)
        {
            // 命題階段倒數 5 天內：任何尚未 100% 完成配額的教師皆需警示
            // 即使總達成率高（例：5/7 ≈ 71%），只要部分題型仍是 0/N，仍會被列入
            const string sqlTeacherShortage = """
                SELECT  pm.UserId,
                        u.DisplayName AS TeacherName,
                        SUM(mq.QuotaCount)                   AS TotalAssigned,
                        ISNULL(SUM(prod.Produced), 0)        AS TotalProduced
                FROM    dbo.MT_ProjectMembers pm
                JOIN    dbo.MT_MemberQuotas   mq ON mq.ProjectMemberId = pm.Id
                JOIN    dbo.MT_Users          u  ON u.Id = pm.UserId
                OUTER APPLY (
                    SELECT COUNT(*) AS Produced
                    FROM   dbo.MT_Questions
                    WHERE  ProjectId      = pm.ProjectId
                      AND  QuestionTypeId = mq.QuestionTypeId
                      AND  CreatorId      = pm.UserId
                      AND  IsDeleted      = 0
                      AND  Status BETWEEN 2 AND 9
                ) prod
                WHERE   pm.ProjectId = @pid
                GROUP BY pm.UserId, u.DisplayName
                HAVING  SUM(mq.QuotaCount) > 0
                  AND   ISNULL(SUM(prod.Produced), 0) < SUM(mq.QuotaCount)
                ORDER   BY (ISNULL(SUM(prod.Produced), 0) * 1.0 / SUM(mq.QuotaCount)) ASC
                """;

            var teacherRows = (await conn.QueryAsync<TeacherShortageRow>(
                sqlTeacherShortage, new { pid = projectId })).ToList();

            foreach (var t in teacherRows)
            {
                var rate     = t.TotalAssigned > 0 ? (decimal)t.TotalProduced / t.TotalAssigned : 0m;
                // 嚴重度：< 30% 嚴重落後 / < 70% 進度警示 / 70~99% 提醒（最後一哩）
                var severity = rate < 0.3m  ? UrgentSeverity.Critical
                             : rate < 0.7m  ? UrgentSeverity.Warning
                             :                UrgentSeverity.Notice;

                items.Add(new DashboardUrgentItem
                {
                    Severity  = severity,
                    Source    = UrgentSourceType.TeacherShortage,
                    Title     = $"{t.TeacherName} 命題進度落後",
                    Subtitle  = $"目前 {t.TotalProduced}/{t.TotalAssigned}（{rate:P0}）",
                    UserId    = t.UserId,
                    TargetUrl = $"/overview?creatorId={t.UserId}"
                });
            }
        }

        // ── 5. 排序：Severity DESC → DaysRemaining ASC（不截斷）──────
        // 警示窗內所有未完成項目都得顯示，不應因清單上限漏掉任何提醒
        // 卡片以 max-height + scroll 控制視覺空間
        var top5 = items
            .OrderBy(x => (int)x.Severity)
            .ThenBy(x => x.DaysRemaining ?? int.MaxValue)
            .ToList();

        // ── 6. 批次查詢 TeacherShortage 的教師×題型明細（避免 N+1）──────
        var userIds = top5
            .Where(x => x.Source == UrgentSourceType.TeacherShortage && x.UserId.HasValue)
            .Select(x => x.UserId!.Value)
            .Distinct()
            .ToList();

        if (userIds.Count > 0)
        {
            const string sqlTypeDetails = """
                SELECT  pm.UserId,
                        mq.QuestionTypeId,
                        qt.Name AS TypeName,
                        mq.QuotaCount AS Assigned,
                        ISNULL(prod.Produced, 0) AS Produced
                FROM    dbo.MT_ProjectMembers pm
                JOIN    dbo.MT_MemberQuotas   mq ON mq.ProjectMemberId = pm.Id
                JOIN    dbo.MT_QuestionTypes  qt ON qt.Id = mq.QuestionTypeId
                OUTER APPLY (
                    SELECT COUNT(*) AS Produced
                    FROM   dbo.MT_Questions
                    WHERE  ProjectId      = pm.ProjectId
                      AND  QuestionTypeId = mq.QuestionTypeId
                      AND  CreatorId      = pm.UserId
                      AND  IsDeleted      = 0
                      AND  Status BETWEEN 2 AND 9
                ) prod
                WHERE   pm.ProjectId = @pid AND pm.UserId IN @userIds
                ORDER   BY pm.UserId, prod.Produced * 1.0 / NULLIF(mq.QuotaCount, 0) ASC
                """;

            var detailRows = (await conn.QueryAsync<TeacherTypeDetailRow>(
                sqlTypeDetails, new { pid = projectId, userIds })).ToList();

            // 依 UserId 分組，塞回對應的 UrgentItem
            var grouped = detailRows
                .GroupBy(r => r.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var item in top5.Where(x => x.Source == UrgentSourceType.TeacherShortage
                                               && x.UserId.HasValue))
            {
                if (!grouped.TryGetValue(item.UserId!.Value, out var rows)) continue;

                item.TeacherDetails = rows.Select(r => new UrgentTeacherDetail
                {
                    QuestionTypeId = r.QuestionTypeId,
                    TypeName       = r.TypeName,
                    Assigned       = r.Assigned,
                    Produced       = r.Produced,
                    Achievement    = r.Assigned > 0
                        ? Math.Round((decimal)r.Produced / r.Assigned, 4)
                        : 0m
                }).ToList();
            }
        }

        return top5;
    }

    /// <inheritdoc />
    public async Task<AuditLogPage> GetAuditLogsAsync(AuditLogQuery query)
    {
        using var conn = _db.CreateConnection();

        // 過濾條件（C# 端解析，避免 SQL 動態字串拼接）：
        //   typeCode  → 對應 MT_AuditLogs.TargetType（null = 不過濾）
        //   loginOnly → 1 表示僅顯示 Action 3/4（登入/登出）
        int? typeCode = query.TypeFilter switch
        {
            LogTypeFilter.Question     => 3,
            LogTypeFilter.Announcement => 4,
            LogTypeFilter.Role         => 1,
            LogTypeFilter.Teacher      => 5,
            LogTypeFilter.Review       => 6,
            _                          => null
        };
        int loginOnly  = query.TypeFilter == LogTypeFilter.Login ? 1 : 0;
        int includeGlb = query.IncludeGlobal ? 1 : 0;

        var sqlParams = new
        {
            pid           = query.ProjectId,
            includeGlobal = includeGlb,
            typeCode,
            loginOnly,
            skip          = query.Skip,
            take          = query.Take
        };

        // ── Step 1：主查詢（OFFSET FETCH 分頁）──────────────────────────
        // OldValue/NewValue 也帶出，以便目標資料已刪除時從 JSON 解析名稱
        const string sqlMain = """
            SELECT
                al.Id,
                al.UserId,
                ISNULL(u.DisplayName, N'系統') AS UserName,
                al.Action,
                al.TargetType,
                al.TargetId,
                al.CreatedAt,
                al.OldValue,
                al.NewValue
            FROM   dbo.MT_AuditLogs al
            LEFT   JOIN dbo.MT_Users u ON u.Id = al.UserId
            WHERE  ( al.ProjectId = @pid
                     OR (@includeGlobal = 1 AND al.ProjectId IS NULL) )
              AND  al.Action IN (0, 1, 2, 3, 4)
              AND  ( @typeCode IS NULL OR al.TargetType = @typeCode )
              AND  ( @loginOnly = 0     OR al.Action IN (3, 4) )
            ORDER  BY al.CreatedAt DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """;

        const string sqlCount = """
            SELECT COUNT(*)
            FROM   dbo.MT_AuditLogs al
            WHERE  ( al.ProjectId = @pid
                     OR (@includeGlobal = 1 AND al.ProjectId IS NULL) )
              AND  al.Action IN (0, 1, 2, 3, 4)
              AND  ( @typeCode IS NULL OR al.TargetType = @typeCode )
              AND  ( @loginOnly = 0     OR al.Action IN (3, 4) );
            """;

        var logs = (await conn.QueryAsync<RecentAuditLog>(sqlMain, sqlParams)).ToList();
        var total = await conn.ExecuteScalarAsync<int>(sqlCount, sqlParams);

        // 沒結果直接回傳（仍帶總數，讓 UI 顯示空狀態）
        if (logs.Count == 0)
        {
            return new AuditLogPage { Logs = logs, TotalCount = total, HasMore = false };
        }

        await ResolveLogTargetNamesAsync(conn, logs);

        return new AuditLogPage
        {
            Logs       = logs,
            TotalCount = total,
            HasMore    = query.Skip + logs.Count < total
        };
    }

    /// <summary>批次解析 LOG 的 TargetName（避免 N+1）。</summary>
    private static async Task ResolveLogTargetNamesAsync(
        System.Data.IDbConnection conn, List<RecentAuditLog> logs)
    {
        if (logs.Count == 0) return;

        // ── Step 2：依 TargetType 分組批次解析 TargetName ──────────────
        // 聚合各 TargetType → TargetId 清單
        // - Login/Logout（Action 3,4）：UserName 已在 Step 1 取得，無需再查目標名稱
        // - TargetType=6(Reviews) JOIN MT_Questions 顯示對應題目的 QuestionCode
        var grouped = logs
            .Where(l => l.Action != 3 && l.Action != 4)
            .GroupBy(l => l.TargetType)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TargetId).Distinct().ToList());

        // 每種 TargetType 一次批次查詢，存入 Dictionary<int, string>
        var nameMap = new Dictionary<(byte type, int id), string>();

        foreach (var (targetType, ids) in grouped)
        {
            // 依 TargetType 選擇對應的查詢
            string? sql = targetType switch
            {
                0 => "SELECT Id AS TargetId, DisplayName AS TargetName FROM dbo.MT_Users WHERE Id IN @ids",
                1 => "SELECT Id AS TargetId, Name AS TargetName FROM dbo.MT_Roles WHERE Id IN @ids",
                2 => "SELECT Id AS TargetId, Name AS TargetName FROM dbo.MT_Projects WHERE Id IN @ids",
                3 => "SELECT Id AS TargetId, QuestionCode AS TargetName FROM dbo.MT_Questions WHERE Id IN @ids",
                4 => "SELECT Id AS TargetId, Title AS TargetName FROM dbo.MT_Announcements WHERE Id IN @ids",
                5 => """
                     SELECT t.Id AS TargetId, u.DisplayName AS TargetName
                     FROM   dbo.MT_Teachers t
                     JOIN   dbo.MT_Users u ON u.Id = t.UserId
                     WHERE  t.Id IN @ids
                     """,
                6 => """
                     SELECT ra.Id AS TargetId, q.QuestionCode AS TargetName
                     FROM   dbo.MT_ReviewAssignments ra
                     JOIN   dbo.MT_Questions q ON q.Id = ra.QuestionId
                     WHERE  ra.Id IN @ids
                     """,
                _ => null
            };

            if (sql is null) continue;

            var rows = await conn.QueryAsync<(int TargetId, string TargetName)>(sql, new { ids });
            foreach (var (id, name) in rows)
                nameMap[(targetType, id)] = name;
        }

        // ── 填入 TargetName ─────────────────────────────────────────────
        foreach (var log in logs)
        {
            if (log.Action == 3 || log.Action == 4)
            {
                // Login/Logout：目標就是使用者本人，UI 端顯示時不引用 TargetName
                log.TargetName = string.Empty;
            }
            else if (nameMap.TryGetValue((log.TargetType, log.TargetId), out var found))
            {
                log.TargetName = found;
            }
            else
            {
                // 目標資料表查無 → fallback：從 OldValue/NewValue JSON 解析原始名稱
                // Delete 操作優先看 OldValue；Create/Update 優先看 NewValue
                var json = log.Action == 2
                    ? (log.OldValue ?? log.NewValue)
                    : (log.NewValue ?? log.OldValue);
                log.TargetName = ExtractNameFromJson(log.TargetType, json) ?? "已刪除";
            }
        }
    }

    /// <summary>
    /// 從 AuditLog 的 OldValue/NewValue JSON 中抽取對應 TargetType 的名稱欄位。
    /// 解析失敗（JSON 損壞、無對應欄位）時回 null，由呼叫端 fallback 為「已刪除」。
    /// </summary>
    private static string? ExtractNameFromJson(byte targetType, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        // 各 TargetType 對應的 JSON 名稱欄位（同時嘗試 camelCase 與 PascalCase）
        string fieldKey = targetType switch
        {
            0 or 5 => "displayName",   // Users / Teachers
            1 or 2 => "name",          // Roles / Projects
            3      => "questionCode",  // Questions
            4      => "title",         // Announcements
            _      => "name"
        };

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            // 同時嘗試小寫開頭與大寫開頭兩種 key（Json options 不確定）
            string[] keys =
            {
                fieldKey,
                char.ToUpperInvariant(fieldKey[0]) + fieldKey[1..]
            };
            foreach (var k in keys)
            {
                if (doc.RootElement.TryGetProperty(k, out var v)
                    && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }
        catch (JsonException)
        {
            // JSON 格式錯誤 → 視為查無
        }
        return null;
    }

    /// <summary>
    /// 查詢當前進行中的 PhaseCode（StartDate ≤ 今天，取最大的 PhaseCode）。
    /// 若梯次尚未啟動或無符合資料，回傳 null。
    /// </summary>
    private static async Task<int?> GetCurrentPhaseCodeAsync(
        System.Data.IDbConnection conn, int projectId)
    {
        const string sql = """
            SELECT TOP 1 PhaseCode
            FROM   dbo.MT_ProjectPhases
            WHERE  ProjectId = @pid
              AND  StartDate <= CAST(GETDATE() AS DATE)
            ORDER  BY PhaseCode DESC
            """;
        return await conn.QuerySingleOrDefaultAsync<int?>(sql, new { pid = projectId });
    }


    /// <summary>
    /// 依階段碼回傳對應的目的 URL（DB 實際 PhaseCode：1=產學區間, 2=命題, 3=互審, 4=互修...）。
    /// 命題階段（2）→ 命題作業區；各修題階段（4,6,8）→ 審修作業區；各審題階段（3,5,7）→ 審題作業區。
    /// </summary>
    private static string ResolvePhaseUrl(int phaseCode) => phaseCode switch
    {
        2                => "/cwt-list?tab=compose",
        4 or 6 or 8      => "/cwt-list?tab=revision",
        3 or 5 or 7      => "/reviews?tab=review",
        _                => "/projects"
    };

    /// <summary>
    /// 依梯次狀態碼與 Phase 查詢結果，決定卡片 3 Footer 要顯示的狀態。
    /// 抽出成獨立方法以便日後維護，純運算不查 DB。
    /// </summary>
    private static (PhaseStatusType type, string text, int? days) ResolvePhaseStatus(
        int? projectStatus, PhaseRow? phaseRow, NeighborPhase? neighbor)
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
        // 用鄰近階段補上有意義的階段名稱：
        //   - 下一階段為命題（PhaseCode=2，第一個工作階段）→「準備階段」
        //   - 其他下一階段尚未開始 → 「{PhaseName}（預計 N 天後開始）」
        //   - 全部階段已結束    → 「{PhaseName}（已結束）」
        if (neighbor is not null)
        {
            if (neighbor.IsUpcoming == 1)
            {
                // 第一個工作階段（命題階段）尚未開始 → 對使用者來說等同於「準備中」
                if (neighbor.PhaseCode == 2)
                    return (PhaseStatusType.Preparing, "準備階段", null);

                var daysToStart = (neighbor.StartDate.Date - DateTime.Today).Days;
                var text = daysToStart > 0
                    ? $"{neighbor.PhaseName}（預計 {daysToStart} 天後開始）"
                    : $"{neighbor.PhaseName}（即將開始）";
                return (PhaseStatusType.BetweenPhases, text, null);
            }
            else
            {
                return (PhaseStatusType.BetweenPhases, $"{neighbor.PhaseName}（已結束）", null);
            }
        }

        // 完全沒有設定階段（防呆 fallback）
        return (PhaseStatusType.BetweenPhases, "階段銜接中", null);
    }

    // ──── 內部 mapping 型別（僅此 Service 使用，無需暴露）────────────

    /// <summary>各審題相關 Status 的待辦計數，用於 PhaseDeadline 抑制邏輯。</summary>
    private sealed class PhaseStatusCounts
    {
        public int Status3 { get; init; }
        public int Status4 { get; init; }
        public int Status5 { get; init; }
        public int Status6 { get; init; }
        public int Status7 { get; init; }
        public int Status8 { get; init; }
    }

    private sealed class UrgentPhaseRow
    {
        public int      PhaseCode     { get; init; }
        public string   PhaseName     { get; init; } = string.Empty;
        public DateTime StartDate     { get; init; }
        public DateTime EndDate       { get; init; }
        public int      DaysRemaining { get; init; }
        public int      DaysToStart   { get; init; }
    }

    /// <summary>教師落後彙總列（GROUP BY 教師）。</summary>
    private sealed record TeacherShortageRow(
        int UserId, string TeacherName, int TotalAssigned, int TotalProduced);

    /// <summary>教師×題型明細列（展開用）。</summary>
    private sealed record TeacherTypeDetailRow(
        int UserId, int QuestionTypeId, string TypeName, int Assigned, int Produced);

    private sealed class StatusCountRow
    {
        public int AdoptedCount    { get; init; }
        public int InReviewCount   { get; init; }
        public int ReturnEditCount { get; init; }
        public int PeerEditCount   { get; init; }
        public int ExpertEditCount { get; init; }
        public int FinalEditCount  { get; init; }
    }

    /// <summary>卡片 3 審題進度查詢結果列。</summary>
    private sealed class ReviewProgressRow
    {
        public int Reviewed   { get; init; }
        public int TotalCount { get; init; }
    }

    /// <summary>專案狀態 + 結案時間（卡片 3 已結案狀態用）。</summary>
    private sealed class ProjectStatusRow
    {
        public int       Status   { get; init; }
        public DateTime? ClosedAt { get; init; }
    }

    private sealed class PhaseRow
    {
        public string PhaseName { get; init; } = string.Empty;
        public int    DaysLeft  { get; init; }
    }

    /// <summary>階段銜接空窗期時，鄰近階段查詢結果（即將開始 / 剛結束）。</summary>
    private sealed class NeighborPhase
    {
        public byte     PhaseCode  { get; init; }
        public string   PhaseName  { get; init; } = string.Empty;
        public DateTime StartDate  { get; init; }
        public DateTime EndDate    { get; init; }
        /// <summary>1 = 尚未開始（upcoming）、0 = 已結束（past）。</summary>
        public byte     IsUpcoming { get; init; }
    }
}
