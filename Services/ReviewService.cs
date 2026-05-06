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
    /// </summary>
    Task<ReviewModalData?> GetModalDataAsync(int questionId, int currentUserId);

    /// <summary>當前專案是否已進入互審以後階段（用 MT_ReviewAssignments 是否有紀錄判斷）</summary>
    Task<bool> HasAnyAssignmentAsync(int projectId);

    /// <summary>
    /// 取得指定題目的審題歷程（管理員監控用，不匿名）。
    /// 內部複用 LoadHistoryAsync —— 避免在 OverviewService 重寫 union 三個來源的 SQL。
    /// </summary>
    Task<List<ReviewHistoryEntry>> GetHistoryByQuestionIdAsync(int questionId);

    // ====== Phase 3.5：寫入 ======
    /// <summary>儲存審題意見草稿（不做決策、不變更 Question 狀態）。回傳是否成功。</summary>
    Task<bool> SaveCommentDraftAsync(SaveReviewCommentRequest req, int operatorUserId);

    /// <summary>
    /// 提交審題決策（含意見一併存入），同時更新 Assignment 的 Decision/ReviewStatus/DecidedAt。
    /// Phase 3.5：僅寫入 Assignment 與 AuditLog；題目狀態流轉與總召退回計數由 Plan_006 處理。
    /// </summary>
    Task<bool> SubmitDecisionAsync(SubmitReviewDecisionRequest req, int operatorUserId);
}

public class ReviewService(IDatabaseService db, IQuestionService questionSvc) : IReviewService
{
    private readonly IDatabaseService _db = db;
    private readonly IQuestionService _questionSvc = questionSvc;

    // ====================================================================
    //  Phase 3.1：讀取
    // ====================================================================

    public async Task<ReviewAssignmentListResult> GetMyAssignmentsAsync(int projectId, int reviewerUserId)
    {
        // 不再依 ReviewStage 篩選 — 顯示該使用者於本專案的全部分配
        // 同 query 撈當前 phase；前端 IsHistorical 由本 service 計算後填入，避免 UI 重做判斷
        const string sql = """
            SELECT
                ra.Id              AS AssignmentId,
                ra.QuestionId,
                q.QuestionCode,
                q.QuestionTypeId   AS TypeId,
                q.Level,
                q.Difficulty,
                q.Stem             AS SummaryHtml,
                ra.ReviewStage     AS Stage,
                ra.Decision,
                ra.ReviewStatus    AS Status,
                ISNULL(ra.DecidedAt, ra.CreatedAt) AS LastEditedAt
            FROM dbo.MT_ReviewAssignments ra
            INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
            WHERE ra.ProjectId   = @ProjectId
              AND ra.ReviewerId  = @ReviewerId
              AND q.IsDeleted    = 0
            ORDER BY
                CASE WHEN ra.ReviewStatus = 2 THEN 1 ELSE 0 END,  -- 待處理(0/1) 優先
                ra.ReviewStage DESC,                              -- 較新階段在上方
                q.Id ASC;                                          -- 同階段依題目 ID 升冪（與 CwtList 統一）
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
                AssignmentId = r.AssignmentId,
                QuestionId   = r.QuestionId,
                QuestionCode = r.QuestionCode,
                TypeKey      = QuestionConstants.TypeIdToKey.GetValueOrDefault(r.TypeId, ""),
                Level        = r.Level,
                Difficulty   = r.Difficulty,
                SummaryText  = StripHtml(r.SummaryHtml),
                Stage        = stage,
                Decision     = r.Decision is null ? null : (ReviewDecision)r.Decision.Value,
                Status       = (ReviewTaskStatus)r.Status,
                LastEditedAt = r.LastEditedAt,
                IsHistorical = currentStage is null || stage != currentStage
            };
        }).ToList();

        return new ReviewAssignmentListResult(items, currentStage);
    }

    public async Task<List<ReviewHistoryItem>> GetHistoryAsync(int projectId)
    {
        const string sql = """
            SELECT
                q.Id               AS QuestionId,
                q.QuestionCode,
                q.QuestionTypeId   AS TypeId,
                q.Level,
                q.Difficulty,
                q.Stem             AS SummaryHtml,
                q.Status           AS FinalStatus,
                q.UpdatedAt        AS FinalDecidedAt
            FROM dbo.MT_Questions q
            WHERE q.ProjectId = @ProjectId
              AND q.IsDeleted = 0
              AND q.Status IN @Statuses
            ORDER BY q.Id ASC;  -- 依題目 ID 升冪（與 CwtList 統一）
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<HistoryListRow>(sql, new
        {
            ProjectId = projectId,
            Statuses  = QuestionStatus.HistoryTabStatuses.Select(b => (int)b).ToList()
        });

        return rows.Select(r => new ReviewHistoryItem
        {
            QuestionId     = r.QuestionId,
            QuestionCode   = r.QuestionCode,
            TypeKey        = QuestionConstants.TypeIdToKey.GetValueOrDefault(r.TypeId, ""),
            Level          = r.Level,
            Difficulty     = r.Difficulty,
            SummaryText    = StripHtml(r.SummaryHtml),
            FinalStatus    = r.FinalStatus,
            FinalDecidedAt = r.FinalDecidedAt
        }).ToList();
    }

    public async Task<bool> HasAnyAssignmentAsync(int projectId)
    {
        const string sql = """
            SELECT TOP 1 1 FROM dbo.MT_ReviewAssignments WHERE ProjectId = @ProjectId;
            """;

        using var conn = _db.CreateConnection();
        var hit = await conn.ExecuteScalarAsync<int?>(sql, new { ProjectId = projectId });
        return hit.HasValue;
    }

    public async Task<List<ReviewHistoryEntry>> GetHistoryByQuestionIdAsync(int questionId)
    {
        using var conn = _db.CreateConnection();
        return await LoadHistoryAsync(conn, questionId);
    }

    public async Task<ReviewModalData?> GetModalDataAsync(int questionId, int currentUserId)
    {
        // 1. 題目本體（複用 QuestionService.GetByIdAsync，避免兩處維護七題型載入邏輯）
        var question = await _questionSvc.GetByIdAsync(questionId);
        if (question is null) return null;

        using var conn = _db.CreateConnection();

        // 1.5 從 AuditLogs 取「命題老師最後真實編輯時間」，覆蓋可能因舊版本污染的 UpdatedAt。
        // 篩選邏輯：
        //   - TargetType=3（Questions）、TargetId=questionId
        //   - UserId IS NOT NULL（排除系統批次事件，系統批次 UserId 為 NULL）
        //   - Action IN (0=Create, 1=Modify)
        //   - NewValue 的 Reason 欄位不存在（一般命題編輯），或 = 'Revision'（修題也屬老師親自編輯）
        //   - 排除 Reason 屬於已知系統批次清單
        // Fallback：若無任何符合記錄，保留原始 CreatedAt 作為「建立時間」顯示。
        const string lastEditSql = """
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
                  OR JSON_VALUE(NewValue, '$.Reason') NOT IN (
                      'CompositionPhaseEnded',
                      'PeerEditingPhaseStart',
                      'ExpertReviewingPhaseStart',
                      'ExpertEditingPhaseStart',
                      'FinalReviewingPhaseStart',
                      'FinalEditingPhaseStart',
                      'FinalReviewAssigned'
                  )
              )
            ORDER BY CreatedAt DESC;
            """;
        var lastTeacherEditAt = await conn.ExecuteScalarAsync<DateTime?>(lastEditSql, new { QuestionId = questionId });

        // 用 AuditLogs 的真實命題老師編輯時間覆蓋 question.UpdatedAt
        // 若 AuditLogs 無紀錄（極舊資料），fallback 為 question.CreatedAt（建立時間永遠正確）
        question.UpdatedAt = lastTeacherEditAt ?? question.CreatedAt;

        // 2. 命題者顯示名稱
        const string creatorSql = """
            SELECT ISNULL(u.DisplayName, '') FROM dbo.MT_Questions q
            LEFT JOIN dbo.MT_Users u ON u.Id = q.CreatorId
            WHERE q.Id = @Id;
            """;
        var creatorName = await conn.ExecuteScalarAsync<string>(creatorSql, new { Id = questionId }) ?? "";

        // 3. 當前登入者於此題目的自己 Assignment（可能多階段，取最新一筆）
        const string assignSql = """
            SELECT TOP 1
                Id, QuestionId, ReviewStage AS Stage, ReviewStatus AS Status,
                Decision, ISNULL(Comment, '') AS Comment, DecidedAt, CreatedAt
            FROM dbo.MT_ReviewAssignments
            WHERE QuestionId = @QuestionId AND ReviewerId = @ReviewerId
            ORDER BY ReviewStage DESC;
            """;
        var myAssignment = await conn.QueryFirstOrDefaultAsync<AssignmentDto>(assignSql, new
        {
            QuestionId = questionId,
            ReviewerId = currentUserId
        });

        // 4. 歷程軌跡：union 三個來源
        var history = await LoadHistoryAsync(conn, questionId);

        // 5. 相似題比對
        var similar = await LoadSimilaritiesAsync(conn, questionId);

        // 6. 總召退回次數（僅總審階段需要顯示）
        const string returnSql = """
            SELECT TOP 1 ReturnCount, CanEditByReviewer
            FROM dbo.MT_ReviewReturnCounts
            WHERE QuestionId = @QuestionId
            ORDER BY Id DESC;
            """;
        var returnInfo = await conn.QueryFirstOrDefaultAsync<ReturnCountDto>(returnSql, new { QuestionId = questionId });

        return new ReviewModalData
        {
            Question     = question,
            CreatorName  = creatorName,
            MyAssignment = myAssignment is null ? null : new ReviewAssignmentInfo
            {
                Id         = myAssignment.Id,
                QuestionId = myAssignment.QuestionId,
                Stage      = (ReviewStage)myAssignment.Stage,
                Status     = (ReviewTaskStatus)myAssignment.Status,
                Decision   = myAssignment.Decision is null ? null : (ReviewDecision)myAssignment.Decision.Value,
                // 舊資料可能殘留 Quill HTML 標籤（<u> / <br> / <p> 等），textarea 不渲染 HTML
                // 會把標籤直接當成字面顯示出現怪線條 — 載入時統一 strip 為純文字
                Comment    = StripHtmlFull(myAssignment.Comment),
                DecidedAt  = myAssignment.DecidedAt,
                CreatedAt  = myAssignment.CreatedAt
            },
            History              = history,
            SimilarQuestions     = similar,
            FinalReturnCount     = returnInfo?.ReturnCount ?? 0,
            CanFinalReviewerEdit = returnInfo?.CanEditByReviewer ?? false
        };
    }

    // ====================================================================
    //  Phase 3.5：寫入
    // ====================================================================

    public async Task<bool> SaveCommentDraftAsync(SaveReviewCommentRequest req, int operatorUserId)
    {
        using var conn = _db.CreateConnection();

        // 取出 Stage / ProjectId / QuestionId / DecidedAt（區分首次完成 vs 修改既有意見）
        const string fetchSql = """
            SELECT ReviewStage AS Stage, ProjectId, QuestionId, DecidedAt
            FROM dbo.MT_ReviewAssignments
            WHERE Id = @AssignmentId
              AND ReviewerId = @ReviewerId
              AND Decision IS NULL;
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
                await conn.ExecuteAsync(auditSql, new
                {
                    UserId     = operatorUserId,
                    ProjectId  = meta.ProjectId,
                    Action     = wasDecided ? AuditLogAction.Modify : AuditLogAction.Create,
                    TargetType = AuditLogTargetType.Reviews,
                    TargetId   = req.AssignmentId,
                    NewValue   = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Stage      = meta.Stage,
                        QuestionId = meta.QuestionId,
                        ReviewStatus = (byte)ReviewTaskStatus.Completed
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
    }

    public async Task<bool> SubmitDecisionAsync(SubmitReviewDecisionRequest req, int operatorUserId)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            // 1. 取出 Assignment 元資訊（驗證權限、防重複決策、取 ProjectId 寫稽核）
            const string fetchSql = """
                SELECT Id, ProjectId, QuestionId, ReviewStage AS Stage, Decision
                FROM dbo.MT_ReviewAssignments
                WHERE Id = @AssignmentId AND ReviewerId = @ReviewerId;
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

            // 6. 依「階段 + PhaseCode + 決策」計算題目應切換到的新狀態（null 代表不變或交給後續邏輯）
            byte? oldQuestionStatus = null;
            byte? newQuestionStatus = MapDecisionToQuestionStatus(meta.Stage, req.Decision, currentPhaseCode);

            // 6-1. 第 3 輪 Reject 偵測：PhaseCode=8 + Final + Reject + 該題已退回 ≥2 次 → Rejected(10)
            //      ReturnCount=2 表示已退回 2 次（老師已修兩輪）；本次 Reject 等同第 3 輪終結
            if (newQuestionStatus is null
                && currentPhaseCode == 8
                && meta.Stage == (byte)ReviewStage.Final
                && req.Decision == ReviewDecision.Reject)
            {
                var existingReturnCount = await conn.ExecuteScalarAsync<int?>(
                    "SELECT TOP 1 ReturnCount FROM dbo.MT_ReviewReturnCounts WHERE QuestionId = @Id ORDER BY Id DESC;",
                    new { Id = meta.QuestionId }, tx) ?? 0;

                if (existingReturnCount >= 2)
                {
                    newQuestionStatus = QuestionStatus.Rejected;   // 10
                }
            }

            if (newQuestionStatus is not null)
            {
                oldQuestionStatus = await conn.ExecuteScalarAsync<byte?>(
                    "SELECT Status FROM dbo.MT_Questions WHERE Id = @Id",
                    new { Id = meta.QuestionId }, tx);

                // 審題決策僅更新 Status，不動 UpdatedAt（UpdatedAt 語意保留給命題老師實際編輯內容的時刻）
                await conn.ExecuteAsync(
                    "UPDATE dbo.MT_Questions SET Status = @Status WHERE Id = @Id;",
                    new { Id = meta.QuestionId, Status = newQuestionStatus.Value }, tx);
            }

            // 7. 總召「改後再審」或「不採用」→ 累加退回計數（達 2 次自動解鎖總召自編）
            //    再次決策時不重複加，避免使用者「Revise → Approve → Revise」誤計多次
            //    Reject 同樣計入：退回次數涵蓋所有「不採用→交回修改」情境
            if (!isResubmit && meta.Stage == (byte)ReviewStage.Final &&
                (req.Decision == ReviewDecision.Revise || req.Decision == ReviewDecision.Reject))
            {
                await BumpReturnCountAsync(conn, tx, meta.QuestionId, operatorUserId);
            }

            // 8. 寫稽核（帶狀態快照，方便日後追溯）
            const string auditSql = """
                INSERT INTO dbo.MT_AuditLogs
                    (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, CreatedAt)
                VALUES
                    (@UserId, @ProjectId, @Action, @TargetType, @TargetId, @OldValue, @NewValue, SYSDATETIME());
                """;
            await conn.ExecuteAsync(auditSql, new
            {
                UserId     = operatorUserId,
                ProjectId  = meta.ProjectId,
                Action     = AuditLogAction.Modify,
                TargetType = AuditLogTargetType.Reviews,
                TargetId   = meta.QuestionId,
                OldValue   = oldQuestionStatus is null ? null : System.Text.Json.JsonSerializer.Serialize(new { QuestionStatus = oldQuestionStatus.Value }),
                NewValue   = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Stage          = meta.Stage,
                    Decision       = (byte)req.Decision,
                    QuestionStatus = newQuestionStatus
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
    ///     Approve → Archived(12) 直接入庫（兩輪通過視同結案採用）
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

            // 次輪以後 (PhaseCode=8)：Approve→12（兩輪通過直接入庫）；Revise→8（退回老師）
            // Reject 由 caller 依 ReturnCount 決定 8（buffer）或 10（第 3 輪終結）
            (8, ReviewDecision.Approve) => QuestionStatus.Archived,       // 12
            (8, ReviewDecision.Revise)  => QuestionStatus.FinalEditing,   // 8
            (8, ReviewDecision.Reject)  => null,

            _ => null
        };
    }

    /// <summary>
    /// 總召退回計數 +1。第 1 次 INSERT，後續 UPDATE。
    /// ReturnCount 達 2 後自動設 CanEditByReviewer=true：
    ///   第 1 次退回：老師可修，再送審；ReturnCount=1
    ///   第 2 次退回：老師可修，再送審；ReturnCount=2 → 解鎖總召自編（第 3 輪由總召親自定奪）
    ///   第 3 輪：總召不再退回老師，直接 Approve→Archived(12) 或 Reject→Rejected(10)
    /// MERGE + HOLDLOCK 確保並發安全。
    /// </summary>
    private static async Task BumpReturnCountAsync(
        IDbConnection conn, IDbTransaction tx, int questionId, int finalReviewerId)
    {
        const string sql = """
            MERGE dbo.MT_ReviewReturnCounts WITH (HOLDLOCK) AS target
            USING (VALUES (@QuestionId, @FinalReviewerId)) AS src(QuestionId, FinalReviewerId)
                ON target.QuestionId = src.QuestionId
            WHEN MATCHED THEN
                UPDATE SET ReturnCount = target.ReturnCount + 1,
                           CanEditByReviewer = CASE WHEN target.ReturnCount + 1 >= 2 THEN 1 ELSE 0 END
            WHEN NOT MATCHED THEN
                INSERT (QuestionId, FinalReviewerId, ReturnCount, CanEditByReviewer)
                VALUES (@QuestionId, @FinalReviewerId, 1, 0);
            """;

        await conn.ExecuteAsync(sql, new { QuestionId = questionId, FinalReviewerId = finalReviewerId }, tx);
    }

    // ====================================================================
    //  Helper：歷程軌跡（union 三個來源 → 時序排列）
    // ====================================================================

    private static async Task<List<ReviewHistoryEntry>> LoadHistoryAsync(System.Data.IDbConnection conn, int questionId)
    {
        // (a) MT_AuditLogs：題目層級事件（建立、修改、送審、決策）
        const string auditSql = """
            SELECT al.Action, al.CreatedAt AS At,
                   ISNULL(u.DisplayName, '系統') AS ActorName,
                   al.OldValue, al.NewValue
            FROM dbo.MT_AuditLogs al
            LEFT JOIN dbo.MT_Users u ON u.Id = al.UserId
            WHERE al.TargetType = @TargetType AND al.TargetId = @QuestionId
            ORDER BY al.CreatedAt;
            """;
        var auditRows = await conn.QueryAsync<AuditEventRow>(auditSql, new
        {
            TargetType = AuditLogTargetType.Questions,
            QuestionId = questionId
        });

        // (b) MT_ReviewAssignments：每階段的審題意見
        const string commentSql = """
            SELECT ra.ReviewStage AS Stage, ra.Decision, ra.Comment,
                   ISNULL(ra.DecidedAt, ra.CreatedAt) AS At,
                   ISNULL(u.DisplayName, '') AS ActorName
            FROM dbo.MT_ReviewAssignments ra
            LEFT JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
            WHERE ra.QuestionId = @QuestionId AND ra.Comment IS NOT NULL AND ra.Comment <> ''
            ORDER BY ra.DecidedAt;
            """;
        var commentRows = await conn.QueryAsync<ReviewCommentRow>(commentSql, new { QuestionId = questionId });

        // (c) MT_RevisionReplies：命題教師的修題說明
        const string replySql = """
            SELECT rr.Stage, rr.Content, rr.CreatedAt AS At,
                   ISNULL(u.DisplayName, '') AS ActorName
            FROM dbo.MT_RevisionReplies rr
            LEFT JOIN dbo.MT_Users u ON u.Id = rr.UserId
            WHERE rr.QuestionId = @QuestionId
            ORDER BY rr.CreatedAt;
            """;
        var replyRows = await conn.QueryAsync<RevisionReplyRow>(replySql, new { QuestionId = questionId });

        var entries = new List<ReviewHistoryEntry>();

        foreach (var row in auditRows)
        {
            // 過濾系統自動事件（如命題階段結束自動分配審題者）— 不屬於使用者歷程語意
            if (IsSystemAutoAuditEvent(row.NewValue)) continue;

            entries.Add(new ReviewHistoryEntry
            {
                Kind        = ReviewHistoryKind.QuestionEvent,
                At          = row.At,
                ActorName   = row.ActorName,
                Label       = LabelFromAuditAction(row.Action, row.NewValue),
                ContentHtml = null
            });
        }

        foreach (var row in commentRows)
        {
            entries.Add(new ReviewHistoryEntry
            {
                Kind        = ReviewHistoryKind.ReviewComment,
                At          = row.At,
                ActorName   = row.ActorName,
                Label       = StageToLabel((ReviewStage)row.Stage, row.Decision),
                // 審題意見已改純 textarea 儲存；舊資料若含 HTML 在此一次性清洗，前端純文字渲染
                ContentHtml = StripHtmlFull(row.Comment)
            });
        }

        foreach (var row in replyRows)
        {
            entries.Add(new ReviewHistoryEntry
            {
                Kind        = ReviewHistoryKind.RevisionReply,
                At          = row.At,
                ActorName   = row.ActorName,
                Label       = StageToRevisionLabel(row.Stage),
                ContentHtml = row.Content
            });
        }

        // 對總審「改後採用」加退回次數標示（總審第一次退回／總審第二次退回...）
        ApplyFinalReturnSequence(entries);

        return entries.OrderByDescending(e => e.At).ToList();
    }

    /// <summary>
    /// 判斷某筆 Audit 是否為系統自動事件（如命題階段結束的自動分配、系統批次升級 Status），
    /// 這類事件不顯示於使用者歷程。
    /// 判斷依據：NewValue JSON 含有 Reason 欄位，且其值屬於已知的系統批次 Reason 清單。
    /// </summary>
    private static bool IsSystemAutoAuditEvent(string? newValueJson)
    {
        if (string.IsNullOrEmpty(newValueJson)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(newValueJson);
            if (doc.RootElement.TryGetProperty("Reason", out var reasonProp))
            {
                var reason = reasonProp.GetString();
                // 系統行為清單：含命題階段結束、各審題/修題階段批次升級 Status 的事件
                // EnsureCompositionPhaseClosedAsync 寫的 Reason
                // EnsurePhaseTransitionAsync 各階段寫的 Reason
                return reason is "CompositionPhaseEnded"
                               or "PeerEditingPhaseStart"
                               or "ExpertReviewingPhaseStart"
                               or "ExpertEditingPhaseStart"
                               or "FinalReviewingPhaseStart"
                               or "FinalEditingPhaseStart";
            }
        }
        catch
        {
            // JSON 解析失敗視為非系統事件，保留顯示
        }
        return false;
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

    private static string LabelFromAuditAction(byte action, string? newValueJson) => action switch
    {
        AuditLogAction.Create => "建立題目",
        AuditLogAction.Modify => "修改題目",
        AuditLogAction.Delete => "刪除題目",
        _                     => "題目事件"
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
                q.Stem AS SummaryHtml
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
    //  私有 DTO（僅 Service 內部用）
    // ====================================================================

    private sealed class AssignmentListRow
    {
        public int AssignmentId { get; set; }
        public int QuestionId { get; set; }
        public string QuestionCode { get; set; } = "";
        public int TypeId { get; set; }
        public byte? Level { get; set; }
        public byte? Difficulty { get; set; }
        public string? SummaryHtml { get; set; }
        public byte Stage { get; set; }
        public byte? Decision { get; set; }
        public byte Status { get; set; }
        public DateTime LastEditedAt { get; set; }
    }

    private sealed class HistoryListRow
    {
        public int QuestionId { get; set; }
        public string QuestionCode { get; set; } = "";
        public int TypeId { get; set; }
        public byte? Level { get; set; }
        public byte? Difficulty { get; set; }
        public string? SummaryHtml { get; set; }
        public byte FinalStatus { get; set; }
        public DateTime FinalDecidedAt { get; set; }
    }

    private sealed class AssignmentDto
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public byte Stage { get; set; }
        public byte Status { get; set; }
        public byte? Decision { get; set; }
        public string Comment { get; set; } = "";
        public DateTime? DecidedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class AuditEventRow
    {
        public byte Action { get; set; }
        public DateTime At { get; set; }
        public string ActorName { get; set; } = "";
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }

    private sealed class ReviewCommentRow
    {
        public byte Stage { get; set; }
        public byte? Decision { get; set; }
        public string? Comment { get; set; }
        public DateTime At { get; set; }
        public string ActorName { get; set; } = "";
    }

    private sealed class RevisionReplyRow
    {
        public byte Stage { get; set; }
        public string? Content { get; set; }
        public DateTime At { get; set; }
        public string ActorName { get; set; } = "";
    }

    private sealed class SimilarityRow
    {
        public int ComparedQuestionId { get; set; }
        public string ComparedQuestionCode { get; set; } = "";
        public decimal SimilarityScore { get; set; }
        public byte Determination { get; set; }
        public string? SummaryHtml { get; set; }
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
        public byte Stage { get; set; }
        public byte? Decision { get; set; }
    }
}
