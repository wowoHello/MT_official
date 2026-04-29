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
        // 5. 圖表 2：各題型依狀態分佈
        //    使用 LEFT JOIN 確保無資料時仍回傳 7 筆全零資料
        // ──────────────────────────────────────────────────────────────
        const string sqlStatusByType = """
            SELECT
                qt.Name AS TypeName,
                ISNULL(SUM(CASE WHEN q.Status IN (0, 1)         THEN 1 ELSE 0 END), 0) AS Drafting,
                ISNULL(SUM(CASE WHEN q.Status IN (2, 3, 5, 7)   THEN 1 ELSE 0 END), 0) AS InReview,
                ISNULL(SUM(CASE WHEN q.Status IN (4, 6, 8)       THEN 1 ELSE 0 END), 0) AS Returned,
                ISNULL(SUM(CASE WHEN q.Status = 9                THEN 1 ELSE 0 END), 0) AS Adopted,
                ISNULL(SUM(CASE WHEN q.Status = 10               THEN 1 ELSE 0 END), 0) AS Rejected
            FROM dbo.MT_QuestionTypes qt
            LEFT JOIN dbo.MT_Questions q
                   ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
            GROUP BY qt.Id, qt.Name
            ORDER BY qt.Id
            """;

        var statusByTypeRows = (await conn.QueryAsync<DashboardStatusByTypeItem>(
            sqlStatusByType, new { pid = projectId })).ToList();

        // 計算卡片 3 的狀態類型與顯示文字
        var (phaseStatusType, phaseStatusText, phaseDaysRemaining) =
            ResolvePhaseStatus(projectStatus, phaseRow);

        // ──────────────────────────────────────────────────────────────
        // 6. 逾期與緊急待辦 Top 5
        //    需在 achievementRows 產出後才可呼叫（TypeShortage 依賴達成率資料）
        // ──────────────────────────────────────────────────────────────
        var urgentItems = await BuildUrgentItemsAsync(conn, projectId, achievementRows);

        // ──────────────────────────────────────────────────────────────
        // 7. 最新稽核歷程 Top 10
        // ──────────────────────────────────────────────────────────────
        var recentLogs = await GetRecentAuditLogsAsync(conn, projectId);

        // ──────────────────────────────────────────────────────────────
        // 組裝 DTO
        // ──────────────────────────────────────────────────────────────
        return new DashboardKpiDto
        {
            TotalTarget        = targetRows.Sum(r => r.TargetCount),
            TargetBreakdown    = targetRows,
            AdoptedCount       = counts?.AdoptedCount    ?? 0,
            InReviewCount      = counts?.InReviewCount   ?? 0,
            ReturnEditCount    = counts?.ReturnEditCount ?? 0,
            PeerEditCount      = counts?.PeerEditCount   ?? 0,
            ExpertEditCount    = counts?.ExpertEditCount ?? 0,
            FinalEditCount     = counts?.FinalEditCount  ?? 0,
            PhaseStatusType    = phaseStatusType,
            PhaseStatusText    = phaseStatusText,
            PhaseDaysRemaining = phaseDaysRemaining,
            AchievementByType  = achievementRows,
            StatusByType       = statusByTypeRows,
            UrgentItems        = urgentItems,
            RecentLogs         = recentLogs
        };
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

        // Phase 1（命題階段）抑制條件：所有題型達成率 ≥ 100%
        bool phase1Completed = achievement.All(x => x.Target == 0 || x.Produced >= x.Target);

        foreach (var p in phaseRows)
        {
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
            bool suppress = p.PhaseCode switch
            {
                1 => phase1Completed,
                2 => statusCounts2.Status3 == 0,
                3 => statusCounts2.Status4 == 0,
                4 => statusCounts2.Status5 == 0,
                5 => statusCounts2.Status6 == 0,
                6 => statusCounts2.Status7 == 0,
                7 => statusCounts2.Status8 == 0,
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
        var proposalPhase = phaseRows.FirstOrDefault(p => p.PhaseCode == 1);

        bool isProposingPhase =
                proposalPhase is not null
             && proposalPhase.StartDate    <= today
             && proposalPhase.DaysRemaining >= 0   // 尚未過 EndDate
             && proposalPhase.DaysRemaining <= 5;  // 在 5 天倒數窗口內

        if (isProposingPhase)
        {
            // 教師總配額達成率 < 70%（GROUP BY 教師）
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
                  AND   ISNULL(SUM(prod.Produced), 0) * 1.0 / SUM(mq.QuotaCount) < 0.7
                ORDER   BY (ISNULL(SUM(prod.Produced), 0) * 1.0 / SUM(mq.QuotaCount)) ASC
                """;

            var teacherRows = (await conn.QueryAsync<TeacherShortageRow>(
                sqlTeacherShortage, new { pid = projectId })).ToList();

            foreach (var t in teacherRows)
            {
                var rate     = t.TotalAssigned > 0 ? (decimal)t.TotalProduced / t.TotalAssigned : 0m;
                var severity = rate < 0.3m  ? UrgentSeverity.Critical
                             : rate < 0.5m  ? UrgentSeverity.Warning
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

        // ── 5. 排序：Severity DESC → DaysRemaining ASC → Take(5) ────────
        var top5 = items
            .OrderBy(x => (int)x.Severity)
            .ThenBy(x => x.DaysRemaining ?? int.MaxValue)
            .Take(5)
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

    /// <summary>
    /// 取得最新稽核歷程（依 CreatedAt DESC，預設 Top 10）。
    /// Step 1：主查詢撈 LOG + 操作者名稱。
    /// Step 2：依 TargetType 分組批次 JOIN 目標名稱，避免 N+1。
    /// </summary>
    private static async Task<List<RecentAuditLog>> GetRecentAuditLogsAsync(
        System.Data.IDbConnection conn, int projectId, int top = 10)
    {
        // ── Step 1：主查詢 ──────────────────────────────────────────────
        const string sqlMain = """
            SELECT TOP (@top)
                al.Id,
                al.UserId,
                ISNULL(u.DisplayName, N'系統') AS UserName,
                al.Action,
                al.TargetType,
                al.TargetId,
                al.CreatedAt
            FROM   dbo.MT_AuditLogs al
            LEFT   JOIN dbo.MT_Users u ON u.Id = al.UserId
            WHERE  (al.ProjectId = @pid OR al.ProjectId IS NULL)
              AND  al.Action IN (0, 1, 2)   -- 僅顯示「建立/修改/刪除」三種有語意的動作
            ORDER  BY al.CreatedAt DESC
            """;

        var logs = (await conn.QueryAsync<RecentAuditLog>(
            sqlMain, new { pid = projectId, top })).ToList();

        if (logs.Count == 0) return logs;

        // ── Step 2：依 TargetType 分組批次解析 TargetName ──────────────
        // 聚合各 TargetType → TargetId 清單
        var grouped = logs
            .Where(l => l.TargetType != 6)                        // TargetType=6(Reviews) 直接用 #{Id}
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
            if (log.TargetType == 6)
            {
                // Reviews 不查名稱，直接用流水號
                log.TargetName = $"#{log.TargetId}";
            }
            else if (nameMap.TryGetValue((log.TargetType, log.TargetId), out var found))
            {
                log.TargetName = found;
            }
            else
            {
                log.TargetName = "已刪除";
            }
        }

        return logs;
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
    /// 依階段碼回傳對應的目的 URL。
    /// 命題階段（1）→ 命題作業區；各修題階段（3,5,7）→ 審修作業區；各審題階段（2,4,6）→ 審題作業區。
    /// </summary>
    private static string ResolvePhaseUrl(int phaseCode) => phaseCode switch
    {
        1           => "/cwt-list?tab=compose",
        3 or 5 or 7 => "/cwt-list?tab=revision",
        2 or 4 or 6 => "/reviews?tab=review",
        _           => "/projects"
    };

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

    private sealed class PhaseRow
    {
        public string PhaseName { get; init; } = string.Empty;
        public int    DaysLeft  { get; init; }
    }
}
