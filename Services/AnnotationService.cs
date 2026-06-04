using System.Data;
using Dapper;
using MT.Models;

namespace MT.Services;

// ============================================================
//  審題劃記評語（Inline Annotation）服務層
//  - 對應 MT_ReviewAnnotations
//  - 審題端：只看 own AssignmentId 的劃記
//  - 修題端：依 (Q, SubQuestionId) 跨 stage 彙整
// ============================================================

public interface IAnnotationService
{
    /// <summary>
    /// 審題端用：取得指定 Assignment 的所有劃記（含建立人 / 回應人顯示名稱）。
    /// 依 CreatedAt 升冪排序；不跨 stage、不跨 reviewer，思路乾淨。
    /// </summary>
    Task<List<ReviewAnnotation>> GetByAssignmentAsync(int assignmentId);

    /// <summary>
    /// 修題端用：彙整該題該單元所有 stage / reviewer 的劃記。
    /// NULL-safe 比對 SubQuestionId；依 (ReviewStage, CreatedAt) 排序。
    /// 命題者修題時用此查詢匯入「待修點」清單。
    /// </summary>
    Task<List<ReviewAnnotation>> GetByQuestionUnitAsync(int questionId, int? subQuestionId);

    /// <summary>
    /// 建立新劃記（審題者）。
    /// 驗證 Assignment 屬於 operatorUserId（防越權），通過後寫入 annotation + AuditLog。
    /// 回傳新 Annotation Id；驗證失敗回 0。
    /// </summary>
    Task<int> CreateAsync(SaveAnnotationRequest req, int operatorUserId);

    /// <summary>
    /// 刪除自己未被回應的劃記。
    /// 僅 CreatedByUserId = operatorUserId 且 ResponseState IS NULL 才允許刪除。
    /// 回傳成功與否。
    /// </summary>
    Task<bool> DeleteAsync(int annotationId, int operatorUserId);

    /// <summary>
    /// 命題者回應某劃記（確認修改 / 不修改 + 理由）。
    /// 驗證 operatorUserId 為該題的 CreatorId。
    /// State=Rejected 時 NoChangeReason 必填，State=Accepted 時忽略 NoChangeReason。
    /// </summary>
    Task<bool> RespondAsync(RespondAnnotationRequest req, int operatorUserId);
}

public class AnnotationService(IDatabaseService db) : IAnnotationService
{
    private readonly IDatabaseService _db = db;

    // ====================================================================
    //  讀取
    // ====================================================================

    public async Task<List<ReviewAnnotation>> GetByAssignmentAsync(int assignmentId)
    {
        // 全部從直接欄位 SELECT — 不再需要 JOIN MT_ReviewAssignments
        const string sql = """
            SELECT
                a.Id, a.AssignmentId, a.QuestionId, a.SubQuestionId, a.ReviewStage AS StageByte,
                a.FieldKey, a.AnchorStart, a.AnchorEnd, a.SelectedText, a.Comment,
                a.ResponseState, a.NoChangeReason, a.ResponseAt,
                a.ResponseByUserId, ISNULL(ru.DisplayName, '') AS ResponseByName,
                a.CreatedAt, a.CreatedByUserId,
                ISNULL(u.DisplayName, '') AS CreatorName
            FROM dbo.MT_ReviewAnnotations a
            LEFT JOIN dbo.MT_Users u  ON u.Id  = a.CreatedByUserId
            LEFT JOIN dbo.MT_Users ru ON ru.Id = a.ResponseByUserId
            WHERE a.AssignmentId = @AssignmentId
            ORDER BY a.CreatedAt;
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<AnnotationRow>(sql, new { AssignmentId = assignmentId });
        return rows.Select(MapRow).ToList();
    }

    public async Task<List<ReviewAnnotation>> GetByQuestionUnitAsync(int questionId, int? subQuestionId)
    {
        // 走冗餘直接欄位 + 新建的 IX_MT_ReviewAnnotations_Q_Sub_Stage 索引，零 JOIN（除了 Users 取顯示名稱）
        const string sql = """
            SELECT
                a.Id, a.AssignmentId, a.QuestionId, a.SubQuestionId, a.ReviewStage AS StageByte,
                a.FieldKey, a.AnchorStart, a.AnchorEnd, a.SelectedText, a.Comment,
                a.ResponseState, a.NoChangeReason, a.ResponseAt,
                a.ResponseByUserId, ISNULL(ru.DisplayName, '') AS ResponseByName,
                a.CreatedAt, a.CreatedByUserId,
                ISNULL(u.DisplayName, '') AS CreatorName
            FROM dbo.MT_ReviewAnnotations a
            LEFT JOIN dbo.MT_Users u  ON u.Id  = a.CreatedByUserId
            LEFT JOIN dbo.MT_Users ru ON ru.Id = a.ResponseByUserId
            WHERE a.QuestionId = @QuestionId
              AND ISNULL(a.SubQuestionId, -1) = ISNULL(@SubQuestionId, -1)
            ORDER BY a.ReviewStage, a.CreatedAt;
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<AnnotationRow>(sql, new
        {
            QuestionId    = questionId,
            SubQuestionId = subQuestionId
        });
        return rows.Select(MapRow).ToList();
    }

    // ====================================================================
    //  寫入
    // ====================================================================

    public async Task<int> CreateAsync(SaveAnnotationRequest req, int operatorUserId)
    {
        // 1. 驗證 Assignment 屬於此 user + 反查冗餘欄位（QuestionId/SubQuestionId/Stage）+ audit 用 ProjectId / QuestionCode
        const string fetchSql = """
            SELECT ra.ProjectId, ra.QuestionId, ra.SubQuestionId, ra.ReviewStage AS Stage,
                   q.QuestionCode
            FROM dbo.MT_ReviewAssignments ra
            INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
            WHERE ra.Id = @AssignmentId AND ra.ReviewerId = @ReviewerId;
            """;

        using var conn = _db.CreateConnection();
        conn.Open();
        var meta = await conn.QueryFirstOrDefaultAsync<AssignmentMetaRow>(fetchSql, new
        {
            req.AssignmentId,
            ReviewerId = operatorUserId
        });
        if (meta is null) return 0;   // 不是此 user 的 Assignment，或 Assignment 不存在

        // 簡易輸入規範化
        var selected = (req.SelectedText ?? string.Empty).Trim();
        var comment  = (req.Comment ?? string.Empty).Trim();
        if (selected.Length == 0 || comment.Length == 0) return 0;
        if (req.AnchorEnd <= req.AnchorStart || req.AnchorStart < 0) return 0;

        using var tx = conn.BeginTransaction();
        try
        {
            // 2. INSERT annotation — 冗餘三欄由 Service 由 Assignment 反查填入
            const string insertSql = """
                INSERT INTO dbo.MT_ReviewAnnotations
                    (AssignmentId, QuestionId, SubQuestionId, ReviewStage,
                     FieldKey, AnchorStart, AnchorEnd, SelectedText, Comment,
                     CreatedByUserId, CreatedAt)
                VALUES
                    (@AssignmentId, @QuestionId, @SubQuestionId, @Stage,
                     @FieldKey, @AnchorStart, @AnchorEnd, @SelectedText, @Comment,
                     @UserId, SYSDATETIME());

                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;

            var newId = await conn.ExecuteScalarAsync<int>(insertSql, new
            {
                req.AssignmentId,
                QuestionId    = meta.QuestionId,
                SubQuestionId = meta.SubQuestionId,
                Stage         = meta.Stage,
                FieldKey      = req.FieldKey,
                req.AnchorStart,
                req.AnchorEnd,
                SelectedText  = selected,
                Comment       = comment,
                UserId        = operatorUserId
            }, tx);

            // 3. AuditLog — 視為「審題行為」TargetType=Reviews(6)，TargetId 用 AssignmentId
            // kind="annotation"：明確區分於審題決策（Reviews 主流程也用 TargetType=6），給 Dashboard LOG 細分用
            // targetDisplayName 用 "{QuestionCode}.{FieldKey}" 方便日後檢視
            await WriteAnnotationAuditAsync(conn, tx,
                userId:    operatorUserId,
                projectId: meta.ProjectId,
                action:    AuditLogAction.Create,
                targetId:  req.AssignmentId,
                payload:   new
                {
                    kind              = "annotation",
                    targetDisplayName = $"{meta.QuestionCode}.{req.FieldKey}",
                    annotationId      = newId,
                    fieldKey          = req.FieldKey,
                    selectedText      = selected,
                    commentLength     = comment.Length
                });

            tx.Commit();
            return newId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int annotationId, int operatorUserId)
    {
        // 反查 + 守門條件全部交給 SQL：CreatedByUserId 必須一致 + 尚未回應才允許刪
        const string fetchSql = """
            SELECT a.AssignmentId, ra.ProjectId, q.QuestionCode, a.FieldKey, a.ResponseState
            FROM dbo.MT_ReviewAnnotations a
            INNER JOIN dbo.MT_ReviewAssignments ra ON ra.Id = a.AssignmentId
            INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
            WHERE a.Id = @Id AND a.CreatedByUserId = @UserId;
            """;

        using var conn = _db.CreateConnection();
        conn.Open();
        var meta = await conn.QueryFirstOrDefaultAsync<AnnotationDeleteMetaRow>(fetchSql, new
        {
            Id     = annotationId,
            UserId = operatorUserId
        });
        if (meta is null) return false;             // 不存在或非本人建立
        if (meta.ResponseState.HasValue) return false; // 已被命題者回應，不可刪

        using var tx = conn.BeginTransaction();
        try
        {
            const string deleteSql = """
                DELETE FROM dbo.MT_ReviewAnnotations
                WHERE Id = @Id
                  AND CreatedByUserId = @UserId
                  AND ResponseState IS NULL;
                """;
            var affected = await conn.ExecuteAsync(deleteSql, new
            {
                Id     = annotationId,
                UserId = operatorUserId
            }, tx);
            if (affected == 0) { tx.Rollback(); return false; }

            await WriteAnnotationAuditAsync(conn, tx,
                userId:    operatorUserId,
                projectId: meta.ProjectId,
                action:    AuditLogAction.Delete,
                targetId:  meta.AssignmentId,
                payload:   new
                {
                    kind              = "annotation",
                    targetDisplayName = $"{meta.QuestionCode}.{meta.FieldKey}",
                    annotationId
                });

            tx.Commit();
            return true;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<bool> RespondAsync(RespondAnnotationRequest req, int operatorUserId)
    {
        // Rejected 必填理由
        var reason = (req.NoChangeReason ?? string.Empty).Trim();
        if (req.State == AnnotationResponseState.Rejected && reason.Length == 0) return false;

        // 1. 反查：劃記 → Assignment → Question.CreatorId / ProjectId / QuestionCode
        const string fetchSql = """
            SELECT q.CreatorId, q.ProjectId, q.QuestionCode, a.FieldKey, a.AssignmentId, a.ResponseState
            FROM dbo.MT_ReviewAnnotations a
            INNER JOIN dbo.MT_ReviewAssignments ra ON ra.Id = a.AssignmentId
            INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
            WHERE a.Id = @Id;
            """;

        using var conn = _db.CreateConnection();
        conn.Open();
        var meta = await conn.QueryFirstOrDefaultAsync<AnnotationRespondMetaRow>(fetchSql, new
        {
            Id = req.AnnotationId
        });
        if (meta is null) return false;
        if (meta.CreatorId != operatorUserId) return false;   // 非命題者本人
        if (meta.ResponseState.HasValue) return false;        // 已回應過，不可重複

        using var tx = conn.BeginTransaction();
        try
        {
            const string updateSql = """
                UPDATE dbo.MT_ReviewAnnotations
                SET ResponseState    = @State,
                    NoChangeReason   = @Reason,
                    ResponseAt       = SYSDATETIME(),
                    ResponseByUserId = @UserId
                WHERE Id = @Id AND ResponseState IS NULL;
                """;

            // Accepted 時清空 NoChangeReason 欄位
            var storedReason = req.State == AnnotationResponseState.Rejected ? reason : null;

            var affected = await conn.ExecuteAsync(updateSql, new
            {
                Id     = req.AnnotationId,
                State  = (byte)req.State,
                Reason = storedReason,
                UserId = operatorUserId
            }, tx);
            if (affected == 0) { tx.Rollback(); return false; }

            await WriteAnnotationAuditAsync(conn, tx,
                userId:    operatorUserId,
                projectId: meta.ProjectId,
                action:    AuditLogAction.Modify,
                targetId:  meta.AssignmentId,
                payload:   new
                {
                    kind              = "annotationResponse",
                    targetDisplayName = $"{meta.QuestionCode}.{meta.FieldKey}",
                    annotationId      = req.AnnotationId,
                    responseState     = (byte)req.State,
                    hasReason         = storedReason is not null
                });

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
    //  共用 helper
    // ====================================================================

    private static async Task WriteAnnotationAuditAsync(IDbConnection conn, IDbTransaction tx,
        int userId, int projectId, byte action, int targetId, object payload)
    {
        const string sql = """
            INSERT INTO dbo.MT_AuditLogs
                (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, CreatedAt)
            VALUES
                (@UserId, @ProjectId, @Action, @TargetType, @TargetId, NULL, @NewValue, SYSDATETIME());
            """;

        await conn.ExecuteAsync(sql, new
        {
            UserId     = userId,
            ProjectId  = projectId,
            Action     = action,
            TargetType = AuditLogTargetType.Reviews,
            TargetId   = targetId,
            NewValue   = AuditLogJsonHelper.Serialize(payload)
        }, tx);
    }

    private static ReviewAnnotation MapRow(AnnotationRow r) => new()
    {
        Id              = r.Id,
        AssignmentId    = r.AssignmentId,
        QuestionId      = r.QuestionId,
        SubQuestionId   = r.SubQuestionId,
        Stage           = (ReviewStage)r.StageByte,
        FieldKey        = r.FieldKey,
        AnchorStart     = r.AnchorStart,
        AnchorEnd       = r.AnchorEnd,
        SelectedText    = r.SelectedText,
        Comment         = r.Comment,
        ResponseState   = r.ResponseState is null ? null : (AnnotationResponseState)r.ResponseState.Value,
        NoChangeReason  = r.NoChangeReason,
        ResponseAt      = r.ResponseAt,
        ResponseByUserId = r.ResponseByUserId,
        ResponseByName  = r.ResponseByName,
        CreatedAt       = r.CreatedAt,
        CreatedByUserId = r.CreatedByUserId,
        CreatorName     = r.CreatorName
    };

    // ====================================================================
    //  私有 DTO（僅 Service 內部用）
    // ====================================================================

    private sealed class AnnotationRow
    {
        public int Id { get; set; }
        public int AssignmentId { get; set; }
        public int QuestionId { get; set; }
        public int? SubQuestionId { get; set; }
        public byte StageByte { get; set; }    // 對應 MT_ReviewAnnotations.ReviewStage（NOT NULL）
        public string FieldKey { get; set; } = "";
        public int AnchorStart { get; set; }
        public int AnchorEnd { get; set; }
        public string SelectedText { get; set; } = "";
        public string Comment { get; set; } = "";
        public byte? ResponseState { get; set; }
        public string? NoChangeReason { get; set; }
        public DateTime? ResponseAt { get; set; }
        public int? ResponseByUserId { get; set; }
        public string ResponseByName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
        public string CreatorName { get; set; } = "";
    }

    private sealed class AssignmentMetaRow
    {
        public int ProjectId { get; set; }
        public int QuestionId { get; set; }
        public int? SubQuestionId { get; set; }
        public byte Stage { get; set; }
        public string QuestionCode { get; set; } = "";
    }

    private sealed class AnnotationDeleteMetaRow
    {
        public int AssignmentId { get; set; }
        public int ProjectId { get; set; }
        public string QuestionCode { get; set; } = "";
        public string FieldKey { get; set; } = "";
        public byte? ResponseState { get; set; }
    }

    private sealed class AnnotationRespondMetaRow
    {
        public int CreatorId { get; set; }
        public int ProjectId { get; set; }
        public string QuestionCode { get; set; } = "";
        public string FieldKey { get; set; } = "";
        public int AssignmentId { get; set; }
        public byte? ResponseState { get; set; }
    }
}
