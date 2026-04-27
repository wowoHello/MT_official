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

    // P4 新增：使用者在某專案是否為成員（用於審題任務權限攔截）
    Task<bool> IsProjectMemberAsync(int userId, int projectId);
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
                Topic, Subtopic, Genre, Material, WritingMode, AudioType, CoreAbility, DetailIndicator
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
            AudioUrl        = master.AudioUrl ?? ""
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
        byte[] statuses = filter.StatusFilter is not null
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

        // ⚠️ Dapper 把 byte[] 當作 VARBINARY，不會展開為 IN (...)；必須轉成 List<int> 才會自動展開
        if (statuses.Length > 0)
            args.Add("Statuses", statuses.Select(b => (int)b).ToList());

        // 動態 IsDeleted 條件：CwtList 預設只看未刪；Overview 設 IncludeDeleted=true 拿全部
        var deletedClauseQ = filter.IncludeDeleted ? "" : "AND q.IsDeleted = 0";

        var countSql = $"""
            SELECT COUNT(*)
            FROM dbo.MT_Questions q
            WHERE q.ProjectId = @ProjectId
              {deletedClauseQ}
              AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
              {(statuses.Length > 0 ? "AND q.Status IN @Statuses" : "")}
              AND (@QuestionTypeId IS NULL OR q.QuestionTypeId = @QuestionTypeId)
              AND (@Level IS NULL OR q.Level = @Level)
              AND (@Keyword IS NULL OR q.Stem LIKE '%' + @Keyword + '%' OR q.QuestionCode LIKE '%' + @Keyword + '%');
            """;

        var listSql = $"""
            SELECT
                q.Id, q.QuestionCode,
                q.QuestionTypeId AS TypeId,
                q.Level, q.Difficulty, q.Status,
                q.Stem AS SummaryHtml,
                q.CreatedAt, q.UpdatedAt,
                q.IsDeleted,
                (SELECT COUNT(*) FROM dbo.MT_SubQuestions sq
                 WHERE sq.ParentQuestionId = q.Id AND sq.IsDeleted = 0) AS SubQuestionCount
            FROM dbo.MT_Questions q
            WHERE q.ProjectId = @ProjectId
              {deletedClauseQ}
              AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
              {(statuses.Length > 0 ? "AND q.Status IN @Statuses" : "")}
              AND (@QuestionTypeId IS NULL OR q.QuestionTypeId = @QuestionTypeId)
              AND (@Level IS NULL OR q.Level = @Level)
              AND (@Keyword IS NULL OR q.Stem LIKE '%' + @Keyword + '%' OR q.QuestionCode LIKE '%' + @Keyword + '%')
            ORDER BY
                CASE WHEN q.Status = 0 THEN 0
                     WHEN q.Status = 1 THEN 1
                     ELSE 2 END,
                q.UpdatedAt DESC
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
                SubQuestionCount = r.SubQuestionCount
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

            // 2. 狀態 1 → 2
            await conn.ExecuteAsync("""
                UPDATE dbo.MT_Questions
                SET Status = @Submitted, UpdatedAt = SYSDATETIME()
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

            // 2. 復原：IsDeleted=0、DeletedAt=NULL、UpdatedAt 重置
            await conn.ExecuteAsync("""
                UPDATE dbo.MT_Questions
                SET IsDeleted = 0, DeletedAt = NULL, UpdatedAt = SYSDATETIME()
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
    }
}
