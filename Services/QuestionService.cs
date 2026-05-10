using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Http;
using MT.Models;

namespace MT.Services;

public interface IQuestionService
{
    // 既有：配額與階段
    Task<List<QuotaProgressItem>> GetMyQuotaProgressAsync(int userId, int projectId);
    Task<ProjectPhaseInfo?> GetCurrentPhaseAsync(int projectId);

    // P3 新增 CRUD
    Task<int> CreateAsync(QuestionFormData formData, int creatorUserId, int projectId, byte initialStatus);
    Task<bool> UpdateAsync(int questionId, QuestionFormData formData, byte newStatus, int operatorUserId);
    Task<QuestionFormData?> GetByIdAsync(int questionId);
    Task<QuestionListResult> ListAsync(QuestionListFilter filter);
    Task<bool> SoftDeleteAsync(int questionId, int operatorUserId);
    Task<bool> SubmitForReviewAsync(int questionId, int operatorUserId);
    Task<bool> RestoreAsync(int questionId, int operatorUserId);

    // P4 新增：列表頁統計卡片用（依 status 分桶計數）
    Task<Dictionary<byte, int>> GetStatusCountsAsync(int projectId, int? creatorId);

    // Plan_012：CwtList 審修作業區「已修題」卡片計數（僅本人視角）。
    // 回傳 Status∈{4,6,8} 且本人在當前 Stage 已寫過 RevisionReplies 的題目數。
    Task<int> GetMyRevisionRepliedCountAsync(int projectId, int userId);

    // P4 新增：使用者在某專案是否為成員（用於審題任務權限攔截）
    Task<bool> IsProjectMemberAsync(int userId, int projectId);

    // Plan_008：命題階段結束後的批次轉換與互審分配（Idempotent）
    Task<DateTime?> GetCompositionPhaseEndAsync(int projectId);
    Task<bool> IsCompositionPhaseClosedAsync(int projectId);
    Task<int> EnsureCompositionPhaseClosedAsync(int projectId);

    // Plan_010：審修作業區「修題」Slide-Over 後端
    Task<RevisionSlideOverData?> GetRevisionDataAsync(int questionId, int currentUserId);
    Task<bool> SaveRevisionAsync(SaveRevisionRequest req, int operatorUserId);

    /// <summary>
    /// 總審修題「完成送審」：將 Status 8 → 7，為 Stage=3 既有審題者 INSERT 新一筆 Pending Assignment
    /// （保留歷次決策歷程），並寫稽核。回傳 false 表示題目當前不是 FinalEditing 狀態。
    /// </summary>
    Task<bool> ResubmitAfterFinalEditingAsync(int questionId, int userId);

    /// <summary>
    /// 依當前梯次階段，批次升級題目 Status + 階段需要的審題分配（Idempotent，可重複呼叫）。
    /// PhaseCode=4 互審修題：Status 2/3 → 4（PeerEditing）
    /// PhaseCode=5 專家審題：Status → 5；建立 ReviewStage=2 分配（Plan_011）
    /// PhaseCode=6 專審修題：Status → 6（無分配）
    /// PhaseCode=7 總召審題：Status → 7；建立 ReviewStage=3 分配，含總召迴避（Plan_011）
    /// PhaseCode=8 總審修題：Status → 8（無分配）
    /// </summary>
    Task<int> EnsurePhaseTransitionAsync(int projectId);
}

public class QuestionService(IDatabaseService db, IHttpContextAccessor httpAccessor) : IQuestionService
{
    private readonly IDatabaseService _db = db;
    private readonly IHttpContextAccessor _httpAccessor = httpAccessor;

    // ====================================================================
    //  既有：配額與階段
    // ====================================================================

    public async Task<List<QuotaProgressItem>> GetMyQuotaProgressAsync(int userId, int projectId)
    {
        const string sql = """
            SELECT
                qt.Id          AS QuestionTypeId,
                qt.Name        AS TypeName,
                mq.QuotaCount  AS Target,
                COUNT(q.Id)    AS Completed
            FROM dbo.MT_MemberQuotas mq
            INNER JOIN dbo.MT_ProjectMembers pm  ON pm.Id  = mq.ProjectMemberId
            INNER JOIN dbo.MT_QuestionTypes  qt  ON qt.Id  = mq.QuestionTypeId
            LEFT  JOIN dbo.MT_Questions      q   ON q.CreatorId      = pm.UserId
                                                AND q.ProjectId      = @ProjectId
                                                AND q.QuestionTypeId = mq.QuestionTypeId
                                                AND q.IsDeleted      = 0
                                                AND q.Status         >= 1
            WHERE pm.UserId    = @UserId
              AND pm.ProjectId = @ProjectId
            GROUP BY qt.Id, qt.Name, qt.SortOrder, mq.QuotaCount
            ORDER BY qt.SortOrder;
            """;

        using var conn = _db.CreateConnection();
        var result = await conn.QueryAsync<QuotaProgressItem>(sql, new { UserId = userId, ProjectId = projectId });
        return result.AsList();
    }

    public async Task<ProjectPhaseInfo?> GetCurrentPhaseAsync(int projectId)
    {
        // 排除 PhaseCode=1「產學計畫區間」（整體框架時程，不視為具體階段）
        // 從 PhaseCode=2 命題階段起的 7 個實際任務階段才是要顯示的
        const string sql = """
            SELECT TOP 1
                PhaseCode,
                PhaseName,
                StartDate,
                EndDate,
                DATEDIFF(DAY, CAST(GETDATE() AS DATE), EndDate) AS DaysLeft
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId
              AND PhaseCode > 1
              AND CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
            ORDER BY SortOrder;
            """;

        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<ProjectPhaseInfo>(sql, new { ProjectId = projectId });
    }

    /// <summary>
    /// 取得指定專案「命題階段（PhaseCode=2）」的結束日期。
    /// 若該專案沒有命題階段紀錄，回傳 null。
    /// </summary>
    public async Task<DateTime?> GetCompositionPhaseEndAsync(int projectId)
    {
        const string sql = """
            SELECT EndDate
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId AND PhaseCode = 2;
            """;

        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<DateTime?>(sql, new { ProjectId = projectId });
    }

    /// <summary>
    /// 命題階段是否已結束（EndDate &lt; 今日）。
    /// </summary>
    public async Task<bool> IsCompositionPhaseClosedAsync(int projectId)
    {
        var endDate = await GetCompositionPhaseEndAsync(projectId);
        return endDate.HasValue && endDate.Value.Date < DateTime.Today;
    }

    /// <summary>
    /// 在現有 Transaction 內檢查命題階段是否已結束（給 UpdateAsync / SoftDeleteAsync 防呆用）。
    /// </summary>
    private static async Task<bool> IsCompositionPhaseClosedInTxAsync(
        IDbConnection conn, IDbTransaction tx, int projectId)
    {
        var endDate = await conn.ExecuteScalarAsync<DateTime?>(
            "SELECT EndDate FROM dbo.MT_ProjectPhases WHERE ProjectId = @ProjectId AND PhaseCode = 2;",
            new { ProjectId = projectId }, tx);
        return endDate.HasValue && endDate.Value.Date < DateTime.Today;
    }

    /// <summary>
    /// 在現有 Transaction 內取當前進行中階段的 PhaseCode（找不到則回 null）。
    /// 用於 Plan_009：修題狀態與階段對齊防呆。
    /// </summary>
    private static async Task<byte?> GetCurrentPhaseCodeInTxAsync(
        IDbConnection conn, IDbTransaction tx, int projectId)
    {
        return await conn.ExecuteScalarAsync<byte?>("""
            SELECT TOP 1 PhaseCode
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId
              AND PhaseCode > 1
              AND CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
            ORDER BY SortOrder;
            """, new { ProjectId = projectId }, tx);
    }

    // ====================================================================
    //  P3：CRUD
    // ====================================================================

    /// <summary>
    /// 新增題目。
    /// initialStatus = 0 草稿 / 1 命題完成 / 2 命題送審
    /// 一個 transaction 內：產生題碼 → INSERT 主題 → INSERT 子題 → 寫 LOG
    /// </summary>
    public async Task<int> CreateAsync(QuestionFormData formData, int creatorUserId, int projectId, byte initialStatus)
    {
        var typeId = QuestionConstants.TypeKeyToId[formData.QuestionType];

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            // 1. 取年度（依 MT_Projects.Year，非建立日期）
            var projectYear = await conn.QuerySingleAsync<int>(
                "SELECT Year FROM dbo.MT_Projects WHERE Id = @ProjectId",
                new { ProjectId = projectId }, tx);

            // 2. 取流水號 → 組題碼
            var nextNo = await GetNextQuestionNumberAsync(conn, tx, projectYear);
            var code   = $"Q-{projectYear}-{nextNo:D5}";

            // 3. INSERT 主題
            const string insertSql = """
                INSERT INTO dbo.MT_Questions (
                    ProjectId, QuestionTypeId, QuestionCode, CreatorId, Status,
                    Level, Difficulty,
                    Stem, Analysis, CorrectAnswer,
                    OptionA, OptionB, OptionC, OptionD,
                    ArticleTitle, ArticleContent, AudioUrl, GradingNote,
                    Topic, Subtopic, Genre, Material,
                    WritingMode, AudioType, CoreAbility, DetailIndicator
                )
                OUTPUT INSERTED.Id
                VALUES (
                    @ProjectId, @TypeId, @Code, @CreatorId, @Status,
                    @Level, @Difficulty,
                    @Stem, @Analysis, @Answer,
                    @OptA, @OptB, @OptC, @OptD,
                    @ArticleTitle, @ArticleContent, @AudioUrl, @GradingNote,
                    @Topic, @Subtopic, @Genre, @Material,
                    @WritingMode, @AudioType, @CoreAbility, @DetailIndicator
                );
                """;

            var newId = await conn.QuerySingleAsync<int>(insertSql, BuildQuestionParams(formData, projectId, creatorUserId, initialStatus, typeId, code), tx);

            // 4. 子題（題組型才有）
            await InsertSubQuestionsAsync(conn, tx, newId, formData);

            // 5. 系統稽核：建立題目
            await WriteAuditLogAsync(conn, tx, creatorUserId, projectId,
                AuditLogAction.Create, newId,
                oldValue: null,
                newValue: new { Status = initialStatus, QuestionCode = code });

            tx.Commit();
            return newId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 編輯題目（涵蓋存草稿、命題完成、送審、修題回覆）。
    /// 子題策略：DELETE 全部後 INSERT 新清單。
    /// </summary>
    public async Task<bool> UpdateAsync(int questionId, QuestionFormData formData, byte newStatus, int operatorUserId)
    {
        var typeId = QuestionConstants.TypeKeyToId[formData.QuestionType];

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            // 1. 讀舊狀態與 ProjectId（找不到或已軟刪除 → 失敗）
            var meta = await conn.QueryFirstOrDefaultAsync<QuestionMetaDto>(
                "SELECT Status, ProjectId FROM dbo.MT_Questions WHERE Id = @Id AND IsDeleted = 0",
                new { Id = questionId }, tx);

            if (meta is null) return false;
            var oldStatus = meta.Status;
            var projectId = meta.ProjectId;

            // 1.5 命題階段結束防呆：禁止對草稿/完成題目做修改（Plan_008）
            if (oldStatus is QuestionStatus.Draft or QuestionStatus.Completed
                && await IsCompositionPhaseClosedInTxAsync(conn, tx, projectId))
            {
                throw new InvalidOperationException("命題階段已結束，無法修改題目。");
            }

            // 1.6 修題狀態階段對齊防呆（Plan_009）
            //     PeerEditing(4) 只能在 PhaseCode=4 互審修題、ExpertEditing(6) 只能在 PhaseCode=6 專審修題、
            //     FinalEditing(8) 只能在 PhaseCode=8 總審修題。
            if (oldStatus is QuestionStatus.PeerEditing or QuestionStatus.ExpertEditing or QuestionStatus.FinalEditing)
            {
                var currentPhaseCode = await GetCurrentPhaseCodeInTxAsync(conn, tx, projectId);
                var requiredPhaseCode = oldStatus switch
                {
                    QuestionStatus.PeerEditing    => (byte)4,
                    QuestionStatus.ExpertEditing  => (byte)6,
                    QuestionStatus.FinalEditing   => (byte)8,
                    _                             => (byte)0
                };
                if (currentPhaseCode != requiredPhaseCode)
                {
                    throw new InvalidOperationException("修題期間已結束，無法儲存變更。");
                }
            }

            // 2. UPDATE 主題
            const string updateSql = """
                UPDATE dbo.MT_Questions SET
                    QuestionTypeId  = @TypeId,
                    Status          = @Status,
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

            var affected = await conn.ExecuteAsync(updateSql, new
            {
                Id             = questionId,
                TypeId         = typeId,
                Status         = newStatus,
                formData.Level,
                formData.Difficulty,
                Stem           = NullIfEmpty(formData.Stem),
                Analysis       = NullIfEmpty(formData.Analysis),
                Answer         = NullIfEmpty(formData.Answer),
                OptA           = SafeOption(formData.Options, 0),
                OptB           = SafeOption(formData.Options, 1),
                OptC           = SafeOption(formData.Options, 2),
                OptD           = SafeOption(formData.Options, 3),
                ArticleTitle   = NullIfEmpty(formData.ArticleTitle),
                ArticleContent = NullIfEmpty(formData.ArticleContent),
                AudioUrl       = NullIfEmpty(formData.AudioUrl),
                GradingNote    = NullIfEmpty(formData.GradingNote),
                formData.Topic,
                formData.Subtopic,
                formData.Genre,
                formData.Material,
                formData.WritingMode,
                formData.AudioType,
                formData.CoreAbility,
                formData.DetailIndicator
            }, tx);

            if (affected == 0) return false;

            // 3. 子題：UPSERT by SubId + 缺席軟刪除（保留 Id 穩定）
            await UpsertSubQuestionsAsync(conn, tx, questionId, formData, operatorUserId, projectId);

            // 4. 系統稽核：修改題目（狀態轉移寫進 OldValue/NewValue）
            await WriteAuditLogAsync(conn, tx, operatorUserId, projectId,
                AuditLogAction.Modify, questionId,
                oldValue: new { Status = oldStatus },
                newValue: new { Status = newStatus });

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
    /// 取單筆題目（含子題清單）。題目不存在回 null。
    /// 不過濾 IsDeleted —— Overview 管理員需要在復原前檢視已刪題目；
    /// CwtList 端本來就只列未刪除題目，不會經此漏出已刪題目。
    /// </summary>
    public async Task<QuestionFormData?> GetByIdAsync(int questionId)
    {
        const string masterSql = """
            SELECT
                Id, QuestionCode, QuestionTypeId, Status, Level, Difficulty,
                Stem, Analysis, CorrectAnswer,
                OptionA, OptionB, OptionC, OptionD,
                ArticleTitle, ArticleContent, AudioUrl, GradingNote,
                Topic, Subtopic, Genre, Material, WritingMode, AudioType, CoreAbility, DetailIndicator,
                CreatedAt, UpdatedAt
            FROM dbo.MT_Questions
            WHERE Id = @Id;
            """;

        const string subSql = """
            SELECT Id, ParentQuestionId, SortOrder,
                   Stem, CorrectAnswer,
                   OptionA, OptionB, OptionC, OptionD,
                   Analysis, CoreAbility, Indicator, FixedDifficulty
            FROM dbo.MT_SubQuestions
            WHERE ParentQuestionId = @Id AND IsDeleted = 0
            ORDER BY SortOrder;
            """;

        using var conn = _db.CreateConnection();

        var master = await conn.QueryFirstOrDefaultAsync<QuestionRowDto>(masterSql, new { Id = questionId });
        if (master is null) return null;

        var data = new QuestionFormData
        {
            Id              = master.Id,
            QuestionCode    = master.QuestionCode,
            QuestionType    = QuestionConstants.TypeIdToKey[master.QuestionTypeId],
            Level           = master.Level,
            Difficulty      = master.Difficulty,
            Topic           = master.Topic,
            Subtopic        = master.Subtopic,
            Genre           = master.Genre,
            Material        = master.Material,
            WritingMode     = master.WritingMode,
            AudioType       = master.AudioType,
            CoreAbility     = master.CoreAbility,
            DetailIndicator = master.DetailIndicator,
            Stem            = master.Stem ?? "",
            ArticleTitle    = master.ArticleTitle ?? "",
            ArticleContent  = master.ArticleContent ?? "",
            Analysis        = master.Analysis ?? "",
            GradingNote     = master.GradingNote ?? "",
            Options         = [master.OptionA ?? "", master.OptionB ?? "", master.OptionC ?? "", master.OptionD ?? ""],
            Answer          = master.CorrectAnswer ?? "",
            AudioUrl        = master.AudioUrl ?? "",
            CreatedAt       = master.CreatedAt,
            UpdatedAt       = master.UpdatedAt
        };

        // 題組型才需要載子題
        var isGroupType = data.QuestionType is QuestionTypeCodes.ReadGroup
                                            or QuestionTypeCodes.ShortGroup
                                            or QuestionTypeCodes.ListenGroup;
        if (!isGroupType) return data;

        var subRows = (await conn.QueryAsync<SubQuestionRowDto>(subSql, new { Id = questionId })).AsList();

        if (data.QuestionType == QuestionTypeCodes.ReadGroup)
        {
            data.ReadSubQuestions = subRows.Count == 0
                ? [new()]
                : subRows.Select(r => new SubQuestionChoice
                {
                    Id       = r.Id,
                    Stem     = r.Stem ?? "",
                    Options  = [r.OptionA ?? "", r.OptionB ?? "", r.OptionC ?? "", r.OptionD ?? ""],
                    Answer   = r.CorrectAnswer ?? "",
                    Analysis = r.Analysis ?? ""
                }).ToList();
        }
        else if (data.QuestionType == QuestionTypeCodes.ShortGroup)
        {
            data.ShortSubQuestions = subRows.Count == 0
                ? [new()]
                : subRows.Select(r => new SubQuestionFreeResponse
                {
                    Id          = r.Id,
                    Stem        = r.Stem ?? "",
                    CoreAbility = r.CoreAbility,
                    Indicator   = r.Indicator,
                    Analysis    = r.Analysis ?? ""
                }).ToList();
        }
        else // ListenGroup（固定 2 子題）
        {
            data.ListenGroupSubQuestions = subRows.Count == 2
                ? subRows.Select(r => new ListenGroupSubQuestion
                {
                    Id              = r.Id,
                    FixedDifficulty = r.FixedDifficulty ?? 3,
                    Stem            = r.Stem ?? "",
                    Options         = [r.OptionA ?? "", r.OptionB ?? "", r.OptionC ?? "", r.OptionD ?? ""],
                    Answer          = r.CorrectAnswer ?? "",
                    Analysis        = r.Analysis ?? "",
                    CoreAbility     = r.CoreAbility,
                    DetailIndicator = r.Indicator
                }).ToList()
                : [new() { FixedDifficulty = 3 }, new() { FixedDifficulty = 4 }];
        }

        return data;
    }

    /// <summary>列表查詢（三 Tab + 篩選 + 分頁）。回傳分頁項目與總數。</summary>
    public async Task<QuestionListResult> ListAsync(QuestionListFilter filter)
    {
        // 1. 決定要查的 status 範圍（空 = 不限）
        // 優先順序：StatusesOverride（多狀態）> StatusFilter（單狀態）> Tab 預設
        byte[] statuses = filter.StatusesOverride is { Length: > 0 } ovr
            ? ovr
            : filter.StatusFilter is not null
                ? [filter.StatusFilter.Value]
                : filter.Tab switch
                {
                    "compose"  => QuestionStatus.ComposeTabStatuses,
                    "revision" => QuestionStatus.RevisionTabStatuses,
                    "history"  => QuestionStatus.HistoryTabStatuses,
                    _          => []
                };

        var page     = Math.Max(1, filter.Page);
        var pageSize = Math.Max(1, filter.PageSize);

        // 用 DynamicParameters 精準控制：只在有 status 範圍時才把 @Statuses 加入參數
        var args = new DynamicParameters();
        args.Add("ProjectId",      filter.ProjectId);
        args.Add("CreatorId",      filter.CreatorId);
        args.Add("QuestionTypeId", filter.QuestionTypeId);
        args.Add("Level",          filter.Level);
        args.Add("Keyword",        filter.Keyword);
        args.Add("Offset",         (page - 1) * pageSize);
        args.Add("PageSize",       pageSize);
        // Plan_012：HasReplied 篩選（僅 CwtList revision tab 帶；其它 Tab 為 null 不影響 SQL 判定）
        args.Add("HasReplied",     filter.HasReplied);

        // ⚠️ Dapper 把 byte[] 當作 VARBINARY，不會展開為 IN (...)；必須轉成 List<int> 才會自動展開
        if (statuses.Length > 0)
            args.Add("Statuses", statuses.Select(b => (int)b).ToList());

        // 動態 IsDeleted 條件：CwtList 預設只看未刪；Overview 設 IncludeDeleted=true 拿全部
        var deletedClauseQ = filter.IncludeDeleted ? "" : "AND q.IsDeleted = 0";

        // 關鍵字 SQL 片段：SearchCreatorName=true 時額外比對 u.DisplayName（僅 Overview 啟用）
        var keywordClause = filter.SearchCreatorName
            ? "AND (@Keyword IS NULL OR q.Stem LIKE '%' + @Keyword + '%' OR q.QuestionCode LIKE '%' + @Keyword + '%' OR u.DisplayName LIKE '%' + @Keyword + '%')"
            : "AND (@Keyword IS NULL OR q.Stem LIKE '%' + @Keyword + '%' OR q.QuestionCode LIKE '%' + @Keyword + '%')";

        // count 查詢需要 JOIN MT_Users 才能比對姓名；CwtList/Reviews 不需要 JOIN（效能最佳）
        var countJoin = filter.SearchCreatorName ? "LEFT JOIN dbo.MT_Users u ON u.Id = q.CreatorId" : "";

        // Plan_012 + Plan_013：HasReplied 條件 — 僅在 @HasReplied 非 null 時生效；
        // 條件意義：題目 Status∈{4,6,8} 且本人在「本輪」當前 Stage 是否已寫過 MT_RevisionReplies
        // Plan_013：只看本輪（CreatedAt > 上次總審退回時間 MAX DecidedAt），舊輪 reply 不算
        const string repliedClause = """
              AND (@HasReplied IS NULL OR
                   (q.Status IN (4, 6, 8) AND
                    @HasReplied = CAST(CASE WHEN EXISTS (
                        SELECT 1 FROM dbo.MT_RevisionReplies rr
                        WHERE rr.QuestionId = q.Id
                          AND rr.UserId     = q.CreatorId
                          AND rr.Stage      = q.Status
                          AND rr.CreatedAt > ISNULL((SELECT MAX(DecidedAt) FROM dbo.MT_ReviewAssignments WHERE QuestionId = q.Id AND ReviewStage = 3 AND Decision IN (2, 3)), '1900-01-01')
                    ) THEN 1 ELSE 0 END AS BIT)))
            """;

        var countSql = $"""
            SELECT COUNT(*)
            FROM dbo.MT_Questions q
            {countJoin}
            WHERE q.ProjectId = @ProjectId
              {deletedClauseQ}
              AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
              {(statuses.Length > 0 ? "AND q.Status IN @Statuses" : "")}
              AND (@QuestionTypeId IS NULL OR q.QuestionTypeId = @QuestionTypeId)
              AND (@Level IS NULL OR q.Level = @Level)
              {keywordClause}
              {repliedClause};
            """;

        // Plan_010 + Plan_013：HasRepliedThisStage —— 同題 + 同 UserId（= q.CreatorId）+ Stage 對齊 q.Status
        // 4=互修 / 6=專修 / 8=總修；其它狀態 q.Status≠4/6/8，子查詢自然 0 筆，HasReplied=0
        // Plan_013：只看本輪（CreatedAt > 上次總審退回時間 MAX DecidedAt），舊輪 reply 不算
        var listSql = $"""
            SELECT
                q.Id, q.QuestionCode,
                q.QuestionTypeId AS TypeId,
                q.Level, q.Difficulty, q.Status,
                q.Stem AS SummaryHtml,
                q.CreatedAt, q.UpdatedAt,
                q.IsDeleted,
                (SELECT COUNT(*) FROM dbo.MT_SubQuestions sq
                 WHERE sq.ParentQuestionId = q.Id AND sq.IsDeleted = 0) AS SubQuestionCount,
                ISNULL(u.DisplayName, '') AS CreatorName,
                CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.MT_RevisionReplies rr
                    WHERE rr.QuestionId = q.Id
                      AND rr.UserId     = q.CreatorId
                      AND rr.Stage      = q.Status
                      AND q.Status IN (4, 6, 8)
                      AND rr.CreatedAt > ISNULL((SELECT MAX(DecidedAt) FROM dbo.MT_ReviewAssignments WHERE QuestionId = q.Id AND ReviewStage = 3 AND Decision IN (2, 3)), '1900-01-01')
                ) THEN 1 ELSE 0 END AS BIT) AS HasRepliedThisStage
            FROM dbo.MT_Questions q
            LEFT JOIN dbo.MT_Users u ON u.Id = q.CreatorId
            WHERE q.ProjectId = @ProjectId
              {deletedClauseQ}
              AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
              {(statuses.Length > 0 ? "AND q.Status IN @Statuses" : "")}
              AND (@QuestionTypeId IS NULL OR q.QuestionTypeId = @QuestionTypeId)
              AND (@Level IS NULL OR q.Level = @Level)
              {keywordClause}
              {repliedClause}
            ORDER BY
                CASE WHEN q.Status = 0 THEN 0
                     WHEN q.Status = 1 THEN 1
                     ELSE 2 END,
                q.Id ASC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        using var conn = _db.CreateConnection();
         var total = await conn.ExecuteScalarAsync<int>(countSql, args);
        var rows  = (await conn.QueryAsync<QuestionListRowDto>(listSql, args)).AsList();

        return new QuestionListResult
        {
            TotalCount = total,
            Page       = page,
            PageSize   = pageSize,
            Items      = rows.Select(r => new QuestionListItem
            {
                Id               = r.Id,
                QuestionCode     = r.QuestionCode,
                TypeKey          = QuestionConstants.TypeIdToKey.GetValueOrDefault(r.TypeId, ""),
                Level            = r.Level,
                Difficulty       = r.Difficulty,
                Status           = r.Status,
                SummaryHtml      = r.SummaryHtml ?? "",
                CreatedAt        = r.CreatedAt,
                UpdatedAt        = r.UpdatedAt,
                IsDeleted        = r.IsDeleted,
                SubQuestionCount = r.SubQuestionCount,
                CreatorName      = r.CreatorName ?? "",
                HasRepliedThisStage = r.HasRepliedThisStage
            }).ToList()
        };
    }

    /// <summary>
    /// 依 status 分桶計數（給統計卡片用）。一次 SELECT GROUP BY，前端依 Tab 自行加總。
    /// creatorId = null 時取整個 project 的全部題目（管理員 / 命題總覽用）。
    /// </summary>
    public async Task<Dictionary<byte, int>> GetStatusCountsAsync(int projectId, int? creatorId)
    {
        const string sql = """
            SELECT Status, COUNT(*) AS Cnt
            FROM dbo.MT_Questions
            WHERE IsDeleted = 0
              AND ProjectId = @ProjectId
              AND (@CreatorId IS NULL OR CreatorId = @CreatorId)
            GROUP BY Status;
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<StatusCountDto>(sql,
            new { ProjectId = projectId, CreatorId = creatorId });
        return rows.ToDictionary(r => r.Status, r => r.Cnt);
    }

    /// <summary>
    /// Plan_012 + Plan_013：CwtList 審修作業區「已修題」卡片計數（本人視角）。
    /// 計算 Status∈{4,6,8} 且本人在「本輪」當前 Stage 已寫過 MT_RevisionReplies 的題目數。
    /// Plan_013：只看本輪（CreatedAt > 上次總審退回時間 MAX DecidedAt），舊輪 reply 不算。
    /// 條件鍵 (ProjectId, CreatorId, Status, IsDeleted) 與 (QuestionId, UserId, Stage)
    /// 皆為 ListAsync 既有索引服務範圍，不額外造成負擔。
    /// </summary>
    public async Task<int> GetMyRevisionRepliedCountAsync(int projectId, int userId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.MT_Questions q
            WHERE q.ProjectId = @ProjectId
              AND q.IsDeleted = 0
              AND q.CreatorId = @UserId
              AND q.Status IN (4, 6, 8)
              AND EXISTS (
                  SELECT 1 FROM dbo.MT_RevisionReplies rr
                  WHERE rr.QuestionId = q.Id
                    AND rr.UserId     = q.CreatorId
                    AND rr.Stage      = q.Status
                    AND rr.CreatedAt > ISNULL((SELECT MAX(DecidedAt) FROM dbo.MT_ReviewAssignments WHERE QuestionId = q.Id AND ReviewStage = 3 AND Decision IN (2, 3)), '1900-01-01')
              );
            """;
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql,
            new { ProjectId = projectId, UserId = userId });
    }

    /// <summary>使用者是否為該專案的成員（命題或審題身分均算）。</summary>
    public async Task<bool> IsProjectMemberAsync(int userId, int projectId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.MT_ProjectMembers
            WHERE UserId = @UserId AND ProjectId = @ProjectId;
            """;
        using var conn = _db.CreateConnection();
        var cnt = await conn.ExecuteScalarAsync<int>(sql, new { UserId = userId, ProjectId = projectId });
        return cnt > 0;
    }

    /// <summary>軟刪除（僅草稿可刪）。回傳是否成功。</summary>
    public async Task<bool> SoftDeleteAsync(int questionId, int operatorUserId)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            // 1. 讀 ProjectId（順便驗證題目存在、未刪、為草稿）
            var projectId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT ProjectId FROM dbo.MT_Questions WHERE Id = @Id AND IsDeleted = 0 AND Status = @Draft",
                new { Id = questionId, Draft = QuestionStatus.Draft }, tx);

            if (projectId is null)
            {
                tx.Rollback();
                return false;
            }

            // 1.5 命題階段結束防呆：禁止刪除草稿（Plan_008）
            if (await IsCompositionPhaseClosedInTxAsync(conn, tx, projectId.Value))
            {
                throw new InvalidOperationException("命題階段已結束，無法刪除題目。");
            }

            // 2. 軟刪除
            await conn.ExecuteAsync("""
                UPDATE dbo.MT_Questions
                SET IsDeleted = 1, DeletedAt = SYSDATETIME()
                WHERE Id = @Id;
                """,
                new { Id = questionId }, tx);

            // 3. 系統稽核：刪除題目
            await WriteAuditLogAsync(conn, tx, operatorUserId, projectId.Value,
                AuditLogAction.Delete, questionId,
                oldValue: null, newValue: null);

            tx.Commit();
            return true;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>命題送審（僅命題完成可送審；status 1 → 2）。回傳是否成功。</summary>
    public async Task<bool> SubmitForReviewAsync(int questionId, int operatorUserId)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            // 1. 讀 ProjectId（順便驗證題目存在、未刪、為命題完成）
            var projectId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT ProjectId FROM dbo.MT_Questions WHERE Id = @Id AND IsDeleted = 0 AND Status = @Completed",
                new { Id = questionId, Completed = QuestionStatus.Completed }, tx);

            if (projectId is null)
            {
                tx.Rollback();
                return false;
            }

            // 2. 狀態 1 → 2（僅改 Status；UpdatedAt 語意保留給命題老師實際編輯內容的時刻）
            await conn.ExecuteAsync("""
                UPDATE dbo.MT_Questions
                SET Status = @Submitted
                WHERE Id = @Id;
                """,
                new { Id = questionId, Submitted = QuestionStatus.Submitted }, tx);

            // 3. 系統稽核：送審
            await WriteAuditLogAsync(conn, tx, operatorUserId, projectId.Value,
                AuditLogAction.Modify, questionId,
                oldValue: new { Status = QuestionStatus.Completed },
                newValue: new { Status = QuestionStatus.Submitted });

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
    /// 復原已軟刪除的題目（管理員用，於命題總覽 Overview 操作）。
    /// 將 IsDeleted=1 的題目改回 0、清除 DeletedAt，並寫稽核紀錄。
    /// </summary>
    public async Task<bool> RestoreAsync(int questionId, int operatorUserId)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            // 1. 讀 ProjectId（驗證題目存在且為已刪狀態）
            var projectId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT ProjectId FROM dbo.MT_Questions WHERE Id = @Id AND IsDeleted = 1",
                new { Id = questionId }, tx);

            if (projectId is null)
            {
                tx.Rollback();
                return false;
            }

            // 2. 復原：IsDeleted=0、DeletedAt=NULL；不動 UpdatedAt（保留老師最後編輯的時間語意）
            await conn.ExecuteAsync("""
                UPDATE dbo.MT_Questions
                SET IsDeleted = 0, DeletedAt = NULL
                WHERE Id = @Id;
                """,
                new { Id = questionId }, tx);

            // 3. 系統稽核：用 Modify + JSON 描述變化（避免擴張 enum）
            await WriteAuditLogAsync(conn, tx, operatorUserId, projectId.Value,
                AuditLogAction.Modify, questionId,
                oldValue: new { IsDeleted = true },
                newValue: new { IsDeleted = false });

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
    /// 命題階段結束後的批次自動轉換 + 互審分配（Plan_008）。
    /// Idempotent：可重複呼叫，無事可做時直接 return 0。
    /// 行為：將該專案 (Status=1, IsDeleted=0) 題目升級為 Status=2，並按 Load-balanced
    /// Round-Robin（自命不自審）分配給「命題教師」進行互審（ReviewStage=1）。
    /// </summary>
    /// <returns>本次轉換的題目數量。</returns>
    public async Task<int> EnsureCompositionPhaseClosedAsync(int projectId)
    {
        // 1. 確認命題階段已結束
        var endDate = await GetCompositionPhaseEndAsync(projectId);
        if (endDate is null || endDate.Value.Date >= DateTime.Today) return 0;

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);

        try
        {
            // 2. 撈待升級題目（含 CreatorId，用於互審迴避）
            var candidates = (await conn.QueryAsync<(int Id, int CreatorId)>(
                """
                SELECT Id, CreatorId
                FROM dbo.MT_Questions
                WHERE ProjectId = @ProjectId
                  AND Status = @Completed
                  AND IsDeleted = 0
                ORDER BY Id;
                """,
                new { ProjectId = projectId, Completed = QuestionStatus.Completed }, tx)).AsList();

            if (candidates.Count == 0)
            {
                tx.Commit();
                return 0;
            }

            // 3. 取得本梯次「命題教師」清單（互審池）
            var peerReviewerIds = (await conn.QueryAsync<int>(
                """
                SELECT pm.UserId
                FROM dbo.MT_ProjectMembers pm
                INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
                INNER JOIN dbo.MT_Roles r ON r.Id = pmr.RoleId
                WHERE pm.ProjectId = @ProjectId AND r.Name = N'命題教師';
                """,
                new { ProjectId = projectId }, tx)).Distinct().ToList();

            if (peerReviewerIds.Count < 2)
            {
                throw new InvalidOperationException(
                    "命題階段已結束，但本梯次命題教師不足 2 人，無法執行互審分配。");
            }

            // 4. 批次升級狀態 1 → 2（系統批次，僅改 Status；不動 UpdatedAt）
            await conn.ExecuteAsync(
                """
                UPDATE dbo.MT_Questions
                SET Status = @Submitted
                WHERE ProjectId = @ProjectId
                  AND Status = @Completed
                  AND IsDeleted = 0;
                """,
                new
                {
                    ProjectId = projectId,
                    Submitted = QuestionStatus.Submitted,
                    Completed = QuestionStatus.Completed
                }, tx);

            // 5. 載入既有 ReviewStage=1 負載基準（避免重跑時負載失衡）
            var loadCounts = peerReviewerIds.ToDictionary(id => id, _ => 0);
            var existingLoads = await conn.QueryAsync<(int ReviewerId, int Cnt)>(
                """
                SELECT ReviewerId, COUNT(*) AS Cnt
                FROM dbo.MT_ReviewAssignments
                WHERE ProjectId = @ProjectId AND ReviewStage = 1
                GROUP BY ReviewerId;
                """,
                new { ProjectId = projectId }, tx);
            foreach (var (rid, cnt) in existingLoads)
            {
                if (loadCounts.ContainsKey(rid)) loadCounts[rid] = cnt;
            }

            // 6. 撈該專案所有 ReviewStage=1 既有 (QuestionId, ReviewerId) 防重複
            var existingPairs = (await conn.QueryAsync<(int QuestionId, int ReviewerId)>(
                """
                SELECT QuestionId, ReviewerId
                FROM dbo.MT_ReviewAssignments
                WHERE ProjectId = @ProjectId AND ReviewStage = 1;
                """,
                new { ProjectId = projectId }, tx))
                .Select(x => (x.QuestionId, x.ReviewerId))
                .ToHashSet();

            // 7. 逐題分配：選擇「負載最低 + ≠ Creator + 該題尚未分配給此人」的審題者
            const string insertAssignmentSql = """
                INSERT INTO dbo.MT_ReviewAssignments
                    (QuestionId, ProjectId, ReviewerId, ReviewStage, ReviewStatus, CreatedAt)
                VALUES
                    (@QuestionId, @ProjectId, @ReviewerId, 1, 0, SYSDATETIME());
                """;

            const string auditSql = """
                INSERT INTO dbo.MT_AuditLogs
                    (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, CreatedAt)
                VALUES
                    (NULL, @ProjectId, @Action, @TargetType, @TargetId, @OldValue, @NewValue, SYSDATETIME());
                """;

            var processed = 0;
            foreach (var (questionId, creatorId) in candidates)
            {
                // 候選：排除自命教師、排除該題已存在的 ReviewerId（避免唯一鍵衝突）
                var bestReviewer = loadCounts
                    .Where(kv => kv.Key != creatorId
                              && !existingPairs.Contains((questionId, kv.Key)))
                    .OrderBy(kv => kv.Value)
                    .ThenBy(kv => kv.Key)
                    .Select(kv => (int?)kv.Key)
                    .FirstOrDefault();

                if (bestReviewer is null)
                {
                    // 該題已分配過或無人可分配 → 跳過（idempotent 場景：第二次呼叫已分配完畢）
                    continue;
                }

                await conn.ExecuteAsync(insertAssignmentSql, new
                {
                    QuestionId = questionId,
                    ProjectId  = projectId,
                    ReviewerId = bestReviewer.Value
                }, tx);

                loadCounts[bestReviewer.Value]++;
                existingPairs.Add((questionId, bestReviewer.Value));

                await conn.ExecuteAsync(auditSql, new
                {
                    ProjectId  = projectId,
                    Action     = AuditLogAction.Modify,
                    TargetType = AuditLogTargetType.Questions,
                    TargetId   = questionId,
                    OldValue   = JsonSerializer.Serialize(new { Status = QuestionStatus.Completed }),
                    NewValue   = JsonSerializer.Serialize(new
                    {
                        Status     = QuestionStatus.Submitted,
                        Reason     = "CompositionPhaseEnded",
                        ReviewerId = bestReviewer.Value
                    })
                }, tx);

                processed++;
            }

            tx.Commit();
            return processed;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ====================================================================
    //  Plan_010 / Plan_011：階段推進時的批次 Status 升級 + 分配
    //  PhaseCode=4 互審修題：Status IN (2 已送審 / 3 互審中) → 4 PeerEditing
    //  PhaseCode=5 專家審題：升級 Status → 5；同步建立 ReviewStage=2 分配
    //  PhaseCode=6 專審修題：升級 Status → 6（無分配）
    //  PhaseCode=7 總召審題：升級 Status → 7；同步建立 ReviewStage=3 分配（含總召迴避）
    //  PhaseCode=8 總審修題：升級 Status → 8（無分配）
    // ====================================================================
    public async Task<int> EnsurePhaseTransitionAsync(int projectId)
    {
        using var conn = _db.CreateConnection();
        conn.Open();

        // 1. 取當前進行中階段
        var phaseCode = await conn.ExecuteScalarAsync<byte?>("""
            SELECT TOP 1 PhaseCode
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId
              AND PhaseCode > 1
              AND CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
            ORDER BY SortOrder;
            """, new { ProjectId = projectId });

        if (phaseCode is null) return 0;

        // PhaseCode=8 總審修題：依總審決策分流處理（Buffer 化）
        // 與其他階段走完全不同路徑，獨立 return
        if (phaseCode.Value == 8)
        {
            return await EnsureFinalEditingPhaseAsync(conn, projectId);
        }

        // 2. 依階段決定要升級的舊狀態與目標狀態
        // 各階段也接收前序「被遺漏」的題目（避免某題卡在舊狀態）
        var (fromStatuses, toStatus, reasonLabel) = phaseCode.Value switch
        {
            // 交互審題（3）：已送審 → 互審中（Plan_013 §3.3：補上狀態升級，迴避 case 遺漏）
            // 互審分配（ReviewStage=1）由 EnsureCompositionPhaseClosedAsync 負責，此處只升 Status
            3 => (new byte[] { QuestionStatus.Submitted },
                  QuestionStatus.PeerReviewing,
                  "PeerReviewingPhaseStart"),
            // 互審修題（4）：把已送審 / 互審中 → 互審修題中
            4 => (new byte[] { QuestionStatus.Submitted,
                               QuestionStatus.PeerReviewing },
                  QuestionStatus.PeerEditing,
                  "PeerEditingPhaseStart"),
            // 專家審題（5）：互審修題中 / 互審中 / 已送審（stragglers） → 專審中
            5 => (new byte[] { QuestionStatus.Submitted,
                               QuestionStatus.PeerReviewing,
                               QuestionStatus.PeerEditing },
                  QuestionStatus.ExpertReviewing,
                  "ExpertReviewingPhaseStart"),
            // 專審修題（6）：專審中 → 專審修題中
            6 => (new byte[] { QuestionStatus.ExpertReviewing },
                  QuestionStatus.ExpertEditing,
                  "ExpertEditingPhaseStart"),
            // 總召審題（7）：專審修題中 / 專審中 + 前序殘留 → 總審中
            7 => (new byte[] { QuestionStatus.Submitted,
                               QuestionStatus.PeerReviewing,
                               QuestionStatus.PeerEditing,
                               QuestionStatus.ExpertReviewing,
                               QuestionStatus.ExpertEditing },
                  QuestionStatus.FinalReviewing,
                  "FinalReviewingPhaseStart"),
            _ => (Array.Empty<byte>(), (byte)0, "")
        };

        if (fromStatuses.Length == 0) return 0;

        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // 3. 撈出待升級題目（給 AuditLog 用）
            var candidates = (await conn.QueryAsync<(int Id, byte OldStatus)>(
                """
                SELECT Id, Status AS OldStatus
                FROM dbo.MT_Questions
                WHERE ProjectId = @ProjectId
                  AND IsDeleted = 0
                  AND Status IN @Froms;
                """,
                new { ProjectId = projectId, Froms = fromStatuses.Select(b => (int)b).ToList() }, tx)).AsList();

            // 4. 批次 UPDATE（candidates 可能為空，例如題目早已升級至目標狀態，仍須繼續執行分配）
            if (candidates.Count > 0)
            {
                // 系統批次階段升級，僅改 Status；不動 UpdatedAt（保留命題老師最後編輯內容的時間語意）
                await conn.ExecuteAsync("""
                    UPDATE dbo.MT_Questions
                    SET Status = @ToStatus
                    WHERE ProjectId = @ProjectId
                      AND IsDeleted = 0
                      AND Status IN @Froms;
                    """,
                    new
                    {
                        ProjectId = projectId,
                        ToStatus  = toStatus,
                        Froms     = fromStatuses.Select(b => (int)b).ToList()
                    }, tx);

                // 5. 逐筆 AuditLog（UserId=NULL 表系統批次）
                const string auditSql = """
                    INSERT INTO dbo.MT_AuditLogs
                        (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, CreatedAt)
                    VALUES
                        (NULL, @ProjectId, @Action, @TargetType, @TargetId, @OldValue, @NewValue, SYSDATETIME());
                    """;

                foreach (var (qid, oldStatus) in candidates)
                {
                    await conn.ExecuteAsync(auditSql, new
                    {
                        ProjectId  = projectId,
                        Action     = AuditLogAction.Modify,
                        TargetType = AuditLogTargetType.Questions,
                        TargetId   = qid,
                        OldValue   = JsonSerializer.Serialize(new { Status = oldStatus }),
                        NewValue   = JsonSerializer.Serialize(new { Status = toStatus, Reason = reasonLabel })
                    }, tx);
                }
            }

            // Plan_011：PhaseCode=5/7 不論本次是否有題目升級，都要嘗試分配（idempotent）
            //  情境：題目早已被升級至目標 Status，但分配紀錄尚未建立（例如舊版本進入過該階段）
            //  - 5 專家審題：分配給「審題委員」，ReviewStage=2
            //  - 7 總召審題：分配給「總召集人」，ReviewStage=3，並排除其在 Stage=2 已審過的題目
            if (phaseCode.Value == 5)
            {
                await AssignExpertReviewersAsync(conn, tx, projectId);
            }
            else if (phaseCode.Value == 7)
            {
                await AssignFinalReviewersAsync(conn, tx, projectId);
            }

            tx.Commit();
            return candidates.Count;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ====================================================================
    //  PhaseCode=8 總審修題：殘留分流（PhaseCode=7→8 transition 與後續每次 coordinator 觸發時呼叫）
    //
    //  PhaseCode=7 期間總審 Approve 已即時設 Status=9（ReviewService.SubmitDecisionAsync），
    //  本 method 只負責把仍卡在 Status=7 的殘留題目升至 FinalEditing(8)，
    //  讓命題者可以進入 [審修作業區] 編輯後再送審。
    //
    //  Idempotent 守門：必須排除「老師已用 [完成送審] resubmit 過」的題目，否則會把回到
    //  Status=7 等待下一輪總審的題目錯誤升回 Status=8（資料來回切換）。
    //  判定為 resubmit 的條件：(Q, R, Stage=3) 存在某筆 Pending 紀錄，
    //  其 CreatedAt 晚於同 (Q, R, Stage=3) 已 Completed 的紀錄。
    //
    //  AuditLog 只記錄本次實際升級的題目（先 SELECT 後 UPDATE，避免每次重複記錄既有 Status=8）。
    // ====================================================================
    private async Task<int> EnsureFinalEditingPhaseAsync(IDbConnection conn, int projectId)
    {
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // 1. 撈出本次將升級的題目：Status=7 且非 resubmit 過
            //    （無 Stage=3 Assignment 的孤兒也涵蓋於此 — NOT EXISTS 的兩個 row 都找不到 → 條件成立）
            const string candidatesSql = """
                SELECT q.Id
                FROM dbo.MT_Questions q
                WHERE q.ProjectId = @ProjectId
                  AND q.IsDeleted = 0
                  AND q.Status    = @FinalReviewing
                  AND NOT EXISTS (
                      SELECT 1
                      FROM dbo.MT_ReviewAssignments p
                      INNER JOIN dbo.MT_ReviewAssignments c
                          ON c.QuestionId  = p.QuestionId
                         AND c.ReviewerId  = p.ReviewerId
                         AND c.ReviewStage = p.ReviewStage
                      WHERE p.QuestionId   = q.Id
                        AND p.ReviewStage  = 3
                        AND p.ReviewStatus = 0   -- Pending（resubmit 後新增的待審 row）
                        AND c.ReviewStatus = 2   -- Completed（先前已決策的歷史 row）
                        AND p.CreatedAt    > c.CreatedAt
                  );
                """;

            var ids = (await conn.QueryAsync<int>(candidatesSql, new
            {
                ProjectId      = projectId,
                FinalReviewing = (int)QuestionStatus.FinalReviewing
            }, tx)).ToList();

            if (ids.Count == 0)
            {
                tx.Commit();
                return 0;
            }

            // 2. 批次 UPDATE → FinalEditing(8)
            await conn.ExecuteAsync("""
                UPDATE dbo.MT_Questions
                SET Status = @FinalEditing
                WHERE Id IN @Ids;
                """, new
            {
                FinalEditing = (int)QuestionStatus.FinalEditing,
                Ids          = ids
            }, tx);

            // 3. 批次 AuditLog（UserId=NULL 表系統批次）
            const string auditSql = """
                INSERT INTO dbo.MT_AuditLogs
                    (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, CreatedAt)
                VALUES
                    (NULL, @ProjectId, @Action, @TargetType, @TargetId, @OldValue, @NewValue, SYSDATETIME());
                """;

            foreach (var qid in ids)
            {
                await conn.ExecuteAsync(auditSql, new
                {
                    ProjectId  = projectId,
                    Action     = AuditLogAction.Modify,
                    TargetType = AuditLogTargetType.Questions,
                    TargetId   = qid,
                    OldValue   = JsonSerializer.Serialize(new { Status = QuestionStatus.FinalReviewing }),
                    NewValue   = JsonSerializer.Serialize(new { Status = QuestionStatus.FinalEditing, Reason = "FinalEditingPhaseStart" })
                }, tx);
            }

            tx.Commit();
            return ids.Count;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ====================================================================
    //  總審修題「完成送審」：Status 8 → 7 + 為 Stage=3 既有 Reviewer INSERT 新 Pending row
    //  保留歷次決策歷程（不刪舊 Assignment 紀錄；走 INSERT 形成多輪審題軌跡）
    // ====================================================================
    public async Task<bool> ResubmitAfterFinalEditingAsync(int questionId, int userId)
    {
        using var conn = _db.CreateConnection();
        conn.Open();

        // 守門：必須是 FinalEditing(8) 才能送審；其他狀態回 false 由 caller toast 提示
        var status = await conn.ExecuteScalarAsync<byte?>(
            "SELECT Status FROM dbo.MT_Questions WHERE Id = @Id AND IsDeleted = 0;",
            new { Id = questionId });
        if (status != QuestionStatus.FinalEditing) return false;

        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // 1. Status 8 → 7
            await conn.ExecuteAsync(
                "UPDATE dbo.MT_Questions SET Status = @Final WHERE Id = @Id;",
                new { Id = questionId, Final = QuestionStatus.FinalReviewing }, tx);

            // 2. 取該題 Stage=3 所有 ReviewerId（distinct，跨多輪保留全部歷史審題者）
            var reviewerIds = (await conn.QueryAsync<int>(
                "SELECT DISTINCT ReviewerId FROM dbo.MT_ReviewAssignments WHERE QuestionId = @Id AND ReviewStage = 3;",
                new { Id = questionId }, tx)).AsList();

            // 3. 為每位 reviewer INSERT 新一筆 Pending(0) Assignment — 形成新一輪審題任務
            //    既有 row 不動（保留 Decision/Comment/DecidedAt 歷程）
            //    Idempotent：若該 (Q, R, Stage=3) 已存在 Pending row（前次 resubmit 殘留 / 重複按）則 skip insert，
            //    避免撞 UQ_MT_ReviewAssignments_Pending 唯一索引；同時整個 method 重複呼叫不會出錯
            const string insertSql = """
                INSERT INTO dbo.MT_ReviewAssignments
                    (QuestionId, ProjectId, ReviewerId, ReviewStage, ReviewStatus, CreatedAt)
                SELECT @QuestionId, q.ProjectId, @ReviewerId, 3, 0, SYSDATETIME()
                FROM dbo.MT_Questions q
                WHERE q.Id = @QuestionId
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.MT_ReviewAssignments
                      WHERE QuestionId   = @QuestionId
                        AND ReviewerId   = @ReviewerId
                        AND ReviewStage  = 3
                        AND ReviewStatus = 0
                  );
                """;
            foreach (var reviewerId in reviewerIds)
            {
                await conn.ExecuteAsync(insertSql,
                    new { QuestionId = questionId, ReviewerId = reviewerId }, tx);
            }

            // 4. AuditLog（題目層級）
            await conn.ExecuteAsync("""
                INSERT INTO dbo.MT_AuditLogs
                    (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, CreatedAt)
                SELECT @UserId, ProjectId, @Action, @TargetType, @TargetId, @OldValue, @NewValue, SYSDATETIME()
                FROM dbo.MT_Questions WHERE Id = @TargetId;
                """,
                new
                {
                    UserId     = userId,
                    Action     = AuditLogAction.Modify,
                    TargetType = AuditLogTargetType.Questions,
                    TargetId   = questionId,
                    OldValue   = JsonSerializer.Serialize(new { Status = (byte)QuestionStatus.FinalEditing }),
                    NewValue   = JsonSerializer.Serialize(new { Status = (byte)QuestionStatus.FinalReviewing, Reason = "FinalEditingResubmit" })
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
    //  Plan_011：專家審題（ReviewStage=2）分配
    //  - 池：MT_Roles.Name = N'審題委員'
    //  - 迴避：自命不自審（ReviewerId != Question.CreatorId）
    //  - Idempotent：透過 existingPairs 去重，可重複呼叫
    // ====================================================================
    private async Task AssignExpertReviewersAsync(IDbConnection conn, IDbTransaction tx, int projectId)
    {
        // 1. 撈題目候選（Status=ExpertReviewing 且未刪除）
        var candidates = (await conn.QueryAsync<(int Id, int CreatorId)>(
            """
            SELECT Id, CreatorId
            FROM dbo.MT_Questions
            WHERE ProjectId = @ProjectId
              AND IsDeleted = 0
              AND Status = @Expert
            ORDER BY Id;
            """,
            new { ProjectId = projectId, Expert = QuestionStatus.ExpertReviewing }, tx)).AsList();

        if (candidates.Count == 0) return;

        // 2. 撈審題池
        var reviewerIds = (await conn.QueryAsync<int>(
            """
            SELECT pm.UserId
            FROM dbo.MT_ProjectMembers pm
            INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
            INNER JOIN dbo.MT_Roles r ON r.Id = pmr.RoleId
            WHERE pm.ProjectId = @ProjectId AND r.Name = N'審題委員';
            """,
            new { ProjectId = projectId }, tx)).Distinct().ToList();

        // 3. 池不足前置警告（Plan_013 §3.1：改為 graceful return，不再阻擋 Status 升級）
        // 改法說明：原本 throw 會讓 EnsurePhaseTransitionAsync 的 transaction rollback，
        // Status 升級也一併回滾；PhaseTransitionCoordinator 的 catch 吞例外後設 cache，
        // 導致 60 秒內重試無效。改為 return 讓 Status 升級正常完成，分配記錄留空，
        // 下次 coordinator 觸發時再嘗試（需人員配置正確後才分得到題）。
        if (reviewerIds.Count == 0)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.MT_AuditLogs
                    (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, CreatedAt)
                VALUES
                    (NULL, @ProjectId, @Action, @TargetType, @TargetId, NULL, @NewValue, SYSDATETIME());
                """,
                new
                {
                    ProjectId  = projectId,
                    Action     = AuditLogAction.Modify,
                    TargetType = AuditLogTargetType.Projects,
                    TargetId   = projectId,
                    NewValue   = JsonSerializer.Serialize(new
                    {
                        Reason = "ExpertReviewerPoolEmpty",
                        Message = "專家審題階段已開始，但本梯次尚未指派任何審題委員"
                    })
                }, tx);
            return;  // graceful return：Status 升級已完成，分配跳過，待人員配置後重試
        }

        // 4. 載入既有 ReviewStage=2 負載基準（idempotent）
        var loadCounts = reviewerIds.ToDictionary(id => id, _ => 0);
        var existingLoads = await conn.QueryAsync<(int ReviewerId, int Cnt)>(
            """
            SELECT ReviewerId, COUNT(*) AS Cnt
            FROM dbo.MT_ReviewAssignments
            WHERE ProjectId = @ProjectId AND ReviewStage = 2
            GROUP BY ReviewerId;
            """,
            new { ProjectId = projectId }, tx);
        foreach (var (rid, cnt) in existingLoads)
        {
            if (loadCounts.ContainsKey(rid)) loadCounts[rid] = cnt;
        }

        // 5. 撈既有 (QuestionId, ReviewerId) 防重複
        var existingPairs = (await conn.QueryAsync<(int QuestionId, int ReviewerId)>(
            """
            SELECT QuestionId, ReviewerId
            FROM dbo.MT_ReviewAssignments
            WHERE ProjectId = @ProjectId AND ReviewStage = 2;
            """,
            new { ProjectId = projectId }, tx))
            .Select(x => (x.QuestionId, x.ReviewerId))
            .ToHashSet();

        // 6. 逐題分配：負載最低 + 不為 Creator + 該題尚未分給此人
        //    — 先從 existingPairs 衍生已分配過的 QuestionId 集合，loop 頂部 skip 整題去重
        var existingQuestionIds = existingPairs.Select(p => p.QuestionId).ToHashSet();

        // NOT EXISTS 版 INSERT：雙重保護（C# 層去重 + DB 層 WHERE NOT EXISTS）
        const string insertSql = """
            INSERT INTO dbo.MT_ReviewAssignments
                (QuestionId, ProjectId, ReviewerId, ReviewStage, ReviewStatus, CreatedAt)
            SELECT @QuestionId, @ProjectId, @ReviewerId, 2, 0, SYSDATETIME()
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.MT_ReviewAssignments
                WHERE QuestionId = @QuestionId AND ReviewStage = 2
            );
            """;

        const string auditSql = """
            INSERT INTO dbo.MT_AuditLogs
                (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, CreatedAt)
            VALUES
                (NULL, @ProjectId, @Action, @TargetType, @TargetId, @OldValue, @NewValue, SYSDATETIME());
            """;

        foreach (var (questionId, creatorId) in candidates)
        {
            // 此題已有 Stage=2 分配記錄 → 直接跳過（演算法主條件）
            if (existingQuestionIds.Contains(questionId)) continue;

            var bestReviewer = loadCounts
                .Where(kv => kv.Key != creatorId
                          && !existingPairs.Contains((questionId, kv.Key)))
                .OrderBy(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Select(kv => (int?)kv.Key)
                .FirstOrDefault();

            if (bestReviewer is null) continue; // 無人可分（所有審題者皆為命題者）

            var inserted = await conn.ExecuteAsync(insertSql, new
            {
                QuestionId = questionId,
                ProjectId  = projectId,
                ReviewerId = bestReviewer.Value
            }, tx);

            if (inserted == 0) continue; // NOT EXISTS 攔截（資料庫層去重）

            loadCounts[bestReviewer.Value]++;
            existingPairs.Add((questionId, bestReviewer.Value));
            existingQuestionIds.Add(questionId); // 同步更新，防後續 loop 重複

            await conn.ExecuteAsync(auditSql, new
            {
                ProjectId  = projectId,
                Action     = AuditLogAction.Modify,
                TargetType = AuditLogTargetType.Questions,
                TargetId   = questionId,
                OldValue   = JsonSerializer.Serialize(new { ReviewStage = 2, Status = "Pending" }),
                NewValue   = JsonSerializer.Serialize(new
                {
                    ReviewStage = 2,
                    ReviewerId = bestReviewer.Value,
                    Reason = "ExpertReviewAssigned"
                })
            }, tx);
        }
    }

    // ====================================================================
    //  Plan_011：總召審題（ReviewStage=3）分配
    //  - 池：MT_Roles.Name = N'總召集人'
    //  - 迴避 A：不分配 Stage=2 已審過題目
    //  - 迴避 B：池下限 — 兼任審題委員時強制 ≥ 2
    //  - 迴避 C：自命不自審
    // ====================================================================
    private async Task AssignFinalReviewersAsync(IDbConnection conn, IDbTransaction tx, int projectId)
    {
        var candidates = (await conn.QueryAsync<(int Id, int CreatorId)>(
            """
            SELECT Id, CreatorId
            FROM dbo.MT_Questions
            WHERE ProjectId = @ProjectId
              AND IsDeleted = 0
              AND Status = @Final
            ORDER BY Id;
            """,
            new { ProjectId = projectId, Final = QuestionStatus.FinalReviewing }, tx)).AsList();

        if (candidates.Count == 0) return;

        var reviewerIds = (await conn.QueryAsync<int>(
            """
            SELECT pm.UserId
            FROM dbo.MT_ProjectMembers pm
            INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
            INNER JOIN dbo.MT_Roles r ON r.Id = pmr.RoleId
            WHERE pm.ProjectId = @ProjectId AND r.Name = N'總召集人';
            """,
            new { ProjectId = projectId }, tx)).Distinct().ToList();

        const string warnSql = """
            INSERT INTO dbo.MT_AuditLogs
                (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, CreatedAt)
            VALUES
                (NULL, @ProjectId, @Action, @TargetType, @TargetId, NULL, @NewValue, SYSDATETIME());
            """;

        // 池為 0 → graceful return（Plan_013 §3.1：同 Expert 池空，不阻擋 Status 升級）
        if (reviewerIds.Count == 0)
        {
            await conn.ExecuteAsync(warnSql, new
            {
                ProjectId  = projectId,
                Action     = AuditLogAction.Modify,
                TargetType = AuditLogTargetType.Projects,
                TargetId   = projectId,
                NewValue   = JsonSerializer.Serialize(new
                {
                    Reason  = "FinalReviewerPoolEmpty",
                    Message = "總召審題階段已開始，但本梯次尚未指派任何總召集人"
                })
            }, tx);
            return;  // graceful return，Status 升級已完成，分配跳過，待人員補齊後重試
        }

        // 撈 Stage=2 已審過的 (QuestionId, ReviewerId)，作為迴避基準
        var stage2Pairs = (await conn.QueryAsync<(int QuestionId, int ReviewerId)>(
            """
            SELECT QuestionId, ReviewerId
            FROM dbo.MT_ReviewAssignments
            WHERE ProjectId = @ProjectId AND ReviewStage = 2;
            """,
            new { ProjectId = projectId }, tx))
            .Select(x => (x.QuestionId, x.ReviewerId))
            .ToHashSet();

        // 兼任審題委員的總召（出現在 stage2Pairs 任何 ReviewerId 中且也在 reviewerIds）
        var stage2ReviewerSet = stage2Pairs.Select(p => p.ReviewerId).ToHashSet();
        var dualRoleCount = reviewerIds.Count(id => stage2ReviewerSet.Contains(id));

        // 池下限保護：兼任 ≥ 1 時，總召池須 ≥ 2
        if (dualRoleCount >= 1 && reviewerIds.Count < 2)
        {
            await conn.ExecuteAsync(warnSql, new
            {
                ProjectId  = projectId,
                Action     = AuditLogAction.Modify,
                TargetType = AuditLogTargetType.Projects,
                TargetId   = projectId,
                NewValue   = JsonSerializer.Serialize(new
                {
                    Reason  = "FinalReviewerPoolUnderQuota",
                    Message = "總召集人兼任審題委員時，至少需設定 2 名總召集人"
                })
            }, tx);
            return;  // graceful return（Plan_013：不阻擋 Status 升級，分配跳過，待人員補齊後重試）
        }

        // 載入既有 Stage=3 負載
        var loadCounts = reviewerIds.ToDictionary(id => id, _ => 0);
        var existingLoads = await conn.QueryAsync<(int ReviewerId, int Cnt)>(
            """
            SELECT ReviewerId, COUNT(*) AS Cnt
            FROM dbo.MT_ReviewAssignments
            WHERE ProjectId = @ProjectId AND ReviewStage = 3
            GROUP BY ReviewerId;
            """,
            new { ProjectId = projectId }, tx);
        foreach (var (rid, cnt) in existingLoads)
        {
            if (loadCounts.ContainsKey(rid)) loadCounts[rid] = cnt;
        }

        // 既有 Stage=3 (QuestionId, ReviewerId) 防重
        var existingPairs = (await conn.QueryAsync<(int QuestionId, int ReviewerId)>(
            """
            SELECT QuestionId, ReviewerId
            FROM dbo.MT_ReviewAssignments
            WHERE ProjectId = @ProjectId AND ReviewStage = 3;
            """,
            new { ProjectId = projectId }, tx))
            .Select(x => (x.QuestionId, x.ReviewerId))
            .ToHashSet();

        // 把 Stage=2 的 (Q, R) 也納入 existingPairs，自動迴避（總召不審自己在專審審過的題）
        foreach (var pair in stage2Pairs)
        {
            existingPairs.Add(pair);
        }

        // 已分配過 Stage=3 的 QuestionId — loop 頂部 skip 整題去重
        var existingQuestionIds = existingPairs
            .Where(p => stage2Pairs.Contains(p) == false || existingPairs.Contains(p))
            .Select(p => p.QuestionId)
            .ToHashSet();
        // 重新取 Stage=3 既有 QuestionId（不含 Stage=2 迴避集合）
        existingQuestionIds = (await conn.QueryAsync<int>(
            """
            SELECT QuestionId
            FROM dbo.MT_ReviewAssignments
            WHERE ProjectId = @ProjectId AND ReviewStage = 3;
            """,
            new { ProjectId = projectId }, tx)).ToHashSet();

        // NOT EXISTS 版 INSERT：雙重保護（C# 層去重 + DB 層 WHERE NOT EXISTS）
        const string insertSql = """
            INSERT INTO dbo.MT_ReviewAssignments
                (QuestionId, ProjectId, ReviewerId, ReviewStage, ReviewStatus, CreatedAt)
            SELECT @QuestionId, @ProjectId, @ReviewerId, 3, 0, SYSDATETIME()
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.MT_ReviewAssignments
                WHERE QuestionId = @QuestionId AND ReviewStage = 3
            );
            """;

        const string auditSql = """
            INSERT INTO dbo.MT_AuditLogs
                (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, CreatedAt)
            VALUES
                (NULL, @ProjectId, @Action, @TargetType, @TargetId, @OldValue, @NewValue, SYSDATETIME());
            """;

        foreach (var (questionId, creatorId) in candidates)
        {
            // 此題已有 Stage=3 分配記錄 → 直接跳過（演算法主條件）
            if (existingQuestionIds.Contains(questionId)) continue;

            var bestReviewer = loadCounts
                .Where(kv => kv.Key != creatorId
                          && !existingPairs.Contains((questionId, kv.Key)))
                .OrderBy(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Select(kv => (int?)kv.Key)
                .FirstOrDefault();

            if (bestReviewer is null) continue;

            var inserted = await conn.ExecuteAsync(insertSql, new
            {
                QuestionId = questionId,
                ProjectId  = projectId,
                ReviewerId = bestReviewer.Value
            }, tx);

            if (inserted == 0) continue; // NOT EXISTS 攔截（資料庫層去重）

            loadCounts[bestReviewer.Value]++;
            existingPairs.Add((questionId, bestReviewer.Value));
            existingQuestionIds.Add(questionId); // 同步更新，防後續 loop 重複

            await conn.ExecuteAsync(auditSql, new
            {
                ProjectId  = projectId,
                Action     = AuditLogAction.Modify,
                TargetType = AuditLogTargetType.Questions,
                TargetId   = questionId,
                OldValue   = JsonSerializer.Serialize(new { ReviewStage = 3, Status = "Pending" }),
                NewValue   = JsonSerializer.Serialize(new
                {
                    ReviewStage = 3,
                    ReviewerId  = bestReviewer.Value,
                    Reason      = "FinalReviewAssigned"
                })
            }, tx);
        }
    }

    // ====================================================================
    //  Plan_010：審修作業區「修題」Slide-Over
    // ====================================================================

    /// <summary>
    /// 一次撈齊修題 Slide-Over 所需資料（題目、跨階段意見、自己歷次修題說明、當前階段、退回計數）。
    /// 題目找不到或已軟刪 → 回 null。
    /// </summary>
    public async Task<RevisionSlideOverData?> GetRevisionDataAsync(int questionId, int currentUserId)
    {
        // 1. 題目本體（沿用 GetByIdAsync 邏輯但合併進來省一次連線）
        var question = await GetByIdAsync(questionId);
        if (question is null) return null;

        using var conn = _db.CreateConnection();

        // 2. 取題目當前 Status / ProjectId（IsDeleted 排除）
        var meta = await conn.QueryFirstOrDefaultAsync<(byte Status, int ProjectId)>(
            "SELECT Status, ProjectId FROM dbo.MT_Questions WHERE Id = @Id AND IsDeleted = 0",
            new { Id = questionId });
        if (meta.ProjectId == 0) return null;

        // 3. 跨階段審題意見（匿名化：同階段內依 DecidedAt 排序給 A/B/C）
        const string commentSql = """
            SELECT Stage, Comment, DecidedAt, AnonIndex
            FROM (
                SELECT
                    ra.ReviewStage AS Stage,
                    ra.Comment,
                    ra.DecidedAt,
                    ROW_NUMBER() OVER (PARTITION BY ra.ReviewStage
                                       ORDER BY ra.DecidedAt, ra.Id) AS AnonIndex
                FROM dbo.MT_ReviewAssignments ra
                WHERE ra.QuestionId = @Id
                  AND ra.Comment IS NOT NULL
                  AND LEN(ra.Comment) > 0
            ) t
            ORDER BY Stage, DecidedAt;
            """;
        var commentRows = (await conn.QueryAsync<(byte Stage, string Comment, DateTime DecidedAt, int AnonIndex)>(
            commentSql, new { Id = questionId })).AsList();

        var comments = commentRows.Select(r => new ReviewCommentEntry
        {
            Stage     = r.Stage,
            AnonName  = $"審題委員 {(char)('A' + (r.AnonIndex - 1) % 26)}",
            Comment   = r.Comment ?? "",
            DecidedAt = r.DecidedAt
        }).ToList();

        // 4. 自己歷次修題說明（Plan_014：列表最新在最上）
        const string mySql = """
            SELECT Stage, Content, CreatedAt
            FROM dbo.MT_RevisionReplies
            WHERE QuestionId = @Id AND UserId = @UserId
            ORDER BY CreatedAt DESC;
            """;
        var myReplies = (await conn.QueryAsync<RevisionReplyEntry>(
            mySql, new { Id = questionId, UserId = currentUserId })).AsList();

        // 5. 當前進行中階段（無 → null）
        var phaseRow = await conn.QueryFirstOrDefaultAsync<(byte PhaseCode, DateTime EndDate)>(
            """
            SELECT TOP 1 PhaseCode, EndDate
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId
              AND PhaseCode > 1
              AND CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
            ORDER BY SortOrder;
            """, new { ProjectId = meta.ProjectId });

        // 6. 總審退回次數（給總修階段的 [送出再審] 用）
        var returnCount = await conn.ExecuteScalarAsync<int?>(
            "SELECT TOP 1 ReturnCount FROM dbo.MT_ReviewReturnCounts WHERE QuestionId = @Id",
            new { Id = questionId }) ?? 0;

        // Plan_013：本輪起點時間 — 上次總審退回時間（MAX DecidedAt for ReviewStage=3 且 Decision IN (2,3)）
        // 沒有任何總審退回紀錄時 fallback 為 1900-01-01（等同於不過濾，全部 reply 視為本輪）
        var roundStartedAt = await conn.ExecuteScalarAsync<DateTime?>(
            """
            SELECT MAX(DecidedAt)
            FROM dbo.MT_ReviewAssignments
            WHERE QuestionId = @Id
              AND ReviewStage = 3
              AND Decision IN (2, 3);
            """, new { Id = questionId }) ?? new DateTime(1900, 1, 1);

        // 7. 當前階段最新一筆「本輪」reply（CreatedAt > roundStartedAt 才算本輪；舊輪不取）
        var draft = myReplies
            .Where(r => r.Stage == phaseRow.PhaseCode && r.CreatedAt > roundStartedAt)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefault()?.Content ?? "";

        return new RevisionSlideOverData
        {
            Question            = question,
            Comments            = comments,
            MyReplies           = myReplies,
            CurrentPhaseCode    = phaseRow.PhaseCode,
            PhaseEndDate        = phaseRow.PhaseCode == 0 ? null : phaseRow.EndDate,
            CurrentDraftContent = draft,
            // Plan_013：HasReplied 只看本輪（CreatedAt > roundStartedAt）
            HasReplied          = myReplies.Any(r => r.Stage == phaseRow.PhaseCode && r.CreatedAt > roundStartedAt),
            FinalReturnCount    = returnCount,
            QStatus             = meta.Status
        };
    }

    /// <summary>
    /// 儲存修題（題目本體 + 修題說明）。
    /// 1. 階段對齊驗證（沿用 Plan_009 的 GetCurrentPhaseCodeInTxAsync）
    /// 2. UPDATE MT_Questions（Status 不變，僅內容）
    /// 3. UPSERT MT_SubQuestions
    /// 4. INSERT MT_RevisionReplies（每輪一筆 append-only；跨輪以 CreatedAt 區辨本輪／歷史）
    /// 5. 寫 AuditLog（Action=Modify、Reason=Revision）
    /// 不變更 Question.Status；總審「送出再審」由另一支 SubmitFinalRevisionAsync 處理（Plan_011）。
    /// </summary>
    public async Task<bool> SaveRevisionAsync(SaveRevisionRequest req, int operatorUserId)
    {
        if (string.IsNullOrWhiteSpace(req.RevisionNote))
            throw new InvalidOperationException("修題說明為必填欄位。");

        var typeId = QuestionConstants.TypeKeyToId[req.FormData.QuestionType];

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // 1. 取題目當前 Status / ProjectId
            var meta = await conn.QueryFirstOrDefaultAsync<QuestionMetaDto>(
                "SELECT Status, ProjectId FROM dbo.MT_Questions WHERE Id = @Id AND IsDeleted = 0",
                new { Id = req.QuestionId }, tx);
            if (meta is null) return false;

            // 2. Status 必須在修題狀態（4/6/8）；階段必須對齊
            if (meta.Status is not (QuestionStatus.PeerEditing or QuestionStatus.ExpertEditing or QuestionStatus.FinalEditing))
                throw new InvalidOperationException("題目目前不在修題狀態，無法儲存修題。");

            var phaseCode = await GetCurrentPhaseCodeInTxAsync(conn, tx, meta.ProjectId);
            var requiredPhase = (byte)meta.Status;   // PeerEditing=4 / ExpertEditing=6 / FinalEditing=8
            if (phaseCode != requiredPhase)
                throw new InvalidOperationException("修題期間已結束，無法儲存變更。");

            // 3. UPDATE 題目（沿用既有 update SQL，但 Status 維持原值）
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
            await conn.ExecuteAsync(updateSql, new
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

            // 4. UPSERT 子題
            await UpsertSubQuestionsAsync(conn, tx, req.QuestionId, req.FormData, operatorUserId, meta.ProjectId);

            // 5. INSERT MT_RevisionReplies — 純 append-only（每輪一筆獨立 row）
            //    Plan_014：總修階段（Stage=8）允許跨輪多次回覆，每輪退回後重新送出都產生新 row
            //    判別「本輪 reply」靠 CreatedAt > 上次總審退回 DecidedAt（見 ListAsync / GetRevisionDataAsync 等處）
            //    Stage=4/6 線性單輪，append-only 等同每題每階段最多一筆，行為不變
            const string insertReplySql = """
                INSERT INTO dbo.MT_RevisionReplies (QuestionId, UserId, Stage, Content, CreatedAt)
                VALUES (@Qid, @Uid, @Stage, @Content, SYSDATETIME());
                """;
            await conn.ExecuteAsync(insertReplySql, new
            {
                Qid     = req.QuestionId,
                Uid     = operatorUserId,
                Stage   = phaseCode,
                Content = req.RevisionNote
            }, tx);

            // 6. AuditLog
            await WriteAuditLogAsync(conn, tx, operatorUserId, meta.ProjectId,
                AuditLogAction.Modify, req.QuestionId,
                oldValue: new { Status = meta.Status },
                newValue: new { Status = meta.Status, Reason = "Revision", Stage = phaseCode });

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
    //  私有 Helper
    // ====================================================================

    /// <summary>
    /// 取指定年度的下一個流水號。並發安全（MERGE + HOLDLOCK 達到 SERIALIZABLE 鎖）。
    /// 一條 SQL 原子完成「找 / 建 / +1 / 取值」，徹底消除年首筆並發撞 PK 的風險。
    /// </summary>
    private static async Task<int> GetNextQuestionNumberAsync(IDbConnection conn, IDbTransaction tx, int year)
    {
        // MERGE：年度 row 已存在 → +1 後回傳舊值；不存在 → INSERT (Year, 2) 後回傳 1
        // HOLDLOCK 保證 key range lock，避免兩個 transaction 都判定「不存在」而同時 INSERT 撞 PK
        const string sql = """
            MERGE dbo.MT_QuestionCodeSequence WITH (HOLDLOCK) AS target
            USING (VALUES (@Year)) AS src(Year) ON target.Year = src.Year
            WHEN MATCHED     THEN UPDATE SET NextValue = NextValue + 1
            WHEN NOT MATCHED THEN INSERT (Year, NextValue) VALUES (@Year, 2)
            OUTPUT
                CASE WHEN $action = 'INSERT' THEN 1 ELSE deleted.NextValue END AS NextNo;
            """;

        return await conn.QuerySingleAsync<int>(sql, new { Year = year }, tx);
    }

    /// <summary>
    /// CreateAsync 用：把表單子題清單純 INSERT。
    /// 因為母題剛 INSERT 完，子題不會有既存 Id（即使表單帶了 Id 也會被忽略）。
    /// INSERT 後把新 Id 寫回 formData，以便後續 UpdateAsync 能一致追蹤。
    /// </summary>
    private static async Task InsertSubQuestionsAsync(IDbConnection conn, IDbTransaction tx,
        int parentId, QuestionFormData formData)
    {
        const string sql = """
            INSERT INTO dbo.MT_SubQuestions (
                ParentQuestionId, SortOrder,
                Stem, CorrectAnswer,
                OptionA, OptionB, OptionC, OptionD,
                Analysis, CoreAbility, Indicator, FixedDifficulty
            )
            OUTPUT INSERTED.Id
            VALUES (
                @ParentId, @SortOrder,
                @Stem, @Answer,
                @OptA, @OptB, @OptC, @OptD,
                @Analysis, @CoreAbility, @Indicator, @FixedDifficulty
            );
            """;

        if (formData.QuestionType == QuestionTypeCodes.ReadGroup)
        {
            for (var i = 0; i < formData.ReadSubQuestions.Count; i++)
            {
                var sq = formData.ReadSubQuestions[i];
                sq.Id = await conn.QuerySingleAsync<int>(sql, BuildReadSubParams(parentId, i + 1, sq), tx);
            }
        }
        else if (formData.QuestionType == QuestionTypeCodes.ShortGroup)
        {
            for (var i = 0; i < formData.ShortSubQuestions.Count; i++)
            {
                var sq = formData.ShortSubQuestions[i];
                sq.Id = await conn.QuerySingleAsync<int>(sql, BuildShortSubParams(parentId, i + 1, sq), tx);
            }
        }
        else if (formData.QuestionType == QuestionTypeCodes.ListenGroup)
        {
            for (var i = 0; i < formData.ListenGroupSubQuestions.Count; i++)
            {
                var sq = formData.ListenGroupSubQuestions[i];
                sq.Id = await conn.QuerySingleAsync<int>(sql, BuildListenSubParams(parentId, i + 1, sq), tx);
            }
        }
        // 其他四種非題組型題目：不寫子題
    }

    /// <summary>
    /// UpdateAsync 用：依 SubId 比對「DB 既有未刪 Id 集合 vs 表單帶來的 Id 集合」：
    /// - Id == 0  → INSERT，OUTPUT 取回新 Id 寫回 formData
    /// - Id  > 0  → UPDATE 該列所有欄位 + SortOrder
    /// - 缺席者   → IsDeleted=1, DeletedAt=now，並寫一筆 AuditLog（每筆獨立）
    /// </summary>
    private async Task UpsertSubQuestionsAsync(IDbConnection conn, IDbTransaction tx,
        int parentId, QuestionFormData formData, int operatorUserId, int projectId)
    {
        // 1. 撈出 DB 端目前未刪的子題 Id
        var existingIds = (await conn.QueryAsync<int>(
            "SELECT Id FROM dbo.MT_SubQuestions WHERE ParentQuestionId = @Id AND IsDeleted = 0",
            new { Id = parentId }, tx)).ToHashSet();

        // 2. 表單帶上來的子題（依題型）
        var formIds = new HashSet<int>();

        const string insertSql = """
            INSERT INTO dbo.MT_SubQuestions (
                ParentQuestionId, SortOrder,
                Stem, CorrectAnswer,
                OptionA, OptionB, OptionC, OptionD,
                Analysis, CoreAbility, Indicator, FixedDifficulty
            )
            OUTPUT INSERTED.Id
            VALUES (
                @ParentId, @SortOrder,
                @Stem, @Answer,
                @OptA, @OptB, @OptC, @OptD,
                @Analysis, @CoreAbility, @Indicator, @FixedDifficulty
            );
            """;

        const string updateSql = """
            UPDATE dbo.MT_SubQuestions SET
                SortOrder       = @SortOrder,
                Stem            = @Stem,
                CorrectAnswer   = @Answer,
                OptionA         = @OptA,
                OptionB         = @OptB,
                OptionC         = @OptC,
                OptionD         = @OptD,
                Analysis        = @Analysis,
                CoreAbility     = @CoreAbility,
                Indicator       = @Indicator,
                FixedDifficulty = @FixedDifficulty
            WHERE Id = @Id AND IsDeleted = 0;
            """;

        if (formData.QuestionType == QuestionTypeCodes.ReadGroup)
        {
            for (var i = 0; i < formData.ReadSubQuestions.Count; i++)
            {
                var sq = formData.ReadSubQuestions[i];
                var p  = BuildReadSubParams(parentId, i + 1, sq);
                if (sq.Id == 0)
                    sq.Id = await conn.QuerySingleAsync<int>(insertSql, p, tx);
                else
                    await conn.ExecuteAsync(updateSql, MergeId(p, sq.Id), tx);
                formIds.Add(sq.Id);
            }
        }
        else if (formData.QuestionType == QuestionTypeCodes.ShortGroup)
        {
            for (var i = 0; i < formData.ShortSubQuestions.Count; i++)
            {
                var sq = formData.ShortSubQuestions[i];
                var p  = BuildShortSubParams(parentId, i + 1, sq);
                if (sq.Id == 0)
                    sq.Id = await conn.QuerySingleAsync<int>(insertSql, p, tx);
                else
                    await conn.ExecuteAsync(updateSql, MergeId(p, sq.Id), tx);
                formIds.Add(sq.Id);
            }
        }
        else if (formData.QuestionType == QuestionTypeCodes.ListenGroup)
        {
            for (var i = 0; i < formData.ListenGroupSubQuestions.Count; i++)
            {
                var sq = formData.ListenGroupSubQuestions[i];
                var p  = BuildListenSubParams(parentId, i + 1, sq);
                if (sq.Id == 0)
                    sq.Id = await conn.QuerySingleAsync<int>(insertSql, p, tx);
                else
                    await conn.ExecuteAsync(updateSql, MergeId(p, sq.Id), tx);
                formIds.Add(sq.Id);
            }
        }

        // 3. 缺席者（DB 有但表單沒了）→ 軟刪除 + 每筆獨立 AuditLog
        var orphanIds = existingIds.Except(formIds).ToList();
        if (orphanIds.Count == 0) return;

        await conn.ExecuteAsync("""
            UPDATE dbo.MT_SubQuestions
            SET IsDeleted = 1, DeletedAt = SYSDATETIME()
            WHERE Id IN @Ids;
            """, new { Ids = orphanIds }, tx);

        foreach (var subId in orphanIds)
        {
            await WriteAuditLogAsync(conn, tx, operatorUserId, projectId,
                AuditLogAction.Modify, subId,
                oldValue: new { IsDeleted = false, ParentQuestionId = parentId },
                newValue: new { IsDeleted = true,  ParentQuestionId = parentId });
        }
    }

    // ---- 子題 INSERT/UPDATE 共用參數組裝 ----
    private static object BuildReadSubParams(int parentId, int sortOrder, SubQuestionChoice sq) => new
    {
        ParentId        = parentId,
        SortOrder       = sortOrder,
        Stem            = NullIfEmpty(sq.Stem),
        Answer          = NullIfEmpty(sq.Answer),
        OptA            = SafeOption(sq.Options, 0),
        OptB            = SafeOption(sq.Options, 1),
        OptC            = SafeOption(sq.Options, 2),
        OptD            = SafeOption(sq.Options, 3),
        Analysis        = NullIfEmpty(sq.Analysis),
        CoreAbility     = (byte?)null,
        Indicator       = (byte?)null,
        FixedDifficulty = (byte?)null
    };

    private static object BuildShortSubParams(int parentId, int sortOrder, SubQuestionFreeResponse sq) => new
    {
        ParentId        = parentId,
        SortOrder       = sortOrder,
        Stem            = NullIfEmpty(sq.Stem),
        Answer          = (string?)null,
        OptA            = (string?)null,
        OptB            = (string?)null,
        OptC            = (string?)null,
        OptD            = (string?)null,
        Analysis        = NullIfEmpty(sq.Analysis),
        sq.CoreAbility,
        sq.Indicator,
        FixedDifficulty = (byte?)null
    };

    private static object BuildListenSubParams(int parentId, int sortOrder, ListenGroupSubQuestion sq) => new
    {
        ParentId        = parentId,
        SortOrder       = sortOrder,
        Stem            = NullIfEmpty(sq.Stem),
        Answer          = NullIfEmpty(sq.Answer),
        OptA            = SafeOption(sq.Options, 0),
        OptB            = SafeOption(sq.Options, 1),
        OptC            = SafeOption(sq.Options, 2),
        OptD            = SafeOption(sq.Options, 3),
        Analysis        = NullIfEmpty(sq.Analysis),
        sq.CoreAbility,
        Indicator       = sq.DetailIndicator,
        FixedDifficulty = (byte?)sq.FixedDifficulty
    };

    /// <summary>把 BuildXxxSubParams 的匿名物件再合併一個 Id 進去（給 UPDATE 用）。</summary>
    private static DynamicParameters MergeId(object source, int id)
    {
        var dp = new DynamicParameters(source);
        dp.Add("Id", id);
        return dp;
    }

    /// <summary>
    /// 寫入系統稽核 LOG（MT_AuditLogs）。
    /// TargetType 固定為 Questions；oldValue/newValue 以 JSON 序列化僅記關鍵欄位。
    /// </summary>
    private async Task WriteAuditLogAsync(IDbConnection conn, IDbTransaction tx,
        int userId, int? projectId, byte action, int targetId,
        object? oldValue, object? newValue)
    {
        const string sql = """
            INSERT INTO dbo.MT_AuditLogs
                (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, IpAddress, CreatedAt)
            VALUES
                (@UserId, @ProjectId, @Action, @TargetType, @TargetId, @OldValue, @NewValue, @IpAddress, SYSDATETIME());
            """;

        var ip = _httpAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

        await conn.ExecuteAsync(sql, new
        {
            UserId     = userId,
            ProjectId  = projectId,
            Action     = action,
            TargetType = AuditLogTargetType.Questions,
            TargetId   = targetId,
            OldValue   = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValue   = newValue is null ? null : JsonSerializer.Serialize(newValue),
            IpAddress  = ip
        }, tx);
    }

    /// <summary>組 INSERT MT_Questions 的 Dapper 參數物件（CreateAsync 用）。空字串欄位轉 NULL。</summary>
    private static object BuildQuestionParams(QuestionFormData f, int projectId, int creatorUserId,
        byte status, int typeId, string code) => new
    {
        ProjectId      = projectId,
        TypeId         = typeId,
        Code           = code,
        CreatorId      = creatorUserId,
        Status         = status,
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
    };

    /// <summary>從選項陣列安全取出索引（不存在或空白回 NULL，避免 DB 存空字串）。</summary>
    private static string? SafeOption(string[] options, int index)
        => options.Length > index ? NullIfEmpty(options[index]) : null;

    /// <summary>空字串 / 純空白一律轉 NULL，避免 NVARCHAR 欄位儲存無意義的 ''。</summary>
    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;

    // ====================================================================
    //  Dapper DTO（私有，僅 Service 內部使用）
    // ====================================================================

    private sealed class QuestionRowDto
    {
        public int Id { get; set; }
        public string QuestionCode { get; set; } = "";
        public int QuestionTypeId { get; set; }
        public byte Status { get; set; }
        public byte? Level { get; set; }
        public byte? Difficulty { get; set; }
        public string? Stem { get; set; }
        public string? Analysis { get; set; }
        public string? CorrectAnswer { get; set; }
        public string? OptionA { get; set; }
        public string? OptionB { get; set; }
        public string? OptionC { get; set; }
        public string? OptionD { get; set; }
        public string? ArticleTitle { get; set; }
        public string? ArticleContent { get; set; }
        public string? AudioUrl { get; set; }
        public string? GradingNote { get; set; }
        public byte? Topic { get; set; }
        public byte? Subtopic { get; set; }
        public byte? Genre { get; set; }
        public byte? Material { get; set; }
        public byte? WritingMode { get; set; }
        public byte? AudioType { get; set; }
        public byte? CoreAbility { get; set; }
        public byte? DetailIndicator { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class SubQuestionRowDto
    {
        public int Id { get; set; }
        public int ParentQuestionId { get; set; }
        public int SortOrder { get; set; }
        public string? Stem { get; set; }
        public string? CorrectAnswer { get; set; }
        public string? OptionA { get; set; }
        public string? OptionB { get; set; }
        public string? OptionC { get; set; }
        public string? OptionD { get; set; }
        public string? Analysis { get; set; }
        public byte? CoreAbility { get; set; }
        public byte? Indicator { get; set; }
        public byte? FixedDifficulty { get; set; }
    }

    private sealed class QuestionMetaDto
    {
        public byte Status { get; set; }
        public int ProjectId { get; set; }
    }

    private sealed class StatusCountDto
    {
        public byte Status { get; set; }
        public int Cnt { get; set; }
    }

    private sealed class QuestionListRowDto
    {
        public int Id { get; set; }
        public string QuestionCode { get; set; } = "";
        public int TypeId { get; set; }
        public byte? Level { get; set; }
        public byte? Difficulty { get; set; }
        public byte Status { get; set; }
        public string? SummaryHtml { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }
        public int SubQuestionCount { get; set; }
        public string? CreatorName { get; set; }
        public bool HasRepliedThisStage { get; set; }
    }
}
