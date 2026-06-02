using System.Data;
using System.Text.Json;
using Dapper;
using MT.Models;
using static MT.Services.TextHelper;

namespace MT.Services;

// ======================================================================
//  RevisionService — 審後修訂（Post-Decision Revision）
//
//  服務對象：計畫主持人 / 總召集人 / 系統管理員
//  使用情境：題目已完成三審決策（Status IN 9,10,11,12）後，允許再編輯內容並調整最終決策
//
//  與 QuestionService.SaveRevisionAsync 的差異：
//    SaveRevisionAsync — 教師、修題期間（Status 4/6/8 + 對應 PhaseCode）、寫 MT_RevisionReplies
//    本 Service       — 管理員、決策後（Status 9/10/11/12）、寫 MT_AuditLogs revisionReason
//
//  資料寫入：
//    - 母題修訂 → UPDATE MT_Questions（內容欄位 + Status）
//    - 子題修訂 → UPDATE MT_SubQuestions（內容欄位 + Status，不動母題）
//    - 每筆修訂寫 MT_AuditLogs：OldValue 含修訂前快照、NewValue 含修訂後快照 + revisionReason
//
//  Status 自動對應：
//    未結案專案：採用 → 9 (Adopted)、不採用 → 10 (Rejected)
//    已結案專案：採用 → 12 (Archived)、不採用 → 11 (ClosedNotAdopted)
// ======================================================================

public interface IRevisionService
{
    /// <summary>判斷使用者是否有「審後修訂」資格（計畫主持人 / 總召集人 / 系統管理員）。</summary>
    Task<bool> CanReviseAsync(int userId, int projectId);

    /// <summary>儲存修訂（母題或子題擇一，依 req.SubQuestionId 是否為 null 決定）。</summary>
    Task<AdminReviseResult> SaveAsync(AdminReviseRequest req, int operatorUserId);

    /// <summary>修訂歷史列表查詢（給 /revision-history 頁面用，分頁）。</summary>
    Task<RevisionListResult> ListAsync(RevisionListFilter filter);

    /// <summary>取單筆修訂的欄位 diff 細節（accordion 展開時用）。</summary>
    Task<RevisionDiffDetail?> GetDiffAsync(long auditLogId);
}

public class RevisionService : IRevisionService
{
    // 三種具修訂資格的角色名稱（對應 MT_Roles.Name）
    private static readonly string[] AllowedRoleNames = ["計畫主持人", "總召集人", "系統管理員"];

    private readonly IDatabaseService _db;
    private readonly IMembershipService _membership;
    private readonly IHttpContextAccessor _httpAccessor;
    private readonly ILogger<RevisionService> _logger;
    private readonly IHtmlSanitizationService _sanitizer;

    public RevisionService(
        IDatabaseService db,
        IMembershipService membership,
        IHttpContextAccessor httpAccessor,
        ILogger<RevisionService> logger,
        IHtmlSanitizationService sanitizer)
    {
        _db = db;
        _membership = membership;
        _httpAccessor = httpAccessor;
        _logger = logger;
        _sanitizer = sanitizer;
    }

    // ====================================================================
    //  權限判斷
    // ====================================================================

    public async Task<bool> CanReviseAsync(int userId, int projectId)
    {
        using var conn = _db.CreateConnection();
        return await CanReviseCoreAsync(conn, null, userId, projectId);
    }

    /// <summary>
    /// 核心權限判斷：使用者於該梯次的有效角色是否落在可審後修訂的角色集合。
    /// 可選 tx：SaveAsync 在既有 transaction 內呼叫傳入；公開的 CanReviseAsync 自開連線傳 null。
    /// </summary>
    private async Task<bool> CanReviseCoreAsync(IDbConnection conn, IDbTransaction? tx, int userId, int projectId)
    {
        var roleIds = await _membership.GetEffectiveRoleIdsAsync(userId, projectId);
        if (roleIds.Count == 0) return false;

        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.MT_Roles WHERE Id IN @Ids AND Name IN @Names;",
            new { Ids = roleIds, Names = AllowedRoleNames }, tx);

        return count > 0;
    }

    // ====================================================================
    //  SaveAsync — 主要寫入入口
    // ====================================================================

    public async Task<AdminReviseResult> SaveAsync(AdminReviseRequest req, int operatorUserId)
    {
        // 輸入檢查
        if (string.IsNullOrWhiteSpace(req.RevisionReason))
            return new AdminReviseResult { Success = false, ErrorMessage = "修訂原因為必填" };

        // 題目富文本寫入前消毒（共用 QuestionFormSanitizer，防 Stored XSS）
        QuestionFormSanitizer.Sanitize(req.FormData, _sanitizer);
        req.FormData.NormalizeFixedAttributes();

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            // 1. 讀目標題目（或子題）的當前 meta
            var meta = await LoadUnitMetaAsync(conn, tx, req.QuestionId, req.SubQuestionId);
            if (meta is null)
                return new AdminReviseResult { Success = false, ErrorMessage = "題目不存在或已刪除" };

            // 2. 權限檢查（讀完才知道 ProjectId）
            if (!await CanReviseCoreAsync(conn, tx, operatorUserId, meta.ProjectId))
                return new AdminReviseResult { Success = false, ErrorMessage = "您無權執行此修訂操作" };

            // 3. Status 必須是決策後狀態（9/10/11/12）
            if (!IsPostDecisionStatus(meta.OldStatus))
                return new AdminReviseResult { Success = false, ErrorMessage = "此單元尚未完成三審決策，無法執行審後修訂" };

            // 4. 依結案狀態決定新 Status pair（採用/不採用）
            var newStatus = ResolveNewStatus(req.NewDecision, meta.IsProjectClosed);

            // 5. 取舊內容快照（給 AuditLog 用）
            var oldSnapshot = await LoadContentSnapshotAsync(conn, tx, req.QuestionId, req.SubQuestionId);

            // 6. 寫入 — 母題與子題分支
            if (req.SubQuestionId is null)
                await UpdateMasterContentAsync(conn, tx, req.QuestionId, req.FormData, newStatus);
            else
                await UpdateSubContentAsync(conn, tx, req.SubQuestionId.Value, req.FormData, newStatus);

            // 7. 取新內容快照
            var newSnapshot = await LoadContentSnapshotAsync(conn, tx, req.QuestionId, req.SubQuestionId);

            // 8. 寫 MT_AuditLogs（含 revisionReason 標記，未來 ListAsync 用此過濾）
            await WriteRevisionAuditLogAsync(conn, tx,
                operatorUserId, meta.ProjectId, req.QuestionId, req.SubQuestionId, meta.DisplayCode,
                oldSnapshot, meta.OldStatus, newSnapshot, newStatus, req.RevisionReason.Trim());

            tx.Commit();
            return new AdminReviseResult { Success = true, NewStatus = newStatus };
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "RevisionService.SaveAsync 失敗 QuestionId={QuestionId} SubQuestionId={SubQuestionId}",
                req.QuestionId, req.SubQuestionId);
            throw;
        }
    }

    // ---- SaveAsync 內部 helpers ----

    private static bool IsPostDecisionStatus(byte status) =>
        status is QuestionStatus.Adopted
                or QuestionStatus.Rejected
                or QuestionStatus.ClosedNotAdopted
                or QuestionStatus.Archived;

    private static byte ResolveNewStatus(AdminReviseDecision decision, bool isProjectClosed) =>
        (decision, isProjectClosed) switch
        {
            (AdminReviseDecision.Adopt,  false) => QuestionStatus.Adopted,           // 9
            (AdminReviseDecision.Reject, false) => QuestionStatus.Rejected,          // 10
            (AdminReviseDecision.Adopt,  true)  => QuestionStatus.Archived,          // 12
            (AdminReviseDecision.Reject, true)  => QuestionStatus.ClosedNotAdopted,  // 11
            _ => throw new InvalidOperationException($"未知決策：{decision}")
        };

    private sealed class UnitMeta
    {
        public int ProjectId { get; init; }
        public byte OldStatus { get; init; }
        public bool IsProjectClosed { get; init; }
        public string DisplayCode { get; init; } = "";   // 母題 Q-115-00013；子題 Q-115-00013-01
    }

    private async Task<UnitMeta?> LoadUnitMetaAsync(
        IDbConnection conn, IDbTransaction tx, int questionId, int? subQuestionId)
    {
        if (subQuestionId is null)
        {
            var row = await conn.QueryFirstOrDefaultAsync<(int ProjectId, byte Status, DateTime? ClosedAt, string QuestionCode)>(
                """
                SELECT q.ProjectId, q.Status, p.ClosedAt, q.QuestionCode
                FROM   dbo.MT_Questions q
                INNER JOIN dbo.MT_Projects p ON p.Id = q.ProjectId
                WHERE  q.Id = @Id AND q.IsDeleted = 0;
                """, new { Id = questionId }, tx);

            if (row == default) return null;
            return new UnitMeta
            {
                ProjectId       = row.ProjectId,
                OldStatus       = row.Status,
                IsProjectClosed = row.ClosedAt.HasValue,
                DisplayCode     = row.QuestionCode
            };
        }
        else
        {
            var row = await conn.QueryFirstOrDefaultAsync<(int ProjectId, byte Status, DateTime? ClosedAt, string QuestionCode, byte SortOrder)>(
                """
                SELECT q.ProjectId, sq.Status, p.ClosedAt, q.QuestionCode, sq.SortOrder
                FROM   dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                INNER JOIN dbo.MT_Projects p  ON p.Id = q.ProjectId
                WHERE  sq.Id = @Id AND sq.ParentQuestionId = @ParentId
                       AND sq.IsDeleted = 0 AND q.IsDeleted = 0;
                """, new { Id = subQuestionId.Value, ParentId = questionId }, tx);

            if (row == default) return null;
            return new UnitMeta
            {
                ProjectId       = row.ProjectId,
                OldStatus       = row.Status,
                IsProjectClosed = row.ClosedAt.HasValue,
                DisplayCode     = $"{row.QuestionCode}-{row.SortOrder:D2}"
            };
        }
    }

    /// <summary>母題內容 UPDATE（不動子題、不動母題的 QuestionTypeId/Level/Difficulty/題型固定屬性）。</summary>
    private static async Task UpdateMasterContentAsync(
        IDbConnection conn, IDbTransaction tx, int questionId, QuestionFormData f, byte newStatus)
    {
        const string sql = """
            UPDATE dbo.MT_Questions SET
                Status          = @Status,
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
                UpdatedAt       = SYSDATETIME()
            WHERE Id = @Id AND IsDeleted = 0;
            """;

        await conn.ExecuteAsync(sql, new
        {
            Id             = questionId,
            Status         = newStatus,
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
        }, tx);

        // 母題附圖整批覆寫（子題附圖不在本流程處理）
        var masterImages = f.Images?.Where(i => i.SubQuestionIndex is null).ToList() ?? [];
        await QuestionImagePersistence.UpsertMasterAsync(conn, tx, questionId, masterImages);
    }

    /// <summary>子題內容 UPDATE（不動母題、不動 SortOrder/FixedDifficulty/CoreAbility/Indicator）。</summary>
    private static async Task UpdateSubContentAsync(
        IDbConnection conn, IDbTransaction tx, int subQuestionId, QuestionFormData f, byte newStatus)
    {
        // formData 內含 master 完整 sub 清單；依 subQuestionId 找出對應的子題物件
        var (stem, answer, options, analysis) = ExtractSubFields(f, subQuestionId);

        const string sql = """
            UPDATE dbo.MT_SubQuestions SET
                Status        = @Status,
                Stem          = @Stem,
                CorrectAnswer = @Answer,
                OptionA       = @OptA,
                OptionB       = @OptB,
                OptionC       = @OptC,
                OptionD       = @OptD,
                Analysis      = @Analysis
            WHERE Id = @Id AND IsDeleted = 0;
            """;

        await conn.ExecuteAsync(sql, new
        {
            Id       = subQuestionId,
            Status   = newStatus,
            Stem     = NullIfEmpty(stem),
            Answer   = NullIfEmpty(answer),
            OptA     = SafeOption(options, 0),
            OptB     = SafeOption(options, 1),
            OptC     = SafeOption(options, 2),
            OptD     = SafeOption(options, 3),
            Analysis = NullIfEmpty(analysis),
        }, tx);
    }

    /// <summary>
    /// 從 QuestionFormData 拉出指定 SubQuestionId 對應的子題內容欄位。
    /// formData 內含 master 的完整 sub 清單（GetByIdAsync 載入所有 subs），
    /// 但 RevisionService 只會寫入「使用者點按修訂的那一筆」，故依 Id 精準定位。
    /// </summary>
    private static (string Stem, string Answer, string[] Options, string Analysis) ExtractSubFields(
        QuestionFormData f, int subQuestionId)
    {
        var read = f.ReadSubQuestions.FirstOrDefault(s => s.Id == subQuestionId);
        if (read is not null) return (read.Stem, read.Answer, read.Options, read.Analysis);

        var shrt = f.ShortSubQuestions.FirstOrDefault(s => s.Id == subQuestionId);
        if (shrt is not null) return (shrt.Stem, "", ["", "", "", ""], shrt.Analysis);

        var listen = f.ListenGroupSubQuestions.FirstOrDefault(s => s.Id == subQuestionId);
        if (listen is not null) return (listen.Stem, listen.Answer, listen.Options, listen.Analysis);

        return ("", "", ["", "", "", ""], "");
    }

    // ====================================================================
    //  快照與 AuditLog 寫入
    // ====================================================================

    /// <summary>讀取單元的內容快照（母題或子題），用於 AuditLog OldValue / NewValue。</summary>
    private static async Task<Dictionary<string, object?>> LoadContentSnapshotAsync(
        IDbConnection conn, IDbTransaction tx, int questionId, int? subQuestionId)
    {
        if (subQuestionId is null)
        {
            var row = await conn.QueryFirstOrDefaultAsync<MasterSnapshotRow>(
                """
                SELECT Status, Stem, Analysis, CorrectAnswer AS Answer,
                       OptionA, OptionB, OptionC, OptionD,
                       ArticleTitle, ArticleContent, AudioUrl, GradingNote, QuestionCode
                FROM   dbo.MT_Questions WHERE Id = @Id;
                """, new { Id = questionId }, tx);

            if (row is null) return [];
            return new Dictionary<string, object?>
            {
                ["targetDisplayName"] = row.QuestionCode,
                ["status"]            = row.Status,
                ["statusLabel"]       = QuestionStatus.Labels.GetValueOrDefault(row.Status, ""),
                ["stem"]              = row.Stem,
                ["analysis"]          = row.Analysis,
                ["answer"]            = row.Answer,
                ["optionA"]           = row.OptionA,
                ["optionB"]           = row.OptionB,
                ["optionC"]           = row.OptionC,
                ["optionD"]           = row.OptionD,
                ["articleTitle"]      = row.ArticleTitle,
                ["articleContent"]    = row.ArticleContent,
                ["audioUrl"]          = row.AudioUrl,
                ["gradingNote"]       = row.GradingNote,
            };
        }
        else
        {
            var row = await conn.QueryFirstOrDefaultAsync<SubSnapshotRow>(
                """
                SELECT sq.Status, sq.Stem, sq.Analysis, sq.CorrectAnswer AS Answer,
                       sq.OptionA, sq.OptionB, sq.OptionC, sq.OptionD,
                       q.QuestionCode, sq.SortOrder
                FROM   dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                WHERE  sq.Id = @Id;
                """, new { Id = subQuestionId.Value }, tx);

            if (row is null) return [];
            return new Dictionary<string, object?>
            {
                ["targetDisplayName"] = $"{row.QuestionCode}-{row.SortOrder:D2}",
                ["subQuestionId"]     = subQuestionId.Value,
                ["status"]            = row.Status,
                ["statusLabel"]       = QuestionStatus.Labels.GetValueOrDefault(row.Status, ""),
                ["stem"]              = row.Stem,
                ["analysis"]          = row.Analysis,
                ["answer"]            = row.Answer,
                ["optionA"]           = row.OptionA,
                ["optionB"]           = row.OptionB,
                ["optionC"]           = row.OptionC,
                ["optionD"]           = row.OptionD,
            };
        }
    }

    private sealed class MasterSnapshotRow
    {
        public byte Status { get; set; }
        public string? Stem { get; set; }
        public string? Analysis { get; set; }
        public string? Answer { get; set; }
        public string? OptionA { get; set; }
        public string? OptionB { get; set; }
        public string? OptionC { get; set; }
        public string? OptionD { get; set; }
        public string? ArticleTitle { get; set; }
        public string? ArticleContent { get; set; }
        public string? AudioUrl { get; set; }
        public string? GradingNote { get; set; }
        public string QuestionCode { get; set; } = "";
    }

    private sealed class SubSnapshotRow
    {
        public byte Status { get; set; }
        public string? Stem { get; set; }
        public string? Analysis { get; set; }
        public string? Answer { get; set; }
        public string? OptionA { get; set; }
        public string? OptionB { get; set; }
        public string? OptionC { get; set; }
        public string? OptionD { get; set; }
        public string QuestionCode { get; set; } = "";
        public byte SortOrder { get; set; }
    }

    /// <summary>
    /// 寫 MT_AuditLogs。重點欄位：
    ///   Action=1(Modify) / TargetType=3(Questions) / TargetId=母題 Id（子題以 OldValue/NewValue 的 subQuestionId 標示）
    ///   NewValue 含 revisionReason 與 statusChanged（後續 ListAsync 用此過濾）
    /// </summary>
    private async Task WriteRevisionAuditLogAsync(
        IDbConnection conn, IDbTransaction tx,
        int operatorUserId, int projectId, int questionId, int? subQuestionId, string displayCode,
        Dictionary<string, object?> oldSnapshot, byte oldStatus,
        Dictionary<string, object?> newSnapshot, byte newStatus,
        string revisionReason)
    {
        // 在 NewValue 加上 revisionReason 與 statusChanged 標記（用於 RevisionHistory 篩選與顯示）
        newSnapshot["revisionReason"] = revisionReason;
        newSnapshot["statusChanged"]  = oldStatus != newStatus;
        if (subQuestionId.HasValue)
            newSnapshot["subQuestionId"] = subQuestionId.Value;

        var ip = _httpAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

        const string sql = """
            INSERT INTO dbo.MT_AuditLogs
                (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, IpAddress, CreatedAt)
            VALUES
                (@UserId, @ProjectId, @Action, @TargetType, @TargetId, @OldValue, @NewValue, @IpAddress, SYSDATETIME());
            """;

        await conn.ExecuteAsync(sql, new
        {
            UserId     = operatorUserId,
            ProjectId  = projectId,
            Action     = AuditLogAction.Modify,
            TargetType = AuditLogTargetType.Questions,
            TargetId   = questionId,
            OldValue   = AuditLogJsonHelper.Serialize(oldSnapshot),
            NewValue   = AuditLogJsonHelper.Serialize(newSnapshot),
            IpAddress  = ip
        }, tx);
    }

    // ====================================================================
    //  ListAsync — RevisionHistory 列表
    // ====================================================================

    public async Task<RevisionListResult> ListAsync(RevisionListFilter filter)
    {
        // 篩選條件：MT_AuditLogs WHERE TargetType=3 AND Action=1 AND NewValue 含 revisionReason
        // 用 JSON_VALUE 過濾 revisionReason 是否存在（NULL 過濾掉一般審題修改的 AuditLog）
        var args = new DynamicParameters();
        args.Add("ProjectId",  filter.ProjectId);
        args.Add("OperatorId", filter.OperatorId);
        args.Add("Keyword",    string.IsNullOrWhiteSpace(filter.Keyword) ? null : filter.Keyword.Trim());
        args.Add("DateFrom",   filter.DateFrom);
        args.Add("DateTo",     filter.DateTo);
        args.Add("OnlyDecisionChanged", filter.OnlyDecisionChanged);
        args.Add("Offset",     (Math.Max(1, filter.Page) - 1) * Math.Max(1, filter.PageSize));
        args.Add("PageSize",   Math.Max(1, filter.PageSize));

        const string baseWhere = """
            WHERE  a.TargetType = 3
              AND  a.Action     = 1
              AND  JSON_VALUE(a.NewValue, '$.revisionReason') IS NOT NULL
              AND  (@ProjectId  IS NULL OR a.ProjectId  = @ProjectId)
              AND  (@OperatorId IS NULL OR a.UserId     = @OperatorId)
              AND  (@DateFrom   IS NULL OR a.CreatedAt >= @DateFrom)
              AND  (@DateTo     IS NULL OR a.CreatedAt <  DATEADD(DAY, 1, @DateTo))
              AND  (@Keyword    IS NULL OR q.QuestionCode LIKE '%' + @Keyword + '%')
              AND  (@OnlyDecisionChanged IS NULL
                    OR (CAST(@OnlyDecisionChanged AS BIT) = 1
                        AND JSON_VALUE(a.NewValue, '$.statusChanged') = 'true')
                    OR (CAST(@OnlyDecisionChanged AS BIT) = 0
                        AND JSON_VALUE(a.NewValue, '$.statusChanged') = 'false'))
            """;

        var countSql = $"""
            SELECT COUNT(*) FROM dbo.MT_AuditLogs a
            INNER JOIN dbo.MT_Questions q ON q.Id = a.TargetId
            {baseWhere};
            """;

        var listSql = $"""
            SELECT
                a.Id              AS AuditLogId,
                a.CreatedAt       AS RevisedAt,
                a.UserId          AS OperatorId,
                ISNULL(u.DisplayName, '') AS OperatorName,
                a.ProjectId,
                ISNULL(p.Name, '')        AS ProjectName,
                q.Id              AS QuestionId,
                JSON_VALUE(a.NewValue, '$.subQuestionId') AS SubQuestionIdRaw,
                CASE
                    WHEN JSON_VALUE(a.NewValue, '$.subQuestionId') IS NULL
                        THEN q.QuestionCode
                    ELSE JSON_VALUE(a.NewValue, '$.targetDisplayName')
                END               AS QuestionCode,
                qt.Name           AS TypeKey,
                JSON_VALUE(a.OldValue, '$.statusLabel') AS OldStatusLabel,
                JSON_VALUE(a.NewValue, '$.statusLabel') AS NewStatusLabel,
                JSON_VALUE(a.NewValue, '$.revisionReason') AS RevisionReason
            FROM dbo.MT_AuditLogs a
            INNER JOIN dbo.MT_Questions q ON q.Id = a.TargetId
            LEFT  JOIN dbo.MT_Users    u  ON u.Id = a.UserId
            LEFT  JOIN dbo.MT_Projects p  ON p.Id = a.ProjectId
            LEFT  JOIN dbo.MT_QuestionTypes qt ON qt.Id = q.QuestionTypeId
            {baseWhere}
            ORDER BY a.CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        using var conn = _db.CreateConnection();
        var total = await conn.ExecuteScalarAsync<int>(countSql, args);
        var rows  = await conn.QueryAsync<ListRow>(listSql, args);

        return new RevisionListResult
        {
            Items = [..rows.Select(r => new RevisionListItem
            {
                AuditLogId            = r.AuditLogId,
                RevisedAt             = r.RevisedAt,
                OperatorId            = r.OperatorId,
                OperatorName          = string.IsNullOrEmpty(r.OperatorName) ? "（已刪除）" : r.OperatorName,
                ProjectId             = r.ProjectId,
                ProjectName           = r.ProjectName,
                QuestionId            = r.QuestionId,
                SubQuestionId         = int.TryParse(r.SubQuestionIdRaw, out var subId) ? subId : null,
                QuestionCode          = r.QuestionCode,
                TypeKey               = r.TypeKey,
                OldStatusLabel        = r.OldStatusLabel ?? "",
                NewStatusLabel        = r.NewStatusLabel ?? "",
                DecisionChanged       = !string.Equals(r.OldStatusLabel, r.NewStatusLabel, StringComparison.Ordinal),
                RevisionReasonSummary = Truncate(r.RevisionReason, 60),
            })],
            TotalCount = total,
            Page       = Math.Max(1, filter.Page),
            PageSize   = Math.Max(1, filter.PageSize)
        };
    }

    private sealed class ListRow
    {
        public long AuditLogId { get; set; }
        public DateTime RevisedAt { get; set; }
        public int OperatorId { get; set; }
        public string OperatorName { get; set; } = "";
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public int QuestionId { get; set; }
        public string? SubQuestionIdRaw { get; set; }
        public string QuestionCode { get; set; } = "";
        public string TypeKey { get; set; } = "";
        public string? OldStatusLabel { get; set; }
        public string? NewStatusLabel { get; set; }
        public string? RevisionReason { get; set; }
    }

    // ====================================================================
    //  GetDiffAsync — 單筆 diff 細節
    // ====================================================================

    public async Task<RevisionDiffDetail?> GetDiffAsync(long auditLogId)
    {
        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<(string? OldValue, string? NewValue)>(
            "SELECT OldValue, NewValue FROM dbo.MT_AuditLogs WHERE Id = @Id;",
            new { Id = auditLogId });

        if (row == default) return null;

        var oldMap = ParseJsonToDict(row.OldValue);
        var newMap = ParseJsonToDict(row.NewValue);

        var detail = new RevisionDiffDetail
        {
            AuditLogId     = auditLogId,
            RevisionReason = newMap.TryGetValue("revisionReason", out var r) ? r ?? "" : "",
            Fields         = BuildFieldDiffs(oldMap, newMap)
        };
        return detail;
    }

    private static List<RevisionFieldDiff> BuildFieldDiffs(
        Dictionary<string, string?> oldMap, Dictionary<string, string?> newMap)
    {
        // 欄位顯示順序與中文標籤對照表
        var fieldOrder = new (string Key, string Label, bool IsDecision)[]
        {
            ("statusLabel",    "最終決策", true),
            ("stem",           "題目",     false),
            ("articleContent", "內容",     false),
            ("articleTitle",   "文章標題", false),
            ("audioUrl",       "音檔",     false),
            ("optionA",        "選項 A",   false),
            ("optionB",        "選項 B",   false),
            ("optionC",        "選項 C",   false),
            ("optionD",        "選項 D",   false),
            ("answer",         "正解",     false),
            ("analysis",       "解析",     false),
            ("gradingNote",    "批閱說明", false),
        };

        var diffs = new List<RevisionFieldDiff>();
        foreach (var (key, label, isDecision) in fieldOrder)
        {
            var oldVal = oldMap.TryGetValue(key, out var ov) ? ov ?? "" : "";
            var newVal = newMap.TryGetValue(key, out var nv) ? nv ?? "" : "";

            if (oldVal == newVal) continue;   // 沒變動的欄位不列出

            diffs.Add(new RevisionFieldDiff
            {
                FieldLabel      = label,
                OldValue        = StripHtml(oldVal),
                NewValue        = StripHtml(newVal),
                IsDecisionField = isDecision
            });
        }
        return diffs;
    }

    private static Dictionary<string, string?> ParseJsonToDict(string? json)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Null   => null,
                    _                    => prop.Value.GetRawText()
                };
            }
        }
        catch (JsonException)
        {
            // JSON 損壞 → 回空 dict
        }
        return result;
    }

    // ====================================================================
    //  通用 helpers
    // ====================================================================

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var stripped = StripHtml(text);
        return stripped.Length <= maxLength ? stripped : stripped[..maxLength] + "…";
    }

    /// <summary>輕量 HTML strip：移除標籤、合併空白。給 AuditLog diff 顯示用，不負責 XSS 防護。</summary>
    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var noTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        var collapsed = System.Text.RegularExpressions.Regex.Replace(noTags, @"\s+", " ").Trim();
        return System.Net.WebUtility.HtmlDecode(collapsed);
    }
}
