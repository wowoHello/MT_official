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
    private readonly IQuestionTypeCatalog _typeCatalog;

    public DashboardService(IDatabaseService db, ILogger<DashboardService> logger, IQuestionTypeCatalog typeCatalog)
    {
        _db = db;
        _logger = logger;
        _typeCatalog = typeCatalog;
    }

    /// <summary>
    /// 修補 G：給並行 SQL 使用 — 每次呼叫建一個獨立 conn（Dapper 同 conn 不支援並發 command），
    /// action 完成後自動 Dispose。回傳 Task 由 caller 用 await/Task.WhenAll 處理。
    /// </summary>
    private async Task<T> WithOwnConnAsync<T>(Func<System.Data.IDbConnection, Task<T>> action)
    {
        using var conn = _db.CreateConnection();
        return await action(conn);
    }

    /// <inheritdoc />
    public async Task<DashboardKpiDto> GetKpiAsync(int projectId)
    {
        // 修補 G Stage 1：5 個無相依 SQL 並行（各自開 conn — Dapper 同 conn 不支援並發 command）

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

        // ──────────────────────────────────────────────────────────────
        // 2. 各題目狀態計數（一次掃表，按 Status 分組）
        //    IsDeleted = 0：只計算未軟刪除的題目
        //    僅統計卡片 2「採用」需要的欄位；其餘修題/審題進度由 sqlStatusBased + 修題 SQL 分別處理
        // ──────────────────────────────────────────────────────────────
        const string sqlStatusCounts = """
            SELECT
                SUM(CASE WHEN Status IN (9, 12) THEN 1 ELSE 0 END) AS AdoptedCount
            FROM dbo.MT_Questions
            WHERE ProjectId = @pid AND IsDeleted = 0
            """;

        // ──────────────────────────────────────────────────────────────
        // 3. 梯次狀態 + 結案時間
        //    MT_Projects 無 Status 欄位，需用 ClosedAt / EndDate / StartDate 計算
        //    （結案判定與 HomeService.cs 一致：手動結案 ClosedAt 有值 / 自然結案 EndDate 已過）
        // ──────────────────────────────────────────────────────────────
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

        // 修補 B：一次性撈所有階段資料，後續 in-memory 推導 phaseRow / neighborPhase / currentPhaseCode / urgentItems
        const string sqlAllPhases = """
            SELECT PhaseCode, PhaseName, StartDate, EndDate, SortOrder,
                   DATEDIFF(DAY, CAST(GETDATE() AS DATE), EndDate)    AS DaysRemaining,
                   DATEDIFF(DAY, CAST(GETDATE() AS DATE), StartDate)  AS DaysToStart
            FROM   dbo.MT_ProjectPhases
            WHERE  ProjectId = @pid
            ORDER  BY PhaseCode ASC
            """;

        // 4. 圖表 1：題型缺口達成率
        // 修補 F：CTE 預先聚合 + LEFT JOIN（hash join 取代 nested loop）
        const string sqlAchievement = """
            WITH ProducedCounts AS (
                SELECT QuestionTypeId,
                       SUM(CASE WHEN Status NOT IN (0, 10, 11) THEN 1 ELSE 0 END) AS Produced
                FROM   dbo.MT_Questions
                WHERE  ProjectId = @pid AND IsDeleted = 0
                GROUP BY QuestionTypeId
            ),
            AggregatedTargets AS (
                SELECT QuestionTypeId, SUM(TargetCount) AS TotalTarget
                FROM   dbo.MT_ProjectTargets
                WHERE  ProjectId = @pid
                GROUP BY QuestionTypeId
            )
            SELECT
                qt.Id   AS QuestionTypeId,
                qt.Name AS TypeName,
                ISNULL(pc.Produced, 0)    AS Produced,
                ISNULL(at.TotalTarget, 0) AS Target
            FROM dbo.MT_QuestionTypes qt
            LEFT JOIN ProducedCounts     pc ON pc.QuestionTypeId = qt.Id
            LEFT JOIN AggregatedTargets  at ON at.QuestionTypeId = qt.Id
            ORDER BY qt.Id
            """;

        // ── 並行發出 5 個 task ──
        var targetsTask       = WithOwnConnAsync(c => c.QueryAsync<DashboardTargetBreakdown>(sqlTargets, new { pid = projectId }));
        var statusCountsTask  = WithOwnConnAsync(c => c.QuerySingleOrDefaultAsync<StatusCountRow>(sqlStatusCounts, new { pid = projectId }));
        var projectStatusTask = WithOwnConnAsync(c => c.QuerySingleOrDefaultAsync<ProjectStatusRow>(sqlProjectStatus, new { pid = projectId }));
        var allPhasesTask     = WithOwnConnAsync(c => c.QueryAsync<UrgentPhaseRow>(sqlAllPhases, new { pid = projectId }));
        var achievementTask   = WithOwnConnAsync(c => c.QueryAsync<DashboardAchievementItem>(sqlAchievement, new { pid = projectId }));

        await Task.WhenAll(targetsTask, statusCountsTask, projectStatusTask, allPhasesTask, achievementTask);

        var targetRows      = targetsTask.Result.ToList();
        var counts          = statusCountsTask.Result;
        var projectInfo     = projectStatusTask.Result;
        var allPhases       = allPhasesTask.Result.ToList();
        var achievementRows = achievementTask.Result.ToList();

        var projectStatus = projectInfo?.Status;
        var closedAt      = projectInfo?.ClosedAt;
        var today         = DateTime.Today;

        // phaseRow（卡片 3+4 Footer 用）：當前在區間內的工作階段（排除 PhaseCode=1 產學區間框架）
        // 排序：SortOrder ASC 取第一筆作為「主要當前階段」（與原 sqlCurrentPhase 邏輯一致）
        var currentPhaseRecord = allPhases
            .Where(p => p.PhaseCode > 1 && p.StartDate <= today && p.EndDate >= today)
            .OrderBy(p => p.SortOrder)
            .FirstOrDefault();
        var phaseRow = currentPhaseRecord is null
            ? null
            : new PhaseRow { PhaseName = currentPhaseRecord.PhaseName, DaysLeft = currentPhaseRecord.DaysRemaining };

        // neighborPhase（卡片 Footer 銜接空窗備援）：當前無 phaseRow 時找鄰近階段
        // 優先順序：upcoming（即將開始）距離小 → past（剛結束）距離小
        NeighborPhase? neighborPhase = null;
        if (phaseRow is null)
        {
            neighborPhase = allPhases
                .Where(p => p.PhaseCode > 1 && (p.StartDate > today || p.EndDate < today))
                .Select(p => new
                {
                    IsUpcoming   = p.StartDate > today,
                    DistanceDays = p.StartDate > today ? (p.StartDate - today).Days : (today - p.EndDate).Days,
                    Phase = new NeighborPhase
                    {
                        PhaseCode  = (byte)p.PhaseCode,
                        PhaseName  = p.PhaseName,
                        StartDate  = p.StartDate,
                        EndDate    = p.EndDate,
                        IsUpcoming = (byte)(p.StartDate > today ? 1 : 0)
                    }
                })
                .OrderByDescending(x => x.IsUpcoming)
                .ThenBy(x => x.DistanceDays)
                .Select(x => x.Phase)
                .FirstOrDefault();
        }

        // ──────────────────────────────────────────────────────────────
        // 5. 圖表 2：各題型依狀態分佈（依當前 PhaseCode 動態分桶）
        //    草稿(0) / 進行中 / 階段完成 / 採用(9) / 不採用(10)
        //
        //    判定差異：
        //    - 審題階段（PhaseCode 3/5/7）：依 MT_ReviewAssignments.Comment 是否填寫
        //         任一 assignment 未填 Comment → 進行中；全填 → 階段完成
        //    - 其他階段（命題/修題）：依 Question.Status 推進
        // ──────────────────────────────────────────────────────────────
        // 修補 B：currentPhaseCode 從 allPhases in-memory 算（StartDate ≤ today + 取 Max PhaseCode）
        int? currentPhaseForChart = allPhases
            .Where(p => p.StartDate <= today)
            .Select(p => (int?)p.PhaseCode)
            .DefaultIfEmpty(null)
            .Max();

        // 修補 G Stage 3：4 個依賴 currentPhaseForChart 的 SQL 並行（各自開 conn）
        //   - statusByType（依 phaseCode 走 4 種 SQL 分支） / GetReviewProgress / GetRevisionProgress / BuildUrgentItems
        var statusByTypeTask     = WithOwnConnAsync(c => LoadStatusByTypeRowsAsync(c, projectId, currentPhaseForChart));
        var reviewProgressTask   = WithOwnConnAsync(c => GetReviewProgressAsync(c, projectId, currentPhaseForChart));
        var revisionProgressTask = WithOwnConnAsync(c => GetRevisionProgressAsync(c, projectId, currentPhaseForChart));
        var urgentItemsTask      = WithOwnConnAsync(c => BuildUrgentItemsAsync(c, projectId, achievementRows, currentPhaseForChart, allPhases));

        await Task.WhenAll(statusByTypeTask, reviewProgressTask, revisionProgressTask, urgentItemsTask);

        var statusByTypeRows                                    = statusByTypeTask.Result;
        var (reviewLabel, reviewedCount, reviewTotalCount)      = reviewProgressTask.Result;
        var (revisionLabel, revisedCount, revisionTotalCount)   = revisionProgressTask.Result;
        var urgentItems                                         = urgentItemsTask.Result;

        // 計算卡片 3 的狀態類型與顯示文字
        var (phaseStatusType, phaseStatusText, phaseDaysRemaining) =
            ResolvePhaseStatus(projectStatus, phaseRow, neighborPhase);

        // 已結案專案：覆蓋審/修題階段判定為 Closed（即使中途結案也顯示為「已結案」）
        if (projectStatus == 2)
        {
            reviewLabel        = ReviewPhaseLabel.Closed;
            reviewedCount      = 0;
            reviewTotalCount   = 0;
            revisionLabel      = RevisionPhaseLabel.Closed;
            revisedCount       = 0;
            revisionTotalCount = 0;
        }

        // ──────────────────────────────────────────────────────────────
        // 組裝 DTO（LOG 已獨立至 GetAuditLogsAsync，此處不再帶入）
        // ──────────────────────────────────────────────────────────────
        return new DashboardKpiDto
        {
            TotalTarget         = targetRows.Sum(r => r.TargetCount),
            TargetBreakdown     = targetRows,
            AdoptedCount        = counts?.AdoptedCount    ?? 0,
            PhaseStatusType     = phaseStatusType,
            PhaseStatusText     = phaseStatusText,
            PhaseDaysRemaining  = phaseDaysRemaining,
            CurrentReviewPhase   = reviewLabel,
            ReviewedCount        = reviewedCount,
            ReviewTotalCount     = reviewTotalCount,
            CurrentRevisionPhase = revisionLabel,
            RevisedCount         = revisedCount,
            RevisionTotalCount   = revisionTotalCount,
            ClosedAt             = closedAt,
            AchievementByType   = achievementRows,
            StatusByType        = statusByTypeRows,
            UrgentItems         = urgentItems
        };
    }

    /// <summary>
    /// 修補 G：原本 GetKpiAsync 內 4 個 if-elseif 分支抽出。
    /// 依當前 PhaseCode 走不同 SQL 路徑算「依題型狀態分佈」資料（給圖表 2 用）。
    ///   PhaseCode 4/6/8 修題階段 → 依 MT_RevisionReplies.Content 區分
    ///   PhaseCode 3/5/7 審題階段 → 依 MT_ReviewAssignments.Comment 區分
    ///   PhaseCode 2     命題階段 → 依 Target 計算缺口
    ///   其他           → 純 Status 推進
    /// </summary>
    private static async Task<List<DashboardStatusByTypeItem>> LoadStatusByTypeRowsAsync(
        System.Data.IDbConnection conn, int projectId, int? currentPhaseForChart)
    {
        if (currentPhaseForChart is 4 or 6 or 8)
        {
            // 修題階段：依 MT_RevisionReplies.Content 區分（與卡片 4 同口徑）
            // Plan_014 本輪過濾 — PC=8 跨輪退回後舊 reply 不算本輪已修；PC=4/6 線性單輪不受影響
            byte revisionStage = (byte)currentPhaseForChart.Value;
            const string sqlRevisionBased = """
                WITH QuestionRevisionStatus AS (
                    SELECT DISTINCT rr.QuestionId
                    FROM   dbo.MT_RevisionReplies rr
                    WHERE  rr.Stage = @revisionStage
                      AND  rr.Content IS NOT NULL
                      AND  LEN(TRIM(rr.Content)) > 0
                      AND  rr.CreatedAt > ISNULL(
                          (SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt
                           WHERE QuestionId = rr.QuestionId),
                          '1900-01-01')
                )
                SELECT
                    qt.Name AS TypeName,
                    ISNULL(SUM(CASE WHEN q.Status = 0 THEN 1 ELSE 0 END), 0) AS Drafts,
                    ISNULL(SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND qrs.QuestionId IS NULL     THEN 1 ELSE 0 END), 0) AS InProgress,
                    ISNULL(SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND qrs.QuestionId IS NOT NULL THEN 1 ELSE 0 END), 0) AS DoneStage,
                    ISNULL(SUM(CASE WHEN q.Status IN (9, 12)  THEN 1 ELSE 0 END), 0) AS Adopted,
                    -- 不採用合計 = 10(Rejected 三審判決) + 11(ClosedNotAdopted 結案清盤)；警示型不採用走 Status=8 不會落此桶
                    ISNULL(SUM(CASE WHEN q.Status IN (10, 11) THEN 1 ELSE 0 END), 0) AS Rejected
                FROM      dbo.MT_QuestionTypes qt
                LEFT JOIN dbo.MT_Questions q
                       ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
                LEFT JOIN QuestionRevisionStatus qrs ON qrs.QuestionId = q.Id
                GROUP BY qt.Id, qt.Name
                ORDER BY qt.Id
                """;
            return (await conn.QueryAsync<DashboardStatusByTypeItem>(sqlRevisionBased, new { pid = projectId, revisionStage })).ToList();
        }

        if (currentPhaseForChart is 3 or 5 or 7)
        {
            // 審題階段：依 ReviewAssignments.DecidedAt 區分（與卡片 3 同口徑）
            byte reviewStage = currentPhaseForChart.Value switch { 3 => 1, 5 => 2, 7 => 3, _ => 0 };
            const string sqlReviewBased = """
                WITH QuestionStageStatus AS (
                    SELECT  ra.QuestionId,
                            SUM(CASE WHEN ra.DecidedAt IS NULL
                                     THEN 1 ELSE 0 END) AS PendingCount
                    FROM    dbo.MT_ReviewAssignments ra
                    WHERE   ra.ProjectId = @pid AND ra.ReviewStage = @stage
                    GROUP BY ra.QuestionId
                )
                SELECT
                    qt.Name AS TypeName,
                    ISNULL(SUM(CASE WHEN q.Status = 0 THEN 1 ELSE 0 END), 0) AS Drafts,
                    ISNULL(SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND (qss.PendingCount IS NULL OR qss.PendingCount > 0) THEN 1 ELSE 0 END), 0) AS InProgress,
                    ISNULL(SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND qss.PendingCount = 0 THEN 1 ELSE 0 END), 0) AS DoneStage,
                    ISNULL(SUM(CASE WHEN q.Status IN (9, 12)  THEN 1 ELSE 0 END), 0) AS Adopted,
                    ISNULL(SUM(CASE WHEN q.Status IN (10, 11) THEN 1 ELSE 0 END), 0) AS Rejected
                FROM      dbo.MT_QuestionTypes qt
                LEFT JOIN dbo.MT_Questions q
                       ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
                LEFT JOIN QuestionStageStatus qss ON qss.QuestionId = q.Id
                GROUP BY qt.Id, qt.Name
                ORDER BY qt.Id
                """;
            return (await conn.QueryAsync<DashboardStatusByTypeItem>(sqlReviewBased, new { pid = projectId, stage = reviewStage })).ToList();
        }

        if (currentPhaseForChart == 2)
        {
            // 命題階段：橘=剩餘工作（缺口）/ 藍=做完的工作（Status 1/2）
            const string sqlCompositionPhase = """
                WITH TypeAgg AS (
                    SELECT
                        qt.Id,
                        qt.Name AS TypeName,
                        ISNULL((SELECT SUM(pt.TargetCount)
                                FROM   dbo.MT_ProjectTargets pt
                                WHERE  pt.QuestionTypeId = qt.Id AND pt.ProjectId = @pid), 0) AS TargetCount,
                        ISNULL(SUM(CASE WHEN q.Status = 0          THEN 1 ELSE 0 END), 0) AS Drafts,
                        ISNULL(SUM(CASE WHEN q.Status IN (1, 2)    THEN 1 ELSE 0 END), 0) AS DoneStage,
                        ISNULL(SUM(CASE WHEN q.Status IN (9, 12)   THEN 1 ELSE 0 END), 0) AS Adopted,
                        ISNULL(SUM(CASE WHEN q.Status IN (10, 11)  THEN 1 ELSE 0 END), 0) AS Rejected
                    FROM      dbo.MT_QuestionTypes qt
                    LEFT JOIN dbo.MT_Questions q
                           ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
                    GROUP BY qt.Id, qt.Name
                )
                SELECT
                    TypeName, Drafts,
                    CASE WHEN TargetCount - Drafts - DoneStage - Adopted - Rejected > 0
                         THEN TargetCount - Drafts - DoneStage - Adopted - Rejected
                         ELSE 0 END AS InProgress,
                    DoneStage, Adopted, Rejected
                FROM   TypeAgg
                ORDER BY Id
                """;
            return (await conn.QueryAsync<DashboardStatusByTypeItem>(sqlCompositionPhase, new { pid = projectId })).ToList();
        }

        // 修題 / 結案 / 未啟動：純 Status 推進
        int inProgressStatus = currentPhaseForChart switch
        {
            4 or 6 or 8 => currentPhaseForChart.Value,
            _          => -1
        };
        const string sqlStatusBased = """
            SELECT
                qt.Name AS TypeName,
                ISNULL(SUM(CASE WHEN q.Status = 0  THEN 1 ELSE 0 END), 0) AS Drafts,
                ISNULL(SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND (q.Status = @inProgress OR q.Status = 2) THEN 1 ELSE 0 END), 0) AS InProgress,
                ISNULL(SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND q.Status <> @inProgress AND q.Status <> 2 THEN 1 ELSE 0 END), 0) AS DoneStage,
                ISNULL(SUM(CASE WHEN q.Status IN (9, 12)  THEN 1 ELSE 0 END), 0) AS Adopted,
                ISNULL(SUM(CASE WHEN q.Status IN (10, 11) THEN 1 ELSE 0 END), 0) AS Rejected
            FROM dbo.MT_QuestionTypes qt
            LEFT JOIN dbo.MT_Questions q
                   ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
            GROUP BY qt.Id, qt.Name
            ORDER BY qt.Id
            """;
        return (await conn.QueryAsync<DashboardStatusByTypeItem>(sqlStatusBased, new { pid = projectId, inProgress = inProgressStatus })).ToList();
    }

    /// <summary>
    /// 依當前 PhaseCode 對應 ReviewStage，查 MT_ReviewAssignments 統計審題完成度。
    /// 對應規則：PhaseCode 2→Stage 1（互審）/ 4→Stage 2（專審）/ 6→Stage 3（總召）。
    /// 完成判定：DecidedAt IS NOT NULL（DecidedAt 為 NULL 代表純草稿意見，尚未正式決策）。
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

        // 題組類整題口徑：母題 + 所有子題 必須全部 DecidedAt 才把該題的全部 row 計入 Reviewed
        //   - 部分完成（如母題 decided 但部分 sub 仍 NULL）→ 該題全部 row 都不算入 Reviewed
        //   - 與圖表 line 316 的 PendingCount = 0 判定一致，避免兩處數字不對盤
        //   - TotalCount 不變（仍是所有 row 數，與 footnote「母題與每子題分別計算數量」對齊）
        const string sql = """
            WITH QuestionDecisionStatus AS (
                SELECT QuestionId,
                       COUNT(*) AS TotalRows,
                       SUM(CASE WHEN DecidedAt IS NOT NULL THEN 1 ELSE 0 END) AS DecidedRows
                FROM   dbo.MT_ReviewAssignments
                WHERE  ProjectId = @pid AND ReviewStage = @stage
                GROUP BY QuestionId
            )
            SELECT
                ISNULL(SUM(CASE WHEN TotalRows = DecidedRows THEN TotalRows ELSE 0 END), 0) AS Reviewed,
                ISNULL(SUM(TotalRows), 0)                                                   AS TotalCount
            FROM   QuestionDecisionStatus;
            """;

        var row = await conn.QuerySingleOrDefaultAsync<ReviewProgressRow>(
            sql, new { pid = projectId, stage });

        var reviewed = row?.Reviewed ?? 0;
        var total    = row?.TotalCount ?? 0;

        // Fallback：階段尚未跑過 EnsurePhaseTransitionAsync 觸發分配時，ReviewAssignments 可能還沒建好
        // → 改用單元粒度（母題單元 + 子題單元）作為「待審池」總數，與主路徑 COUNT(*) MT_ReviewAssignments 對齊
        // （正常流程下 Plan_011 已會在進入 PhaseCode=5/7 時自動建立 ReviewAssignments）
        if (total == 0)
        {
            const string fallbackSql = """
                SELECT
                    (SELECT COUNT(*)
                     FROM   dbo.MT_Questions
                     WHERE  ProjectId = @pid AND IsDeleted = 0
                       AND  Status BETWEEN 2 AND 8)
                  + (SELECT COUNT(*)
                     FROM   dbo.MT_SubQuestions sq
                     JOIN   dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                     WHERE  q.ProjectId = @pid AND q.IsDeleted = 0
                       AND  sq.IsDeleted = 0
                       AND  sq.Status BETWEEN 2 AND 8)
                """;
            total = await conn.ExecuteScalarAsync<int>(fallbackSql, new { pid = projectId });
            reviewed = 0;
        }

        return (label, reviewed, total);
    }

    /// <summary>
    /// 依當前 PhaseCode 對應 RevisionStage，查 MT_ReviewAssignments + MT_RevisionReplies
    /// 統計修題完成度（XX/OO 題：已修完 / 待修總數）。
    /// 對應規則：PhaseCode 4→Stage 1（互修）/ 6→Stage 2（專修）/ 8→Stage 3（總修）。
    ///
    /// 待修總數 (OO) = 該階段對應 ReviewStage 中有寫過 Comment 的 distinct QuestionId
    /// 已修完  (XX) = 上述題目中，老師已寫過 MT_RevisionReplies.Content（不為空）的數量
    ///
    /// 非修題階段（PhaseCode 不在 4/6/8）→ 直接回 (None, 0, 0)，不下 SQL。
    /// </summary>
    private static async Task<(RevisionPhaseLabel label, int revised, int total)> GetRevisionProgressAsync(
        System.Data.IDbConnection conn, int projectId, int? currentPhaseCode)
    {
        // 注意：兩個 Stage 欄位數值定義不同！
        //   MT_ReviewAssignments.ReviewStage  = 1 / 2 / 3（互審 / 專審 / 總召）
        //   MT_RevisionReplies.Stage          = PhaseCode 4 / 6 / 8（QuestionService.SaveRevisionAsync 實作）
        var (label, reviewStage, revisionStage) = currentPhaseCode switch
        {
            4 => (RevisionPhaseLabel.Peer,   (byte)1, (byte)4),
            6 => (RevisionPhaseLabel.Expert, (byte)2, (byte)6),
            8 => (RevisionPhaseLabel.Final,  (byte)3, (byte)8),
            _ => (RevisionPhaseLabel.None,   (byte)0, (byte)0)
        };

        if (label == RevisionPhaseLabel.None) return (label, 0, 0);

        // 單元粒度（Stage B-4-2 後）：母題與每子題各為獨立修題單元
        // ─ AssignedUnits CTE 改為 (QuestionId, SubQuestionId) 兩維 distinct，與審題卡片 COUNT(*) 對齊
        // ─ EXISTS 子句加 ISNULL(SubQuestionId, -1) NULL-safe 比對，避免母題 reply 認證子題、或反之
        const string sql = """
            WITH AssignedUnits AS (
                SELECT DISTINCT ra.QuestionId, ra.SubQuestionId
                FROM   dbo.MT_ReviewAssignments ra
                JOIN   dbo.MT_Questions q ON q.Id = ra.QuestionId
                WHERE  ra.ProjectId   = @pid
                  AND  ra.ReviewStage = @reviewStage
                  AND  ra.DecidedAt IS NOT NULL
                  AND  q.IsDeleted = 0
            )
            SELECT
                (SELECT COUNT(*) FROM AssignedUnits) AS TotalCount,
                (SELECT COUNT(*) FROM AssignedUnits a
                 WHERE EXISTS (
                     SELECT 1 FROM dbo.MT_RevisionReplies rr
                     WHERE rr.QuestionId = a.QuestionId
                       AND ISNULL(rr.SubQuestionId, -1) = ISNULL(a.SubQuestionId, -1)
                       AND rr.Stage      = @revisionStage
                       AND rr.Content IS NOT NULL
                       AND LEN(TRIM(rr.Content)) > 0
                       AND rr.CreatedAt > ISNULL(
                           (SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt
                            WHERE QuestionId = a.QuestionId),
                           '1900-01-01')
                 )
                ) AS Revised
            """;

        var row = await conn.QuerySingleOrDefaultAsync<RevisionProgressRow>(
            sql, new { pid = projectId, reviewStage, revisionStage });

        var revised = row?.Revised ?? 0;
        var total   = row?.TotalCount ?? 0;

        // Fallback：階段尚未跑過 EnsurePhaseTransitionAsync 時 assignments 可能還沒建好
        // → 待修池與已修數都改用單元粒度（母題單元 + 子題單元分別計算），與主路徑口徑一致
        if (total == 0)
        {
            // 待修總數 = 母題單元（MT_Questions）+ 子題單元（MT_SubQuestions），兩段相加
            const string fallbackTotalSql = """
                SELECT
                    (SELECT COUNT(*)
                     FROM   dbo.MT_Questions
                     WHERE  ProjectId = @pid AND IsDeleted = 0
                       AND  Status BETWEEN 2 AND 8)
                  + (SELECT COUNT(*)
                     FROM   dbo.MT_SubQuestions sq
                     JOIN   dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                     WHERE  q.ProjectId = @pid AND q.IsDeleted = 0
                       AND  sq.IsDeleted = 0
                       AND  sq.Status BETWEEN 2 AND 8)
                """;
            total = await conn.ExecuteScalarAsync<int>(fallbackTotalSql, new { pid = projectId });

            // Plan_014：本輪過濾 — PC=8 跨輪退回後舊 reply 不算本輪已修
            // 已修數 = (QuestionId, SubQuestionId) 兩維 distinct，與主路徑單元粒度一致
            const string fallbackRevisedSql = """
                SELECT COUNT(*) FROM (
                    SELECT DISTINCT rr.QuestionId, rr.SubQuestionId
                    FROM   dbo.MT_RevisionReplies rr
                    JOIN   dbo.MT_Questions q ON q.Id = rr.QuestionId
                    WHERE  q.ProjectId = @pid AND q.IsDeleted = 0
                      AND  rr.Stage    = @revisionStage
                      AND  rr.Content IS NOT NULL
                      AND  LEN(TRIM(rr.Content)) > 0
                      AND  rr.CreatedAt > ISNULL(
                          (SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt
                           WHERE QuestionId = rr.QuestionId),
                          '1900-01-01')
                ) t
                """;
            revised = await conn.ExecuteScalarAsync<int>(
                fallbackRevisedSql, new { pid = projectId, revisionStage });
        }

        return (label, revised, total);
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
        List<DashboardAchievementItem> achievement,
        int? currentPhaseCode,
        List<UrgentPhaseRow> allPhases)
    {
        var items = new List<DashboardUrgentItem>();

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

        // ── 3. A. 階段資料來自上層傳入的 allPhases（修補 B：合併 3 次 MT_ProjectPhases 查詢）──
        var phaseRows = allPhases;

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
                8 => statusCounts2.Status8 == 0,  // 總審修題
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
                      AND  Status NOT IN (0, 10, 11)
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

        // ── 4-b. 修題階段教師落後（PhaseCode 4/6/8 倒數 ≤ 5 天）─────────
        // 待修題目 = 在當前 ReviewStage 有寫過 Comment 的所有題目
        // 已修完  = 上述題目中老師已寫過 MT_RevisionReplies.Content（Stage=PhaseCode）
        var revisionPhase = phaseRows.FirstOrDefault(p =>
                p.PhaseCode is 4 or 6 or 8
             && p.StartDate    <= today
             && p.DaysRemaining >= 0
             && p.DaysRemaining <= 5);

        if (revisionPhase is not null)
        {
            byte revisionReviewStage = revisionPhase.PhaseCode switch
            {
                4 => 1, 6 => 2, 8 => 3, _ => 0
            };
            byte revisionStageCode = (byte)revisionPhase.PhaseCode;

            // Plan_014：本輪過濾 — PC=8 跨輪退回後舊 reply 不算本輪已修
            const string sqlRevisionShortage = """
                WITH RevisionScope AS (
                    SELECT ra.QuestionId, q.CreatorId,
                           CASE WHEN EXISTS (
                               SELECT 1 FROM dbo.MT_RevisionReplies rr
                               WHERE rr.QuestionId = ra.QuestionId
                                 AND rr.Stage      = @revisionStage
                                 AND rr.Content IS NOT NULL
                                 AND LEN(TRIM(rr.Content)) > 0
                                 AND rr.CreatedAt > ISNULL(
                                     (SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt
                                      WHERE QuestionId = ra.QuestionId),
                                     '1900-01-01')
                           ) THEN 1 ELSE 0 END AS IsRevised
                    FROM   dbo.MT_ReviewAssignments ra
                    JOIN   dbo.MT_Questions q ON q.Id = ra.QuestionId
                    WHERE  ra.ProjectId   = @pid
                      AND  ra.ReviewStage = @reviewStage
                      AND  ra.DecidedAt IS NOT NULL
                      AND  q.IsDeleted = 0
                    GROUP BY ra.QuestionId, q.CreatorId
                )
                SELECT  rs.CreatorId AS UserId,
                        u.DisplayName AS TeacherName,
                        COUNT(*)               AS TotalAssigned,
                        SUM(rs.IsRevised)      AS TotalProduced
                FROM    RevisionScope rs
                JOIN    dbo.MT_Users u ON u.Id = rs.CreatorId
                GROUP BY rs.CreatorId, u.DisplayName
                HAVING  COUNT(*) > SUM(rs.IsRevised)
                ORDER   BY (1.0 * SUM(rs.IsRevised) / NULLIF(COUNT(*), 0)) ASC
                """;

            var teacherRows = (await conn.QueryAsync<TeacherShortageRow>(
                sqlRevisionShortage, new
                {
                    pid           = projectId,
                    reviewStage   = revisionReviewStage,
                    revisionStage = revisionStageCode
                })).ToList();

            foreach (var t in teacherRows)
            {
                var rate     = t.TotalAssigned > 0 ? (decimal)t.TotalProduced / t.TotalAssigned : 0m;
                var severity = rate < 0.3m  ? UrgentSeverity.Critical
                             : rate < 0.7m  ? UrgentSeverity.Warning
                             :                UrgentSeverity.Notice;

                items.Add(new DashboardUrgentItem
                {
                    Severity  = severity,
                    Source    = UrgentSourceType.TeacherShortage,
                    Title     = $"{t.TeacherName} 修題進度落後",
                    Subtitle  = $"目前 {t.TotalProduced}/{t.TotalAssigned}（{rate:P0}）",
                    UserId    = t.UserId,
                    TargetUrl = $"/overview?creatorId={t.UserId}"
                });
            }
        }

        // ── 4-c. 審題階段審題委員落後（PhaseCode 3/5/7 倒數 ≤ 5 天）─────────
        // 該階段被指派但 DecidedAt 仍 NULL 即視為「尚未完成審題」（DecidedAt IS NOT NULL = 已審）
        var reviewerPhase = phaseRows.FirstOrDefault(p =>
                p.PhaseCode is 3 or 5 or 7
             && p.StartDate    <= today
             && p.DaysRemaining >= 0
             && p.DaysRemaining <= 5);

        if (reviewerPhase is not null)
        {
            byte reviewStage = reviewerPhase.PhaseCode switch
            {
                3 => 1, 5 => 2, 7 => 3, _ => 0
            };

            const string sqlReviewerShortage = """
                SELECT  ra.ReviewerId AS UserId,
                        u.DisplayName AS TeacherName,
                        COUNT(*)      AS TotalAssigned,
                        SUM(CASE WHEN ra.DecidedAt IS NOT NULL
                                 THEN 1 ELSE 0 END) AS TotalProduced
                FROM    dbo.MT_ReviewAssignments ra
                JOIN    dbo.MT_Questions q ON q.Id = ra.QuestionId
                JOIN    dbo.MT_Users     u ON u.Id = ra.ReviewerId
                WHERE   ra.ProjectId   = @pid
                  AND   ra.ReviewStage = @reviewStage
                  AND   q.IsDeleted    = 0
                GROUP BY ra.ReviewerId, u.DisplayName
                HAVING  COUNT(*) > SUM(CASE WHEN ra.DecidedAt IS NOT NULL
                                            THEN 1 ELSE 0 END)
                ORDER BY (1.0 * SUM(CASE WHEN ra.DecidedAt IS NOT NULL
                                         THEN 1 ELSE 0 END)
                         / NULLIF(COUNT(*), 0)) ASC
                """;

            var reviewerRows = (await conn.QueryAsync<TeacherShortageRow>(
                sqlReviewerShortage, new { pid = projectId, reviewStage })).ToList();

            foreach (var t in reviewerRows)
            {
                var rate     = t.TotalAssigned > 0 ? (decimal)t.TotalProduced / t.TotalAssigned : 0m;
                var severity = rate < 0.3m  ? UrgentSeverity.Critical
                             : rate < 0.7m  ? UrgentSeverity.Warning
                             :                UrgentSeverity.Notice;

                items.Add(new DashboardUrgentItem
                {
                    Severity  = severity,
                    Source    = UrgentSourceType.TeacherShortage,
                    Title     = $"{t.TeacherName} 審題進度落後",
                    Subtitle  = $"目前 {t.TotalProduced}/{t.TotalAssigned}（{rate:P0}）",
                    UserId    = t.UserId,
                    TargetUrl = "/reviews"
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
            // 依當前進行中階段選用對應的 Modal 明細查詢
            //   命題階段 (PC=2)：MT_ProjectMembers + MT_MemberQuotas（配額制）
            //   審題階段 (PC=3/5/7)：MT_ReviewAssignments per-Reviewer × 題型（指派 vs Comment 非空）
            //   修題階段 (PC=4/6/8)：MT_ReviewAssignments + MT_RevisionReplies（待修題目制）
            string sqlTypeDetails;
            object sqlParams;

            if (revisionPhase is not null)
            {
                byte revisionReviewStage = revisionPhase.PhaseCode switch
                {
                    4 => 1, 6 => 2, 8 => 3, _ => 0
                };
                byte revisionStageCode = (byte)revisionPhase.PhaseCode;

                // Plan_014：本輪過濾 — 與卡片 4／教師落後排行同口徑
                sqlTypeDetails = """
                    WITH RevisionScope AS (
                        SELECT ra.QuestionId, q.CreatorId, q.QuestionTypeId,
                               CASE WHEN EXISTS (
                                   SELECT 1 FROM dbo.MT_RevisionReplies rr
                                   WHERE rr.QuestionId = ra.QuestionId
                                     AND rr.Stage      = @revisionStage
                                     AND rr.Content IS NOT NULL
                                     AND LEN(TRIM(rr.Content)) > 0
                                     AND rr.CreatedAt > ISNULL(
                                         (SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt
                                          WHERE QuestionId = ra.QuestionId),
                                         '1900-01-01')
                               ) THEN 1 ELSE 0 END AS IsRevised
                        FROM   dbo.MT_ReviewAssignments ra
                        JOIN   dbo.MT_Questions q ON q.Id = ra.QuestionId
                        WHERE  ra.ProjectId   = @pid
                          AND  ra.ReviewStage = @reviewStage
                          AND  ra.DecidedAt IS NOT NULL
                          AND  q.IsDeleted = 0
                          AND  q.CreatorId IN @userIds
                        GROUP BY ra.QuestionId, q.CreatorId, q.QuestionTypeId
                    )
                    SELECT  rs.CreatorId AS UserId,
                            rs.QuestionTypeId,
                            COUNT(*)     AS Assigned,
                            SUM(rs.IsRevised) AS Produced
                    FROM    RevisionScope rs
                    GROUP BY rs.CreatorId, rs.QuestionTypeId
                    ORDER BY rs.CreatorId,
                             (1.0 * SUM(rs.IsRevised) / NULLIF(COUNT(*), 0)) ASC
                    """;
                sqlParams = new
                {
                    pid           = projectId,
                    userIds,
                    reviewStage   = revisionReviewStage,
                    revisionStage = revisionStageCode
                };
            }
            else if (reviewerPhase is not null)
            {
                byte reviewStage = reviewerPhase.PhaseCode switch
                {
                    3 => 1, 5 => 2, 7 => 3, _ => 0
                };

                // 審題階段 Modal 明細：每位委員 × 題型 → (Comment 非空 / 被指派) 數
                sqlTypeDetails = """
                    SELECT  ra.ReviewerId AS UserId,
                            q.QuestionTypeId,
                            COUNT(*)     AS Assigned,
                            SUM(CASE WHEN ra.DecidedAt IS NOT NULL
                                     THEN 1 ELSE 0 END) AS Produced
                    FROM    dbo.MT_ReviewAssignments ra
                    JOIN    dbo.MT_Questions      q  ON q.Id = ra.QuestionId
                    WHERE   ra.ProjectId   = @pid
                      AND   ra.ReviewStage = @reviewStage
                      AND   ra.ReviewerId IN @userIds
                      AND   q.IsDeleted    = 0
                    GROUP BY ra.ReviewerId, q.QuestionTypeId
                    ORDER BY ra.ReviewerId,
                             (1.0 * SUM(CASE WHEN ra.DecidedAt IS NOT NULL
                                             THEN 1 ELSE 0 END)
                             / NULLIF(COUNT(*), 0)) ASC
                    """;
                sqlParams = new { pid = projectId, userIds, reviewStage };
            }
            else
            {
                sqlTypeDetails = """
                    SELECT  pm.UserId,
                            mq.QuestionTypeId,
                            mq.QuotaCount AS Assigned,
                            ISNULL(prod.Produced, 0) AS Produced
                    FROM    dbo.MT_ProjectMembers pm
                    JOIN    dbo.MT_MemberQuotas   mq ON mq.ProjectMemberId = pm.Id
                    OUTER APPLY (
                        SELECT COUNT(*) AS Produced
                        FROM   dbo.MT_Questions
                        WHERE  ProjectId      = pm.ProjectId
                          AND  QuestionTypeId = mq.QuestionTypeId
                          AND  CreatorId      = pm.UserId
                          AND  IsDeleted      = 0
                          AND  Status NOT IN (0, 10, 11)
                    ) prod
                    WHERE   pm.ProjectId = @pid AND pm.UserId IN @userIds
                    ORDER   BY pm.UserId, prod.Produced * 1.0 / NULLIF(mq.QuotaCount, 0) ASC
                    """;
                sqlParams = new { pid = projectId, userIds };
            }

            var detailRows = (await conn.QueryAsync<TeacherTypeDetailRow>(
                sqlTypeDetails, sqlParams)).ToList();

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
                    TypeName       = _typeCatalog.GetName(r.QuestionTypeId),
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

        // Dashboard 只看「梯次內」活動（命題 + 審題）；跨梯次活動已移至 SystemLogs.razor
        // 強制 ProjectId = @pid，永遠不顯示 ProjectId IS NULL 的全站紀錄
        int[] typeCodes = query.TypeFilter switch
        {
            LogTypeFilter.Question => [3],
            LogTypeFilter.Review   => [6],
            _                      => [3, 6]   // All：試題 + 審題
        };

        var sqlParams = new
        {
            pid       = query.ProjectId,
            typeCodes,
            skip      = query.Skip,
            take      = query.Take
        };

        // ── Step 1：主查詢（OFFSET FETCH 分頁）──────────────────────────
        // OldValue/NewValue 也帶出，以便目標資料已刪除時從 JSON 解析名稱
        // 強制 ProjectId = @pid（不接受 ProjectId IS NULL）+ Action IN (0,1,2) + TargetType IN (3,6)
        string sqlMain = """
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
            WHERE  al.ProjectId = @pid
              AND  al.Action IN (0, 1, 2)
              AND  al.TargetType IN @typeCodes
              AND  al.UserId IS NOT NULL   -- 過濾系統批次（階段轉換、審題分配等 UserId=NULL），DB 仍完整保留，僅 UI 不顯示
            ORDER  BY al.CreatedAt DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """;

        string sqlCount = """
            SELECT COUNT(*)
            FROM   dbo.MT_AuditLogs al
            WHERE  al.ProjectId = @pid
              AND  al.Action IN (0, 1, 2)
              AND  al.TargetType IN @typeCodes
              AND  al.UserId IS NOT NULL;
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
        // - TargetType=6(Reviews) JOIN MT_Questions 顯示對應題目的 QuestionCode
        var grouped = logs
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
            if (nameMap.TryGetValue((log.TargetType, log.TargetId), out var found))
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
        /// <summary>修補 B：合併三次 MT_ProjectPhases 查詢後，由 in-memory 端按 SortOrder 排序取「主要當前階段」。</summary>
        public int      SortOrder     { get; init; }
    }

    /// <summary>教師落後彙總列（GROUP BY 教師）。</summary>
    private sealed record TeacherShortageRow(
        int UserId, string TeacherName, int TotalAssigned, int TotalProduced);

    /// <summary>教師×題型明細列（展開用）。TypeName 由 IQuestionTypeCatalog 在消費端補。</summary>
    private sealed record TeacherTypeDetailRow(
        int UserId, int QuestionTypeId, int Assigned, int Produced);

    private sealed class StatusCountRow
    {
        public int AdoptedCount { get; init; }
    }

    /// <summary>卡片 3 審題進度查詢結果列。</summary>
    private sealed class ReviewProgressRow
    {
        public int Reviewed   { get; init; }
        public int TotalCount { get; init; }
    }

    /// <summary>卡片 4 修題進度查詢結果列。</summary>
    private sealed class RevisionProgressRow
    {
        public int Revised    { get; init; }
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
