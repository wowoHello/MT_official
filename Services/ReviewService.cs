using System.Data;
using Dapper;
using MT.Models;

namespace MT.Services;

// ============================================================
//  審題任務（Reviews.razor）服務層
//  - 互審分配演算法本身已存在於 QuestionService.EnsureCompositionPhaseClosedAsync
//  - 本服務專注於：審題列表、Modal 開啟資料、意見儲存、決策提交
// ============================================================

public interface IReviewService
{
    // ====== Phase 3.1：讀取 ======
    /// <summary>
    /// 取得當前使用者於指定專案的所有審題分配（跨所有階段）+ 專案目前進行中的審題階段。
    /// 排序：待處理優先、其次階段新→舊、最後依編輯時間新→舊。
    /// 每筆 Item 已標記 IsHistorical（Stage != currentStage），UI 端不再做 stage 比對。
    /// </summary>
    Task<ReviewAssignmentListResult> GetMyAssignmentsAsync(int projectId, int reviewerUserId);

    /// <summary>
    /// 取得指定專案的「審核結果與歷史」清單（Adopted / Rejected / ClosedNotAdopted）。
    /// </summary>
    Task<List<ReviewHistoryItem>> GetHistoryAsync(int projectId);

    /// <summary>
    /// 開啟審題 Modal 時一次拉取的完整資料包（題目本體 + 自己的 assignment + 歷程 + 相似題）。
    /// subQuestionId：NULL=開啟母題單元、非 NULL=開啟該子題單元。
    /// MyAssignment 與 ReturnCount 都會依該單元（QuestionId + SubQuestionId）精準對應。
    /// </summary>
    Task<ReviewModalData?> GetModalDataAsync(int questionId, int? subQuestionId, int currentUserId);

    /// <summary>
    /// 取得指定題目的審題歷程（管理員監控用，不匿名）。
    /// subQuestionId = null：母題單元歷程；非 null：該子題單元歷程；
    /// 預設不再彙整全題（避免 Overview 母題/子題視角共用同一張列表造成混淆）。
    /// 內部複用 LoadHistoryAsync —— 避免在 OverviewService 重寫 union 三個來源的 SQL。
    /// </summary>
    Task<List<ReviewHistoryEntry>> GetHistoryByQuestionIdAsync(int questionId, int? subQuestionId = null);

    // ====== Phase 3.5：寫入 ======
    /// <summary>儲存審題意見草稿（不做決策、不變更 Question 狀態）。回傳是否成功。</summary>
    Task<bool> SaveCommentDraftAsync(SaveReviewCommentRequest req, int operatorUserId);

    /// <summary>
    /// 提交審題決策（含意見一併存入），同時更新 Assignment 的 Decision/ReviewStatus/DecidedAt。
    /// Phase 3.5：僅寫入 Assignment 與 AuditLog；題目狀態流轉與總召退回計數由 Plan_006 處理。
    /// </summary>
    Task<bool> SubmitDecisionAsync(SubmitReviewDecisionRequest req, int operatorUserId);

    /// <summary>
    /// 總召代修題並做最終決策（ReturnCount >= 3 解鎖後，Plan_021 實作）。
    /// 單一 transaction：UPDATE 題目所有欄位 → UPDATE Status(9/10) → UPDATE Assignment → 2 筆 AuditLog。
    /// 只允許 Decision = Approve(採用) 或 Reject(不採用)；禁止 Revise（改後採用）。
    /// </summary>
    Task<bool> FinalReviewerEditAndDecideAsync(FinalReviewerEditRequest req, int operatorUserId);
}

public class ReviewService(IDatabaseService db, IQuestionService questionSvc) : IReviewService
{
    private readonly IDatabaseService _db = db;
    private readonly IQuestionService _questionSvc = questionSvc;

    // ====================================================================
    //  系統自動 Audit Reason 清單（單一資料來源）
    // ====================================================================
    // 凡是以 UserId=NULL 由系統批次寫入 MT_AuditLogs 的事件，其 NewValue.Reason
    // 都會落在這份清單裡。這類事件在語意上不屬於「使用者操作軌跡」，
    // 不應出現在審題 Modal 的 timeline，也不該被當作命題老師的「最後編輯時間」候選。
    //
    // 為什麼要排除：
    //  1) 階段批次升級 Status（PhaseTransitionCoordinator 觸發）會把所有題目的
    //     Status 由 A→B 寫成一筆 audit；若顯示在歷程上會誤導為「題目被某人修改」。
    //  2) 互審/專審/總審的自動分配（EnsureCompositionPhaseClosedAsync /
    //     AssignExpertReviewers / AssignFinalReviewers）也會以 UserId=NULL 寫 audit；
    //     在歷程裡顯示「ExpertReviewAssigned」對使用者沒有意義。
    //  3) 各種 Pool 警告（ExpertReviewerPoolEmpty 等）TargetType 是 Projects，
    //     理論上不會被 Question 歷程查詢撈到；放進清單只是雙保險。
    //
    // 注意：以下兩個 Reason 屬於使用者親自編輯，必須保留在歷程／最後編輯時間：
    //  - "Revision"             —— 老師修題（QuestionService.SaveRevisionAsync）
    //  - "FinalEditingResubmit" —— 老師修題後送出再審（UserId 為老師本人）
    private static readonly string[] SystemAutoAuditReasons =
    [
        "CompositionPhaseEnded",        // 命題階段結束、自動分配互審 reviewer
        "PeerReviewingPhaseStart",      // 進入交互審題階段、批次升級 Status（Plan_013 §3.3 新增）
        "PeerEditingPhaseStart",        // 進入互審修題階段、批次升級 Status
        "ExpertReviewingPhaseStart",    // 進入專家審題階段、批次升級 Status
        "ExpertEditingPhaseStart",      // 進入專審修題階段、批次升級 Status
        "FinalReviewingPhaseStart",     // 進入總召審題階段、批次升級 Status
        "FinalEditingPhaseStart",       // 進入總召修題階段、批次升級 Status
        "ExpertReviewAssigned",         // 自動分配專審 reviewer（先前遺漏）
        "FinalReviewAssigned",          // 自動分配總審 reviewer（先前遺漏，且須迴避 stage2 已審過的人）
        "ExpertReviewerPoolEmpty",      // 警告：專審池為空（TargetType=Projects，雙保險納入）
        "FinalReviewerPoolEmpty",       // 警告：總審池為空（TargetType=Projects，雙保險納入）
        "FinalReviewerPoolUnderQuota",  // 警告：總審池不足（TargetType=Projects，雙保險納入）
    ];

    // ====================================================================
    //  Phase 3.1：讀取
    // ====================================================================

    public async Task<ReviewAssignmentListResult> GetMyAssignmentsAsync(int projectId, int reviewerUserId)
    {
        // 不再依 ReviewStage 篩選 — 顯示該使用者於本專案的全部分配
        // 同 query 撈當前 phase；前端 IsHistorical 由本 service 計算後填入，避免 UI 重做判斷
        // ROW_NUMBER CTE：同一審題單元（QuestionId, SubQuestionId, Stage）可能有多筆
        // PARTITION BY (QuestionId, SubQuestionId, ReviewStage) ORDER BY Id DESC → 取最新一筆去重
        // NULL-safe：SQL Server 的 PARTITION BY 將 NULL 視為相等，故母題列（SubQuestionId NULL）會正確分組
        //
        // 列展開：每個審題單元獨立一列。
        //   - 非題組題：N=1（僅母題列，SubQuestionId 為 NULL）
        //   - 題組題（QuestionTypeId IN 3,5,7）：1 母題 + M 子題；每列來自一筆 MT_ReviewAssignments
        //
        // SubSortOrder：JOIN MT_SubQuestions 取得；母題列為 NULL，子題列為實際排序碼。
        // ReturnCount：僅 Final Stage 有紀錄；NULL-safe join 比對 SubQuestionId（含 NULL=NULL）。
        const string sql = """
            WITH ranked AS (
                SELECT
                    ra.Id              AS AssignmentId,
                    ra.QuestionId,
                    ra.SubQuestionId,
                    ra.ReviewStage     AS Stage,
                    ra.Decision,
                    ra.ReviewStatus    AS Status,
                    ISNULL(ra.DecidedAt, ra.CreatedAt) AS LastEditedAt,
                    ROW_NUMBER() OVER (
                        PARTITION BY ra.QuestionId, ra.SubQuestionId, ra.ReviewStage
                        ORDER BY ra.Id DESC
                    ) AS rn
                FROM dbo.MT_ReviewAssignments ra
                INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
                LEFT JOIN dbo.MT_SubQuestions sq ON sq.Id = ra.SubQuestionId
                WHERE ra.ProjectId  = @ProjectId
                  AND ra.ReviewerId = @ReviewerId
                  AND q.IsDeleted   = 0
                  -- Stage B-4：每單元獨立判定最終態，避免「母題採用後連坐子題從 UI 消失」邏輯級聯。
                  -- 母題列（SubQuestionId IS NULL）依母題 q.Status；子題列依子題 sq.Status。
                  -- 最終態 9/10/11/12 由 GetHistoryAsync 接手呈現於「審核結果與歷史」Tab。
                  AND (
                        (ra.SubQuestionId IS NULL     AND q.Status  NOT IN (9, 10, 11, 12))
                     OR (ra.SubQuestionId IS NOT NULL AND sq.Status NOT IN (9, 10, 11, 12))
                  )
            )
            SELECT
                r.AssignmentId,
                r.QuestionId,
                q.QuestionCode,
                r.SubQuestionId,
                sq.SortOrder          AS SubSortOrder,
                q.QuestionTypeId      AS TypeId,
                q.Level,
                q.Difficulty,
                sq.FixedDifficulty    AS FixedDifficulty,    -- 聽力題組子題固定難度（母題列為 NULL）
                CASE
                    WHEN q.QuestionTypeId IN (3, 5, 7) THEN q.ArticleContent
                    WHEN q.QuestionTypeId = 4          THEN COALESCE(NULLIF(q.Stem, ''), q.ArticleContent)
                    ELSE q.Stem
                END AS SummaryHtml,
                r.Stage,
                r.Decision,
                r.Status,
                r.LastEditedAt,
                ISNULL(rc.ReturnCount, 0)      AS ReturnCount,
                ISNULL(rc.CanEditByReviewer, 0) AS CanEditByReviewer
            FROM ranked r
            INNER JOIN dbo.MT_Questions q ON q.Id = r.QuestionId
            LEFT JOIN dbo.MT_SubQuestions sq ON sq.Id = r.SubQuestionId
            -- 僅 Final Stage(3) 才有退回計數紀錄，其他 Stage 為 NULL → ISNULL 填 0
            -- ISNULL(.., -1) 做 NULL-safe 比對：母題對母題、子題對自己的紀錄
            LEFT JOIN dbo.MT_ReviewReturnCounts rc
                ON rc.QuestionId = r.QuestionId
               AND ISNULL(rc.SubQuestionId, -1) = ISNULL(r.SubQuestionId, -1)
               AND r.Stage = 3   -- Final Stage only
            WHERE r.rn = 1
            ORDER BY
                CASE WHEN r.Status = 2 THEN 1 ELSE 0 END,  -- 待處理(0/1) 優先
                r.Stage DESC,                               -- 較新階段在上方
                q.Id ASC,                                   -- 同階段依題目 ID 升冪（與 CwtList 統一）
                ISNULL(sq.SortOrder, 0) ASC;                -- 母題（SortOrder NULL→0）在前，子題依序排列
            """;

        // 當前進行中的審題階段（僅 PhaseCode 3/5/7 對應 Mutual/Expert/Final；其餘為 null）
        const string phaseSql = """
            SELECT TOP 1 PhaseCode FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId
              AND PhaseCode > 1
              AND CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
            ORDER BY SortOrder;
            """;

        // 不能 Task.WhenAll —— 同一個 connection 並行 query 需要 MultipleActiveResultSets=True，
        // 我們的連線字串沒開（也不該開，MARS 有效能 / 鎖定副作用）。改順序執行
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<AssignmentListRow>(sql, new { ProjectId = projectId, ReviewerId = reviewerUserId });
        var phaseCode = await conn.ExecuteScalarAsync<byte?>(phaseSql, new { ProjectId = projectId });

        var currentStage = phaseCode switch
        {
            3 => (ReviewStage?)ReviewStage.Mutual,
            5 => ReviewStage.Expert,
            7 => ReviewStage.Final,
            _ => null
        };

        var items = rows.Select(r =>
        {
            var stage = (ReviewStage)r.Stage;
            return new ReviewListItem
            {
                AssignmentId         = r.AssignmentId,
                QuestionId           = r.QuestionId,
                QuestionCode         = r.QuestionCode,
                SubQuestionId        = r.SubQuestionId,
                SubSortOrder         = r.SubSortOrder,
                TypeKey              = QuestionConstants.TypeIdToKey.GetValueOrDefault(r.TypeId, ""),
                Level                = r.Level,
                Difficulty           = r.Difficulty,
                FixedDifficulty      = r.FixedDifficulty,
                SummaryText          = StripHtml(r.SummaryHtml),
                Stage                = stage,
                Decision             = r.Decision is null ? null : (ReviewDecision)r.Decision.Value,
                Status               = (ReviewTaskStatus)r.Status,
                LastEditedAt         = r.LastEditedAt,
                FinalReturnCount     = r.ReturnCount,
                CanFinalReviewerEdit = r.CanEditByReviewer,
                // PhaseCode-based IsHistorical：phaseCode=8（總修）時 Stage=Final 應視為 active（非 historical）
                // 舊邏輯 currentStage=null 強制 true，導致 PhaseCode=8 的新 Pending 行被誤標為歷史
                IsHistorical = phaseCode switch
                {
                    3 => stage != ReviewStage.Mutual,   // 互審：Mutual 是 active，其餘為歷史
                    4 => stage != ReviewStage.Mutual,   // 互修：Mutual 行是 active（命題老師查看意見）
                    5 => stage != ReviewStage.Expert,   // 專審：Expert 是 active
                    6 => stage != ReviewStage.Expert,   // 專修：Expert 行是 active
                    7 => stage != ReviewStage.Final,    // 總審：Final 是 active
                    8 => stage != ReviewStage.Final,    // 總修：Final 行是 active（總召看 Pending / 命題老師看意見）
                    _ => false                          // 其他（命題階段等）：保守顯示全部
                }
            };
        }).ToList();

        return new ReviewAssignmentListResult(items, currentStage);
    }

    public async Task<List<ReviewHistoryItem>> GetHistoryAsync(int projectId)
    {
        // Stage B-4：母題與子題各為獨立決策單元 — 歷史 Tab 同樣需要兩段呈現。
        //   段 A：母題本身進入最終態
        //   段 B：子題自己進最終態、但母題尚未進最終態（否則段 A 已涵蓋整題的歷史視角）
        // 摘要 fallback 與 GetMyAssignmentsAsync / QuestionService.ListAsync 統一規則：
        //   3/5/7 題組母題 → ArticleContent；4 長文 → Stem 優先 fallback ArticleContent；其餘 → Stem
        //   子題列一律用 sq.Stem（子題本身的題幹）。
        // FinalDecidedAt：子題優先用 sq.DecidedAt（決策當下寫入），結案批次降為 11 時可能為 NULL → fallback q.UpdatedAt
        // 包外層 SELECT 是為了讓 UNION ALL 後的 ORDER BY 能引用 alias（SQL Server 對 UNION 後 ORDER BY 的 alias 識別有限制）
        const string sql = """
            SELECT *
            FROM (
                SELECT
                    q.Id                  AS QuestionId,
                    q.QuestionCode,
                    CAST(NULL AS INT)     AS SubQuestionId,
                    CAST(NULL AS INT)     AS SubSortOrder,
                    q.QuestionTypeId      AS TypeId,
                    q.Level,
                    q.Difficulty,
                    CAST(NULL AS TINYINT) AS FixedDifficulty,
                    CASE
                        WHEN q.QuestionTypeId IN (3, 5, 7) THEN q.ArticleContent
                        WHEN q.QuestionTypeId = 4          THEN COALESCE(NULLIF(q.Stem, ''), q.ArticleContent)
                        ELSE q.Stem
                    END                   AS SummaryHtml,
                    q.Status              AS FinalStatus,
                    q.UpdatedAt           AS FinalDecidedAt
                FROM dbo.MT_Questions q
                WHERE q.ProjectId = @ProjectId
                  AND q.IsDeleted = 0
                  AND q.Status IN @Statuses

                UNION ALL

                SELECT
                    q.Id                                 AS QuestionId,
                    q.QuestionCode,
                    sq.Id                                AS SubQuestionId,
                    sq.SortOrder                         AS SubSortOrder,
                    q.QuestionTypeId                     AS TypeId,
                    q.Level,
                    q.Difficulty,
                    sq.FixedDifficulty                   AS FixedDifficulty,
                    sq.Stem                              AS SummaryHtml,
                    sq.Status                            AS FinalStatus,
                    ISNULL(sq.DecidedAt, q.UpdatedAt)    AS FinalDecidedAt
                FROM dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                WHERE q.ProjectId = @ProjectId
                  AND q.IsDeleted = 0
                  AND q.QuestionTypeId IN (3, 5, 7)     -- 僅題組類有子題
                  AND sq.Status IN @Statuses             -- 子題已進最終態
                  -- 不檢查 q.Status：母題列 (SubQuestionId=NULL) 與子題列 (SubQuestionId=N) PK 組合不同，
                  -- 不會重複；Stage B-4「每單元獨立呈現」要求 — 母題採用後，已決策的子題仍須出現
            ) t
            ORDER BY t.QuestionId ASC, ISNULL(t.SubSortOrder, 0) ASC;  -- 母題（SortOrder NULL→0）在前，子題依序排列
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<HistoryListRow>(sql, new
        {
            ProjectId = projectId,
            Statuses  = QuestionStatus.HistoryTabStatuses.Select(b => (int)b).ToList()
        });

        return rows.Select(r => new ReviewHistoryItem
        {
            QuestionId      = r.QuestionId,
            QuestionCode    = r.QuestionCode,
            SubQuestionId   = r.SubQuestionId,
            SubSortOrder    = r.SubSortOrder,
            TypeKey         = QuestionConstants.TypeIdToKey.GetValueOrDefault(r.TypeId, ""),
            Level           = r.Level,
            Difficulty      = r.Difficulty,
            FixedDifficulty = r.FixedDifficulty,
            SummaryText     = StripHtml(r.SummaryHtml),
            FinalStatus     = r.FinalStatus,
            FinalDecidedAt  = r.FinalDecidedAt
        }).ToList();
    }

    public async Task<List<ReviewHistoryEntry>> GetHistoryByQuestionIdAsync(int questionId, int? subQuestionId = null)
    {
        // Overview 單元視角：母題單元（subQuestionId=null）僅看母題歷程、子題單元僅看該子題歷程
        // aggregateAllUnits = false → LoadHistoryAsync 以 ISNULL(SubQuestionId,-1) = ISNULL(@SubQuestionId,-1) 精準過濾
        using var conn = _db.CreateConnection();
        return await LoadHistoryAsync(conn, questionId, subQuestionId, aggregateAllUnits: false);
    }

    public async Task<ReviewModalData?> GetModalDataAsync(int questionId, int? subQuestionId, int currentUserId)
    {
        // 1. 題目本體（複用 QuestionService.GetByIdAsync，避免兩處維護七題型載入邏輯）
        //    GetByIdAsync 自帶獨立連線，內部 2-4 round-trip（master / image / sub / subImg）
        var question = await _questionSvc.GetByIdAsync(questionId);
        if (question is null) return null;

        using var conn = _db.CreateConnection();

        // 2-7. 一次 QueryMultipleAsync 撈完 Modal 所需的 7 個 result set（原本 7 次 round-trip → 1 次）
        //   #1 lastEdit：AuditLogs 最後真實編輯時間（過濾系統批次事件）
        //   #2 creator：命題者 DisplayName
        //   #3 myAssignment：當前使用者於此單元的 Assignment
        //   #4 history：審題意見 + 修題說明 UNION ALL（精準模式，無 UnitInfo CTE）
        //   #5 similar：相似題比對
        //   #6 returnCount：總召退回次數
        //   #7 siblings：兄弟子題單元（Stage 由 subquery 自動推導，無 Assignment 時自然回 0 列）
        //
        // 注意：LoadHistoryAsync / LoadSimilaritiesAsync 兩個 helper 保留供 OverviewService（彙整模式）使用，
        //       本路徑直接 inline 精準模式 SQL 以利合併 QueryMultiple。
        const string megaSql = """
            -- #1 lastEdit
            SELECT TOP 1 CreatedAt
            FROM dbo.MT_AuditLogs
            WHERE TargetType  = 3
              AND TargetId    = @QuestionId
              AND UserId      IS NOT NULL
              AND Action      IN (0, 1)
              AND (
                  NewValue IS NULL
                  OR JSON_VALUE(NewValue, '$.Reason') IS NULL
                  OR JSON_VALUE(NewValue, '$.Reason') = 'Revision'
              )
              AND (
                  JSON_VALUE(NewValue, '$.Reason') IS NULL
                  OR JSON_VALUE(NewValue, '$.Reason') NOT IN @SystemAutoReasons
              )
            ORDER BY CreatedAt DESC;

            -- #2 creator
            SELECT ISNULL(u.DisplayName, '')
            FROM dbo.MT_Questions q
            LEFT JOIN dbo.MT_Users u ON u.Id = q.CreatorId
            WHERE q.Id = @QuestionId;

            -- #3 myAssignment
            SELECT TOP 1
                Id, QuestionId, SubQuestionId, ReviewStage AS Stage, ReviewStatus AS Status,
                Decision, ISNULL(Comment, '') AS Comment, DecidedAt, CreatedAt
            FROM dbo.MT_ReviewAssignments
            WHERE QuestionId = @QuestionId
              AND ISNULL(SubQuestionId, -1) = ISNULL(@SubQuestionId, -1)
              AND ReviewerId = @ReviewerId
            ORDER BY ReviewStage DESC, Id DESC;

            -- #4 history union（精準模式，無 UnitInfo CTE）
            SELECT CAST(1 AS TINYINT) AS Kind, ra.ReviewStage AS Stage, ra.Decision,
                   ra.Comment AS Content,
                   ISNULL(ra.DecidedAt, ra.CreatedAt) AS At,
                   ISNULL(u.DisplayName, '') AS ActorName,
                   ra.SubQuestionId,
                   CAST(NULL AS INT) AS SortOrder,
                   CAST(NULL AS NVARCHAR(50)) AS ParentCode
            FROM dbo.MT_ReviewAssignments ra
            LEFT JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
            WHERE ra.QuestionId = @QuestionId
              AND ra.DecidedAt IS NOT NULL  -- 依 DecidedAt 判定：草稿尚未送出不顯示
              AND ISNULL(ra.SubQuestionId, -1) = ISNULL(@SubQuestionId, -1)

            UNION ALL

            SELECT CAST(2 AS TINYINT) AS Kind, rr.Stage, CAST(NULL AS TINYINT) AS Decision,
                   rr.Content,
                   rr.CreatedAt AS At,
                   ISNULL(u.DisplayName, '') AS ActorName,
                   rr.SubQuestionId,
                   CAST(NULL AS INT) AS SortOrder,
                   CAST(NULL AS NVARCHAR(50)) AS ParentCode
            FROM dbo.MT_RevisionReplies rr
            LEFT JOIN dbo.MT_Users u ON u.Id = rr.UserId
            WHERE rr.QuestionId = @QuestionId
              AND ISNULL(rr.SubQuestionId, -1) = ISNULL(@SubQuestionId, -1)

            ORDER BY At DESC;

            -- #5 similar
            SELECT
                sc.ComparedQuestionId,
                q.QuestionCode AS ComparedQuestionCode,
                sc.SimilarityScore,
                sc.Determination,
                CASE
                    WHEN q.QuestionTypeId IN (3, 5, 7) THEN q.ArticleContent
                    WHEN q.QuestionTypeId = 4          THEN COALESCE(NULLIF(q.Stem, ''), q.ArticleContent)
                    ELSE q.Stem
                END AS SummaryHtml
            FROM dbo.MT_SimilarityChecks sc
            INNER JOIN dbo.MT_Questions q ON q.Id = sc.ComparedQuestionId
            WHERE sc.SourceQuestionId = @QuestionId
              AND q.IsDeleted = 0
            ORDER BY sc.SimilarityScore DESC;

            -- #6 returnCount
            SELECT TOP 1 ReturnCount, CanEditByReviewer
            FROM dbo.MT_ReviewReturnCounts
            WHERE QuestionId = @QuestionId
              AND ISNULL(SubQuestionId, -1) = ISNULL(@SubQuestionId, -1)
            ORDER BY Id DESC;

            -- #7 siblings（Stage 由 subquery 推導：無 Assignment 時 subquery 回空 → ranked 為空 → 結果 0 列）
            WITH ranked AS (
                SELECT
                    ra.Id              AS AssignmentId,
                    ra.SubQuestionId,
                    ra.ReviewStage     AS Stage,
                    ra.Decision,
                    ra.ReviewStatus    AS Status,
                    ROW_NUMBER() OVER (
                        PARTITION BY ra.SubQuestionId
                        ORDER BY ra.Id DESC
                    ) AS rn
                FROM dbo.MT_ReviewAssignments ra
                WHERE ra.QuestionId  = @QuestionId
                  AND ra.ReviewerId  = @ReviewerId
                  AND ra.ReviewStage = (
                      SELECT TOP 1 ReviewStage
                      FROM dbo.MT_ReviewAssignments
                      WHERE QuestionId = @QuestionId
                        AND ISNULL(SubQuestionId, -1) = ISNULL(@SubQuestionId, -1)
                        AND ReviewerId = @ReviewerId
                      ORDER BY ReviewStage DESC, Id DESC
                  )
            )
            SELECT r.AssignmentId, r.SubQuestionId, sq.SortOrder, r.Stage, r.Decision, r.Status
            FROM ranked r
            LEFT JOIN dbo.MT_SubQuestions sq ON sq.Id = r.SubQuestionId
            WHERE r.rn = 1
            ORDER BY ISNULL(sq.SortOrder, 0);
            """;

        using var multi = await conn.QueryMultipleAsync(megaSql, new
        {
            QuestionId        = questionId,
            SubQuestionId     = subQuestionId,
            ReviewerId        = currentUserId,
            SystemAutoReasons = SystemAutoAuditReasons
        });

        var lastTeacherEditAt = await multi.ReadFirstOrDefaultAsync<DateTime?>();
        var creatorName       = (await multi.ReadFirstOrDefaultAsync<string>()) ?? "";
        var myAssignment      = await multi.ReadFirstOrDefaultAsync<AssignmentDto>();
        var historyRows       = (await multi.ReadAsync<HistoryUnionRow>()).ToList();
        var similarRows       = (await multi.ReadAsync<SimilarityRow>()).ToList();
        var returnInfo        = await multi.ReadFirstOrDefaultAsync<ReturnCountDto>();
        var siblingRows       = (await multi.ReadAsync<SiblingUnitRow>()).ToList();

        // 用 AuditLogs 的真實命題老師編輯時間覆蓋 question.UpdatedAt
        // 若 AuditLogs 無紀錄（極舊資料），fallback 為 question.CreatedAt（建立時間永遠正確）
        question.UpdatedAt = lastTeacherEditAt ?? question.CreatedAt;

        // history rows → ReviewHistoryEntry（與 LoadHistoryAsync 內部處理邏輯一致）
        var history = new List<ReviewHistoryEntry>();
        foreach (var row in historyRows)
        {
            var entry = new ReviewHistoryEntry
            {
                At        = row.At,
                ActorName = row.ActorName,
            };
            if (row.Kind == 1)
            {
                entry.Kind        = ReviewHistoryKind.ReviewComment;
                entry.Label       = StageToLabel((ReviewStage)row.Stage, row.Decision);
                entry.ContentHtml = StripHtmlFull(row.Content);
            }
            else
            {
                entry.Kind        = ReviewHistoryKind.RevisionReply;
                entry.Label       = StageToRevisionLabel(row.Stage);
                entry.ContentHtml = row.Content;
            }
            // 精準模式：UnitDisplayCode 不需要組（ParentCode 為 NULL）
            history.Add(entry);
        }
        ApplyFinalReturnSequence(history);

        // similar rows → ReviewSimilarityEntry
        var similar = similarRows.Select(r => new ReviewSimilarityEntry
        {
            ComparedQuestionId   = r.ComparedQuestionId,
            ComparedQuestionCode = r.ComparedQuestionCode,
            SimilarityScore      = r.SimilarityScore,
            Determination        = r.Determination,
            SummaryText          = StripHtml(r.SummaryHtml)
        }).ToList();

        // sibling rows → ReviewSiblingUnit（無 Assignment 時 siblingRows 自然為空）
        var siblings = siblingRows.Select(r => new ReviewSiblingUnit
        {
            AssignmentId  = r.AssignmentId,
            SubQuestionId = r.SubQuestionId,
            SortOrder     = r.SortOrder,
            Stage         = (ReviewStage)r.Stage,
            Status        = (ReviewTaskStatus)r.Status,
            Decision      = r.Decision is null ? null : (ReviewDecision)r.Decision.Value
        }).ToList();

        return new ReviewModalData
        {
            Question     = question,
            CreatorName  = creatorName,
            MyAssignment = myAssignment is null ? null : new ReviewAssignmentInfo
            {
                Id            = myAssignment.Id,
                QuestionId    = myAssignment.QuestionId,
                SubQuestionId = myAssignment.SubQuestionId,
                Stage         = (ReviewStage)myAssignment.Stage,
                Status        = (ReviewTaskStatus)myAssignment.Status,
                Decision      = myAssignment.Decision is null ? null : (ReviewDecision)myAssignment.Decision.Value,
                // 舊資料可能殘留 Quill HTML 標籤（<u> / <br> / <p> 等），textarea 不渲染 HTML
                // 會把標籤直接當成字面顯示出現怪線條 — 載入時統一 strip 為純文字
                Comment       = StripHtmlFull(myAssignment.Comment),
                DecidedAt     = myAssignment.DecidedAt,
                CreatedAt     = myAssignment.CreatedAt
            },
            History              = history,
            SimilarQuestions     = similar,
            FinalReturnCount     = returnInfo?.ReturnCount ?? 0,
            CanFinalReviewerEdit = returnInfo?.CanEditByReviewer ?? false,
            SiblingUnits         = siblings
        };
    }

    // ====================================================================
    //  Phase 3.5：寫入
    // ====================================================================

    public async Task<bool> SaveCommentDraftAsync(SaveReviewCommentRequest req, int operatorUserId)
    {
        using var conn = _db.CreateConnection();

        // 取出 Stage / ProjectId / QuestionId / DecidedAt（區分首次完成 vs 修改既有意見）
        // QuestionCode 寫入 AuditLog.targetDisplayName，作 Assignment 被刪後的 fallback 顯示
        const string fetchSql = """
            SELECT ra.ReviewStage AS Stage, ra.ProjectId, ra.QuestionId, ra.DecidedAt, q.QuestionCode
            FROM dbo.MT_ReviewAssignments ra
            INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
            WHERE ra.Id = @AssignmentId
              AND ra.ReviewerId = @ReviewerId
              AND ra.Decision IS NULL;
            """;
        var meta = await conn.QueryFirstOrDefaultAsync<AssignmentMetaForSave>(fetchSql, new
        {
            req.AssignmentId,
            ReviewerId = operatorUserId
        });
        if (meta is null) return false;   // 不是分配給此使用者，或已決策

        // 互審階段：意見儲存 = 完成審題（無採用/退回決策按鈕，儲存即完成）
        // 專/總審階段：純草稿，ReviewStatus 不變、不寫 DecidedAt、不寫 audit
        var isMutual    = meta.Stage == (byte)ReviewStage.Mutual;
        var wasDecided  = meta.DecidedAt.HasValue;   // true = 修改既有意見，false = 首次完成

        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var updateSql = isMutual
                ? """
                    UPDATE dbo.MT_ReviewAssignments
                    SET Comment      = @Comment,
                        ReviewStatus = @Status,
                        DecidedAt    = SYSDATETIME()
                    WHERE Id = @AssignmentId
                      AND ReviewerId = @ReviewerId
                      AND Decision IS NULL;
                    """
                : """
                    UPDATE dbo.MT_ReviewAssignments
                    SET Comment = @Comment
                    WHERE Id = @AssignmentId
                      AND ReviewerId = @ReviewerId
                      AND Decision IS NULL;
                    """;

            var affected = await conn.ExecuteAsync(updateSql, new
            {
                req.AssignmentId,
                ReviewerId = operatorUserId,
                Comment    = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment,
                Status     = (byte)ReviewTaskStatus.Completed
            }, tx);

            if (affected == 0)
            {
                tx.Rollback();
                return false;
            }

            // 互審完成寫稽核：首次=Create、修改既有=Modify；TargetType=Reviews(6)，TargetId=AssignmentId
            if (isMutual)
            {
                const string auditSql = """
                    INSERT INTO dbo.MT_AuditLogs
                        (UserId, ProjectId, Action, TargetType, TargetId, NewValue, CreatedAt)
                    VALUES
                        (@UserId, @ProjectId, @Action, @TargetType, @TargetId, @NewValue, SYSDATETIME());
                    """;
                // B：JSON 補 targetDisplayName 作 fallback（Assignment 被刪時 UI 仍能顯示題號）
                await conn.ExecuteAsync(auditSql, new
                {
                    UserId     = operatorUserId,
                    ProjectId  = meta.ProjectId,
                    Action     = wasDecided ? AuditLogAction.Modify : AuditLogAction.Create,
                    TargetType = AuditLogTargetType.Reviews,
                    TargetId   = req.AssignmentId,
                    NewValue   = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        targetDisplayName = meta.QuestionCode,
                        Stage             = meta.Stage,
                        QuestionId        = meta.QuestionId,
                        ReviewStatus      = (byte)ReviewTaskStatus.Completed
                    })
                }, tx);
            }

            tx.Commit();
            return true;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private sealed class AssignmentMetaForSave
    {
        public byte Stage { get; set; }
        public int ProjectId { get; set; }
        public int QuestionId { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string QuestionCode { get; set; } = string.Empty;
    }

    public async Task<bool> SubmitDecisionAsync(SubmitReviewDecisionRequest req, int operatorUserId)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            // 1. 取出 Assignment 元資訊（驗證權限、防重複決策、取 ProjectId 寫稽核）
            //    SubQuestionId 用於判定本列是母題（NULL）還是子題（決定是否觸發級聯）
            //    QuestionCode 寫入 AuditLog.targetDisplayName，作 Assignment 被刪後的 fallback 顯示
            const string fetchSql = """
                SELECT ra.Id, ra.ProjectId, ra.QuestionId, ra.SubQuestionId,
                       ra.ReviewStage AS Stage, ra.Decision, q.QuestionCode
                FROM dbo.MT_ReviewAssignments ra
                INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
                WHERE ra.Id = @AssignmentId AND ra.ReviewerId = @ReviewerId;
                """;
            var meta = await conn.QueryFirstOrDefaultAsync<AssignmentMetaDto>(fetchSql, new
            {
                req.AssignmentId,
                ReviewerId = operatorUserId
            }, tx);

            if (meta is null)
            {
                tx.Rollback();
                return false;   // 該分配不存在 / 不是分配給此使用者
            }

            // 同階段內允許重複決策（與互審「儲存意見可再修改」概念一致）—
            // 下一階段（修題）開始時 IsHistorical 會在 UI 端鎖定，不會走到這裡
            var isResubmit = meta.Decision is not null;

            // 2. 規則檢查：互審不可下決策（無採用/退回按鈕，本不應走到此 method）
            if (meta.Stage == (byte)ReviewStage.Mutual)
            {
                throw new InvalidOperationException("互審階段僅能儲存意見，不可下決策。");
            }

            // 3. 規則檢查：專審不可選「不採用」
            if (meta.Stage == (byte)ReviewStage.Expert && req.Decision == ReviewDecision.Reject)
            {
                throw new InvalidOperationException("專審階段不可選擇「不採用」。");
            }

            // 4. 寫入 Assignment（Comment + Decision + ReviewStatus=2 已完成 + DecidedAt）
            const string updateSql = """
                UPDATE dbo.MT_ReviewAssignments
                SET Comment      = @Comment,
                    Decision     = @Decision,
                    ReviewStatus = @Completed,
                    DecidedAt    = SYSDATETIME()
                WHERE Id = @AssignmentId;
                """;
            await conn.ExecuteAsync(updateSql, new
            {
                req.AssignmentId,
                Comment   = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment,
                Decision  = (byte)req.Decision,
                Completed = (byte)ReviewTaskStatus.Completed
            }, tx);

            // 5. 取目前進行中的 PhaseCode（決定首輪/次輪總審分流）
            //    與 GetCurrentPhaseCodeInTxAsync 邏輯一致：PhaseCode>1 + 今天落於 Start/EndDate 之間
            const string phaseSql = """
                SELECT TOP 1 PhaseCode FROM dbo.MT_ProjectPhases
                WHERE ProjectId = @ProjectId
                  AND PhaseCode > 1
                  AND CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
                ORDER BY SortOrder;
                """;
            var currentPhaseCode = await conn.ExecuteScalarAsync<byte?>(phaseSql, new { meta.ProjectId }, tx);

            // 6. 依「階段 + PhaseCode + 決策」計算「該單元」應切換到的新狀態（null = 不變或交由後續邏輯處理）
            //    Stage B-4：MapDecisionToQuestionStatus 的回傳值對母題與子題皆適用，差別只在「寫入哪張表」
            byte? oldUnitStatus = null;
            byte? newUnitStatus = MapDecisionToQuestionStatus(meta.Stage, req.Decision, currentPhaseCode);

            // 6-1. 第 3 輪 Reject 偵測：PhaseCode=8 + Final + Reject + 該「單元」已退回 ≥2 次 → Rejected(10)
            //      Stage B-4：退回計次按單元獨立 — 比對 (QuestionId, SubQuestionId) 兩欄
            if (newUnitStatus is null
                && currentPhaseCode == 8
                && meta.Stage == (byte)ReviewStage.Final
                && req.Decision == ReviewDecision.Reject)
            {
                var existingReturnCount = await conn.ExecuteScalarAsync<int?>(
                    """
                    SELECT TOP 1 ReturnCount
                    FROM dbo.MT_ReviewReturnCounts
                    WHERE QuestionId = @Id
                      AND ISNULL(SubQuestionId, -1) = ISNULL(@SubId, -1)
                    ORDER BY Id DESC;
                    """,
                    new { Id = meta.QuestionId, SubId = meta.SubQuestionId }, tx) ?? 0;

                if (existingReturnCount >= 2)
                {
                    newUnitStatus = QuestionStatus.Rejected;   // 10
                }
            }

            // 6-2. 寫入該單元的 Status：母題 → MT_Questions / 子題 → MT_SubQuestions
            //      Stage B-4：每單元獨立流轉，互不影響；不再做「母題 Reject → 子題級聯」（已移除）
            //      → 結案時才依母題狀態做最終處置（B-4-3 範圍）
            if (newUnitStatus is not null)
            {
                if (meta.SubQuestionId is null)
                {
                    // 母題單元 → 寫 MT_Questions.Status
                    // 審題決策僅更新 Status，不動 UpdatedAt（UpdatedAt 語意保留給命題老師實際編輯內容的時刻）
                    oldUnitStatus = await conn.ExecuteScalarAsync<byte?>(
                        "SELECT Status FROM dbo.MT_Questions WHERE Id = @Id;",
                        new { Id = meta.QuestionId }, tx);

                    await conn.ExecuteAsync(
                        "UPDATE dbo.MT_Questions SET Status = @Status WHERE Id = @Id;",
                        new { Id = meta.QuestionId, Status = newUnitStatus.Value }, tx);
                }
                else
                {
                    // 子題單元 → 寫 MT_SubQuestions.Status
                    // 狀態落定為 Adopted(9) / Rejected(10) / Archived(12) 時同時寫 DecidedAt
                    oldUnitStatus = await conn.ExecuteScalarAsync<byte?>(
                        "SELECT Status FROM dbo.MT_SubQuestions WHERE Id = @Id;",
                        new { Id = meta.SubQuestionId.Value }, tx);

                    await conn.ExecuteAsync(
                        """
                        UPDATE dbo.MT_SubQuestions
                        SET Status    = @Status,
                            DecidedAt = CASE WHEN @Status IN (9, 10, 12) THEN SYSDATETIME() ELSE DecidedAt END
                        WHERE Id = @Id;
                        """,
                        new { Id = meta.SubQuestionId.Value, Status = newUnitStatus.Value }, tx);
                }
            }

            // 7. 總召「改後再審」或「不採用」→ 累加退回計數（達 2 次解鎖總召自編）
            //    Stage B-4：按單元獨立計次 — 母題與每子題分別累計，互不影響第 3 輪解鎖判定
            //    再次決策時不重複加，避免使用者「Revise → Approve → Revise」誤計多次
            //    Reject 同樣計入：退回次數涵蓋所有「不採用→交回修改」情境
            if (!isResubmit && meta.Stage == (byte)ReviewStage.Final &&
                (req.Decision == ReviewDecision.Revise || req.Decision == ReviewDecision.Reject))
            {
                await BumpReturnCountAsync(conn, tx, meta.QuestionId, meta.SubQuestionId, operatorUserId);
            }

            // 8. 寫稽核（帶單元層級的狀態快照，方便日後追溯）
            const string auditSql = """
                INSERT INTO dbo.MT_AuditLogs
                    (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, CreatedAt)
                VALUES
                    (@UserId, @ProjectId, @Action, @TargetType, @TargetId, @OldValue, @NewValue, SYSDATETIME());
                """;
            // A：TargetId 統一為 AssignmentId（與 Dashboard 讀取端 JOIN MT_ReviewAssignments.Id 對齊）
            // B：JSON 補 targetDisplayName 作 fallback（Assignment 被刪時 UI 仍能顯示題號）
            await conn.ExecuteAsync(auditSql, new
            {
                UserId     = operatorUserId,
                ProjectId  = meta.ProjectId,
                Action     = AuditLogAction.Modify,
                TargetType = AuditLogTargetType.Reviews,
                TargetId   = meta.Id,
                OldValue   = oldUnitStatus is null ? null : System.Text.Json.JsonSerializer.Serialize(new
                {
                    targetDisplayName = meta.QuestionCode,
                    UnitStatus        = oldUnitStatus.Value,
                    SubQuestionId     = meta.SubQuestionId
                }),
                NewValue   = System.Text.Json.JsonSerializer.Serialize(new
                {
                    targetDisplayName = meta.QuestionCode,
                    Stage             = meta.Stage,
                    SubQuestionId     = meta.SubQuestionId,
                    Decision          = (byte)req.Decision,
                    UnitStatus        = newUnitStatus
                })
            }, tx);

            tx.Commit();
            return true;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ====================================================================
    //  Plan_006：題目狀態流轉 + 退回計數 helper
    // ====================================================================

    /// <summary>
    /// 依「目前 PhaseCode + 決策」計算題目應切換的新 Question.Status（僅總審階段使用）。
    /// 互審不下決策；專審維持 Buffer（PhaseCode=6 開始時由 EnsurePhaseTransitionAsync 批次升 ExpertEditing）。
    ///
    /// 總審分兩輪：
    ///   PhaseCode=7 首輪總審 (Status=FinalReviewing)：
    ///     Approve → Adopted(9) 立即鎖定（題目本梯次無需再走 PhaseCode=8 修題）
    ///     Revise/Reject → Buffer，PhaseCode=8 開始時由 EnsureFinalEditingPhaseAsync 批次降為 FinalEditing(8)
    ///   PhaseCode=8 次輪以後總審 (老師 [完成送審] 後 Status=7、由總審再決策)：
    ///     Approve → Adopted(9)（已採用，等待 CloseProjectAsync 結案才升為 Archived(12)）
    ///     Revise  → FinalEditing(8) 立即退回老師再修
    ///     Reject  → null（caller 依 ReturnCount 判定：第 3 輪 Reject = Rejected(10)；否則維持 buffer 等於 8）
    ///
    /// 集中對應邏輯 — 未來規則調整只動此一處。回傳 null 表示題目狀態不變或由 caller 自行決定。
    /// </summary>
    private static byte? MapDecisionToQuestionStatus(byte stage, ReviewDecision decision, byte? currentPhaseCode)
    {
        // 互審不在此處理；專審決策維持 Buffer
        if (stage != (byte)ReviewStage.Final) return null;

        return (currentPhaseCode, decision) switch
        {
            // 首輪總審 (PhaseCode=7)：Approve 即時鎖定為 9；Revise/Reject Buffer
            (7, ReviewDecision.Approve) => QuestionStatus.Adopted,        // 9
            (7, _)                      => null,

            // 次輪以後 (PhaseCode=8)：Approve→9（採用，結案後由 CloseProjectAsync 升至 12）；Revise→8（退回老師）
            // Reject 由 caller 依 ReturnCount 決定 8（buffer）或 10（第 3 輪終結）
            (8, ReviewDecision.Approve) => QuestionStatus.Adopted,        // 9（不寫 Archived，決策層最多到 9）
            (8, ReviewDecision.Revise)  => QuestionStatus.FinalEditing,   // 8
            (8, ReviewDecision.Reject)  => null,

            _ => null
        };
    }

    /// <summary>
    /// 總召退回計數 +1。第 1 次 INSERT，後續 UPDATE。
    /// ReturnCount 達 3 後自動設 CanEditByReviewer=true：
    ///   第 1 次退回：老師可修，再送審；ReturnCount=1
    ///   第 2 次退回：老師可修，再送審；ReturnCount=2（UI 顯示「下次解鎖」警示）
    ///   第 3 次退回：ReturnCount=3 → 解鎖總召自編（第 3 次由總召親自定奪，不再退回老師）
    /// MERGE + HOLDLOCK 確保並發安全。
    ///
    /// Stage B-4：subQuestionId 區分母題層級與子題層級各自計次。
    ///   NULL = 母題單元；非 NULL = 該子題單元（每子題各自累計 → 各自達 2 次後解鎖總召自改）
    /// MERGE 比對 (QuestionId, SubQuestionId) 用 ISNULL(-1) 處理 NULL-safe equality。
    /// </summary>
    private static async Task BumpReturnCountAsync(
        IDbConnection conn, IDbTransaction tx, int questionId, int? subQuestionId, int finalReviewerId)
    {
        const string sql = """
            MERGE dbo.MT_ReviewReturnCounts WITH (HOLDLOCK) AS target
            USING (VALUES (@QuestionId, @SubQuestionId, @FinalReviewerId))
                  AS src(QuestionId, SubQuestionId, FinalReviewerId)
                ON target.QuestionId = src.QuestionId
                   AND ISNULL(target.SubQuestionId, -1) = ISNULL(src.SubQuestionId, -1)
            WHEN MATCHED THEN
                UPDATE SET ReturnCount = target.ReturnCount + 1,
                           CanEditByReviewer = CASE WHEN target.ReturnCount + 1 >= 3 THEN 1 ELSE 0 END
            WHEN NOT MATCHED THEN
                INSERT (QuestionId, SubQuestionId, FinalReviewerId, ReturnCount, CanEditByReviewer)
                VALUES (@QuestionId, @SubQuestionId, @FinalReviewerId, 1, 0);
            """;

        await conn.ExecuteAsync(sql, new
        {
            QuestionId      = questionId,
            SubQuestionId   = subQuestionId,
            FinalReviewerId = finalReviewerId
        }, tx);
    }

    // ====================================================================
    //  Plan_021：總召代修題並最終裁決
    // ====================================================================

    public async Task<bool> FinalReviewerEditAndDecideAsync(FinalReviewerEditRequest req, int operatorUserId)
    {
        // 只允許採用或不採用；改後採用不應走此路徑
        if (req.Decision == ReviewDecision.Revise)
            throw new InvalidOperationException("代修題後只可「採用」或「不採用」，不可「改後採用」。");

        var typeId = QuestionConstants.TypeKeyToId[req.FormData.QuestionType];

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            // 1. 驗證 Assignment 屬於此使用者、Stage=Final、題目 Id 一致（防禦性雙重確認）
            //    SubQuestionId 用於判定本列是母題（NULL）還是子題單元 — Stage B-4 各單元獨立決策
            //    ReturnCount JOIN 需按單元別匹配：母題對母題列、子題對子題列（ISNULL(-1) NULL-safe）
            //    QuestionCode 寫入 AuditLog.targetDisplayName，作目標被刪後 UI fallback 顯示
            const string fetchAssignSql = """
                SELECT ra.Id, ra.ProjectId, ra.QuestionId, ra.SubQuestionId,
                       ra.ReviewStage AS Stage, rc.CanEditByReviewer, q.QuestionCode
                FROM dbo.MT_ReviewAssignments ra
                INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
                LEFT JOIN dbo.MT_ReviewReturnCounts rc
                    ON rc.QuestionId = ra.QuestionId
                   AND ISNULL(rc.SubQuestionId, -1) = ISNULL(ra.SubQuestionId, -1)
                WHERE ra.Id          = @AssignmentId
                  AND ra.ReviewerId  = @ReviewerId
                  AND ra.QuestionId  = @QuestionId
                ORDER BY rc.Id DESC;
                """;
            var assign = await conn.QueryFirstOrDefaultAsync<FinalEditAssignMeta>(fetchAssignSql, new
            {
                AssignmentId = req.AssignmentId,
                ReviewerId   = operatorUserId,
                QuestionId   = req.QuestionId
            }, tx);

            if (assign is null)
            {
                tx.Rollback();
                return false;   // 無效 Assignment 或權限不符
            }

            if (assign.Stage != (byte)ReviewStage.Final)
                throw new InvalidOperationException("只有總審階段可執行代修題操作。");

            if (!assign.CanEditByReviewer)
                throw new InvalidOperationException("此題目尚未達到總召代修解鎖條件（需退回 3 次）。");

            // 2. 讀取「該單元」舊狀態 — Stage B-4 按 SubQuestionId 路由：母題讀 MT_Questions / 子題讀 MT_SubQuestions
            byte? oldUnitStatus;
            if (assign.SubQuestionId is null)
            {
                oldUnitStatus = await conn.ExecuteScalarAsync<byte?>(
                    "SELECT Status FROM dbo.MT_Questions WHERE Id = @Id AND IsDeleted = 0;",
                    new { Id = req.QuestionId }, tx);
            }
            else
            {
                oldUnitStatus = await conn.ExecuteScalarAsync<byte?>(
                    "SELECT Status FROM dbo.MT_SubQuestions WHERE Id = @Id;",
                    new { Id = assign.SubQuestionId.Value }, tx);
            }
            if (oldUnitStatus is null)
            {
                tx.Rollback();
                return false;
            }

            // 3. 決策對應的新單元狀態
            var newUnitStatus = req.Decision == ReviewDecision.Approve
                ? QuestionStatus.Adopted    // 9
                : QuestionStatus.Rejected;  // 10

            // 4. UPDATE 題目所有欄位（不含 Status，Status 由步驟 4.5 按單元別寫入）
            //    Stage B-4-1：Modal 在 B-4-2 完成前仍會傳全部欄位，先保留全量 UPSERT 兼容；
            //    B-4-2 完成後 UI 會限制只能編當前單元，內容差異自然只發生在該單元。
            const string updateSql = """
                UPDATE dbo.MT_Questions SET
                    QuestionTypeId  = @TypeId,
                    Level           = @Level,
                    Difficulty      = @Difficulty,
                    Stem            = @Stem,
                    Analysis        = @Analysis,
                    CorrectAnswer   = @Answer,
                    OptionA         = @OptA,
                    OptionB         = @OptB,
                    OptionC         = @OptC,
                    OptionD         = @OptD,
                    ArticleTitle    = @ArticleTitle,
                    ArticleContent  = @ArticleContent,
                    AudioUrl        = @AudioUrl,
                    GradingNote     = @GradingNote,
                    Topic           = @Topic,
                    Subtopic        = @Subtopic,
                    Genre           = @Genre,
                    Material        = @Material,
                    WritingMode     = @WritingMode,
                    AudioType       = @AudioType,
                    CoreAbility     = @CoreAbility,
                    DetailIndicator = @DetailIndicator,
                    UpdatedAt       = SYSDATETIME()
                WHERE Id = @Id AND IsDeleted = 0;
                """;

            var f = req.FormData;
            var affected = await conn.ExecuteAsync(updateSql, new
            {
                Id             = req.QuestionId,
                TypeId         = typeId,
                f.Level,
                f.Difficulty,
                Stem           = NullIfEmpty(f.Stem),
                Analysis       = NullIfEmpty(f.Analysis),
                Answer         = NullIfEmpty(f.Answer),
                OptA           = SafeOption(f.Options, 0),
                OptB           = SafeOption(f.Options, 1),
                OptC           = SafeOption(f.Options, 2),
                OptD           = SafeOption(f.Options, 3),
                ArticleTitle   = NullIfEmpty(f.ArticleTitle),
                ArticleContent = NullIfEmpty(f.ArticleContent),
                AudioUrl       = NullIfEmpty(f.AudioUrl),
                GradingNote    = NullIfEmpty(f.GradingNote),
                f.Topic,
                f.Subtopic,
                f.Genre,
                f.Material,
                f.WritingMode,
                f.AudioType,
                f.CoreAbility,
                f.DetailIndicator
            }, tx);

            if (affected == 0) { tx.Rollback(); return false; }

            // 4.5 Status 寫入「該單元」對應的表 — Stage B-4：母題 → MT_Questions / 子題 → MT_SubQuestions
            if (assign.SubQuestionId is null)
            {
                await conn.ExecuteAsync(
                    "UPDATE dbo.MT_Questions SET Status = @Status WHERE Id = @Id;",
                    new { Id = req.QuestionId, Status = newUnitStatus }, tx);
            }
            else
            {
                // 子題：DecidedAt 同時寫入（落定為 9/10/12 才寫）
                await conn.ExecuteAsync(
                    """
                    UPDATE dbo.MT_SubQuestions
                    SET Status    = @Status,
                        DecidedAt = CASE WHEN @Status IN (9, 10, 12) THEN SYSDATETIME() ELSE DecidedAt END
                    WHERE Id = @Id;
                    """,
                    new { Id = assign.SubQuestionId.Value, Status = newUnitStatus }, tx);
            }

            // 5. 子題 UPSERT（使用 QuestionService 的靜態 helper 複製不到，此處內聯子題處理）
            //    總召代修非題組題型較多（Single/Listen），子題邏輯保守：若 formData 含子題清單則 UPSERT
            await UpsertFinalSubQuestionsAsync(conn, tx, req.QuestionId, req.FormData);

            // 5.5 附圖：DELETE + INSERT 全量覆寫（母題層 + 子題層，與 QuestionService.UpdateAsync 邏輯一致）
            await QuestionImagePersistence.UpsertMasterAsync(conn, tx, req.QuestionId, req.FormData.Images);
            await QuestionImagePersistence.UpsertSubAsync(conn, tx, req.QuestionId,
                req.FormData.QuestionType switch
                {
                    QuestionTypeCodes.ReadGroup    => req.FormData.ReadSubQuestions.Select(s => s.Id).ToList(),
                    QuestionTypeCodes.ShortGroup   => req.FormData.ShortSubQuestions.Select(s => s.Id).ToList(),
                    QuestionTypeCodes.ListenGroup  => req.FormData.ListenGroupSubQuestions.Select(s => s.Id).ToList(),
                    _                              => (IReadOnlyList<int>)Array.Empty<int>()
                },
                req.FormData.Images);

            // 6. UPDATE ReviewAssignment → Completed, Decision, DecidedAt
            const string updateAssignSql = """
                UPDATE dbo.MT_ReviewAssignments
                SET Decision     = @Decision,
                    ReviewStatus = @Completed,
                    DecidedAt    = SYSDATETIME()
                WHERE Id = @AssignmentId;
                """;
            await conn.ExecuteAsync(updateAssignSql, new
            {
                AssignmentId = req.AssignmentId,
                Decision     = (byte)req.Decision,
                Completed    = (byte)ReviewTaskStatus.Completed
            }, tx);

            // 6.5 級聯淘汰 — Stage B-4：移除母題 Reject → 子題級聯（按 Q3 拍板每單元獨立決策）。
            //     結案時才依母題狀態做最終處置（B-4-3 範圍：母題 Reject → 整題組丟棄）。

            // 7. AuditLog：FinalReviewerEdit（題目層級） — 帶上「該單元」狀態快照
            const string auditSql = """
                INSERT INTO dbo.MT_AuditLogs
                    (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, CreatedAt)
                VALUES
                    (@UserId, @ProjectId, @Action, @TargetType, @TargetId, @OldValue, @NewValue, SYSDATETIME());
                """;
            // B：JSON 補 targetDisplayName 作 fallback（Question 被刪時 UI 仍能顯示題號）
            await conn.ExecuteAsync(auditSql, new
            {
                UserId     = operatorUserId,
                ProjectId  = assign.ProjectId,
                Action     = AuditLogAction.Modify,
                TargetType = AuditLogTargetType.Questions,
                TargetId   = req.QuestionId,
                OldValue   = System.Text.Json.JsonSerializer.Serialize(new
                {
                    targetDisplayName = assign.QuestionCode,
                    UnitStatus        = oldUnitStatus.Value,
                    SubQuestionId     = assign.SubQuestionId
                }),
                NewValue   = System.Text.Json.JsonSerializer.Serialize(new
                {
                    targetDisplayName = assign.QuestionCode,
                    Reason            = "FinalReviewerEdit",
                    UnitStatus        = (byte)newUnitStatus,
                    SubQuestionId     = assign.SubQuestionId,
                    QuestionType      = f.QuestionType
                })
            }, tx);

            // 8. AuditLog：FinalDecision（審題層級）
            // A：TargetId 統一為 AssignmentId（與 Dashboard 讀取端 JOIN MT_ReviewAssignments.Id 對齊）
            // B：JSON 補 targetDisplayName 作 fallback
            await conn.ExecuteAsync(auditSql, new
            {
                UserId     = operatorUserId,
                ProjectId  = assign.ProjectId,
                Action     = AuditLogAction.Modify,
                TargetType = AuditLogTargetType.Reviews,
                TargetId   = assign.Id,
                OldValue   = (string?)null,
                NewValue   = System.Text.Json.JsonSerializer.Serialize(new
                {
                    targetDisplayName = assign.QuestionCode,
                    Reason            = "FinalDecision",
                    Stage             = (byte)ReviewStage.Final,
                    SubQuestionId     = assign.SubQuestionId,
                    Decision          = (byte)req.Decision,
                    UnitStatus        = (byte)newUnitStatus
                })
            }, tx);

            tx.Commit();
            return true;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 總召代修題的子題 UPSERT（精簡版，只對現有子題做 Stem/Options/Answer 更新，不新增/刪除子題）。
    /// 閱讀題組（ReadGroup）：含 Options + Answer；
    /// 短文題組（ShortGroup）：開放式問答，無 CorrectAnswer；
    /// 聽力題組（ListenGroup）：含 Options + Answer，DetailIndicator 欄位名與閱讀題組不同。
    /// </summary>
    private static async Task UpsertFinalSubQuestionsAsync(
        System.Data.IDbConnection conn, IDbTransaction tx, int questionId, QuestionFormData f)
    {
        // 取出現有子題 Id 清單（依 SortOrder）
        var existingIds = (await conn.QueryAsync<int>(
            "SELECT Id FROM dbo.MT_SubQuestions WHERE ParentQuestionId = @QId AND IsDeleted = 0 ORDER BY SortOrder;",
            new { QId = questionId }, tx)).ToList();

        if (existingIds.Count == 0) return;

        switch (f.QuestionType)
        {
            case QuestionTypeCodes.ReadGroup:
            {
                if (f.ReadSubQuestions is null) break;
                const string sql = """
                    UPDATE dbo.MT_SubQuestions SET
                        Stem          = @Stem,
                        CorrectAnswer = @Answer,
                        OptionA       = @OptA,
                        OptionB       = @OptB,
                        OptionC       = @OptC,
                        OptionD       = @OptD,
                        Analysis      = @Analysis
                    WHERE Id = @Id AND ParentQuestionId = @QId AND IsDeleted = 0;
                    """;
                for (var i = 0; i < f.ReadSubQuestions.Count && i < existingIds.Count; i++)
                {
                    var sq = f.ReadSubQuestions[i];
                    await conn.ExecuteAsync(sql, new
                    {
                        Id       = existingIds[i],
                        QId      = questionId,
                        Stem     = NullIfEmpty(sq.Stem),
                        Answer   = NullIfEmpty(sq.Answer),
                        OptA     = sq.Options?.Length > 0 ? sq.Options[0] : null,
                        OptB     = sq.Options?.Length > 1 ? sq.Options[1] : null,
                        OptC     = sq.Options?.Length > 2 ? sq.Options[2] : null,
                        OptD     = sq.Options?.Length > 3 ? sq.Options[3] : null,
                        Analysis = NullIfEmpty(sq.Analysis)
                    }, tx);
                }
                break;
            }

            case QuestionTypeCodes.ShortGroup:
            {
                // 短文題組子題為開放式問答，無 CorrectAnswer 欄位
                if (f.ShortSubQuestions is null) break;
                const string sql = """
                    UPDATE dbo.MT_SubQuestions SET
                        Stem     = @Stem,
                        Analysis = @Analysis
                    WHERE Id = @Id AND ParentQuestionId = @QId AND IsDeleted = 0;
                    """;
                for (var i = 0; i < f.ShortSubQuestions.Count && i < existingIds.Count; i++)
                {
                    var sq = f.ShortSubQuestions[i];
                    await conn.ExecuteAsync(sql, new
                    {
                        Id       = existingIds[i],
                        QId      = questionId,
                        Stem     = NullIfEmpty(sq.Stem),
                        Analysis = NullIfEmpty(sq.Analysis)
                    }, tx);
                }
                break;
            }

            case QuestionTypeCodes.ListenGroup:
            {
                if (f.ListenGroupSubQuestions is null) break;
                const string sql = """
                    UPDATE dbo.MT_SubQuestions SET
                        Stem          = @Stem,
                        CorrectAnswer = @Answer,
                        OptionA       = @OptA,
                        OptionB       = @OptB,
                        OptionC       = @OptC,
                        OptionD       = @OptD,
                        Analysis      = @Analysis
                    WHERE Id = @Id AND ParentQuestionId = @QId AND IsDeleted = 0;
                    """;
                for (var i = 0; i < f.ListenGroupSubQuestions.Count && i < existingIds.Count; i++)
                {
                    var sq = f.ListenGroupSubQuestions[i];
                    await conn.ExecuteAsync(sql, new
                    {
                        Id       = existingIds[i],
                        QId      = questionId,
                        Stem     = NullIfEmpty(sq.Stem),
                        Answer   = NullIfEmpty(sq.Answer),
                        OptA     = sq.Options?.Length > 0 ? sq.Options[0] : null,
                        OptB     = sq.Options?.Length > 1 ? sq.Options[1] : null,
                        OptC     = sq.Options?.Length > 2 ? sq.Options[2] : null,
                        OptD     = sq.Options?.Length > 3 ? sq.Options[3] : null,
                        Analysis = NullIfEmpty(sq.Analysis)
                    }, tx);
                }
                break;
            }
        }
    }

    // ====================================================================
    //  Helper：歷程軌跡（union 三個來源 → 時序排列）
    // ====================================================================

    /// <summary>
    /// 載入指定題目單元的審題歷程。
    /// 預設「精準過濾」：subQuestionId 傳 null → 只撈 SubQuestionId IS NULL 的母題紀錄；
    /// 傳值 → 只撈該子題紀錄。題組類母題與子題各自擁有獨立的審題紀錄，互不混淆。
    /// aggregateAllUnits = true → 撈整題所有單元（母題 + 全子題），給 OverviewService 管理員整題彙整視角用。
    ///
    /// 資料源僅兩個：(a) MT_ReviewAssignments 審題意見、(b) MT_RevisionReplies 修題說明。
    /// MT_AuditLogs 屬於「命題編輯歷程」（建立 / 修改 / 刪除），由 Dashboard / SystemLogs 呈現，
    /// 不在「審題歷程」面板顯示。
    /// </summary>
    private static async Task<List<ReviewHistoryEntry>> LoadHistoryAsync(
        System.Data.IDbConnection conn, int questionId, int? subQuestionId = null, bool aggregateAllUnits = false)
    {
        // 單元過濾條件：彙整模式時略過 SubQuestionId 比對；否則用 NULL-safe 精準比對
        var unitFilterRA = aggregateAllUnits ? "" : " AND ISNULL(ra.SubQuestionId, -1) = ISNULL(@SubQuestionId, -1)";
        var unitFilterRR = aggregateAllUnits ? "" : " AND ISNULL(rr.SubQuestionId, -1) = ISNULL(@SubQuestionId, -1)";

        // CTE 聚合：用單一 SQL（CTE UnitInfo + UNION ALL 兩個來源）一次撈完所有歷程，省 round-trip
        //   - UnitInfo CTE：彙整模式才需要（提供 ParentCode + SortOrder 組 UnitDisplayCode）；精準模式直接回 NULL
        //   - UNION ALL：審題意見（Kind=1）+ 修題說明（Kind=2）
        //   - DB 端 ORDER BY At DESC 完成排序，C# 不需 OrderByDescending
        var unitInfoCte = aggregateAllUnits
            ? """
                WITH UnitInfo AS (
                    SELECT q.QuestionCode AS ParentCode,
                           CAST(NULL AS INT) AS SubQuestionId,
                           CAST(NULL AS INT) AS SortOrder
                    FROM dbo.MT_Questions q
                    WHERE q.Id = @QuestionId
                    UNION ALL
                    SELECT q.QuestionCode, sq.Id, sq.SortOrder
                    FROM dbo.MT_SubQuestions sq
                    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                    WHERE sq.ParentQuestionId = @QuestionId
                )
                """
            : "";
        var unitJoinRA = aggregateAllUnits ? "LEFT JOIN UnitInfo uiA ON ISNULL(uiA.SubQuestionId, -1) = ISNULL(ra.SubQuestionId, -1)" : "";
        var unitJoinRR = aggregateAllUnits ? "LEFT JOIN UnitInfo uiB ON ISNULL(uiB.SubQuestionId, -1) = ISNULL(rr.SubQuestionId, -1)" : "";
        var unitColsRA = aggregateAllUnits ? "uiA.SortOrder, uiA.ParentCode" : "CAST(NULL AS INT) AS SortOrder, CAST(NULL AS NVARCHAR(50)) AS ParentCode";
        var unitColsRR = aggregateAllUnits ? "uiB.SortOrder, uiB.ParentCode" : "CAST(NULL AS INT) AS SortOrder, CAST(NULL AS NVARCHAR(50)) AS ParentCode";

        var unionSql = $"""
            {unitInfoCte}
            SELECT CAST(1 AS TINYINT) AS Kind, ra.ReviewStage AS Stage, ra.Decision,
                   ra.Comment AS Content,
                   ISNULL(ra.DecidedAt, ra.CreatedAt) AS At,
                   ISNULL(u.DisplayName, '') AS ActorName,
                   ra.SubQuestionId,
                   {unitColsRA}
            FROM dbo.MT_ReviewAssignments ra
            LEFT JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
            {unitJoinRA}
            WHERE ra.QuestionId = @QuestionId AND ra.DecidedAt IS NOT NULL{unitFilterRA}  -- 依 DecidedAt 判定：草稿尚未送出不顯示

            UNION ALL

            SELECT CAST(2 AS TINYINT) AS Kind, rr.Stage, CAST(NULL AS TINYINT) AS Decision,
                   rr.Content,
                   rr.CreatedAt AS At,
                   ISNULL(u.DisplayName, '') AS ActorName,
                   rr.SubQuestionId,
                   {unitColsRR}
            FROM dbo.MT_RevisionReplies rr
            LEFT JOIN dbo.MT_Users u ON u.Id = rr.UserId
            {unitJoinRR}
            WHERE rr.QuestionId = @QuestionId{unitFilterRR}

            ORDER BY At DESC;
            """;

        var rows = await conn.QueryAsync<HistoryUnionRow>(unionSql, new
        {
            QuestionId    = questionId,
            SubQuestionId = subQuestionId
        });

        var entries = new List<ReviewHistoryEntry>();
        foreach (var row in rows)
        {
            var entry = new ReviewHistoryEntry
            {
                At        = row.At,
                ActorName = row.ActorName,
            };

            if (row.Kind == 1)
            {
                entry.Kind  = ReviewHistoryKind.ReviewComment;
                entry.Label = StageToLabel((ReviewStage)row.Stage, row.Decision);
                // 審題意見已改純 textarea 儲存；舊資料若含 HTML 在此一次性清洗，前端純文字渲染
                entry.ContentHtml = StripHtmlFull(row.Content);
            }
            else // Kind == 2
            {
                entry.Kind        = ReviewHistoryKind.RevisionReply;
                entry.Label       = StageToRevisionLabel(row.Stage);
                entry.ContentHtml = row.Content;
            }

            // 彙整視角才組 UnitDisplayCode（精準視角 ParentCode 為 NULL）
            if (aggregateAllUnits && !string.IsNullOrEmpty(row.ParentCode))
            {
                entry.UnitDisplayCode = row.SortOrder is int so
                    ? QuestionCodeHelper.SubCode(row.ParentCode, so)
                    : row.ParentCode;
            }

            entries.Add(entry);
        }

        // 對總審「改後採用」加退回次數標示（總審第一次退回／總審第二次退回...）
        ApplyFinalReturnSequence(entries);

        return entries;
    }

    /// <summary>
    /// 對「總審意見（改後採用 / 不採用）」依時間排序加退回次數，
    /// 產出「總審第一次退回 / 總審第二次退回...」。
    /// 不採用與改後採用在語意上同屬「退回給命題者」，故共用同一序列。
    /// 互審/專審無「退回」業務概念，不處理。
    /// </summary>
    private static void ApplyFinalReturnSequence(List<ReviewHistoryEntry> entries)
    {
        var finalReviseLabel = StageToLabel(ReviewStage.Final, (byte)ReviewDecision.Revise);
        var finalRejectLabel = StageToLabel(ReviewStage.Final, (byte)ReviewDecision.Reject);
        int idx = 0;
        foreach (var e in entries
            .Where(x => x.Kind == ReviewHistoryKind.ReviewComment
                     && (x.Label == finalReviseLabel || x.Label == finalRejectLabel))
            .OrderBy(x => x.At))
        {
            idx++;
            e.Label = $"總審第{ToChineseOrdinal(idx)}次退回";
        }
    }

    private static string ToChineseOrdinal(int n) => n switch
    {
        1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五", _ => n.ToString()
    };

    private static string StageToLabel(ReviewStage stage, byte? decision)
    {
        var stageName = stage switch
        {
            ReviewStage.Mutual => "互審意見",
            ReviewStage.Expert => "專審意見",
            ReviewStage.Final  => "總審意見",
            _                  => "審題意見"
        };
        if (decision is null) return stageName;
        return $"{stageName}（{decision switch
        {
            (byte)ReviewDecision.Approve => "採用",
            (byte)ReviewDecision.Revise  => "改後採用",
            (byte)ReviewDecision.Reject  => "不採用",
            _                            => ""
        }}）";
    }

    private static string StageToRevisionLabel(byte stage) => stage switch
    {
        1 => "互修說明",
        2 => "專修說明",
        3 => "總修說明",
        _ => "修題說明"
    };

    // ====================================================================
    //  Helper：相似題比對載入
    // ====================================================================

    private static async Task<List<ReviewSimilarityEntry>> LoadSimilaritiesAsync(System.Data.IDbConnection conn, int questionId)
    {
        const string sql = """
            SELECT
                sc.ComparedQuestionId,
                q.QuestionCode AS ComparedQuestionCode,
                sc.SimilarityScore,
                sc.Determination,
                CASE
                    WHEN q.QuestionTypeId IN (3, 5, 7) THEN q.ArticleContent
                    WHEN q.QuestionTypeId = 4          THEN COALESCE(NULLIF(q.Stem, ''), q.ArticleContent)
                    ELSE q.Stem
                END AS SummaryHtml
            FROM dbo.MT_SimilarityChecks sc
            INNER JOIN dbo.MT_Questions q ON q.Id = sc.ComparedQuestionId
            WHERE sc.SourceQuestionId = @QuestionId
              AND q.IsDeleted = 0
            ORDER BY sc.SimilarityScore DESC;
            """;

        var rows = await conn.QueryAsync<SimilarityRow>(sql, new { QuestionId = questionId });

        return rows.Select(r => new ReviewSimilarityEntry
        {
            ComparedQuestionId   = r.ComparedQuestionId,
            ComparedQuestionCode = r.ComparedQuestionCode,
            SimilarityScore      = r.SimilarityScore,
            Determination        = r.Determination,
            SummaryText          = StripHtml(r.SummaryHtml)
        }).ToList();
    }

    // ====================================================================
    //  共用 Helper
    // ====================================================================

    /// <summary>HTML 摘要去標籤（與 CwtList 相同實作；可考慮未來抽到共用 Helper）</summary>
    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ");
        text = System.Net.WebUtility.HtmlDecode(text).Trim();
        return text.Length > 100 ? text[..100] + "…" : text;
    }

    /// <summary>StripHtml 但不截斷字數（歷程意見/修題說明全文用，避免被裁掉）。</summary>
    private static string StripHtmlFull(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ");
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }

    // ====================================================================
    //  Plan_021 私有 helper（複用 QuestionService 的字串安全轉換邏輯）
    // ====================================================================

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string? SafeOption(string[] options, int index)
        => options.Length > index ? NullIfEmpty(options[index]) : null;

    // ====================================================================
    //  私有 DTO（僅 Service 內部用）
    // ====================================================================

    /// <summary>Plan_021 代修題時取出 Assignment 元資料的 DTO。</summary>
    private sealed class FinalEditAssignMeta
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public int QuestionId { get; set; }
        public int? SubQuestionId { get; set; }
        public byte Stage { get; set; }
        public bool CanEditByReviewer { get; set; }
        public string QuestionCode { get; set; } = string.Empty;
    }

    private sealed class AssignmentListRow
    {
        public int AssignmentId { get; set; }
        public int QuestionId { get; set; }
        public string QuestionCode { get; set; } = "";
        /// <summary>子題 Id；NULL=該列為母題審題單元。</summary>
        public int? SubQuestionId { get; set; }
        /// <summary>子題 SortOrder（JOIN MT_SubQuestions 取得）；NULL=母題列。</summary>
        public int? SubSortOrder { get; set; }
        public int TypeId { get; set; }
        public byte? Level { get; set; }
        public byte? Difficulty { get; set; }
        /// <summary>聽力題組子題固定難度（1~5）；母題列與其他題型為 null。</summary>
        public byte? FixedDifficulty { get; set; }
        public string? SummaryHtml { get; set; }
        public byte Stage { get; set; }
        public byte? Decision { get; set; }
        public byte Status { get; set; }
        public DateTime LastEditedAt { get; set; }
        /// <summary>總召退回次數（LEFT JOIN MT_ReviewReturnCounts；非 Final 行為 0）</summary>
        public int ReturnCount { get; set; }
        /// <summary>是否已解鎖總召自行修題（ReturnCount >= 3 時為 1）</summary>
        public bool CanEditByReviewer { get; set; }
    }

    private sealed class HistoryListRow
    {
        public int QuestionId { get; set; }
        public string QuestionCode { get; set; } = "";
        public int? SubQuestionId { get; set; }
        public int? SubSortOrder { get; set; }
        public int TypeId { get; set; }
        public byte? Level { get; set; }
        public byte? Difficulty { get; set; }
        public byte? FixedDifficulty { get; set; }
        public string? SummaryHtml { get; set; }
        public byte FinalStatus { get; set; }
        public DateTime FinalDecidedAt { get; set; }
    }

    private sealed class AssignmentDto
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public int? SubQuestionId { get; set; }
        public byte Stage { get; set; }
        public byte Status { get; set; }
        public byte? Decision { get; set; }
        public string Comment { get; set; } = "";
        public DateTime? DecidedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 歷程 UNION ALL 共用 row — Kind=1 對應 ReviewAssignments（審題意見）、Kind=2 對應 RevisionReplies（修題說明）。
    /// 為了讓 SQL 端用 CTE + UNION ALL 一次撈完兩個來源（省 round-trip），兩段 SELECT 必須輸出同樣欄位。
    /// </summary>
    private sealed class HistoryUnionRow
    {
        public byte Kind { get; set; }        // 1=ReviewComment / 2=RevisionReply
        public byte Stage { get; set; }
        public byte? Decision { get; set; }   // 僅 Kind=1 有值
        public string? Content { get; set; }
        public DateTime At { get; set; }
        public string ActorName { get; set; } = "";
        public int? SubQuestionId { get; set; }
        public int? SortOrder { get; set; }
        public string? ParentCode { get; set; }   // 彙整模式才有值
    }

    private sealed class SimilarityRow
    {
        public int ComparedQuestionId { get; set; }
        public string ComparedQuestionCode { get; set; } = "";
        public decimal SimilarityScore { get; set; }
        public byte Determination { get; set; }
        public string? SummaryHtml { get; set; }
    }

    private sealed class SiblingUnitRow
    {
        public int AssignmentId { get; set; }
        public int? SubQuestionId { get; set; }
        public int? SortOrder { get; set; }
        public byte Stage { get; set; }
        public byte? Decision { get; set; }
        public byte Status { get; set; }
    }

    private sealed class ReturnCountDto
    {
        public int ReturnCount { get; set; }
        public bool CanEditByReviewer { get; set; }
    }

    private sealed class AssignmentMetaDto
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public int QuestionId { get; set; }
        public int? SubQuestionId { get; set; }
        public byte Stage { get; set; }
        public byte? Decision { get; set; }
        public string QuestionCode { get; set; } = string.Empty;
    }
}
