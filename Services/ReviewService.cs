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
    /// 取得當前使用者於指定專案、指定階段的審題作業區列表。
    /// 回傳「分配給我但未決策」優先排在前面，已決策的「已決策」附在後面。
    /// </summary>
    Task<List<ReviewListItem>> GetMyAssignmentsAsync(int projectId, int reviewerUserId, ReviewStage stage);

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

    public async Task<List<ReviewListItem>> GetMyAssignmentsAsync(int projectId, int reviewerUserId, ReviewStage stage)
    {
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
                ra.CreatedAt       AS AssignedAt
            FROM dbo.MT_ReviewAssignments ra
            INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
            WHERE ra.ProjectId   = @ProjectId
              AND ra.ReviewerId  = @ReviewerId
              AND ra.ReviewStage = @Stage
              AND q.IsDeleted    = 0
            ORDER BY
                CASE WHEN ra.ReviewStatus = 2 THEN 1 ELSE 0 END,  -- 已完成沉到下方
                ra.CreatedAt DESC;
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<AssignmentListRow>(sql, new
        {
            ProjectId  = projectId,
            ReviewerId = reviewerUserId,
            Stage      = (byte)stage
        });

        return rows.Select(r => new ReviewListItem
        {
            AssignmentId = r.AssignmentId,
            QuestionId   = r.QuestionId,
            QuestionCode = r.QuestionCode,
            TypeKey      = QuestionConstants.TypeIdToKey.GetValueOrDefault(r.TypeId, ""),
            Level        = r.Level,
            Difficulty   = r.Difficulty,
            SummaryText  = StripHtml(r.SummaryHtml),
            Stage        = (ReviewStage)r.Stage,
            Decision     = r.Decision is null ? null : (ReviewDecision)r.Decision.Value,
            Status       = (ReviewTaskStatus)r.Status,
            AssignedAt   = r.AssignedAt
        }).ToList();
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
            ORDER BY q.UpdatedAt DESC;
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

    public async Task<ReviewModalData?> GetModalDataAsync(int questionId, int currentUserId)
    {
        // 1. 題目本體（複用 QuestionService.GetByIdAsync，避免兩處維護七題型載入邏輯）
        var question = await _questionSvc.GetByIdAsync(questionId);
        if (question is null) return null;

        using var conn = _db.CreateConnection();

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
                Comment    = myAssignment.Comment,
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

        // 取出 Stage 以判斷互審 vs 專/總審；同時驗證 reviewer 與未決策（Decision IS NULL）
        const string fetchSql = """
            SELECT ReviewStage AS Stage
            FROM dbo.MT_ReviewAssignments
            WHERE Id = @AssignmentId
              AND ReviewerId = @ReviewerId
              AND Decision IS NULL;
            """;
        var stage = await conn.QueryFirstOrDefaultAsync<byte?>(fetchSql, new
        {
            req.AssignmentId,
            ReviewerId = operatorUserId
        });
        if (stage is null) return false;   // 不是分配給此使用者，或已決策

        // 互審階段：意見儲存 = 完成審題（無採用/退回決策按鈕，儲存即完成）
        // 專/總審階段：純草稿，ReviewStatus 不變、不寫 DecidedAt
        var isMutual = stage.Value == (byte)ReviewStage.Mutual;

        var sql = isMutual
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

        var affected = await conn.ExecuteAsync(sql, new
        {
            req.AssignmentId,
            ReviewerId = operatorUserId,
            Comment    = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment,
            Status     = (byte)ReviewTaskStatus.Completed
        });

        return affected > 0;
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

            if (meta.Decision is not null)
            {
                tx.Rollback();
                return false;   // 已決策過，不允許重複決策
            }

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

            // 5. 依「階段 + 決策」計算題目應切換到的新狀態（null 代表不變）
            byte? oldQuestionStatus = null;
            byte? newQuestionStatus = MapDecisionToQuestionStatus(meta.Stage, req.Decision);
            if (newQuestionStatus is not null)
            {
                oldQuestionStatus = await conn.ExecuteScalarAsync<byte?>(
                    "SELECT Status FROM dbo.MT_Questions WHERE Id = @Id",
                    new { Id = meta.QuestionId }, tx);

                await conn.ExecuteAsync(
                    "UPDATE dbo.MT_Questions SET Status = @Status, UpdatedAt = SYSDATETIME() WHERE Id = @Id;",
                    new { Id = meta.QuestionId, Status = newQuestionStatus.Value }, tx);
            }

            // 6. 總召「改後再審」→ 累加退回計數（達 2 次自動解鎖總召自編）
            if (meta.Stage == (byte)ReviewStage.Final && req.Decision == ReviewDecision.Revise)
            {
                await BumpReturnCountAsync(conn, tx, meta.QuestionId, operatorUserId);
            }

            // 7. 寫稽核（帶狀態快照，方便日後追溯）
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
    /// 依「審題階段 + 決策」計算題目應切換的新 Question.Status。
    /// 回傳 null 表示題目狀態不變（互審無此能力 / 專審採用維持等批次重派）。
    /// 集中對應邏輯 — 未來規則調整只動此一處。
    /// </summary>
    private static byte? MapDecisionToQuestionStatus(byte stage, ReviewDecision decision)
        => (stage, decision) switch
        {
            ((byte)ReviewStage.Expert, ReviewDecision.Approve) => null,
            ((byte)ReviewStage.Expert, ReviewDecision.Revise)  => QuestionStatus.ExpertEditing,

            ((byte)ReviewStage.Final,  ReviewDecision.Approve) => QuestionStatus.Adopted,
            ((byte)ReviewStage.Final,  ReviewDecision.Reject)  => QuestionStatus.Rejected,
            ((byte)ReviewStage.Final,  ReviewDecision.Revise)  => QuestionStatus.FinalEditing,

            _ => null   // 互審不在此處理；異常組合不變更
        };

    /// <summary>
    /// 總召退回計數 +1。第 1 次 INSERT，後續 UPDATE。
    /// ReturnCount 達 2 後自動設 CanEditByReviewer=true（第 3 次解鎖總召自編）。
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
                ContentHtml = row.Comment
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

        // 對總審「改後再審」加退回次數標示（總審第一次退回／總審第二次退回...）
        ApplyFinalReturnSequence(entries);

        return entries.OrderByDescending(e => e.At).ToList();
    }

    /// <summary>判斷某筆 Audit 是否為系統自動事件（如命題階段結束的自動分配），這類事件不顯示於使用者歷程。</summary>
    private static bool IsSystemAutoAuditEvent(string? newValueJson)
    {
        if (string.IsNullOrEmpty(newValueJson)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(newValueJson);
            if (doc.RootElement.TryGetProperty("Reason", out var reasonProp))
            {
                var reason = reasonProp.GetString();
                // 系統行為清單：未來新增系統 Reason 在此補
                return reason is "CompositionPhaseEnded";
            }
        }
        catch
        {
            // JSON 解析失敗視為非系統事件，保留顯示
        }
        return false;
    }

    /// <summary>
    /// 對「總審意見（改後再審）」依時間排序加退回次數，產出「總審第一次退回 / 總審第二次退回...」。
    /// 互審/專審無「退回」業務概念，不處理。
    /// </summary>
    private static void ApplyFinalReturnSequence(List<ReviewHistoryEntry> entries)
    {
        var finalReviseLabel = StageToLabel(ReviewStage.Final, (byte)ReviewDecision.Revise);
        int idx = 0;
        foreach (var e in entries
            .Where(x => x.Kind == ReviewHistoryKind.ReviewComment && x.Label == finalReviseLabel)
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
            (byte)ReviewDecision.Revise  => "改後再審",
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
        public DateTime AssignedAt { get; set; }
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
