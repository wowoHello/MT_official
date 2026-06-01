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
    /// 決策 1：projectType 由 caller 從 CurrentProject.ProjectType 傳入，省 DB round-trip。
    /// </summary>
    Task<DashboardKpiDto> GetKpiAsync(int projectId, ProjectType projectType);

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
    public async Task<DashboardKpiDto> GetKpiAsync(int projectId, ProjectType projectType)
    {
        // 修補 G Stage 1：5 個無相依 SQL 並行（各自開 conn — Dapper 同 conn 不支援並發 command）

        // ──────────────────────────────────────────────────────────────
        // 雙模式題型 IN 清單：
        //   CWT：TypeId IN (1,3,4,5) — 一般/短文題組/長文/閱讀題組，排除精選(2)/聽力(6)/聽力題組(7)
        //   LCT：TypeId IN (6,7) — 聽力測驗/聽力題組
        // ──────────────────────────────────────────────────────────────
        bool isLct = projectType == ProjectType.Lct;
        // CWT 參數（Dapper 接受 int[]）
        int[] cwtTypeIds = [1, 3, 4, 5];
        int[] lctTypeIds = [6, 7];
        int[] activeTypeIds = isLct ? lctTypeIds : cwtTypeIds;

        // ──────────────────────────────────────────────────────────────
        // 1. 各題型目標題數（含明細）
        //    CWT：閱讀(3)/短文(5)題組需拆母+子（Granularity 0=母,1=子），故用 Granularity JOIN
        //    LCT：依難度一~五展開 5 桶；TypeId=7 聽力題組固定貢獻難度三/四各 1 子題
        // ──────────────────────────────────────────────────────────────
        // CWT SQL：左 JOIN MT_ProjectTargets，TypeId 3/5 額外展開子題桶（Granularity=1）
        // Produced 條件：Status > 0（命題草稿以外都算「老師已產出」，含 不採用 Status 10/11）
        //   因為缺口統計的語意是「老師命了幾題」，與最後採不採用無關 — 不採用也是老師的勞動成果
        const string sqlTargetsCwt = """
            SELECT
                qt.Name AS TypeName,
                ISNULL(SUM(CASE WHEN pt.Granularity = 0 THEN pt.TargetCount ELSE 0 END), 0) AS TargetCount,
                0 AS Granularity,
                ISNULL((SELECT COUNT(*) FROM dbo.MT_Questions
                        WHERE ProjectId = @pid AND IsDeleted = 0
                          AND QuestionTypeId = qt.Id
                          AND Status > 0), 0) AS Produced
            FROM   dbo.MT_QuestionTypes qt
            LEFT JOIN dbo.MT_ProjectTargets pt
                   ON pt.QuestionTypeId = qt.Id AND pt.ProjectId = @pid AND pt.Granularity = 0
            WHERE  qt.Id IN @typeIds
            GROUP BY qt.Id, qt.Name
            UNION ALL
            -- 題組子題桶（Granularity=1）：只有 TypeId 3(短文)/5(閱讀) 有子題配額
            -- TypeName 不在 SQL 端組「（子題）」後綴，由 BuildDisplayLabel 統一掛字尾，避免被它再加一次造成「（子題）（子題）」
            SELECT
                qt.Name AS TypeName,
                ISNULL(SUM(CASE WHEN pt.Granularity = 1 THEN pt.TargetCount ELSE 0 END), 0) AS TargetCount,
                1 AS Granularity,
                ISNULL((SELECT COUNT(*) FROM dbo.MT_SubQuestions sq
                        JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                        WHERE q.ProjectId = @pid AND q.IsDeleted = 0 AND sq.IsDeleted = 0
                          AND q.QuestionTypeId = qt.Id
                          AND sq.Status > 0), 0) AS Produced
            FROM   dbo.MT_QuestionTypes qt
            LEFT JOIN dbo.MT_ProjectTargets pt
                   ON pt.QuestionTypeId = qt.Id AND pt.ProjectId = @pid AND pt.Granularity = 1
            WHERE  qt.Id IN (3, 5)
            GROUP BY qt.Id, qt.Name
            ORDER BY Granularity, TypeName
            """;

        // LCT SQL：6 桶 — 聽力測驗 5 個難度（TypeId=6 按 Level）+ 聽力題組（TypeId=7 母+子綁一起）
        // 聽力題組計數規則（與使用者拍板，2026-05-26）：
        //   1 group = 1 母題 + 2 子題（難三/難四）= 3 個 DB 單位
        //   Target = ProjectTargets「組數」 × 3
        //   Produced = 母題已產出數 + 子題已產出數
        // Produced 條件：Status > 0（命題草稿以外都算「老師已產出」，含 不採用 Status 10/11）
        //   缺口統計的語意是「老師命了幾題」，與最後採不採用無關
        // 注意：欄位名稱是 MT_ProjectTargets.Level（不是 ExamLevel — ExamLevel 是 MT_Projects 上的 CWT 統一等級）
        // 注意：SQL Server CTE body 不能直接用 VALUES row constructor，必須包在 SELECT FROM (VALUES ...) AS v(...) 內
        // 注意：UNION ALL 的最終 ORDER BY 不能對 union 子查詢的欄位排序，需包外層 select 並引用 SortKey
        const string sqlTargetsLct = """
            WITH LevelBuckets(LevelNum, LevelName) AS (
                SELECT LevelNum, LevelName
                FROM (VALUES (1, N'難度一'), (2, N'難度二'), (3, N'難度三'), (4, N'難度四'), (5, N'難度五'))
                     AS v(LevelNum, LevelName)
            ),
            Type6Agg AS (
                -- 聽力測驗（TypeId=6）：按設定的 Level 分桶
                SELECT [Level] AS LevelNum, SUM(TargetCount) AS TotalTarget
                FROM   dbo.MT_ProjectTargets
                WHERE  ProjectId = @pid AND QuestionTypeId = 6 AND [Level] IS NOT NULL
                GROUP BY [Level]
            ),
            Type6Produced AS (
                -- 聽力測驗已產出（Status > 0，含 不採用），按 Level 分桶
                SELECT [Level] AS LevelNum, COUNT(*) AS ProducedCount
                FROM   dbo.MT_Questions
                WHERE  ProjectId = @pid AND IsDeleted = 0 AND QuestionTypeId = 6
                  AND  [Level] IS NOT NULL AND Status > 0
                GROUP BY [Level]
            )
            SELECT TypeName, TargetCount, Granularity, Produced
            FROM (
                -- 聽力測驗 5 個難度桶
                SELECT
                    lb.LevelName AS TypeName,
                    ISNULL(la.TotalTarget, 0) AS TargetCount,
                    CAST(0 AS TINYINT) AS Granularity,
                    ISNULL(p.ProducedCount, 0) AS Produced,
                    lb.LevelNum AS SortKey
                FROM   LevelBuckets lb
                LEFT JOIN Type6Agg      la ON la.LevelNum = lb.LevelNum
                LEFT JOIN Type6Produced p  ON p.LevelNum  = lb.LevelNum
                UNION ALL
                -- 聽力題組（TypeId=7）：以「組」為計數單位（1 group = 1）
                -- Target = 組數；Produced = 已產出組數（含不採用，只要 master Status > 0 就算）
                SELECT
                    N'聽力題組',
                    ISNULL((SELECT SUM(TargetCount) FROM dbo.MT_ProjectTargets
                            WHERE ProjectId = @pid AND QuestionTypeId = 7), 0),
                    CAST(0 AS TINYINT),
                    ISNULL((SELECT COUNT(*) FROM dbo.MT_Questions
                            WHERE ProjectId = @pid AND IsDeleted = 0
                              AND QuestionTypeId = 7 AND Status > 0), 0),
                    99
            ) tmp
            ORDER BY SortKey
            """;

        // ──────────────────────────────────────────────────────────────
        // 2. 卡片 2：採用計數（顯示真實，不 clamp — 超量視為「全部達成 + 紅利」）
        //    分子 = 採用母題 + 採用子題（Status IN 9,12），整合進單一 AdoptedCount
        //    超量命題的責任歸屬在「逾期與緊急待辦」處理（HAVING 過濾），dashboard 大數字保留真實
        // ──────────────────────────────────────────────────────────────
        const string sqlStatusCountsCwt = """
            SELECT
                -- 母題層採用（所有 CWT 題型）
                ISNULL((
                    SELECT SUM(CASE WHEN Status IN (9, 12) THEN 1 ELSE 0 END)
                    FROM   dbo.MT_Questions
                    WHERE  ProjectId = @pid AND IsDeleted = 0 AND QuestionTypeId IN @typeIds
                ), 0)
                +
                -- 子題層採用（僅閱讀題組(3)/短文題組(5)）
                ISNULL((
                    SELECT SUM(CASE WHEN sq.Status IN (9, 12) THEN 1 ELSE 0 END)
                    FROM   dbo.MT_SubQuestions sq
                    JOIN   dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                    WHERE  q.ProjectId = @pid AND q.IsDeleted = 0 AND sq.IsDeleted = 0
                      AND  q.QuestionTypeId IN (3, 5)
                ), 0) AS AdoptedCount
            """;

        // LCT 採用計數（與 TargetBreakdown 對齊「組」為計數單位，2026-05-26 拍板）：
        //   - 聽力測驗（TypeId=6）：master Status IN (9,12) → +1/題
        //   - 聽力題組（TypeId=7）：整組採用 → +1/組（以 group 為計數單位，不展開成 3 單位）
        //     LCT 規格 all-or-nothing：母題 IN (9,12) AND 兩個 sub 都 IN (9,12) 才算採用
        //     任一條件不符 → 整組淘汰計 0（含 Bug fix：原 SQL 漏檢查母題）
        const string sqlStatusCountsLct = """
            SELECT
                -- 聽力測驗 master 採用
                ISNULL((
                    SELECT SUM(CASE WHEN Status IN (9, 12) THEN 1 ELSE 0 END)
                    FROM   dbo.MT_Questions
                    WHERE  ProjectId = @pid AND IsDeleted = 0 AND QuestionTypeId = 6
                ), 0)
                +
                -- 聽力題組 整組採用（母題採用 AND 兩個子題都採用 → +1，否則 +0）
                ISNULL((
                    SELECT COUNT(*) FROM dbo.MT_Questions qp
                    WHERE  qp.ProjectId = @pid AND qp.IsDeleted = 0 AND qp.QuestionTypeId = 7
                      AND  qp.Status IN (9, 12)
                      AND  (SELECT COUNT(*) FROM dbo.MT_SubQuestions sq
                            WHERE sq.ParentQuestionId = qp.Id
                              AND sq.IsDeleted = 0
                              AND sq.Status IN (9, 12)) = 2
                ), 0) AS AdoptedCount
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
        // CWT：拆母+子兩列（Granularity=0/1），閱讀(TypeId=3)/短文(TypeId=5)題組有子題桶，其餘 Granularity 固定 0
        // LCT：X 軸改為難度一~五（Level 1-5）
        // Produced 條件：Status > 0（含 不採用 Status 10/11）— 老師命過的題目都算缺口已產出
        const string sqlAchievementCwt = """
            WITH ProducedMaster AS (
                -- 母題/單題層級：各題型直接計數
                SELECT QuestionTypeId, 0 AS Granularity,
                       SUM(CASE WHEN Status > 0 THEN 1 ELSE 0 END) AS Produced
                FROM   dbo.MT_Questions
                WHERE  ProjectId = @pid AND IsDeleted = 0 AND QuestionTypeId IN @typeIds
                GROUP BY QuestionTypeId
            ),
            ProducedSub AS (
                -- 子題層級：只有 TypeId 3(閱讀)/5(短文) 的子題需計入各自桶（Granularity=1）
                SELECT q.QuestionTypeId, 1 AS Granularity,
                       SUM(CASE WHEN sq.Status > 0 THEN 1 ELSE 0 END) AS Produced
                FROM   dbo.MT_SubQuestions sq
                JOIN   dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                WHERE  q.ProjectId = @pid AND q.IsDeleted = 0 AND sq.IsDeleted = 0
                  AND  q.QuestionTypeId IN (3, 5)
                GROUP BY q.QuestionTypeId
            ),
            AggregatedTargets AS (
                SELECT QuestionTypeId, Granularity, SUM(TargetCount) AS TotalTarget
                FROM   dbo.MT_ProjectTargets
                WHERE  ProjectId = @pid AND QuestionTypeId IN @typeIds
                GROUP BY QuestionTypeId, Granularity
            )
            -- 母題桶（全部 4 種 CWT 題型）
            SELECT
                qt.Id   AS QuestionTypeId,
                qt.Name AS TypeName,
                ISNULL(pm.Produced, 0)    AS Produced,
                ISNULL(at.TotalTarget, 0) AS Target,
                CAST(0 AS TINYINT)        AS Granularity,
                NULL                      AS Level
            FROM dbo.MT_QuestionTypes qt
            LEFT JOIN ProducedMaster    pm ON pm.QuestionTypeId = qt.Id AND pm.Granularity = 0
            LEFT JOIN AggregatedTargets at ON at.QuestionTypeId = qt.Id AND at.Granularity = 0
            WHERE qt.Id IN @typeIds
            UNION ALL
            -- 子題桶（僅 TypeId 3/5 閱讀/短文題組）
            SELECT
                qt.Id   AS QuestionTypeId,
                qt.Name AS TypeName,
                ISNULL(ps.Produced, 0)    AS Produced,
                ISNULL(at.TotalTarget, 0) AS Target,
                CAST(1 AS TINYINT)        AS Granularity,
                NULL                      AS Level
            FROM dbo.MT_QuestionTypes qt
            LEFT JOIN ProducedSub       ps ON ps.QuestionTypeId = qt.Id AND ps.Granularity = 1
            LEFT JOIN AggregatedTargets at ON at.QuestionTypeId = qt.Id AND at.Granularity = 1
            WHERE qt.Id IN (3, 5)
            ORDER BY QuestionTypeId, Granularity
            """;

        // LCT 圖表 1：6 個 X 軸桶 — 聽力測驗 5 個難度 + 聽力題組（整組）
        // 設計與 sqlTargetsLct 一致：聽力題組獨立分類，不再 fold-in 到難三/難四
        //   聽力題組 Produced 採用「TypeId=7 母題 Status NOT IN (0,10,11)」= 整組已產出
        //   每 1 master = 1 整組（規格固定 1 母題 + 難三/難四 2 子題，子題不獨立計）
        const string sqlAchievementLct = """
            WITH LevelBuckets(LevelNum, LevelName) AS (
                SELECT LevelNum, LevelName
                FROM (VALUES (1, N'難度一'), (2, N'難度二'), (3, N'難度三'), (4, N'難度四'), (5, N'難度五'))
                     AS v(LevelNum, LevelName)
            ),
            ProducedType6 AS (
                -- 聽力測驗（TypeId=6）：依題目自身 Level 計入對應難度
                -- Produced 條件 Status > 0（含 不採用）— 與 sqlTargetsLct 一致
                SELECT [Level] AS LevelNum,
                       SUM(CASE WHEN Status > 0 THEN 1 ELSE 0 END) AS Produced
                FROM   dbo.MT_Questions
                WHERE  ProjectId = @pid AND IsDeleted = 0 AND QuestionTypeId = 6 AND [Level] IS NOT NULL
                GROUP BY [Level]
            ),
            Type6Targets AS (
                SELECT [Level] AS LevelNum, SUM(TargetCount) AS TotalTarget
                FROM   dbo.MT_ProjectTargets
                WHERE  ProjectId = @pid AND QuestionTypeId = 6 AND [Level] IS NOT NULL
                GROUP BY [Level]
            )
            SELECT QuestionTypeId, TypeName, Produced, Target, Granularity, Level
            FROM (
                -- 聽力測驗 5 個難度桶
                SELECT
                    0                            AS QuestionTypeId,
                    lb.LevelName                 AS TypeName,
                    ISNULL(p.Produced, 0)        AS Produced,
                    ISNULL(t.TotalTarget, 0)     AS Target,
                    CAST(0 AS TINYINT)           AS Granularity,
                    CAST(lb.LevelNum AS TINYINT) AS Level,
                    lb.LevelNum                  AS SortKey
                FROM   LevelBuckets lb
                LEFT JOIN ProducedType6 p ON p.LevelNum = lb.LevelNum
                LEFT JOIN Type6Targets  t ON t.LevelNum = lb.LevelNum
                UNION ALL
                -- 聽力題組（TypeId=7）：獨立分類，以母題 Status 為準（1 master = 1 整組）
                -- Produced 條件 Status > 0（含 不採用）— 與 sqlTargetsLct 一致
                SELECT
                    7,
                    N'聽力題組',
                    ISNULL((SELECT SUM(CASE WHEN Status > 0 THEN 1 ELSE 0 END)
                            FROM dbo.MT_Questions
                            WHERE ProjectId = @pid AND IsDeleted = 0 AND QuestionTypeId = 7), 0),
                    ISNULL((SELECT SUM(TargetCount) FROM dbo.MT_ProjectTargets
                            WHERE ProjectId = @pid AND QuestionTypeId = 7), 0),
                    CAST(0 AS TINYINT),
                    CAST(NULL AS TINYINT),
                    99
            ) tmp
            ORDER BY SortKey
            """;

        // ── 並行發出 5 個 task ──
        // CWT/LCT 選用對應的 targets SQL 與 statusCounts SQL
        Task<IEnumerable<DashboardTargetBreakdown>> targetsTask;
        Task<StatusCountRow?> statusCountsTask;
        Task<IEnumerable<DashboardAchievementItem>> achievementTask;

        if (isLct)
        {
            targetsTask      = WithOwnConnAsync(c => c.QueryAsync<DashboardTargetBreakdown>(sqlTargetsLct, new { pid = projectId }));
            statusCountsTask = WithOwnConnAsync(c => c.QuerySingleOrDefaultAsync<StatusCountRow>(sqlStatusCountsLct, new { pid = projectId }));
            achievementTask  = WithOwnConnAsync(c => c.QueryAsync<DashboardAchievementItem>(sqlAchievementLct, new { pid = projectId }));
        }
        else
        {
            targetsTask      = WithOwnConnAsync(c => c.QueryAsync<DashboardTargetBreakdown>(sqlTargetsCwt, new { pid = projectId, typeIds = activeTypeIds }));
            statusCountsTask = WithOwnConnAsync(c => c.QuerySingleOrDefaultAsync<StatusCountRow>(sqlStatusCountsCwt, new { pid = projectId, typeIds = activeTypeIds }));
            achievementTask  = WithOwnConnAsync(c => c.QueryAsync<DashboardAchievementItem>(sqlAchievementCwt, new { pid = projectId, typeIds = activeTypeIds }));
        }

        var projectStatusTask = WithOwnConnAsync(c => c.QuerySingleOrDefaultAsync<ProjectStatusRow>(sqlProjectStatus, new { pid = projectId }));
        var allPhasesTask     = WithOwnConnAsync(c => c.QueryAsync<UrgentPhaseRow>(sqlAllPhases, new { pid = projectId }));

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
        //   - CWT/LCT 雙模式：傳入 activeTypeIds 與 isLct 供各方法依題型過濾
        var statusByTypeTask     = WithOwnConnAsync(c => LoadStatusByTypeRowsAsync(c, projectId, currentPhaseForChart, activeTypeIds, isLct));
        var reviewProgressTask   = WithOwnConnAsync(c => GetReviewProgressAsync(c, projectId, currentPhaseForChart, activeTypeIds, isLct));
        var revisionProgressTask = WithOwnConnAsync(c => GetRevisionProgressAsync(c, projectId, currentPhaseForChart, activeTypeIds, isLct));
        var urgentItemsTask      = WithOwnConnAsync(c => BuildUrgentItemsAsync(c, projectId, achievementRows, currentPhaseForChart, allPhases, activeTypeIds, isLct));

        await Task.WhenAll(statusByTypeTask, reviewProgressTask, revisionProgressTask, urgentItemsTask);

        var statusByTypeRows                                                                        = statusByTypeTask.Result;
        var (reviewLabel, masterReviewed, masterTotal, subReviewed, subTotal)                       = reviewProgressTask.Result;
        var (revisionLabel, masterRevised, masterRevTotal, subRevised, subRevTotal)                 = revisionProgressTask.Result;
        var urgentItems                                                                             = urgentItemsTask.Result;

        // 計算卡片 3 的狀態類型與顯示文字
        var (phaseStatusType, phaseStatusText, phaseDaysRemaining) =
            ResolvePhaseStatus(projectStatus, phaseRow, neighborPhase);

        // 已結案專案：覆蓋審/修題階段判定為 Closed（即使中途結案也顯示為「已結案」）
        if (projectStatus == 2)
        {
            reviewLabel   = ReviewPhaseLabel.Closed;
            masterReviewed = 0; masterTotal  = 0;
            subReviewed    = 0; subTotal     = 0;
            revisionLabel  = RevisionPhaseLabel.Closed;
            masterRevised  = 0; masterRevTotal = 0;
            subRevised     = 0; subRevTotal    = 0;
        }

        // ──────────────────────────────────────────────────────────────
        // 補填 DisplayLabel（集中在此，SQL 端不做字串拼接以降低 SQL 複雜度）
        // ──────────────────────────────────────────────────────────────
        foreach (var row in targetRows)
            row.DisplayLabel = BuildDisplayLabel(row.TypeName, row.Granularity, null);

        foreach (var row in achievementRows)
            row.DisplayLabel = BuildDisplayLabel(row.TypeName, row.Granularity, row.Level);

        foreach (var row in statusByTypeRows)
            row.DisplayLabel = BuildDisplayLabel(row.TypeName, row.Granularity, row.Level);

        // ──────────────────────────────────────────────────────────────
        // 組裝 DTO（LOG 已獨立至 GetAuditLogsAsync，此處不再帶入）
        // ──────────────────────────────────────────────────────────────
        return new DashboardKpiDto
        {
            TotalTarget          = targetRows.Sum(r => r.TargetCount),
            TargetBreakdown      = targetRows,
            AdoptedCount         = counts?.AdoptedCount    ?? 0,
            PhaseStatusType      = phaseStatusType,
            PhaseStatusText      = phaseStatusText,
            PhaseDaysRemaining   = phaseDaysRemaining,
            CurrentReviewPhase   = reviewLabel,
            ReviewedCount        = masterReviewed,
            ReviewTotalCount     = masterTotal,
            ReviewedSubCount     = subReviewed,
            ReviewTotalSubCount  = subTotal,
            CurrentRevisionPhase = revisionLabel,
            RevisedMasterCount   = masterRevised,
            RevisionMasterTotal  = masterRevTotal,
            RevisedSubCount      = subRevised,
            RevisionSubTotal     = subRevTotal,
            ClosedAt             = closedAt,
            AchievementByType    = achievementRows,
            StatusByType         = statusByTypeRows,
            UrgentItems          = urgentItems
        };
    }

    /// <summary>
    /// 統一組裝 UI 顯示標籤（供 TargetBreakdown / AchievementItem / StatusByTypeItem 三種 DTO 共用）。
    /// CWT 模式（Level == null）：
    ///   - TypeId=3（閱讀題組）/ TypeId=5（短文題組）：Granularity=0 → 「XXX（母題）」；Granularity=1 → 「XXX（子題）」
    ///   - 其他題型：直接用 typeName
    /// LCT 模式（Level 非 null）：
    ///   - 固定回傳「難度X」（Level 1~5 對應一~五）
    /// </summary>
    /// <param name="typeName">DB 回傳的 MT_QuestionTypes.Name，CWT 模式使用；LCT 模式可忽略。</param>
    /// <param name="granularity">0=母題或單題，1=子題。</param>
    /// <param name="level">LCT 難度序號（1-5），null 代表 CWT 模式。</param>
    private static string BuildDisplayLabel(string typeName, byte granularity, byte? level)
    {
        // LCT 模式：level 有值，直接對應「難度X」（聽力測驗 5 個難度）
        if (level.HasValue)
        {
            return $"難度{level.Value switch { 1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五", _ => level.Value.ToString() }}";
        }

        // 題組類（閱讀題組 / 短文題組 / 聽力題組）依 granularity 掛 母/子 字尾，與 CWT 一致設計
        // 用 typeName 內容判斷（比硬編碼 TypeId 更安全，保持與 DB 名稱解耦）
        // 聽力題組（LCT）SQL 也是兩列輸出（Granularity=0 母 / Granularity=1 子），同樣掛字尾
        bool isGroup = typeName.Contains("題組");
        if (isGroup)
        {
            return granularity == 1 ? $"{typeName}（子題）" : $"{typeName}（母題）";
        }

        return typeName;
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
        System.Data.IDbConnection conn, int projectId, int? currentPhaseForChart,
        int[] activeTypeIds, bool isLct)
    {
        // LCT 模式：X 軸改為難度一~五，TypeId=6 按 Level 分組，TypeId=7 子題按 SortOrder 映射（1→難度三、2→難度四）（母題不計）
        if (isLct)
        {
            return await LoadStatusByTypeRowsLctAsync(conn, projectId, currentPhaseForChart);
        }

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
                -- 母題桶（全部 CWT 題型）
                SELECT
                    qt.Name AS TypeName,
                    CAST(0 AS TINYINT)     AS Granularity,
                    NULL                   AS Level,
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
                WHERE qt.Id IN @typeIds
                GROUP BY qt.Id, qt.Name
                UNION ALL
                -- 子題桶（僅 TypeId=3 閱讀題組 / TypeId=5 短文題組，Granularity=1）
                -- 子題狀態以 MT_SubQuestions.Status 為準；修題完成判定同母題（MT_RevisionReplies）
                SELECT
                    qt.Name AS TypeName,
                    CAST(1 AS TINYINT)     AS Granularity,
                    NULL                   AS Level,
                    ISNULL(SUM(CASE WHEN sq.Status = 0 THEN 1 ELSE 0 END), 0) AS Drafts,
                    ISNULL(SUM(CASE WHEN sq.Status BETWEEN 1 AND 8 AND qrs.QuestionId IS NULL     THEN 1 ELSE 0 END), 0) AS InProgress,
                    ISNULL(SUM(CASE WHEN sq.Status BETWEEN 1 AND 8 AND qrs.QuestionId IS NOT NULL THEN 1 ELSE 0 END), 0) AS DoneStage,
                    ISNULL(SUM(CASE WHEN sq.Status IN (9, 12)  THEN 1 ELSE 0 END), 0) AS Adopted,
                    ISNULL(SUM(CASE WHEN sq.Status IN (10, 11) THEN 1 ELSE 0 END), 0) AS Rejected
                FROM      dbo.MT_QuestionTypes qt
                LEFT JOIN dbo.MT_Questions q
                       ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
                LEFT JOIN dbo.MT_SubQuestions sq
                       ON sq.ParentQuestionId = q.Id AND sq.IsDeleted = 0
                LEFT JOIN QuestionRevisionStatus qrs ON qrs.QuestionId = q.Id
                WHERE qt.Id IN (3, 5)
                GROUP BY qt.Id, qt.Name
                ORDER BY TypeName, Granularity
                """;
            return (await conn.QueryAsync<DashboardStatusByTypeItem>(sqlRevisionBased, new { pid = projectId, revisionStage, typeIds = activeTypeIds })).ToList();
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
                -- 母題桶
                SELECT
                    qt.Name AS TypeName,
                    CAST(0 AS TINYINT) AS Granularity,
                    NULL               AS Level,
                    ISNULL(SUM(CASE WHEN q.Status = 0 THEN 1 ELSE 0 END), 0) AS Drafts,
                    ISNULL(SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND (qss.PendingCount IS NULL OR qss.PendingCount > 0) THEN 1 ELSE 0 END), 0) AS InProgress,
                    ISNULL(SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND qss.PendingCount = 0 THEN 1 ELSE 0 END), 0) AS DoneStage,
                    ISNULL(SUM(CASE WHEN q.Status IN (9, 12)  THEN 1 ELSE 0 END), 0) AS Adopted,
                    ISNULL(SUM(CASE WHEN q.Status IN (10, 11) THEN 1 ELSE 0 END), 0) AS Rejected
                FROM      dbo.MT_QuestionTypes qt
                LEFT JOIN dbo.MT_Questions q
                       ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
                LEFT JOIN QuestionStageStatus qss ON qss.QuestionId = q.Id
                WHERE qt.Id IN @typeIds
                GROUP BY qt.Id, qt.Name
                UNION ALL
                -- 子題桶（僅 TypeId=3/5，Granularity=1）
                -- 子題審題完成判定：沿用母題的 PendingCount（子題隸屬於母題的審題分配）
                SELECT
                    qt.Name AS TypeName,
                    CAST(1 AS TINYINT) AS Granularity,
                    NULL               AS Level,
                    ISNULL(SUM(CASE WHEN sq.Status = 0 THEN 1 ELSE 0 END), 0) AS Drafts,
                    ISNULL(SUM(CASE WHEN sq.Status BETWEEN 1 AND 8 AND (qss.PendingCount IS NULL OR qss.PendingCount > 0) THEN 1 ELSE 0 END), 0) AS InProgress,
                    ISNULL(SUM(CASE WHEN sq.Status BETWEEN 1 AND 8 AND qss.PendingCount = 0 THEN 1 ELSE 0 END), 0) AS DoneStage,
                    ISNULL(SUM(CASE WHEN sq.Status IN (9, 12)  THEN 1 ELSE 0 END), 0) AS Adopted,
                    ISNULL(SUM(CASE WHEN sq.Status IN (10, 11) THEN 1 ELSE 0 END), 0) AS Rejected
                FROM      dbo.MT_QuestionTypes qt
                LEFT JOIN dbo.MT_Questions q
                       ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
                LEFT JOIN dbo.MT_SubQuestions sq
                       ON sq.ParentQuestionId = q.Id AND sq.IsDeleted = 0
                LEFT JOIN QuestionStageStatus qss ON qss.QuestionId = q.Id
                WHERE qt.Id IN (3, 5)
                GROUP BY qt.Id, qt.Name
                ORDER BY TypeName, Granularity
                """;
            return (await conn.QueryAsync<DashboardStatusByTypeItem>(sqlReviewBased, new { pid = projectId, stage = reviewStage, typeIds = activeTypeIds })).ToList();
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
                                WHERE  pt.QuestionTypeId = qt.Id AND pt.ProjectId = @pid AND pt.Granularity = 0), 0) AS TargetCount,
                        ISNULL(SUM(CASE WHEN q.Status = 0          THEN 1 ELSE 0 END), 0) AS Drafts,
                        ISNULL(SUM(CASE WHEN q.Status IN (1, 2)    THEN 1 ELSE 0 END), 0) AS DoneStage,
                        ISNULL(SUM(CASE WHEN q.Status IN (9, 12)   THEN 1 ELSE 0 END), 0) AS Adopted,
                        ISNULL(SUM(CASE WHEN q.Status IN (10, 11)  THEN 1 ELSE 0 END), 0) AS Rejected
                    FROM      dbo.MT_QuestionTypes qt
                    LEFT JOIN dbo.MT_Questions q
                           ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
                    WHERE qt.Id IN @typeIds
                    GROUP BY qt.Id, qt.Name
                ),
                SubAgg AS (
                    -- 子題桶（命題階段：子題草稿/已完成/採用 按 Status 計數）
                    SELECT
                        qt.Id,
                        qt.Name AS TypeName,
                        ISNULL((SELECT SUM(pt.TargetCount)
                                FROM   dbo.MT_ProjectTargets pt
                                WHERE  pt.QuestionTypeId = qt.Id AND pt.ProjectId = @pid AND pt.Granularity = 1), 0) AS TargetCount,
                        ISNULL(SUM(CASE WHEN sq.Status = 0          THEN 1 ELSE 0 END), 0) AS Drafts,
                        ISNULL(SUM(CASE WHEN sq.Status IN (1, 2)    THEN 1 ELSE 0 END), 0) AS DoneStage,
                        ISNULL(SUM(CASE WHEN sq.Status IN (9, 12)   THEN 1 ELSE 0 END), 0) AS Adopted,
                        ISNULL(SUM(CASE WHEN sq.Status IN (10, 11)  THEN 1 ELSE 0 END), 0) AS Rejected
                    FROM      dbo.MT_QuestionTypes qt
                    LEFT JOIN dbo.MT_Questions q
                           ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
                    LEFT JOIN dbo.MT_SubQuestions sq
                           ON sq.ParentQuestionId = q.Id AND sq.IsDeleted = 0
                    WHERE qt.Id IN (3, 5)
                    GROUP BY qt.Id, qt.Name
                )
                -- 母題桶
                SELECT
                    TypeName,
                    CAST(0 AS TINYINT) AS Granularity,
                    NULL               AS Level,
                    Drafts,
                    CASE WHEN TargetCount - Drafts - DoneStage - Adopted - Rejected > 0
                         THEN TargetCount - Drafts - DoneStage - Adopted - Rejected
                         ELSE 0 END AS InProgress,
                    DoneStage, Adopted, Rejected
                FROM   TypeAgg
                UNION ALL
                -- 子題桶（TypeId=3/5）
                SELECT
                    TypeName,
                    CAST(1 AS TINYINT) AS Granularity,
                    NULL               AS Level,
                    Drafts,
                    CASE WHEN TargetCount - Drafts - DoneStage - Adopted - Rejected > 0
                         THEN TargetCount - Drafts - DoneStage - Adopted - Rejected
                         ELSE 0 END AS InProgress,
                    DoneStage, Adopted, Rejected
                FROM   SubAgg
                ORDER BY TypeName, Granularity
                """;
            return (await conn.QueryAsync<DashboardStatusByTypeItem>(sqlCompositionPhase, new { pid = projectId, typeIds = activeTypeIds })).ToList();
        }

        // 修題 / 結案 / 未啟動：純 Status 推進
        int inProgressStatus = currentPhaseForChart switch
        {
            4 or 6 or 8 => currentPhaseForChart.Value,
            _          => -1
        };
        const string sqlStatusBased = """
            -- 母題桶
            SELECT
                qt.Name AS TypeName,
                CAST(0 AS TINYINT) AS Granularity,
                NULL               AS Level,
                ISNULL(SUM(CASE WHEN q.Status = 0  THEN 1 ELSE 0 END), 0) AS Drafts,
                ISNULL(SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND (q.Status = @inProgress OR q.Status = 2) THEN 1 ELSE 0 END), 0) AS InProgress,
                ISNULL(SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND q.Status <> @inProgress AND q.Status <> 2 THEN 1 ELSE 0 END), 0) AS DoneStage,
                ISNULL(SUM(CASE WHEN q.Status IN (9, 12)  THEN 1 ELSE 0 END), 0) AS Adopted,
                ISNULL(SUM(CASE WHEN q.Status IN (10, 11) THEN 1 ELSE 0 END), 0) AS Rejected
            FROM dbo.MT_QuestionTypes qt
            LEFT JOIN dbo.MT_Questions q
                   ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
            WHERE qt.Id IN @typeIds
            GROUP BY qt.Id, qt.Name
            UNION ALL
            -- 子題桶（TypeId=3/5，Granularity=1）
            SELECT
                qt.Name AS TypeName,
                CAST(1 AS TINYINT) AS Granularity,
                NULL               AS Level,
                ISNULL(SUM(CASE WHEN sq.Status = 0  THEN 1 ELSE 0 END), 0) AS Drafts,
                ISNULL(SUM(CASE WHEN sq.Status BETWEEN 1 AND 8 AND (sq.Status = @inProgress OR sq.Status = 2) THEN 1 ELSE 0 END), 0) AS InProgress,
                ISNULL(SUM(CASE WHEN sq.Status BETWEEN 1 AND 8 AND sq.Status <> @inProgress AND sq.Status <> 2 THEN 1 ELSE 0 END), 0) AS DoneStage,
                ISNULL(SUM(CASE WHEN sq.Status IN (9, 12)  THEN 1 ELSE 0 END), 0) AS Adopted,
                ISNULL(SUM(CASE WHEN sq.Status IN (10, 11) THEN 1 ELSE 0 END), 0) AS Rejected
            FROM dbo.MT_QuestionTypes qt
            LEFT JOIN dbo.MT_Questions q
                   ON q.QuestionTypeId = qt.Id AND q.ProjectId = @pid AND q.IsDeleted = 0
            LEFT JOIN dbo.MT_SubQuestions sq
                   ON sq.ParentQuestionId = q.Id AND sq.IsDeleted = 0
            WHERE qt.Id IN (3, 5)
            GROUP BY qt.Id, qt.Name
            ORDER BY TypeName, Granularity
            """;
        return (await conn.QueryAsync<DashboardStatusByTypeItem>(sqlStatusBased, new { pid = projectId, inProgress = inProgressStatus, typeIds = activeTypeIds })).ToList();
    }

    /// <summary>
    /// LCT 模式的圖表 2：X 軸為 6 桶 — 難度一~五（聽力測驗 Level 1-5）+ 聽力題組（整組獨立桶）。
    /// 依 PhaseCode 分三套 SQL（鏡像 CWT 的 LoadStatusByTypeRowsAsync 設計）：
    ///   - 審題階段 3/5/7 → JOIN MT_ReviewAssignments，依 DecidedAt 切 InProgress / DoneStage
    ///   - 修題階段 4/6/8 → JOIN MT_RevisionReplies，依本輪修題回覆是否存在切 InProgress / DoneStage
    ///   - 命題階段 2 或其他 → 純 Status 推進（保留 target-gap 計算，剛建專案沒題目仍顯示橙色目標 bar）
    ///
    /// 統計來源（聽力題組獨立計算，不再 fold-in 至難三/難四）：
    /// - TypeId=6 聽力測驗：MT_Questions.Level 直接分組（5 桶）
    /// - TypeId=7 聽力題組：母題 Status 直接分組（1 桶，1 master = 1 整組單位）
    /// </summary>
    private static async Task<List<DashboardStatusByTypeItem>> LoadStatusByTypeRowsLctAsync(
        System.Data.IDbConnection conn, int projectId, int? currentPhaseForChart)
    {
        // 審題階段：依 ReviewAssignments.DecidedAt 區分（與卡片 3 同口徑）
        if (currentPhaseForChart is 3 or 5 or 7)
        {
            byte reviewStage = currentPhaseForChart.Value switch { 3 => 1, 5 => 2, 7 => 3, _ => (byte)0 };
            return await LoadStatusByTypeRowsLctAuditAsync(conn, projectId, reviewStage);
        }

        // 修題階段：依 MT_RevisionReplies.Content 本輪回覆是否存在切（與 CWT 修題分支同邏輯）
        if (currentPhaseForChart is 4 or 6 or 8)
        {
            byte revisionStage = (byte)currentPhaseForChart.Value;
            return await LoadStatusByTypeRowsLctRevisionAsync(conn, projectId, revisionStage);
        }

        // 命題階段 / 閒置 / 結案：純 Status 推進（保留 target-gap 邏輯）
        const string sql = """
            WITH LevelBuckets(LevelNum, LevelName) AS (
                SELECT LevelNum, LevelName
                FROM (VALUES (1, N'難度一'), (2, N'難度二'), (3, N'難度三'), (4, N'難度四'), (5, N'難度五'))
                     AS v(LevelNum, LevelName)
            ),
            Type6Targets AS (
                SELECT [Level] AS LevelNum, SUM(TargetCount) AS TotalTarget
                FROM   dbo.MT_ProjectTargets
                WHERE  ProjectId = @pid AND QuestionTypeId = 6 AND [Level] IS NOT NULL
                GROUP BY [Level]
            ),
            Type6Counts AS (
                SELECT [Level] AS LevelNum,
                       SUM(CASE WHEN Status = 0          THEN 1 ELSE 0 END) AS Drafts,
                       SUM(CASE WHEN Status IN (1, 2)    THEN 1 ELSE 0 END) AS DoneStage,
                       SUM(CASE WHEN Status IN (9, 12)   THEN 1 ELSE 0 END) AS Adopted,
                       SUM(CASE WHEN Status IN (10, 11)  THEN 1 ELSE 0 END) AS Rejected
                FROM   dbo.MT_Questions
                WHERE  ProjectId = @pid AND IsDeleted = 0 AND QuestionTypeId = 6 AND [Level] IS NOT NULL
                GROUP BY [Level]
            ),
            -- 聽力題組母題（TypeId=7）按 master Status 統計（1 master = 1 整組）
            Type7Counts AS (
                SELECT
                    SUM(CASE WHEN Status = 0          THEN 1 ELSE 0 END) AS Drafts,
                    SUM(CASE WHEN Status IN (1, 2)    THEN 1 ELSE 0 END) AS DoneStage,
                    SUM(CASE WHEN Status IN (9, 12)   THEN 1 ELSE 0 END) AS Adopted,
                    SUM(CASE WHEN Status IN (10, 11)  THEN 1 ELSE 0 END) AS Rejected,
                    ISNULL((SELECT SUM(TargetCount) FROM dbo.MT_ProjectTargets
                            WHERE ProjectId = @pid AND QuestionTypeId = 7), 0) AS TotalTarget
                FROM   dbo.MT_Questions
                WHERE  ProjectId = @pid AND IsDeleted = 0 AND QuestionTypeId = 7
            ),
            -- 聽力題組子題：MT_SubQuestions.Status 統計（固定 2 子題/組，目標 = 母題 target × 2）
            Type7SubCounts AS (
                SELECT
                    SUM(CASE WHEN sq.Status = 0          THEN 1 ELSE 0 END) AS Drafts,
                    SUM(CASE WHEN sq.Status IN (1, 2)    THEN 1 ELSE 0 END) AS DoneStage,
                    SUM(CASE WHEN sq.Status IN (9, 12)   THEN 1 ELSE 0 END) AS Adopted,
                    SUM(CASE WHEN sq.Status IN (10, 11)  THEN 1 ELSE 0 END) AS Rejected,
                    ISNULL((SELECT SUM(TargetCount) * 2 FROM dbo.MT_ProjectTargets
                            WHERE ProjectId = @pid AND QuestionTypeId = 7), 0) AS TotalTarget
                FROM   dbo.MT_SubQuestions sq
                JOIN   dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                WHERE  q.ProjectId = @pid AND q.IsDeleted = 0 AND sq.IsDeleted = 0
                  AND  q.QuestionTypeId = 7
            )
            SELECT TypeName, Granularity, Level, Drafts, InProgress, DoneStage, Adopted, Rejected
            FROM (
                SELECT
                    lb.LevelName                 AS TypeName,
                    CAST(0 AS TINYINT)           AS Granularity,
                    CAST(lb.LevelNum AS TINYINT) AS Level,
                    ISNULL(c.Drafts, 0)          AS Drafts,
                    0                            AS InProgress,  -- 命題階段無「審/修進行中」實態；缺額屬左卡職責，不重複呈現
                    ISNULL(c.DoneStage, 0)       AS DoneStage,
                    ISNULL(c.Adopted, 0)         AS Adopted,
                    ISNULL(c.Rejected, 0)        AS Rejected,
                    lb.LevelNum                  AS SortKey
                FROM      LevelBuckets lb
                LEFT JOIN Type6Targets t ON t.LevelNum = lb.LevelNum
                LEFT JOIN Type6Counts  c ON c.LevelNum = lb.LevelNum
                UNION ALL
                -- 聽力題組母題
                SELECT
                    N'聽力題組',  -- Granularity=0 母題（BuildDisplayLabel 統一掛「（母題）」字尾）
                    CAST(0 AS TINYINT),
                    CAST(NULL AS TINYINT),
                    ISNULL(t7.Drafts, 0),
                    0,  -- 命題階段無實態進行中
                    ISNULL(t7.DoneStage, 0),
                    ISNULL(t7.Adopted, 0),
                    ISNULL(t7.Rejected, 0),
                    100
                FROM Type7Counts t7
                UNION ALL
                -- 聽力題組子題（與 CWT 母/子分開設計一致）
                SELECT
                    N'聽力題組',  -- Granularity=1 子題（BuildDisplayLabel 統一掛「（子題）」字尾）
                    CAST(1 AS TINYINT),
                    CAST(NULL AS TINYINT),
                    ISNULL(t7s.Drafts, 0),
                    0,  -- 命題階段無實態進行中
                    ISNULL(t7s.DoneStage, 0),
                    ISNULL(t7s.Adopted, 0),
                    ISNULL(t7s.Rejected, 0),
                    101
                FROM Type7SubCounts t7s
            ) tmp
            -- 與 CWT 一致：所有難度桶都保留，沒命題的難度 X 軸保留、Y 軸 0
            ORDER BY SortKey
            """;

        return (await conn.QueryAsync<DashboardStatusByTypeItem>(sql, new { pid = projectId })).ToList();
    }

    /// <summary>
    /// LCT 審題階段（PhaseCode 3/5/7）的圖表 2 資料：
    /// 用 MT_ReviewAssignments.DecidedAt 判斷「該題本階段是否審完」，跟 CWT 審題分支同邏輯。
    /// 「DoneStage = Status 1-8 AND 所有母題層分配都已決策」；InProgress = 仍有待審分配 + 命題缺口。
    /// 只取母題層分配（SubQuestionId IS NULL）——聽力題組母題會被計入；子題分配不在此圖表桶內。
    /// </summary>
    private static async Task<List<DashboardStatusByTypeItem>> LoadStatusByTypeRowsLctAuditAsync(
        System.Data.IDbConnection conn, int projectId, byte reviewStage)
    {
        const string sql = """
            WITH LevelBuckets(LevelNum, LevelName) AS (
                SELECT LevelNum, LevelName
                FROM (VALUES (1, N'難度一'), (2, N'難度二'), (3, N'難度三'), (4, N'難度四'), (5, N'難度五'))
                     AS v(LevelNum, LevelName)
            ),
            Type6Targets AS (
                SELECT [Level] AS LevelNum, SUM(TargetCount) AS TotalTarget
                FROM   dbo.MT_ProjectTargets
                WHERE  ProjectId = @pid AND QuestionTypeId = 6 AND [Level] IS NOT NULL
                GROUP BY [Level]
            ),
            QuestionStageStatus AS (
                SELECT  ra.QuestionId,
                        SUM(CASE WHEN ra.DecidedAt IS NULL THEN 1 ELSE 0 END) AS PendingCount
                FROM    dbo.MT_ReviewAssignments ra
                WHERE   ra.ProjectId = @pid AND ra.ReviewStage = @stage AND ra.SubQuestionId IS NULL
                GROUP BY ra.QuestionId
            ),
            Type6Counts AS (
                SELECT q.[Level] AS LevelNum,
                       COUNT(*) AS ExistingCount,
                       SUM(CASE WHEN q.Status = 0 THEN 1 ELSE 0 END) AS Drafts,
                       SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND (qss.PendingCount IS NULL OR qss.PendingCount > 0) THEN 1 ELSE 0 END) AS InProgressAudit,
                       SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND qss.PendingCount = 0 THEN 1 ELSE 0 END) AS DoneStage,
                       SUM(CASE WHEN q.Status IN (9, 12)  THEN 1 ELSE 0 END) AS Adopted,
                       SUM(CASE WHEN q.Status IN (10, 11) THEN 1 ELSE 0 END) AS Rejected
                FROM   dbo.MT_Questions q
                LEFT JOIN QuestionStageStatus qss ON qss.QuestionId = q.Id
                WHERE  q.ProjectId = @pid AND q.IsDeleted = 0 AND q.QuestionTypeId = 6 AND q.[Level] IS NOT NULL
                GROUP BY q.[Level]
            ),
            Type7Counts AS (
                SELECT
                    COUNT(*) AS ExistingCount,
                    SUM(CASE WHEN q.Status = 0 THEN 1 ELSE 0 END) AS Drafts,
                    SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND (qss.PendingCount IS NULL OR qss.PendingCount > 0) THEN 1 ELSE 0 END) AS InProgressAudit,
                    SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND qss.PendingCount = 0 THEN 1 ELSE 0 END) AS DoneStage,
                    SUM(CASE WHEN q.Status IN (9, 12)  THEN 1 ELSE 0 END) AS Adopted,
                    SUM(CASE WHEN q.Status IN (10, 11) THEN 1 ELSE 0 END) AS Rejected,
                    ISNULL((SELECT SUM(TargetCount) FROM dbo.MT_ProjectTargets
                            WHERE ProjectId = @pid AND QuestionTypeId = 7), 0) AS TotalTarget
                FROM   dbo.MT_Questions q
                LEFT JOIN QuestionStageStatus qss ON qss.QuestionId = q.Id
                WHERE  q.ProjectId = @pid AND q.IsDeleted = 0 AND q.QuestionTypeId = 7
            ),
            -- 聽力題組子題：依 sub-assignments（SubQuestionId IS NOT NULL）的 PendingCount 切 InProgress/DoneStage
            QuestionSubStageStatus AS (
                SELECT  ra.SubQuestionId,
                        SUM(CASE WHEN ra.DecidedAt IS NULL THEN 1 ELSE 0 END) AS PendingCount
                FROM    dbo.MT_ReviewAssignments ra
                WHERE   ra.ProjectId = @pid AND ra.ReviewStage = @stage AND ra.SubQuestionId IS NOT NULL
                GROUP BY ra.SubQuestionId
            ),
            Type7SubCounts AS (
                SELECT
                    COUNT(*) AS ExistingCount,
                    SUM(CASE WHEN sq.Status = 0 THEN 1 ELSE 0 END) AS Drafts,
                    SUM(CASE WHEN sq.Status BETWEEN 1 AND 8 AND (qsub.PendingCount IS NULL OR qsub.PendingCount > 0) THEN 1 ELSE 0 END) AS InProgressAudit,
                    SUM(CASE WHEN sq.Status BETWEEN 1 AND 8 AND qsub.PendingCount = 0 THEN 1 ELSE 0 END) AS DoneStage,
                    SUM(CASE WHEN sq.Status IN (9, 12)  THEN 1 ELSE 0 END) AS Adopted,
                    SUM(CASE WHEN sq.Status IN (10, 11) THEN 1 ELSE 0 END) AS Rejected,
                    ISNULL((SELECT SUM(TargetCount) * 2 FROM dbo.MT_ProjectTargets
                            WHERE ProjectId = @pid AND QuestionTypeId = 7), 0) AS TotalTarget
                FROM   dbo.MT_SubQuestions sq
                JOIN   dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                LEFT JOIN QuestionSubStageStatus qsub ON qsub.SubQuestionId = sq.Id
                WHERE  q.ProjectId = @pid AND q.IsDeleted = 0 AND sq.IsDeleted = 0
                  AND  q.QuestionTypeId = 7
            )
            SELECT TypeName, Granularity, Level, Drafts, InProgress, DoneStage, Adopted, Rejected
            FROM (
                SELECT
                    lb.LevelName                 AS TypeName,
                    CAST(0 AS TINYINT)           AS Granularity,
                    CAST(lb.LevelNum AS TINYINT) AS Level,
                    ISNULL(c.Drafts, 0)          AS Drafts,
                    ISNULL(c.InProgressAudit, 0) AS InProgress,  -- 只算真實審題進行中；缺額屬左卡「題型缺口達成率」職責
                    ISNULL(c.DoneStage, 0)       AS DoneStage,
                    ISNULL(c.Adopted, 0)         AS Adopted,
                    ISNULL(c.Rejected, 0)        AS Rejected,
                    lb.LevelNum                  AS SortKey
                FROM      LevelBuckets lb
                LEFT JOIN Type6Targets t ON t.LevelNum = lb.LevelNum
                LEFT JOIN Type6Counts  c ON c.LevelNum = lb.LevelNum
                UNION ALL
                SELECT
                    N'聽力題組',  -- Granularity=0 母題（BuildDisplayLabel 統一掛「（母題）」字尾）
                    CAST(0 AS TINYINT),
                    CAST(NULL AS TINYINT),
                    ISNULL(t7.Drafts, 0),
                    ISNULL(t7.InProgressAudit, 0),
                    ISNULL(t7.DoneStage, 0),
                    ISNULL(t7.Adopted, 0),
                    ISNULL(t7.Rejected, 0),
                    100
                FROM Type7Counts t7
                UNION ALL
                SELECT
                    N'聽力題組',  -- Granularity=1 子題（BuildDisplayLabel 統一掛「（子題）」字尾）
                    CAST(1 AS TINYINT),
                    CAST(NULL AS TINYINT),
                    ISNULL(t7s.Drafts, 0),
                    ISNULL(t7s.InProgressAudit, 0),
                    ISNULL(t7s.DoneStage, 0),
                    ISNULL(t7s.Adopted, 0),
                    ISNULL(t7s.Rejected, 0),
                    101
                FROM Type7SubCounts t7s
            ) tmp
            -- 與 CWT 一致：保留全部難度桶，沒命題的難度 X 軸保留、Y 軸 0
            ORDER BY SortKey
            """;

        return (await conn.QueryAsync<DashboardStatusByTypeItem>(sql, new { pid = projectId, stage = reviewStage })).ToList();
    }

    /// <summary>
    /// LCT 修題階段（PhaseCode 4/6/8）的圖表 2 資料：
    /// 用 MT_RevisionReplies 是否存在本輪非空回覆判斷「該題本階段是否修完」，跟 CWT 修題分支同邏輯。
    /// 本輪過濾透過 vw_QuestionRoundStartedAt：PC=8 跨輪退回後舊 reply 不算本輪已修；PC=4/6 線性單輪不受影響。
    /// </summary>
    private static async Task<List<DashboardStatusByTypeItem>> LoadStatusByTypeRowsLctRevisionAsync(
        System.Data.IDbConnection conn, int projectId, byte revisionStage)
    {
        const string sql = """
            WITH LevelBuckets(LevelNum, LevelName) AS (
                SELECT LevelNum, LevelName
                FROM (VALUES (1, N'難度一'), (2, N'難度二'), (3, N'難度三'), (4, N'難度四'), (5, N'難度五'))
                     AS v(LevelNum, LevelName)
            ),
            Type6Targets AS (
                SELECT [Level] AS LevelNum, SUM(TargetCount) AS TotalTarget
                FROM   dbo.MT_ProjectTargets
                WHERE  ProjectId = @pid AND QuestionTypeId = 6 AND [Level] IS NOT NULL
                GROUP BY [Level]
            ),
            QuestionRevisionStatus AS (
                SELECT DISTINCT rr.QuestionId
                FROM   dbo.MT_RevisionReplies rr
                WHERE  rr.Stage = @revisionStage
                  AND  rr.Content IS NOT NULL
                  AND  LEN(TRIM(rr.Content)) > 0
                  AND  rr.CreatedAt > ISNULL(
                      (SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt WHERE QuestionId = rr.QuestionId),
                      '1900-01-01')
            ),
            Type6Counts AS (
                SELECT q.[Level] AS LevelNum,
                       COUNT(*) AS ExistingCount,
                       SUM(CASE WHEN q.Status = 0 THEN 1 ELSE 0 END) AS Drafts,
                       SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND qrs.QuestionId IS NULL     THEN 1 ELSE 0 END) AS InProgressRev,
                       SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND qrs.QuestionId IS NOT NULL THEN 1 ELSE 0 END) AS DoneStage,
                       SUM(CASE WHEN q.Status IN (9, 12)  THEN 1 ELSE 0 END) AS Adopted,
                       SUM(CASE WHEN q.Status IN (10, 11) THEN 1 ELSE 0 END) AS Rejected
                FROM   dbo.MT_Questions q
                LEFT JOIN QuestionRevisionStatus qrs ON qrs.QuestionId = q.Id
                WHERE  q.ProjectId = @pid AND q.IsDeleted = 0 AND q.QuestionTypeId = 6 AND q.[Level] IS NOT NULL
                GROUP BY q.[Level]
            ),
            Type7Counts AS (
                SELECT
                    COUNT(*) AS ExistingCount,
                    SUM(CASE WHEN q.Status = 0 THEN 1 ELSE 0 END) AS Drafts,
                    SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND qrs.QuestionId IS NULL     THEN 1 ELSE 0 END) AS InProgressRev,
                    SUM(CASE WHEN q.Status BETWEEN 1 AND 8 AND qrs.QuestionId IS NOT NULL THEN 1 ELSE 0 END) AS DoneStage,
                    SUM(CASE WHEN q.Status IN (9, 12)  THEN 1 ELSE 0 END) AS Adopted,
                    SUM(CASE WHEN q.Status IN (10, 11) THEN 1 ELSE 0 END) AS Rejected,
                    ISNULL((SELECT SUM(TargetCount) FROM dbo.MT_ProjectTargets
                            WHERE ProjectId = @pid AND QuestionTypeId = 7), 0) AS TotalTarget
                FROM   dbo.MT_Questions q
                LEFT JOIN QuestionRevisionStatus qrs ON qrs.QuestionId = q.Id
                WHERE  q.ProjectId = @pid AND q.IsDeleted = 0 AND q.QuestionTypeId = 7
            ),
            -- 聽力題組子題本輪修題狀態
            SubRevisionStatus AS (
                SELECT DISTINCT rr.SubQuestionId
                FROM   dbo.MT_RevisionReplies rr
                WHERE  rr.Stage = @revisionStage
                  AND  rr.SubQuestionId IS NOT NULL
                  AND  rr.Content IS NOT NULL
                  AND  LEN(TRIM(rr.Content)) > 0
                  AND  rr.CreatedAt > ISNULL(
                      (SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt WHERE QuestionId = rr.QuestionId),
                      '1900-01-01')
            ),
            Type7SubCounts AS (
                SELECT
                    COUNT(*) AS ExistingCount,
                    SUM(CASE WHEN sq.Status = 0 THEN 1 ELSE 0 END) AS Drafts,
                    SUM(CASE WHEN sq.Status BETWEEN 1 AND 8 AND srs.SubQuestionId IS NULL     THEN 1 ELSE 0 END) AS InProgressRev,
                    SUM(CASE WHEN sq.Status BETWEEN 1 AND 8 AND srs.SubQuestionId IS NOT NULL THEN 1 ELSE 0 END) AS DoneStage,
                    SUM(CASE WHEN sq.Status IN (9, 12)  THEN 1 ELSE 0 END) AS Adopted,
                    SUM(CASE WHEN sq.Status IN (10, 11) THEN 1 ELSE 0 END) AS Rejected,
                    ISNULL((SELECT SUM(TargetCount) * 2 FROM dbo.MT_ProjectTargets
                            WHERE ProjectId = @pid AND QuestionTypeId = 7), 0) AS TotalTarget
                FROM   dbo.MT_SubQuestions sq
                JOIN   dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                LEFT JOIN SubRevisionStatus srs ON srs.SubQuestionId = sq.Id
                WHERE  q.ProjectId = @pid AND q.IsDeleted = 0 AND sq.IsDeleted = 0
                  AND  q.QuestionTypeId = 7
            )
            SELECT TypeName, Granularity, Level, Drafts, InProgress, DoneStage, Adopted, Rejected
            FROM (
                SELECT
                    lb.LevelName                 AS TypeName,
                    CAST(0 AS TINYINT)           AS Granularity,
                    CAST(lb.LevelNum AS TINYINT) AS Level,
                    ISNULL(c.Drafts, 0)          AS Drafts,
                    ISNULL(c.InProgressRev, 0)   AS InProgress,  -- 只算真實修題進行中；缺額屬左卡職責
                    ISNULL(c.DoneStage, 0)       AS DoneStage,
                    ISNULL(c.Adopted, 0)         AS Adopted,
                    ISNULL(c.Rejected, 0)        AS Rejected,
                    lb.LevelNum                  AS SortKey
                FROM      LevelBuckets lb
                LEFT JOIN Type6Targets t ON t.LevelNum = lb.LevelNum
                LEFT JOIN Type6Counts  c ON c.LevelNum = lb.LevelNum
                UNION ALL
                SELECT
                    N'聽力題組',  -- Granularity=0 母題（BuildDisplayLabel 統一掛「（母題）」字尾）
                    CAST(0 AS TINYINT),
                    CAST(NULL AS TINYINT),
                    ISNULL(t7.Drafts, 0),
                    ISNULL(t7.InProgressRev, 0),
                    ISNULL(t7.DoneStage, 0),
                    ISNULL(t7.Adopted, 0),
                    ISNULL(t7.Rejected, 0),
                    100
                FROM Type7Counts t7
                UNION ALL
                SELECT
                    N'聽力題組',  -- Granularity=1 子題（BuildDisplayLabel 統一掛「（子題）」字尾）
                    CAST(1 AS TINYINT),
                    CAST(NULL AS TINYINT),
                    ISNULL(t7s.Drafts, 0),
                    ISNULL(t7s.InProgressRev, 0),
                    ISNULL(t7s.DoneStage, 0),
                    ISNULL(t7s.Adopted, 0),
                    ISNULL(t7s.Rejected, 0),
                    101
                FROM Type7SubCounts t7s
            ) tmp
            -- 與 CWT 一致：保留全部難度桶，沒命題的難度 X 軸保留、Y 軸 0
            ORDER BY SortKey
            """;

        return (await conn.QueryAsync<DashboardStatusByTypeItem>(sql, new { pid = projectId, revisionStage })).ToList();
    }

    /// <summary>
    /// 依當前 PhaseCode 對應 ReviewStage，查 MT_ReviewAssignments 統計審題完成度。
    /// 對應規則：PhaseCode 3→Stage 1（互審）/ 5→Stage 2（專審）/ 7→Stage 3（總召）。
    /// 完成判定：DecidedAt IS NOT NULL（草稿只 UPDATE Comment 不寫 DecidedAt）。
    /// 母題、子題各自獨立計數互不影響，回傳 5 元組 (label, masterReviewed, masterTotal, subReviewed, subTotal)。
    /// 非審題階段直接回傳全 0，不下 SQL。
    /// </summary>
    private static async Task<(ReviewPhaseLabel label, int masterReviewed, int masterTotal, int subReviewed, int subTotal)> GetReviewProgressAsync(
        System.Data.IDbConnection conn, int projectId, int? currentPhaseCode,
        int[] activeTypeIds, bool isLct)
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

        if (label == ReviewPhaseLabel.None) return (label, 0, 0, 0, 0);

        // 母題、子題拆成獨立單位分別計數，互不影響：
        //   • 母題（SubQuestionId IS NULL）的 reviewed/total
        //   • 子題（SubQuestionId 非 NULL）的 reviewed/total
        // DecidedAt 是「真正送出決策」的標記（草稿只 UPDATE Comment 不寫 DecidedAt）
        // CWT/LCT 雙模式：JOIN MT_Questions 限定 QuestionTypeId IN @typeIds
        const string sql = """
            SELECT
                SUM(CASE WHEN ra.SubQuestionId IS NULL AND ra.DecidedAt IS NOT NULL THEN 1 ELSE 0 END) AS MasterReviewed,
                SUM(CASE WHEN ra.SubQuestionId IS NULL                               THEN 1 ELSE 0 END) AS MasterTotal,
                SUM(CASE WHEN ra.SubQuestionId IS NOT NULL AND ra.DecidedAt IS NOT NULL THEN 1 ELSE 0 END) AS SubReviewed,
                SUM(CASE WHEN ra.SubQuestionId IS NOT NULL                              THEN 1 ELSE 0 END) AS SubTotal
            FROM   dbo.MT_ReviewAssignments ra
            JOIN   dbo.MT_Questions q ON q.Id = ra.QuestionId
            WHERE  ra.ProjectId = @pid AND ra.ReviewStage = @stage
              AND  q.QuestionTypeId IN @typeIds;
            """;

        var row = await conn.QuerySingleOrDefaultAsync<ReviewProgressRow>(
            sql, new { pid = projectId, stage, typeIds = activeTypeIds });

        var masterReviewed = row?.MasterReviewed ?? 0;
        var masterTotal    = row?.MasterTotal    ?? 0;
        var subReviewed    = row?.SubReviewed    ?? 0;
        var subTotal       = row?.SubTotal       ?? 0;

        // Fallback：階段尚未跑過 EnsurePhaseTransitionAsync 觸發分配時，ReviewAssignments 可能還沒建好
        // → 改用單元粒度（MT_Questions 母題 / MT_SubQuestions 子題各自）估算待審池總數
        if (masterTotal == 0 && subTotal == 0)
        {
            // 母題與子題分開計算（聽力題組母題與子題皆計入，與 CWT 一致設計）
            const string fallbackSql = """
                SELECT
                    (SELECT COUNT(*) FROM dbo.MT_Questions
                     WHERE ProjectId = @pid AND IsDeleted = 0 AND Status BETWEEN 2 AND 8
                       AND QuestionTypeId IN @typeIds) AS MasterTotal,
                    (SELECT COUNT(*) FROM dbo.MT_SubQuestions sq
                     JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                     WHERE q.ProjectId = @pid AND q.IsDeleted = 0 AND sq.IsDeleted = 0
                       AND q.QuestionTypeId IN @typeIds
                       AND sq.Status BETWEEN 2 AND 8) AS SubTotal
                """;
            var fb = await conn.QuerySingleOrDefaultAsync<(int MasterTotal, int SubTotal)>(
                fallbackSql, new { pid = projectId, typeIds = activeTypeIds });
            masterTotal = fb.MasterTotal;
            subTotal    = fb.SubTotal;
            masterReviewed = 0;
            subReviewed    = 0;
        }

        return (label, masterReviewed, masterTotal, subReviewed, subTotal);
    }

    /// <summary>
    /// 依當前 PhaseCode 對應 RevisionStage，查 MT_ReviewAssignments + MT_RevisionReplies
    /// 統計修題完成度，母題與子題各自計數回傳 5 元組。
    /// 對應規則：PhaseCode 4→Stage 1（互修）/ 6→Stage 2（專修）/ 8→Stage 3（總修）。
    ///
    /// 待修總數（母/子）= 該階段對應 ReviewStage 中有 DecidedAt 的 distinct (QuestionId, SubQuestionId)
    /// 已修完（母/子）= 上述單元中，已有有效 MT_RevisionReplies.Content 的數量
    ///
    /// CWT/LCT 雙模式：activeTypeIds 限定只計算有效題型；聽力題組（TypeId=7）母題與子題皆分開計入，與 CWT 一致設計。
    /// 非修題階段（PhaseCode 不在 4/6/8）→ 直接回 (None, 0, 0, 0, 0)，不下 SQL。
    /// </summary>
    private static async Task<(RevisionPhaseLabel label, int masterRevised, int masterTotal, int subRevised, int subTotal)> GetRevisionProgressAsync(
        System.Data.IDbConnection conn, int projectId, int? currentPhaseCode,
        int[] activeTypeIds, bool isLct)
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

        if (label == RevisionPhaseLabel.None) return (label, 0, 0, 0, 0);

        // 單元粒度（Stage B-4-2 後）：母題與每子題各為獨立修題單元，各自計數
        // ─ SubQuestionId IS NULL     → 母題修題單元
        // ─ SubQuestionId IS NOT NULL → 子題修題單元
        // ─ CWT 模式：WHERE QuestionTypeId IN @typeIds 限定有效題型
        // ─ LCT 模式：聽力題組（TypeId=7）母題與子題同樣分開計入（與 CWT 一致設計）
        const string sql = """
            WITH AssignedUnits AS (
                SELECT DISTINCT ra.QuestionId, ra.SubQuestionId, q.QuestionTypeId
                FROM   dbo.MT_ReviewAssignments ra
                JOIN   dbo.MT_Questions q ON q.Id = ra.QuestionId
                LEFT JOIN dbo.MT_SubQuestions sq ON sq.Id = ra.SubQuestionId
                WHERE  ra.ProjectId   = @pid
                  AND  ra.ReviewStage = @reviewStage
                  AND  ra.DecidedAt IS NOT NULL
                  AND  q.IsDeleted = 0
                  AND  q.QuestionTypeId IN @typeIds
                  -- 排除所有「已最終決策 / 結案處理」狀態：9=採用、10=不採用、11=結案未採用、12=結案入庫
                  AND (
                        (ra.SubQuestionId IS NULL     AND q.Status  NOT IN (9, 10, 11, 12))
                     OR (ra.SubQuestionId IS NOT NULL AND sq.Status NOT IN (9, 10, 11, 12))
                  )
            ),
            RevisedCheck AS (
                SELECT a.QuestionId, a.SubQuestionId,
                       CASE WHEN EXISTS (
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
                       ) THEN 1 ELSE 0 END AS IsRevised
                FROM AssignedUnits a
            )
            SELECT
                SUM(CASE WHEN SubQuestionId IS NULL AND IsRevised = 1 THEN 1 ELSE 0 END) AS MasterRevised,
                SUM(CASE WHEN SubQuestionId IS NULL                   THEN 1 ELSE 0 END) AS MasterTotal,
                SUM(CASE WHEN SubQuestionId IS NOT NULL AND IsRevised = 1 THEN 1 ELSE 0 END) AS SubRevised,
                SUM(CASE WHEN SubQuestionId IS NOT NULL               THEN 1 ELSE 0 END) AS SubTotal
            FROM RevisedCheck
            """;

        var row = await conn.QuerySingleOrDefaultAsync<RevisionProgressRow>(
            sql, new { pid = projectId, reviewStage, revisionStage, typeIds = activeTypeIds, isLct = isLct ? 1 : 0 });

        var masterRevised = row?.MasterRevised  ?? 0;
        var masterTotal   = row?.MasterTotal    ?? 0;
        var subRevised    = row?.SubRevised     ?? 0;
        var subTotal      = row?.SubTotal       ?? 0;

        // assignments 為 0 表示本修題階段尚無待修題目（非 fallback 情境），直接回傳 0/0/0/0
        if (masterTotal == 0 && subTotal == 0) return (label, 0, 0, 0, 0);

        return (label, masterRevised, masterTotal, subRevised, subTotal);
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
        List<UrgentPhaseRow> allPhases,
        int[] activeTypeIds,
        bool isLct)
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
            // CWT/LCT 雙模式：Produced 依 Granularity / Level 精準對應配額分桶
            //   - Granularity = 0：MT_Questions 母題層計（LCT 聽力測驗加 mq.Level 過濾）
            //   - Granularity = 1：MT_SubQuestions 子題層計（CWT 閱讀/短文題組才會用到）
            // 修補：原 OUTER APPLY 只按 TypeId 過濾不分 Level，造成 LCT 5 個難度配額共用同一份
            //       Produced 計數（每難度 Produced 都拿到全 TypeId=6 總數），SUM 後膨脹 N 倍，
            //       導致老師明明還有 2 題未完成卻被 HAVING TotalProduced < TotalAssigned 排除掉
            const string sqlTeacherShortage = """
                SELECT  pm.UserId,
                        u.DisplayName AS TeacherName,
                        SUM(mq.QuotaCount) AS TotalAssigned,
                        -- 每列 Produced 先 clamp 在 QuotaCount 上限再 SUM —
                        -- 教師層級「達成」只算配額內的，超量部分不灌水（例 閱讀母 2/1 只算 1）
                        -- 專案層級的真實計數另由卡片 1/2/圖表呈現
                        SUM(CASE WHEN ISNULL(prod.Produced, 0) > mq.QuotaCount
                                 THEN mq.QuotaCount
                                 ELSE ISNULL(prod.Produced, 0) END) AS TotalProduced
                FROM    dbo.MT_ProjectMembers pm
                JOIN    dbo.MT_MemberQuotas   mq ON mq.ProjectMemberId = pm.Id
                JOIN    dbo.MT_Users          u  ON u.Id = pm.UserId
                OUTER APPLY (
                    SELECT
                        CASE WHEN mq.Granularity = 1 THEN
                            -- 子題層：MT_SubQuestions 計數（按母題 TypeId）
                            (SELECT COUNT(*) FROM dbo.MT_SubQuestions sq
                             JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                             WHERE q.ProjectId = pm.ProjectId
                               AND q.CreatorId = pm.UserId
                               AND q.QuestionTypeId = mq.QuestionTypeId
                               AND q.IsDeleted = 0 AND sq.IsDeleted = 0
                               AND sq.Status NOT IN (0, 10, 11))
                        ELSE
                            -- 母題層：MT_Questions 計數，LCT 聽力測驗需加 Level 過濾避免膨脹
                            (SELECT COUNT(*) FROM dbo.MT_Questions
                             WHERE ProjectId = pm.ProjectId
                               AND CreatorId = pm.UserId
                               AND QuestionTypeId = mq.QuestionTypeId
                               AND IsDeleted = 0
                               AND Status NOT IN (0, 10, 11)
                               AND (mq.Level IS NULL OR [Level] = mq.Level))
                        END AS Produced
                ) prod
                WHERE   pm.ProjectId = @pid
                  AND   mq.QuestionTypeId IN @typeIds
                GROUP BY pm.UserId, u.DisplayName
                HAVING  SUM(mq.QuotaCount) > 0
                  -- HAVING 用 clamped SUM（每桶 Produced 上限為 QuotaCount）：
                  -- 確保「某 bucket 超量、另 bucket 未達」的老師一定上榜，不被「平均」掩蓋
                  -- 例：A 命 master 5/1（超 4）但子題 2/4（未達），原始 SUM 7 > 5 會誤判完成
                  -- clamped SUM 1+2=3 < 5 → 正確列入警示
                  AND   SUM(CASE WHEN prod.Produced > mq.QuotaCount
                                 THEN mq.QuotaCount
                                 ELSE ISNULL(prod.Produced, 0) END) < SUM(mq.QuotaCount)
                ORDER   BY (SUM(CASE WHEN prod.Produced > mq.QuotaCount
                                     THEN mq.QuotaCount
                                     ELSE ISNULL(prod.Produced, 0) END) * 1.0
                            / SUM(mq.QuotaCount)) ASC
                """;

            var teacherRows = (await conn.QueryAsync<TeacherShortageRow>(
                sqlTeacherShortage, new { pid = projectId, typeIds = activeTypeIds })).ToList();

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
            // CWT/LCT 雙模式：只計對應題型的修題落後
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
                      AND  q.Status NOT IN (9, 10, 11, 12)
                      AND  q.QuestionTypeId IN @typeIds
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
                    revisionStage = revisionStageCode,
                    typeIds       = activeTypeIds
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

            // CWT/LCT 雙模式：只計對應題型的審題落後
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
                  AND   q.QuestionTypeId IN @typeIds
                GROUP BY ra.ReviewerId, u.DisplayName
                HAVING  COUNT(*) > SUM(CASE WHEN ra.DecidedAt IS NOT NULL
                                            THEN 1 ELSE 0 END)
                ORDER BY (1.0 * SUM(CASE WHEN ra.DecidedAt IS NOT NULL
                                         THEN 1 ELSE 0 END)
                         / NULLIF(COUNT(*), 0)) ASC
                """;

            var reviewerRows = (await conn.QueryAsync<TeacherShortageRow>(
                sqlReviewerShortage, new { pid = projectId, reviewStage, typeIds = activeTypeIds })).ToList();

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
                // CWT/LCT 雙模式：修題明細加入 Granularity / Level 區分
                //   Granularity = ra.SubQuestionId IS NULL ? 0 : 1
                //   Level = q.Level（僅 LCT TypeId=6 帶值；其他題型 null）
                sqlTypeDetails = """
                    WITH RevisionScope AS (
                        SELECT ra.QuestionId,
                               q.CreatorId,
                               q.QuestionTypeId,
                               CAST(CASE WHEN ra.SubQuestionId IS NULL THEN 0 ELSE 1 END AS TINYINT) AS Granularity,
                               CAST(CASE WHEN q.QuestionTypeId = 6 THEN q.[Level] ELSE NULL END AS TINYINT) AS [Level],
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
                          AND  q.Status NOT IN (9, 10, 11, 12)
                          AND  q.CreatorId IN @userIds
                          AND  q.QuestionTypeId IN @typeIds
                        GROUP BY ra.QuestionId, q.CreatorId, q.QuestionTypeId,
                                 CASE WHEN ra.SubQuestionId IS NULL THEN 0 ELSE 1 END,
                                 CASE WHEN q.QuestionTypeId = 6 THEN q.[Level] ELSE NULL END
                    )
                    SELECT  rs.CreatorId AS UserId,
                            rs.QuestionTypeId,
                            rs.Granularity,
                            rs.[Level],
                            COUNT(*)     AS Assigned,
                            SUM(rs.IsRevised) AS Produced
                    FROM    RevisionScope rs
                    GROUP BY rs.CreatorId, rs.QuestionTypeId, rs.Granularity, rs.[Level]
                    ORDER BY rs.CreatorId, rs.QuestionTypeId, rs.Granularity, rs.[Level]
                    """;
                sqlParams = new
                {
                    pid           = projectId,
                    userIds,
                    reviewStage   = revisionReviewStage,
                    revisionStage = revisionStageCode,
                    typeIds       = activeTypeIds
                };
            }
            else if (reviewerPhase is not null)
            {
                byte reviewStage = reviewerPhase.PhaseCode switch
                {
                    3 => 1, 5 => 2, 7 => 3, _ => 0
                };

                // 審題階段 Modal 明細：每位委員 × 題型 × (母/子題粒度) × (LCT 聽力測驗 難度)
                //   Granularity = ra.SubQuestionId IS NULL ? 0 : 1（master vs sub 指派）
                //   Level = q.Level （僅 LCT TypeId=6 有意義；其他題型在 BuildDisplayLabel 端會忽略）
                sqlTypeDetails = """
                    SELECT  ra.ReviewerId AS UserId,
                            q.QuestionTypeId,
                            CAST(CASE WHEN ra.SubQuestionId IS NULL THEN 0 ELSE 1 END AS TINYINT) AS Granularity,
                            CAST(CASE WHEN q.QuestionTypeId = 6 THEN q.[Level] ELSE NULL END AS TINYINT) AS Level,
                            COUNT(*)     AS Assigned,
                            SUM(CASE WHEN ra.DecidedAt IS NOT NULL THEN 1 ELSE 0 END) AS Produced
                    FROM    dbo.MT_ReviewAssignments ra
                    JOIN    dbo.MT_Questions      q  ON q.Id = ra.QuestionId
                    WHERE   ra.ProjectId   = @pid
                      AND   ra.ReviewStage = @reviewStage
                      AND   ra.ReviewerId IN @userIds
                      AND   q.IsDeleted    = 0
                      AND   q.QuestionTypeId IN @typeIds
                    GROUP BY ra.ReviewerId, q.QuestionTypeId,
                             CASE WHEN ra.SubQuestionId IS NULL THEN 0 ELSE 1 END,
                             CASE WHEN q.QuestionTypeId = 6 THEN q.[Level] ELSE NULL END
                    ORDER BY ra.ReviewerId, q.QuestionTypeId,
                             CASE WHEN ra.SubQuestionId IS NULL THEN 0 ELSE 1 END,
                             CASE WHEN q.QuestionTypeId = 6 THEN q.[Level] ELSE NULL END
                    """;
                sqlParams = new { pid = projectId, userIds, reviewStage, typeIds = activeTypeIds };
            }
            else
            {
                // CWT/LCT 雙模式：命題配額明細
                //   每行帶 Granularity（CWT 母/子題拆分） + Level（LCT 聽力測驗 1~5 拆分）
                //   Produced 依 Granularity 走不同統計來源：
                //     - Granularity=0：MT_Questions 母題層計（LCT 聽力測驗加 Level 過濾）
                //     - Granularity=1：MT_SubQuestions 子題層計（僅 CWT 閱讀/短文題組）
                //   排序改為 QuestionTypeId → Granularity → Level（穩定的視覺順序，避免母/子混排）
                sqlTypeDetails = """
                    SELECT  pm.UserId,
                            mq.QuestionTypeId,
                            mq.Granularity,
                            mq.Level,
                            mq.QuotaCount AS Assigned,
                            CASE WHEN mq.Granularity = 0 THEN
                                ISNULL((
                                    SELECT COUNT(*)
                                    FROM   dbo.MT_Questions q
                                    WHERE  q.ProjectId      = pm.ProjectId
                                      AND  q.QuestionTypeId = mq.QuestionTypeId
                                      AND  q.CreatorId      = pm.UserId
                                      AND  q.IsDeleted      = 0
                                      AND  q.Status NOT IN (0, 10, 11)
                                      AND  (mq.Level IS NULL OR q.[Level] = mq.Level)
                                ), 0)
                            ELSE
                                ISNULL((
                                    SELECT COUNT(*)
                                    FROM   dbo.MT_SubQuestions sq
                                    JOIN   dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                                    WHERE  q.ProjectId      = pm.ProjectId
                                      AND  q.QuestionTypeId = mq.QuestionTypeId
                                      AND  q.CreatorId      = pm.UserId
                                      AND  q.IsDeleted      = 0
                                      AND  sq.IsDeleted     = 0
                                      AND  sq.Status NOT IN (0, 10, 11)
                                ), 0)
                            END AS Produced
                    FROM    dbo.MT_ProjectMembers pm
                    JOIN    dbo.MT_MemberQuotas   mq ON mq.ProjectMemberId = pm.Id
                    WHERE   pm.ProjectId = @pid AND pm.UserId IN @userIds
                      AND   mq.QuestionTypeId IN @typeIds
                    ORDER   BY pm.UserId, mq.QuestionTypeId, mq.Granularity, mq.Level
                    """;
                sqlParams = new { pid = projectId, userIds, typeIds = activeTypeIds };
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
                    Granularity    = r.Granularity,
                    Level          = r.Level,
                    // BuildDisplayLabel 已處理：CWT 母/子題加（母題）/（子題）後綴；
                    // LCT 聽力測驗依 Level 顯示「難度X」；LCT 聽力題組顯示「聽力題組」
                    TypeName       = BuildDisplayLabel(
                                        _typeCatalog.GetName(r.QuestionTypeId),
                                        r.Granularity, r.Level),
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
            // 子題 LOG（TargetType=3 + JSON 內有 SubQuestionId）：JSON 內的 questionCode
            // 已預先組成「Q-xxx-NN」完整子題碼，直接取用，跳過 nameMap（nameMap 只查到母題碼會少 -NN 後綴）
            if (log.TargetType == 3 && IsSubQuestionLog(log))
            {
                var json = log.NewValue ?? log.OldValue;
                log.TargetName = ExtractNameFromJson(log.TargetType, json) ?? "已刪除";
                continue;
            }

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

    /// <summary>判斷 AuditLog 是否為子題級紀錄（JSON 內帶 SubQuestionId / subQuestionId 欄位）。</summary>
    private static bool IsSubQuestionLog(RecentAuditLog log)
        => HasJsonField(log.NewValue, "SubQuestionId") || HasJsonField(log.OldValue, "SubQuestionId");

    /// <summary>判斷 JSON 字串內是否存在指定欄位（同時嘗試 PascalCase 與 camelCase）。</summary>
    private static bool HasJsonField(string? json, string field)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            var camel = char.ToLowerInvariant(field[0]) + field[1..];
            return doc.RootElement.TryGetProperty(field, out _)
                || doc.RootElement.TryGetProperty(camel, out _);
        }
        catch (JsonException) { return false; }
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

    /// <summary>
    /// 教師×題型明細列（展開用）。
    /// Granularity（0=母題/單題, 1=子題）+ Level（LCT 聽力測驗 1~5；其他 null）
    /// 與 TypeName 組合後由 BuildDisplayLabel 統一掛 UI 標籤。
    /// </summary>
    private sealed record TeacherTypeDetailRow(
        int UserId, int QuestionTypeId, byte Granularity, byte? Level, int Assigned, int Produced);

    private sealed class StatusCountRow
    {
        /// <summary>本梯次採用總數（CWT：master + sub；LCT：聽力測驗 master + 聽力題組整組）。SQL 端統一彙整。</summary>
        public int AdoptedCount { get; init; }
    }

    /// <summary>卡片 3 審題進度查詢結果列（母題、子題各自獨立計數）。</summary>
    private sealed class ReviewProgressRow
    {
        public int MasterReviewed { get; init; }
        public int MasterTotal    { get; init; }
        public int SubReviewed    { get; init; }
        public int SubTotal       { get; init; }
    }

    /// <summary>卡片 4 修題進度查詢結果列。</summary>
    private sealed class RevisionProgressRow
    {
        // 母題修題統計（SubQuestionId IS NULL）
        public int MasterRevised { get; init; }
        public int MasterTotal   { get; init; }
        // 子題修題統計（SubQuestionId IS NOT NULL）
        public int SubRevised    { get; init; }
        public int SubTotal      { get; init; }
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
