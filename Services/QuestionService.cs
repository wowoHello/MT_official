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
    Task<bool> RestoreAsync(int questionId, int operatorUserId);

    // P4 新增：列表頁統計卡片用（依 status 分桶計數）
    Task<Dictionary<byte, int>> GetStatusCountsAsync(int projectId, int? creatorId);

    // CwtList 審修作業區母題/子題分離統計：依 MT_SubQuestions.Status 分桶計數
    // ListAsync 在 revision tab 把母題與子題拆成獨立列；統計卡片需配合呈現兩條分支的數量。
    Task<Dictionary<byte, int>> GetSubQuestionStatusCountsAsync(int projectId, int? creatorId);

    // Plan_012：CwtList 審修作業區「已修題」卡片計數（僅本人視角）。
    // 回傳 Status∈{4,6,8} 且本人在當前 Stage 已寫過 RevisionReplies 的題目數。
    Task<int> GetMyRevisionRepliedCountAsync(int projectId, int userId);

    // 與 GetMyRevisionRepliedCountAsync 同義，但對應 MT_SubQuestions 子題單元的「已修題」計數。
    Task<int> GetMySubQuestionRevisionRepliedCountAsync(int projectId, int userId);

    // P4 新增：使用者在某專案是否為成員（用於審題任務權限攔截）
    Task<bool> IsProjectMemberAsync(int userId, int projectId);

    // Plan_008：命題階段結束後的批次轉換與互審分配（Idempotent）
    Task<DateTime?> GetCompositionPhaseEndAsync(int projectId);
    Task<int> EnsureCompositionPhaseClosedAsync(int projectId);

    // Plan_010：審修作業區「修題」Slide-Over 後端
    // Stage B-4-2：subQuestionId=null → 母題單元；非 null → 該子題單元（Status / ReturnCount / Replies 皆以該單元為對象）
    Task<RevisionSlideOverData?> GetRevisionDataAsync(int questionId, int currentUserId, int? subQuestionId = null);
    Task<bool> SaveRevisionAsync(SaveRevisionRequest req, int operatorUserId);

    /// <summary>
    /// 總審修題「完成送審」：將 Status 8 → 7，為 Stage=3 既有審題者 INSERT 新一筆 Pending Assignment
    /// （保留歷次決策歷程），並寫稽核。回傳 false 表示該單元當前不是 FinalEditing 狀態。
    /// Stage B-4-2：subQuestionId=null → 母題單元送審；非 null → 該子題單元獨立送審（其他單元不動）。
    /// </summary>
    Task<bool> ResubmitAfterFinalEditingAsync(int questionId, int userId, int? subQuestionId = null);

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

public class QuestionService(
    IDatabaseService db,
    IHttpContextAccessor httpAccessor,
    IQuestionTypeCatalog typeCatalog) : IQuestionService
{
    private readonly IDatabaseService _db = db;
    private readonly IHttpContextAccessor _httpAccessor = httpAccessor;
    private readonly IQuestionTypeCatalog _typeCatalog = typeCatalog;

    // ====================================================================
    //  既有：配額與階段
    // ====================================================================

    public async Task<List<QuotaProgressItem>> GetMyQuotaProgressAsync(int userId, int projectId)
    {
        const string sql = """
            SELECT
                mq.QuestionTypeId,
                mq.QuotaCount  AS Target,
                COUNT(q.Id)    AS Completed
            FROM dbo.MT_MemberQuotas mq
            INNER JOIN dbo.MT_ProjectMembers pm  ON pm.Id  = mq.ProjectMemberId
            LEFT  JOIN dbo.MT_Questions      q   ON q.CreatorId      = pm.UserId
                                                AND q.ProjectId      = @ProjectId
                                                AND q.QuestionTypeId = mq.QuestionTypeId
                                                AND q.IsDeleted      = 0
                                                AND q.Status         >= 1
            WHERE pm.UserId    = @UserId
              AND pm.ProjectId = @ProjectId
            GROUP BY mq.QuestionTypeId, mq.QuotaCount;
            """;

        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<QuotaProgressItem>(sql, new { UserId = userId, ProjectId = projectId })).ToList();

        // catalog 補 TypeName 並依 SortOrder 排序（原 SQL 用 qt.SortOrder，現由記憶體字典代勞）
        foreach (var row in rows)
        {
            row.TypeName = _typeCatalog.GetName(row.QuestionTypeId);
        }
        return rows
            .OrderBy(r => _typeCatalog.Get(r.QuestionTypeId)?.SortOrder ?? int.MaxValue)
            .ToList();
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

            // 4. 子題（題組型才有；本步驟會把每個 sq.Id 從 DB 回填）
            //    Status 與母題一致，使後續階段升級（fromStatuses=[2]→3 等）能正確波及子題
            await InsertSubQuestionsAsync(conn, tx, newId, formData, initialStatus);

            // 5. 附圖（母題層 + 子題層；子題層需 sq.Id 已存在，故放在子題 INSERT 之後）
            await QuestionImagePersistence.UpsertMasterAsync(conn, tx, newId, formData.Images);
            await QuestionImagePersistence.UpsertSubAsync(conn, tx, newId, GetSubQuestionDbIds(formData), formData.Images);

            // 6. 系統稽核：建立題目
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

            // 2.5 子題 Status cascade：與母題 Status 同步，使 N+1 拆列在 [審修作業區] 立即可見
            //     避開已決策落定（>= 9：採用 / 不採用 / 結案類）的子題，保留個別審題結果
            //     僅題組類（3=閱讀 / 5=短文 / 7=聽力題組）才會有子題，其他題型省去這次 SQL
            if (formData.QuestionType is QuestionTypeCodes.ReadGroup
                                       or QuestionTypeCodes.ShortGroup
                                       or QuestionTypeCodes.ListenGroup)
            {
                await conn.ExecuteAsync("""
                    UPDATE dbo.MT_SubQuestions
                    SET Status = @NewStatus
                    WHERE ParentQuestionId = @Id
                      AND IsDeleted = 0
                      AND Status < 9;
                    """,
                    new { Id = questionId, NewStatus = newStatus }, tx);
            }

            // 3. 子題：UPSERT by SubId + 缺席軟刪除（保留 Id 穩定，sq.Id 由本步驟維護）
            //    UpsertSub 內部會 lookup 母題 Status 給新插入子題用（此時已是上方 cascade 後的新值）
            await UpsertSubQuestionsAsync(conn, tx, questionId, formData, operatorUserId, projectId);

            // 4. 附圖：DELETE + INSERT 全量覆寫（母題層 + 子題層）
            await QuestionImagePersistence.UpsertMasterAsync(conn, tx, questionId, formData.Images);
            await QuestionImagePersistence.UpsertSubAsync(conn, tx, questionId, GetSubQuestionDbIds(formData), formData.Images);

            // 5. 系統稽核：修改題目（狀態轉移寫進 OldValue/NewValue）
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
        using var conn = _db.CreateConnection();
        return await LoadFormDataAsync(conn, questionId);
    }

    /// <summary>
    /// 載入題目完整 FormData 的內部實作（共用 IDbConnection，方便呼叫端在同一連線上做後續查詢）。
    /// 由 GetByIdAsync 與 GetRevisionDataAsync 共用，避免後者開兩條連線。
    /// </summary>
    private static async Task<QuestionFormData?> LoadFormDataAsync(IDbConnection conn, int questionId)
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
                   Analysis, CoreAbility, Indicator, FixedDifficulty,
                   Status, SubmittedAt, DecidedAt
            FROM dbo.MT_SubQuestions
            WHERE ParentQuestionId = @Id AND IsDeleted = 0
            ORDER BY SortOrder;
            """;

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

        // 母題附圖（所有題型一律載入）
        data.Images = await QuestionImagePersistence.LoadMasterAsync(conn, questionId);

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
                    Id          = r.Id,
                    Stem        = r.Stem ?? "",
                    Options     = [r.OptionA ?? "", r.OptionB ?? "", r.OptionC ?? "", r.OptionD ?? ""],
                    Answer      = r.CorrectAnswer ?? "",
                    Analysis    = r.Analysis ?? "",
                    CoreAbility = r.CoreAbility,
                    Indicator   = r.Indicator,
                    Status      = r.Status,
                    SubmittedAt = r.SubmittedAt,
                    DecidedAt   = r.DecidedAt
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
                    Analysis    = r.Analysis ?? "",
                    Status      = r.Status,
                    SubmittedAt = r.SubmittedAt,
                    DecidedAt   = r.DecidedAt
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
                    DetailIndicator = r.Indicator,
                    Status          = r.Status,
                    SubmittedAt     = r.SubmittedAt,
                    DecidedAt       = r.DecidedAt
                }).ToList()
                : [new() { FixedDifficulty = 3 }, new() { FixedDifficulty = 4 }];
        }

        // 子題附圖：把 DB SubQuestionId 反查為 form 位置索引（SubQuestionIndex）後 append 進 Images
        data.Images.AddRange(await QuestionImagePersistence.LoadSubAsync(conn, questionId, GetSubQuestionDbIds(data)));

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

        // Stage B-4-2：revision tab 拆 N+1 列 — 母題列 + 每子題列各一筆，使用 UNION ALL。
        //   其他 Tab（compose / history）仍走原本「每題一列」邏輯，UNION 子題分支不啟用。
        //   啟用條件：filter.Tab == "revision" 或 filter.IncludeSubRows（Overview 用後者明示啟用）。
        var includeSubRows = filter.Tab == "revision" || filter.IncludeSubRows;

        // Plan_DB_PerfReview 第二波 #9：原 per-row inline EXISTS 改 CTE 預聚合 + LEFT JOIN
        // 3 個 CTE 各掃一次（hash aggregate），主 SELECT 1:1 LEFT JOIN，消除 per-row subquery
        // 對拍腳本：.claude/rules/sql/verify_listasync_cte_rewrite.sql（已驗證 4 段 PASS）
        const string cteHeader = """
            WITH
            -- 母題層級 HasReplied：本人在「本輪」當前 Stage 已寫過 RevisionReply（SubQuestionId IS NULL）
            MasterReplied AS (
                SELECT q.Id AS QId, 1 AS HasReplied
                FROM dbo.MT_Questions q
                INNER JOIN dbo.MT_RevisionReplies rr
                    ON  rr.QuestionId    = q.Id
                    AND rr.SubQuestionId IS NULL
                    AND rr.UserId        = q.CreatorId
                    AND rr.Stage         = q.Status
                LEFT JOIN dbo.vw_QuestionRoundStartedAt rs ON rs.QuestionId = q.Id
                WHERE q.ProjectId = @ProjectId
                  AND q.Status IN (4, 6, 8)
                  AND rr.CreatedAt > ISNULL(rs.RoundStartedAt, '1900-01-01')
                GROUP BY q.Id
            ),
            -- 子題層級 HasReplied（同模式，比對 SubQuestionId = sq.Id）
            SubReplied AS (
                SELECT sq.Id AS SubId, 1 AS HasReplied
                FROM dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                INNER JOIN dbo.MT_RevisionReplies rr
                    ON  rr.QuestionId    = q.Id
                    AND rr.SubQuestionId = sq.Id
                    AND rr.UserId        = q.CreatorId
                    AND rr.Stage         = sq.Status
                LEFT JOIN dbo.vw_QuestionRoundStartedAt rs ON rs.QuestionId = q.Id
                WHERE q.ProjectId = @ProjectId
                  AND sq.Status IN (4, 6, 8)
                  AND rr.CreatedAt > ISNULL(rs.RoundStartedAt, '1900-01-01')
                GROUP BY sq.Id
            ),
            -- 各題子題數（取代原 per-row COUNT subquery）
            SubCounts AS (
                SELECT sq.ParentQuestionId AS QId, COUNT(*) AS Cnt
                FROM dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                WHERE q.ProjectId = @ProjectId
                GROUP BY sq.ParentQuestionId
            )
            """;

        // HasReplied filter：取代原 MakeRepliedClause lambda 套 master / sub 兩個版本
        // 條件意義：當前單元 Status∈{4,6,8} 且本輪內已寫 RevisionReply
        const string masterRepliedFilter =
              "AND (@HasReplied IS NULL OR (q.Status IN (4, 6, 8) AND @HasReplied = CAST(ISNULL(mr.HasReplied, 0) AS BIT)))";
        const string subRepliedFilter =
              "AND (@HasReplied IS NULL OR (sq.Status IN (4, 6, 8) AND @HasReplied = CAST(ISNULL(sr.HasReplied, 0) AS BIT)))";

        // count 查詢 — revision tab 用 UNION ALL（母題 + 子題分別計數）
        // 其他 Tab：原本的 master-only count
        string countJoin = filter.SearchCreatorName ? "LEFT JOIN dbo.MT_Users u ON u.Id = q.CreatorId" : "";

        string countSql;
        if (includeSubRows)
        {
            countSql = $"""
                {cteHeader},
                combined AS (
                    SELECT q.Id AS QId
                    FROM dbo.MT_Questions q
                    {countJoin}
                    LEFT JOIN MasterReplied mr ON mr.QId = q.Id
                    WHERE q.ProjectId = @ProjectId
                      {deletedClauseQ}
                      AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
                      {(statuses.Length > 0 ? "AND q.Status IN @Statuses" : "")}
                      AND (@QuestionTypeId IS NULL OR q.QuestionTypeId = @QuestionTypeId)
                      AND (@Level IS NULL OR q.Level = @Level)
                      {keywordClause}
                      {masterRepliedFilter}

                    UNION ALL

                    SELECT q.Id AS QId
                    FROM dbo.MT_SubQuestions sq
                    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                    {countJoin}
                    LEFT JOIN SubReplied sr ON sr.SubId = sq.Id
                    WHERE q.ProjectId = @ProjectId
                      {deletedClauseQ}
                      AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
                      {(statuses.Length > 0 ? "AND sq.Status IN @Statuses" : "")}
                      AND (@QuestionTypeId IS NULL OR q.QuestionTypeId = @QuestionTypeId)
                      AND (@Level IS NULL OR q.Level = @Level)
                      AND (@Keyword IS NULL OR sq.Stem LIKE '%' + @Keyword + '%' OR q.QuestionCode LIKE '%' + @Keyword + '%')
                      {subRepliedFilter}
                )
                SELECT COUNT(*) FROM combined;
                """;
        }
        else
        {
            countSql = $"""
                {cteHeader}
                SELECT COUNT(*)
                FROM dbo.MT_Questions q
                {countJoin}
                LEFT JOIN MasterReplied mr ON mr.QId = q.Id
                WHERE q.ProjectId = @ProjectId
                  {deletedClauseQ}
                  AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
                  {(statuses.Length > 0 ? "AND q.Status IN @Statuses" : "")}
                  AND (@QuestionTypeId IS NULL OR q.QuestionTypeId = @QuestionTypeId)
                  AND (@Level IS NULL OR q.Level = @Level)
                  {keywordClause}
                  {masterRepliedFilter};
                """;
        }

        // 母題列 SELECT 主體（兩種模式共用）— SubQuestionId / SubSortOrder 為 NULL
        // HasRepliedThisStage 與 SubQuestionCount 改用 CTE LEFT JOIN 取結果，消除 per-row subquery
        string masterSelectSql = $"""
            SELECT
                q.Id, q.QuestionCode,
                q.QuestionTypeId AS TypeId,
                q.Level, q.Difficulty, q.Status,
                -- 題組類（3=閱讀, 5=短文, 7=聽力題組）母題不寫 Stem，改用 ArticleContent 當摘要
                -- 長文題目（4=作文）的「題目」(Stem) 為選填，若空白則 fallback 到「文章內容」(ArticleContent) 當摘要
                CASE
                    WHEN q.QuestionTypeId IN (3, 5, 7) THEN q.ArticleContent
                    WHEN q.QuestionTypeId = 4         THEN COALESCE(NULLIF(q.Stem, ''), q.ArticleContent)
                    ELSE q.Stem
                END AS SummaryHtml,
                q.CreatedAt, q.UpdatedAt,
                q.IsDeleted,
                ISNULL(sc.Cnt, 0) AS SubQuestionCount,
                ISNULL(u.DisplayName, '') AS CreatorName,
                CAST(ISNULL(mr.HasReplied, 0) AS BIT) AS HasRepliedThisStage,
                CAST(NULL AS INT) AS SubQuestionId,
                CAST(NULL AS INT) AS SubSortOrder
            FROM dbo.MT_Questions q
            LEFT JOIN dbo.MT_Users u   ON u.Id  = q.CreatorId
            LEFT JOIN MasterReplied mr ON mr.QId = q.Id
            LEFT JOIN SubCounts     sc ON sc.QId = q.Id
            WHERE q.ProjectId = @ProjectId
              {deletedClauseQ}
              AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
              {(statuses.Length > 0 ? "AND q.Status IN @Statuses" : "")}
              AND (@QuestionTypeId IS NULL OR q.QuestionTypeId = @QuestionTypeId)
              AND (@Level IS NULL OR q.Level = @Level)
              {keywordClause}
              {masterRepliedFilter}
            """;

        // 子題列 SELECT 主體（僅 revision tab 啟用）
        // 聽力題組（TypeId=7）特例：母題不分難度／等級，子題的 FixedDifficulty (3=難度三 / 4=難度四) 屬於「等級」而非「易/中/難」，
        // 因此將 FixedDifficulty 映射到 Level 欄位（讓 UI 的 LevelLabel 走 ListenLevelLabels 渲染為「難度三/四」），
        // Difficulty 保持 NULL，避免被 DifficultyLabels 誤翻成「難」或空字串。
        // 其他題組類（閱讀/短文）子題沿用母題的 Level / Difficulty。
        string subSelectSql = $"""
            SELECT
                q.Id, q.QuestionCode,
                q.QuestionTypeId AS TypeId,
                CASE WHEN q.QuestionTypeId = 7 THEN sq.FixedDifficulty ELSE q.Level END AS Level,
                CASE WHEN q.QuestionTypeId = 7 THEN NULL ELSE q.Difficulty END AS Difficulty,
                sq.Status,
                sq.Stem AS SummaryHtml,
                q.CreatedAt, q.UpdatedAt,
                q.IsDeleted,
                0 AS SubQuestionCount,
                ISNULL(u.DisplayName, '') AS CreatorName,
                CAST(ISNULL(sr.HasReplied, 0) AS BIT) AS HasRepliedThisStage,
                sq.Id        AS SubQuestionId,
                sq.SortOrder AS SubSortOrder
            FROM dbo.MT_SubQuestions sq
            INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
            LEFT JOIN dbo.MT_Users u ON u.Id = q.CreatorId
            LEFT JOIN SubReplied   sr ON sr.SubId = sq.Id
            WHERE q.ProjectId = @ProjectId
              {deletedClauseQ}
              AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
              {(statuses.Length > 0 ? "AND sq.Status IN @Statuses" : "")}
              AND (@QuestionTypeId IS NULL OR q.QuestionTypeId = @QuestionTypeId)
              AND (@Level IS NULL OR q.Level = @Level)
              AND (@Keyword IS NULL OR sq.Stem LIKE '%' + @Keyword + '%' OR q.QuestionCode LIKE '%' + @Keyword + '%')
              {subRepliedFilter}
            """;

        // 列出 SQL：revision tab 用 UNION ALL；其他 Tab master-only
        // ORDER BY：先 Status 0/1 優先（保留現有命題作業區排序意圖），再 Id；
        // SubSortOrder=NULL（母題）排前面，子題依 SortOrder 接在母題後（CASE 處理 NULL → -1）
        string listSql;
        if (includeSubRows)
        {
            listSql = $"""
                {cteHeader},
                combined AS (
                    {masterSelectSql}
                    UNION ALL
                    {subSelectSql}
                )
                SELECT * FROM combined
                ORDER BY
                    CASE WHEN Status = 0 THEN 0
                         WHEN Status = 1 THEN 1
                         ELSE 2 END,
                    Id ASC,
                    ISNULL(SubSortOrder, -1) ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """;
        }
        else
        {
            listSql = $"""
                {cteHeader}
                {masterSelectSql}
                ORDER BY
                    CASE WHEN q.Status = 0 THEN 0
                         WHEN q.Status = 1 THEN 1
                         ELSE 2 END,
                    q.Id ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """;
        }

        // CwtList 篩選下拉「有項目才出現」用：
        //   只套 Tab status 範圍 + ProjectId + CreatorId + IsDeleted，刻意忽略使用者自選的
        //   type / level / keyword / HasReplied filter——這樣使用者切 filter 時 dropdown 不會動態消失。
        //   設計與 Overview.StatusKeyCounts 同源語意。
        //
        //   revision tab 要 UNION ALL 母題 + 子題（兩邊 status 範圍各自比對）；其他 Tab master-only。
        //   GROUP BY TypeId / Level 結果只當 boolean key 用（dropdown 出不出現），多列重複計數不影響行為。
        string typeCountsSql;
        string levelCountsSql;
        if (includeSubRows)
        {
            typeCountsSql = $"""
                SELECT TypeId, COUNT(*) AS Cnt FROM (
                    SELECT q.QuestionTypeId AS TypeId
                    FROM dbo.MT_Questions q
                    WHERE q.ProjectId = @ProjectId
                      {deletedClauseQ}
                      AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
                      {(statuses.Length > 0 ? "AND q.Status IN @Statuses" : "")}
                    UNION ALL
                    SELECT q.QuestionTypeId AS TypeId
                    FROM dbo.MT_SubQuestions sq
                    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                    WHERE q.ProjectId = @ProjectId
                      {deletedClauseQ}
                      AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
                      {(statuses.Length > 0 ? "AND sq.Status IN @Statuses" : "")}
                ) AS u
                GROUP BY TypeId;
                """;
            levelCountsSql = $"""
                SELECT Level, COUNT(*) AS Cnt FROM (
                    SELECT q.Level
                    FROM dbo.MT_Questions q
                    WHERE q.ProjectId = @ProjectId
                      {deletedClauseQ}
                      AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
                      {(statuses.Length > 0 ? "AND q.Status IN @Statuses" : "")}
                      AND q.Level IS NOT NULL
                    UNION ALL
                    SELECT q.Level
                    FROM dbo.MT_SubQuestions sq
                    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                    WHERE q.ProjectId = @ProjectId
                      {deletedClauseQ}
                      AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
                      {(statuses.Length > 0 ? "AND sq.Status IN @Statuses" : "")}
                      AND q.Level IS NOT NULL
                ) AS u
                GROUP BY Level;
                """;
        }
        else
        {
            typeCountsSql = $"""
                SELECT q.QuestionTypeId AS TypeId, COUNT(*) AS Cnt
                FROM dbo.MT_Questions q
                WHERE q.ProjectId = @ProjectId
                  {deletedClauseQ}
                  AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
                  {(statuses.Length > 0 ? "AND q.Status IN @Statuses" : "")}
                GROUP BY q.QuestionTypeId;
                """;
            levelCountsSql = $"""
                SELECT q.Level, COUNT(*) AS Cnt
                FROM dbo.MT_Questions q
                WHERE q.ProjectId = @ProjectId
                  {deletedClauseQ}
                  AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
                  {(statuses.Length > 0 ? "AND q.Status IN @Statuses" : "")}
                  AND q.Level IS NOT NULL
                GROUP BY q.Level;
                """;
        }

        // QueryMultipleAsync 一次 round-trip 帶 4 個 result set：count / list / typeCounts / levelCounts
        // 原本 2 次（ExecuteScalar + Query）變 1 次。Dapper 自動處理同一連線串接 SQL 用 ";" 分隔。
        var megaSql = $"{countSql}\n\n{listSql}\n\n{typeCountsSql}\n\n{levelCountsSql}";

        using var conn = _db.CreateConnection();
        using var multi = await conn.QueryMultipleAsync(megaSql, args);

        var total        = await multi.ReadFirstAsync<int>();
        var rows         = (await multi.ReadAsync<QuestionListRowDto>()).AsList();
        var typeCountRows  = await multi.ReadAsync<(int TypeId, int Cnt)>();
        var levelCountRows = await multi.ReadAsync<(byte Level, int Cnt)>();

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
                HasRepliedThisStage = r.HasRepliedThisStage,
                SubQuestionId    = r.SubQuestionId,
                SubSortOrder     = r.SubSortOrder
            }).ToList(),
            TypeIdCounts = typeCountRows.ToDictionary(t => t.TypeId, t => t.Cnt),
            LevelCounts  = levelCountRows.ToDictionary(t => t.Level, t => t.Cnt)
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
        // ⚠️ 必須過濾 rr.SubQuestionId IS NULL：否則「只修了子題」的母題會被誤計為已修題
        //   （MT_RevisionReplies 子題回覆的 QuestionId 是 parent Id，沒過濾就會 match 到 q.Id）
        // 對齊 ListAsync MasterReplied CTE 的判定條件（line 593）
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.MT_Questions q
            WHERE q.ProjectId = @ProjectId
              AND q.IsDeleted = 0
              AND q.CreatorId = @UserId
              AND q.Status IN (4, 6, 8)
              AND EXISTS (
                  SELECT 1 FROM dbo.MT_RevisionReplies rr
                  WHERE rr.QuestionId    = q.Id
                    AND rr.SubQuestionId IS NULL
                    AND rr.UserId        = q.CreatorId
                    AND rr.Stage         = q.Status
                    AND rr.CreatedAt > ISNULL((SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt WHERE QuestionId = q.Id), '1900-01-01')
              );
            """;
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql,
            new { ProjectId = projectId, UserId = userId });
    }

    /// <summary>
    /// 子題層級的 status 分桶計數（依 MT_SubQuestions.Status）。
    /// 篩選與 ListAsync 子題分支對齊：透過 q.ProjectId / q.CreatorId / q.IsDeleted 過濾，
    /// 不檢查 sq.IsDeleted（與 ListAsync 一致），避免列表與統計數量錯位。
    /// </summary>
    public async Task<Dictionary<byte, int>> GetSubQuestionStatusCountsAsync(int projectId, int? creatorId)
    {
        const string sql = """
            SELECT sq.Status, COUNT(*) AS Cnt
            FROM dbo.MT_SubQuestions sq
            INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
            WHERE q.IsDeleted = 0
              AND q.ProjectId = @ProjectId
              AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
            GROUP BY sq.Status;
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<StatusCountDto>(sql,
            new { ProjectId = projectId, CreatorId = creatorId });
        return rows.ToDictionary(r => r.Status, r => r.Cnt);
    }

    /// <summary>
    /// 子題層級的「已修題」計數（本人視角）。
    /// 與 GetMyRevisionRepliedCountAsync 結構相同，但條件落在 MT_SubQuestions / MT_RevisionReplies.SubQuestionId。
    /// </summary>
    public async Task<int> GetMySubQuestionRevisionRepliedCountAsync(int projectId, int userId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.MT_SubQuestions sq
            INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
            WHERE q.ProjectId = @ProjectId
              AND q.IsDeleted = 0
              AND q.CreatorId = @UserId
              AND sq.Status IN (4, 6, 8)
              AND EXISTS (
                  SELECT 1 FROM dbo.MT_RevisionReplies rr
                  WHERE rr.QuestionId    = q.Id
                    AND rr.SubQuestionId = sq.Id
                    AND rr.UserId        = q.CreatorId
                    AND rr.Stage         = sq.Status
                    AND rr.CreatedAt > ISNULL((SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt WHERE QuestionId = q.Id), '1900-01-01')
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

            // 4.5 Stage B-4：題組類子題 Status 同步升級 1 → 2（與母題狀態對齊）
            //     不加此同步：CwtList 審修作業區、Reviews 子題列因 sq.Status 卡在 Completed(1)
            //     而被「審題鎖定」狀態篩選 (sq.Status IN @Statuses) 排除，畫面上看不到子題列。
            //     後續階段邊界 transition（互審→專審 等）的 sub-status 同步邏輯已存在於
            //     EnsureBatchTransitionAsync，本處補上「命題→互審」首次入口的同步缺漏。
            await conn.ExecuteAsync(
                """
                UPDATE sq
                SET sq.Status = @Submitted
                FROM dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                WHERE q.ProjectId = @ProjectId
                  AND q.IsDeleted = 0
                  AND sq.IsDeleted = 0
                  AND sq.Status = @Completed;
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
            //    題組綁定下，同 QuestionId 不同 SubQuestionId 必為同 ReviewerId，故 tuple 仍以「題目層」為比對單位。
            //    Distinct 過濾掉題組子題列，確保 set 大小 = 已分配題目數。
            var existingPairs = (await conn.QueryAsync<(int QuestionId, int ReviewerId)>(
                """
                SELECT DISTINCT QuestionId, ReviewerId
                FROM dbo.MT_ReviewAssignments
                WHERE ProjectId = @ProjectId AND ReviewStage = 1;
                """,
                new { ProjectId = projectId }, tx))
                .Select(x => (x.QuestionId, x.ReviewerId))
                .ToHashSet();

            // 6.5 撈每個 candidate 的子題 mapping（題組綁定整組分配時使用）
            var subMap = await LoadSubQuestionMapAsync(conn, tx, candidates.Select(c => c.Id));

            // 7. 逐題分配：選擇「負載最低 + ≠ Creator + 該題尚未分配給此人」的審題者
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

                // 題組綁定：母題 + 全部子題分給同一 reviewer。回傳 0 代表母題列已存在（視同整組已分配）→ 不寫 audit
                var subIds = subMap.GetValueOrDefault(questionId, []);
                var inserted = await InsertGroupAssignmentsAsync(
                    conn, tx, projectId, questionId, subIds, bestReviewer.Value, reviewStage: 1);

                if (inserted == 0) continue;

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
                        Status         = QuestionStatus.Submitted,
                        Reason         = "CompositionPhaseEnded",
                        ReviewerId     = bestReviewer.Value,
                        SubUnitCount   = subIds.Count   // 0=非題組；>0=題組類，分配 N+1 個審題單元
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

                // 4.5 Stage B-4：題組類子題的 Status 同步升級（透過 ParentQuestionId JOIN 帶 ProjectId）
                //     已決策落定（>= Adopted/9）的子題自動被 fromStatuses 過濾掉，不會被回升
                await conn.ExecuteAsync("""
                    UPDATE sq
                    SET sq.Status = @ToStatus
                    FROM dbo.MT_SubQuestions sq
                    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                    WHERE q.ProjectId = @ProjectId
                      AND q.IsDeleted = 0
                      AND sq.Status IN @Froms;
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
            // 1-A. 撈出母題單元候選：Question.Status=7 且該母題單元 (Q, Sub=NULL) 沒有 resubmit Pending
            //      NOT EXISTS 兩個 row 都找不到的「孤兒」（無 Stage=3 Assignment）也涵蓋於此 → 條件成立。
            //      Stage B-4：NULL-safe 比對 SubQuestionId 確保此 NOT EXISTS 只看母題的 assignment 紀錄。
            const string masterCandidatesSql = """
                SELECT q.Id
                FROM dbo.MT_Questions q
                WHERE q.ProjectId = @ProjectId
                  AND q.IsDeleted = 0
                  AND q.Status    = @FinalReviewing
                  AND NOT EXISTS (
                      SELECT 1
                      FROM dbo.MT_ReviewAssignments p
                      INNER JOIN dbo.MT_ReviewAssignments c
                          ON c.QuestionId    = p.QuestionId
                         AND ISNULL(c.SubQuestionId, -1) = ISNULL(p.SubQuestionId, -1)
                         AND c.ReviewerId    = p.ReviewerId
                         AND c.ReviewStage   = p.ReviewStage
                      WHERE p.QuestionId    = q.Id
                        AND p.SubQuestionId IS NULL   -- 只看母題單元的 assignment
                        AND p.ReviewStage   = 3
                        AND p.ReviewStatus  = 0       -- Pending（resubmit 後新增的待審 row）
                        AND c.ReviewStatus  = 2       -- Completed（先前已決策的歷史 row）
                        AND p.CreatedAt     > c.CreatedAt
                  );
                """;

            var masterIds = (await conn.QueryAsync<int>(masterCandidatesSql, new
            {
                ProjectId      = projectId,
                FinalReviewing = (int)QuestionStatus.FinalReviewing
            }, tx)).ToList();

            // 1-B. 撈出子題單元候選：MT_SubQuestions.Status=7 且該子題單元 (Q, Sub=subId) 沒有 resubmit Pending
            //      Stage B-4：每個子題各自獨立判斷，與母題互不影響
            const string subCandidatesSql = """
                SELECT sq.Id
                FROM dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                WHERE q.ProjectId  = @ProjectId
                  AND q.IsDeleted  = 0
                  AND sq.Status    = @FinalReviewing
                  AND NOT EXISTS (
                      SELECT 1
                      FROM dbo.MT_ReviewAssignments p
                      INNER JOIN dbo.MT_ReviewAssignments c
                          ON c.QuestionId    = p.QuestionId
                         AND ISNULL(c.SubQuestionId, -1) = ISNULL(p.SubQuestionId, -1)
                         AND c.ReviewerId    = p.ReviewerId
                         AND c.ReviewStage   = p.ReviewStage
                      WHERE p.QuestionId    = sq.ParentQuestionId
                        AND p.SubQuestionId = sq.Id    -- 只看本子題單元的 assignment
                        AND p.ReviewStage   = 3
                        AND p.ReviewStatus  = 0
                        AND c.ReviewStatus  = 2
                        AND p.CreatedAt     > c.CreatedAt
                  );
                """;

            var subIds = (await conn.QueryAsync<int>(subCandidatesSql, new
            {
                ProjectId      = projectId,
                FinalReviewing = (int)QuestionStatus.FinalReviewing
            }, tx)).ToList();

            if (masterIds.Count == 0 && subIds.Count == 0)
            {
                tx.Commit();
                return 0;
            }

            // 2-A. 批次 UPDATE 母題 → FinalEditing(8)
            if (masterIds.Count > 0)
            {
                await conn.ExecuteAsync("""
                    UPDATE dbo.MT_Questions
                    SET Status = @FinalEditing
                    WHERE Id IN @Ids;
                    """, new
                {
                    FinalEditing = (int)QuestionStatus.FinalEditing,
                    Ids          = masterIds
                }, tx);
            }

            // 2-B. 批次 UPDATE 子題 → FinalEditing(8)
            if (subIds.Count > 0)
            {
                await conn.ExecuteAsync("""
                    UPDATE dbo.MT_SubQuestions
                    SET Status = @FinalEditing
                    WHERE Id IN @Ids;
                    """, new
                {
                    FinalEditing = (int)QuestionStatus.FinalEditing,
                    Ids          = subIds
                }, tx);
            }

            // 3. 批次 AuditLog（UserId=NULL 表系統批次）— TargetId 用 母題 Id，SubQuestionId 寫 payload
            const string auditSql = """
                INSERT INTO dbo.MT_AuditLogs
                    (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue, CreatedAt)
                VALUES
                    (NULL, @ProjectId, @Action, @TargetType, @TargetId, @OldValue, @NewValue, SYSDATETIME());
                """;

            foreach (var qid in masterIds)
            {
                await conn.ExecuteAsync(auditSql, new
                {
                    ProjectId  = projectId,
                    Action     = AuditLogAction.Modify,
                    TargetType = AuditLogTargetType.Questions,
                    TargetId   = qid,
                    OldValue   = JsonSerializer.Serialize(new { UnitStatus = QuestionStatus.FinalReviewing, SubQuestionId = (int?)null }),
                    NewValue   = JsonSerializer.Serialize(new { UnitStatus = QuestionStatus.FinalEditing,   SubQuestionId = (int?)null, Reason = "FinalEditingPhaseStart" })
                }, tx);
            }

            // 子題的稽核：TargetId 用母題 Id（與審題級稽核一致），透過 SubQuestionId 區分單元
            //   先撈每個 sub 對應的 ParentQuestionId 一次性映射，避免 N 次往返
            if (subIds.Count > 0)
            {
                var subToParent = (await conn.QueryAsync<(int Id, int ParentQuestionId)>(
                    "SELECT Id, ParentQuestionId FROM dbo.MT_SubQuestions WHERE Id IN @Ids;",
                    new { Ids = subIds }, tx)).ToDictionary(r => r.Id, r => r.ParentQuestionId);

                foreach (var sid in subIds)
                {
                    if (!subToParent.TryGetValue(sid, out var parentId)) continue;
                    await conn.ExecuteAsync(auditSql, new
                    {
                        ProjectId  = projectId,
                        Action     = AuditLogAction.Modify,
                        TargetType = AuditLogTargetType.Questions,
                        TargetId   = parentId,
                        OldValue   = JsonSerializer.Serialize(new { UnitStatus = QuestionStatus.FinalReviewing, SubQuestionId = (int?)sid }),
                        NewValue   = JsonSerializer.Serialize(new { UnitStatus = QuestionStatus.FinalEditing,   SubQuestionId = (int?)sid, Reason = "FinalEditingPhaseStart" })
                    }, tx);
                }
            }

            tx.Commit();
            return masterIds.Count + subIds.Count;
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
    //
    //  Stage B-4-2：subQuestionId 用於限定送審範圍 —
    //    NULL → 仍按舊行為（所有處於 FinalEditing 的單元一併送審）；
    //    非 NULL → 只送該子題單元（其他單元維持當前狀態）。
    //  CwtList 拆 N+1 列後，使用者按「完成送審」是針對該行所屬單元，要傳對應的 SubQuestionId。
    // ====================================================================
    public async Task<bool> ResubmitAfterFinalEditingAsync(int questionId, int userId, int? subQuestionId = null)
    {
        using var conn = _db.CreateConnection();
        conn.Open();

        // 守門：該「目標單元」必須處於 FinalEditing(8) 才能送審
        //   母題單元 → MT_Questions.Status == 8
        //   子題單元 → MT_SubQuestions.Status == 8
        int hasEditingUnit;
        if (subQuestionId is null)
        {
            hasEditingUnit = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.MT_Questions WHERE Id = @Id AND IsDeleted = 0 AND Status = @Edit;",
                new { Id = questionId, Edit = (int)QuestionStatus.FinalEditing });
        }
        else
        {
            hasEditingUnit = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                WHERE sq.Id = @SubId AND q.IsDeleted = 0 AND sq.Status = @Edit;
                """,
                new { SubId = subQuestionId.Value, Edit = (int)QuestionStatus.FinalEditing });
        }
        if (hasEditingUnit == 0) return false;

        // 守門：ReturnCount >= 3 表示已達退回上限（總召已解鎖 CanEditByReviewer=1 將親自代修+裁決）
        //   業務規則：第 3 次後教師退場、不可再重送；總召 FinalReviewerEditAndDecideAsync 才是唯一路徑。
        //   防止教師與總召同時操作造成競態（教師重送把 Status 8→7，總召同時讀舊資料寫決策）。
        //   單元級檢查：母題與子題各自獨立計次，用 (QuestionId, SubQuestionId) NULL-safe 比對。
        var returnCount = await conn.ExecuteScalarAsync<int?>(
            """
            SELECT ReturnCount
            FROM dbo.MT_ReviewReturnCounts
            WHERE QuestionId = @Id
              AND ISNULL(SubQuestionId, -1) = ISNULL(@SubId, -1);
            """,
            new { Id = questionId, SubId = subQuestionId });
        if (returnCount >= 3) return false;

        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // 1. 該單元 Status 8 → 7
            //   母題單元 → MT_Questions / 子題單元 → MT_SubQuestions
            if (subQuestionId is null)
            {
                await conn.ExecuteAsync(
                    "UPDATE dbo.MT_Questions SET Status = @Final WHERE Id = @Id AND Status = @Edit;",
                    new
                    {
                        Id    = questionId,
                        Final = QuestionStatus.FinalReviewing,
                        Edit  = QuestionStatus.FinalEditing
                    }, tx);
            }
            else
            {
                await conn.ExecuteAsync(
                    "UPDATE dbo.MT_SubQuestions SET Status = @Final WHERE Id = @SubId AND Status = @Edit;",
                    new
                    {
                        SubId = subQuestionId.Value,
                        Final = QuestionStatus.FinalReviewing,
                        Edit  = QuestionStatus.FinalEditing
                    }, tx);
            }

            // 2. Sticky Reviewer：取該「單元」最早被指派的 Stage=3 ReviewerId（單一原始總召）
            //    — 過去用 DISTINCT 全量會員會在邊界情境（多總召被併入 Stage=3）下，
            //      把同題重派給兩位總召造成雙頭決策衝突。改 TOP 1 ORDER BY Id 鎖回原派 reviewer。
            //    — 範圍下放至 (QuestionId, SubQuestionId) 單元層，子題分歧情境也能各自黏對。
            var stickyReviewerId = await conn.ExecuteScalarAsync<int?>(
                """
                SELECT TOP 1 ReviewerId
                FROM dbo.MT_ReviewAssignments
                WHERE QuestionId = @Id
                  AND ISNULL(SubQuestionId, -1) = ISNULL(@SubId, -1)
                  AND ReviewStage = 3
                ORDER BY Id;
                """,
                new { Id = questionId, SubId = subQuestionId }, tx);
            var reviewerIds = stickyReviewerId is null ? new List<int>() : new List<int> { stickyReviewerId.Value };

            // 2.5 找「需重派的審題單元」— Stage B-4-2 限縮為「該目標單元」
            //     母題單元送審：unitsToReassign = [null]（只重派母題）
            //     子題單元送審：unitsToReassign = [subId]（只重派該子題）
            //     再用同樣的 NOT EXISTS 規則避免重複 INSERT（idempotent）
            var unitsToReassign = new List<int?> { subQuestionId };

            // 3. 為每位 reviewer × 每個待重派單元 INSERT 新一筆 Pending(0)
            //    既有 row 不動（保留 Decision/Comment/DecidedAt 歷程）
            //    Idempotent：NOT EXISTS 守門，重複呼叫不重插同 (Q, Sub, R, Stage=3, Status=0)
            const string insertSql = """
                INSERT INTO dbo.MT_ReviewAssignments
                    (QuestionId, SubQuestionId, ProjectId, ReviewerId, ReviewStage, ReviewStatus, CreatedAt)
                SELECT @QuestionId, @SubQuestionId, q.ProjectId, @ReviewerId, 3, 0, SYSDATETIME()
                FROM dbo.MT_Questions q
                WHERE q.Id = @QuestionId
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.MT_ReviewAssignments
                      WHERE QuestionId   = @QuestionId
                        AND ISNULL(SubQuestionId, -1) = ISNULL(@SubQuestionId, -1)
                        AND ReviewerId   = @ReviewerId
                        AND ReviewStage  = 3
                        AND ReviewStatus = 0
                  );
                """;
            foreach (var reviewerId in reviewerIds)
            {
                foreach (var subId in unitsToReassign)
                {
                    await conn.ExecuteAsync(insertSql,
                        new { QuestionId = questionId, SubQuestionId = subId, ReviewerId = reviewerId }, tx);
                }
            }

            // 4. AuditLog（題目層級）— 帶上 SubQuestionId 供事後追溯哪個單元送審
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
                    OldValue   = JsonSerializer.Serialize(new { UnitStatus = (byte)QuestionStatus.FinalEditing,    SubQuestionId = subQuestionId }),
                    NewValue   = JsonSerializer.Serialize(new { UnitStatus = (byte)QuestionStatus.FinalReviewing,  SubQuestionId = subQuestionId, Reason = "FinalEditingResubmit" })
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
        //    題組綁定下 SubQuestionId 不同列必為同 ReviewerId，故 DISTINCT 到題目層即可
        var existingPairs = (await conn.QueryAsync<(int QuestionId, int ReviewerId)>(
            """
            SELECT DISTINCT QuestionId, ReviewerId
            FROM dbo.MT_ReviewAssignments
            WHERE ProjectId = @ProjectId AND ReviewStage = 2;
            """,
            new { ProjectId = projectId }, tx))
            .Select(x => (x.QuestionId, x.ReviewerId))
            .ToHashSet();

        // 5.1 撈 Stage=1（互審）已審過的 (Q, R)，併入 existingPairs，自動迴避
        //     — 兼任「命題教師＋審題委員」者，不會在專審再拿到自己互審審過的題
        var stage1Pairs = await conn.QueryAsync<(int QuestionId, int ReviewerId)>(
            """
            SELECT DISTINCT QuestionId, ReviewerId
            FROM dbo.MT_ReviewAssignments
            WHERE ProjectId = @ProjectId AND ReviewStage = 1;
            """,
            new { ProjectId = projectId }, tx);
        foreach (var (qid, rid) in stage1Pairs)
        {
            existingPairs.Add((qid, rid));
        }

        // 5.5 撈每題的子題 mapping
        var subMap = await LoadSubQuestionMapAsync(conn, tx, candidates.Select(c => c.Id));

        // 6. 逐題分配：負載最低 + 不為 Creator + 該題尚未分給此人
        //    ⚠️ 必須獨立查 Stage=2（不可從上方 existingPairs 衍生）：existingPairs 已被
        //    stage1Pairs 合併污染，若從中衍生會把所有「互審做過的題目」都 skip 掉，
        //    結果就是 0 分配。對齊 AssignFinalReviewersAsync 的 Stage=3 寫法。
        var existingQuestionIds = (await conn.QueryAsync<int>(
            """
            SELECT DISTINCT QuestionId
            FROM dbo.MT_ReviewAssignments
            WHERE ProjectId = @ProjectId AND ReviewStage = 2;
            """,
            new { ProjectId = projectId }, tx)).ToHashSet();

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

            // 題組綁定：母題 + 子題全部分給同一 reviewer，逐筆 NOT EXISTS 守門
            var subIds = subMap.GetValueOrDefault(questionId, []);
            var inserted = await InsertGroupAssignmentsAsync(
                conn, tx, projectId, questionId, subIds, bestReviewer.Value, reviewStage: 2);

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
                    ReviewStage  = 2,
                    ReviewerId   = bestReviewer.Value,
                    Reason       = "ExpertReviewAssigned",
                    SubUnitCount = subIds.Count
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
        // DISTINCT：題組綁定下同一題會有 N+1 筆同 ReviewerId 紀錄，去重到題目層即可
        var stage2Pairs = (await conn.QueryAsync<(int QuestionId, int ReviewerId)>(
            """
            SELECT DISTINCT QuestionId, ReviewerId
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

        // 既有 Stage=3 (QuestionId, ReviewerId) 防重；DISTINCT 到題目層（同上述理由）
        var existingPairs = (await conn.QueryAsync<(int QuestionId, int ReviewerId)>(
            """
            SELECT DISTINCT QuestionId, ReviewerId
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
        var existingQuestionIds = (await conn.QueryAsync<int>(
            """
            SELECT DISTINCT QuestionId
            FROM dbo.MT_ReviewAssignments
            WHERE ProjectId = @ProjectId AND ReviewStage = 3;
            """,
            new { ProjectId = projectId }, tx)).ToHashSet();

        // 撈每題的子題 mapping
        var subMap = await LoadSubQuestionMapAsync(conn, tx, candidates.Select(c => c.Id));

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

            // 題組綁定：母題 + 子題全部分給同一 reviewer
            var subIds = subMap.GetValueOrDefault(questionId, []);
            var inserted = await InsertGroupAssignmentsAsync(
                conn, tx, projectId, questionId, subIds, bestReviewer.Value, reviewStage: 3);

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
                    ReviewStage  = 3,
                    ReviewerId   = bestReviewer.Value,
                    Reason       = "FinalReviewAssigned",
                    SubUnitCount = subIds.Count
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
    public async Task<RevisionSlideOverData?> GetRevisionDataAsync(int questionId, int currentUserId, int? subQuestionId = null)
    {
        using var conn = _db.CreateConnection();

        // 1. 題目本體（共用同一條 connection，避免兩次往返）
        var question = await LoadFormDataAsync(conn, questionId);
        if (question is null) return null;

        // 2. 取「該單元」當前 Status / ProjectId
        //    Stage B-4-2：母題單元 → MT_Questions.Status；子題單元 → MT_SubQuestions.Status
        //    ProjectId 永遠從母題取（子題沒有 ProjectId 欄位）
        byte unitStatus;
        int projectId;
        if (subQuestionId is null)
        {
            var meta = await conn.QueryFirstOrDefaultAsync<(byte Status, int ProjectId)>(
                "SELECT Status, ProjectId FROM dbo.MT_Questions WHERE Id = @Id AND IsDeleted = 0;",
                new { Id = questionId });
            if (meta.ProjectId == 0) return null;
            unitStatus = meta.Status;
            projectId  = meta.ProjectId;
        }
        else
        {
            var subMeta = await conn.QueryFirstOrDefaultAsync<(byte Status, int ProjectId)>(
                """
                SELECT sq.Status, q.ProjectId
                FROM dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                WHERE sq.Id = @SubId AND q.IsDeleted = 0;
                """,
                new { SubId = subQuestionId.Value });
            if (subMeta.ProjectId == 0) return null;
            unitStatus = subMeta.Status;
            projectId  = subMeta.ProjectId;
        }

        // 3. 跨階段審題意見（匿名化：同階段內依 DecidedAt 排序給 A/B/C）
        //    Stage B-4-2：依 SubQuestionId NULL-safe 過濾 — 母題單元只看母題意見、子題單元只看自己的意見
        //    完成判定：以 DecidedAt IS NOT NULL 為準（專/總審純草稿不寫 DecidedAt，不應洩漏給命題者）
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
                  AND ISNULL(ra.SubQuestionId, -1) = ISNULL(@SubId, -1)
                  AND ra.DecidedAt IS NOT NULL
            ) t
            ORDER BY Stage, DecidedAt;
            """;
        var commentRows = (await conn.QueryAsync<(byte Stage, string Comment, DateTime DecidedAt, int AnonIndex)>(
            commentSql, new { Id = questionId, SubId = subQuestionId })).AsList();

        // AnonName 依 Stage 分流：互審→命題教師、專審→審題委員、總審→總召集人
        // 複用 AnnotationActorLabel.Anonymize 既有規則，避免「審題委員」hardcode 套到互審造成語意錯誤
        var comments = commentRows.Select(r => new ReviewCommentEntry
        {
            Stage     = r.Stage,
            AnonName  = $"{AnnotationActorLabel.Anonymize((ReviewStage)r.Stage)} {(char)('A' + (r.AnonIndex - 1) % 26)}",
            Comment   = r.Comment ?? "",
            DecidedAt = r.DecidedAt
        }).ToList();

        // 4. 自己歷次修題說明（Plan_014：列表最新在最上）
        //    Stage B-4-2：依 SubQuestionId NULL-safe 過濾
        const string mySql = """
            SELECT Stage, Content, CreatedAt
            FROM dbo.MT_RevisionReplies
            WHERE QuestionId = @Id
              AND ISNULL(SubQuestionId, -1) = ISNULL(@SubId, -1)
              AND UserId = @UserId
            ORDER BY CreatedAt DESC;
            """;
        var myReplies = (await conn.QueryAsync<RevisionReplyEntry>(
            mySql, new { Id = questionId, SubId = subQuestionId, UserId = currentUserId })).AsList();

        // 5. 當前進行中階段（無 → null）
        var phaseRow = await conn.QueryFirstOrDefaultAsync<(byte PhaseCode, DateTime EndDate)>(
            """
            SELECT TOP 1 PhaseCode, EndDate
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId
              AND PhaseCode > 1
              AND CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
            ORDER BY SortOrder;
            """, new { ProjectId = projectId });

        // 6. 總審退回次數（給總修階段的 [送出再審] 用）
        //    Stage B-4-2：按單元計次（NULL-safe 比對 SubQuestionId）
        var returnCount = await conn.ExecuteScalarAsync<int?>(
            """
            SELECT TOP 1 ReturnCount FROM dbo.MT_ReviewReturnCounts
            WHERE QuestionId = @Id
              AND ISNULL(SubQuestionId, -1) = ISNULL(@SubId, -1)
            ORDER BY Id DESC;
            """,
            new { Id = questionId, SubId = subQuestionId }) ?? 0;

        // Plan_013 + Stage B-4-2：本輪起點時間 — 上次「該單元」總審退回時間
        // 沒有任何總審退回紀錄時 fallback 為 1900-01-01（等同於不過濾，全部 reply 視為本輪）
        //
        // ⚠️ 不用 vw_QuestionRoundStartedAt：此處為「單元級」（含 SubQuestionId NULL-safe 比對），
        //    view 為題目級（按 QuestionId GROUP BY），涵蓋不到母題 / 子題的獨立時序。
        //    Plan_DB_PerfReview 第二波 #6 已確認此處不納入 View 範圍。
        var roundStartedAt = await conn.ExecuteScalarAsync<DateTime?>(
            """
            SELECT MAX(DecidedAt)
            FROM dbo.MT_ReviewAssignments
            WHERE QuestionId = @Id
              AND ISNULL(SubQuestionId, -1) = ISNULL(@SubId, -1)
              AND ReviewStage = 3
              AND Decision IN (2, 3);
            """, new { Id = questionId, SubId = subQuestionId }) ?? new DateTime(1900, 1, 1);

        // 7. 當前階段最新一筆「本輪」reply（CreatedAt > roundStartedAt 才算本輪；舊輪不取）
        var draft = myReplies
            .Where(r => r.Stage == phaseRow.PhaseCode && r.CreatedAt > roundStartedAt)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefault()?.Content ?? "";

        return new RevisionSlideOverData
        {
            Question            = question,
            SubQuestionId       = subQuestionId,
            Comments            = comments,
            MyReplies           = myReplies,
            CurrentPhaseCode    = phaseRow.PhaseCode,
            PhaseEndDate        = phaseRow.PhaseCode == 0 ? null : phaseRow.EndDate,
            CurrentDraftContent = draft,
            // Plan_013：HasReplied 只看本輪（CreatedAt > roundStartedAt）
            HasReplied          = myReplies.Any(r => r.Stage == phaseRow.PhaseCode && r.CreatedAt > roundStartedAt),
            FinalReturnCount    = returnCount,
            QStatus             = unitStatus   // Stage B-4-2：母題 = MT_Questions.Status / 子題 = MT_SubQuestions.Status
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
        // ReadCommitted 即可：單一題目同時只會被一位命題教師修；
        // 第 2 步檢查 Status + PhaseCode 已有業務層級的「狀態流轉」防護，無需 Serializable 的 key-range lock。
        using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            // 1. 取「該單元」當前 Status / ProjectId
            //    Stage B-4-2：母題單元 → MT_Questions.Status；子題單元 → MT_SubQuestions.Status
            byte unitStatus;
            int projectId;
            if (req.SubQuestionId is null)
            {
                var meta = await conn.QueryFirstOrDefaultAsync<QuestionMetaDto>(
                    "SELECT Status, ProjectId FROM dbo.MT_Questions WHERE Id = @Id AND IsDeleted = 0;",
                    new { Id = req.QuestionId }, tx);
                if (meta is null) return false;
                unitStatus = meta.Status;
                projectId  = meta.ProjectId;
            }
            else
            {
                var subMeta = await conn.QueryFirstOrDefaultAsync<QuestionMetaDto>(
                    """
                    SELECT sq.Status, q.ProjectId
                    FROM dbo.MT_SubQuestions sq
                    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                    WHERE sq.Id = @SubId AND q.IsDeleted = 0;
                    """,
                    new { SubId = req.SubQuestionId.Value }, tx);
                if (subMeta is null) return false;
                unitStatus = subMeta.Status;
                projectId  = subMeta.ProjectId;
            }

            // 2. 該單元 Status 必須在修題狀態（4/6/8）；階段必須對齊
            if (unitStatus is not (QuestionStatus.PeerEditing or QuestionStatus.ExpertEditing or QuestionStatus.FinalEditing))
                throw new InvalidOperationException("該單元目前不在修題狀態，無法儲存修題。");

            var phaseCode = await GetCurrentPhaseCodeInTxAsync(conn, tx, projectId);
            var requiredPhase = unitStatus;   // PeerEditing=4 / ExpertEditing=6 / FinalEditing=8
            if (phaseCode != requiredPhase)
                throw new InvalidOperationException("修題期間已結束，無法儲存變更。");

            // 3. UPDATE 題目（Status 維持原值；UpdatedAt 用 CASE WHEN 判定母題欄位是否真的有變）
            //    語意：只有母題欄位真的被改才刷新「最後編輯時間」，純填修題說明不算編輯
            //    已知限制：純改子題不動母題的情境，UpdatedAt 不會更新（複雜度權衡後接受）
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
                    UpdatedAt       = CASE
                        WHEN QuestionTypeId         <> @TypeId
                          OR ISNULL(Level, 0)       <> ISNULL(@Level, 0)
                          OR ISNULL(Difficulty, 0)  <> ISNULL(@Difficulty, 0)
                          OR ISNULL(Stem, '')              <> ISNULL(@Stem, '')
                          OR ISNULL(Analysis, '')          <> ISNULL(@Analysis, '')
                          OR ISNULL(CorrectAnswer, '')     <> ISNULL(@Answer, '')
                          OR ISNULL(OptionA, '')           <> ISNULL(@OptA, '')
                          OR ISNULL(OptionB, '')           <> ISNULL(@OptB, '')
                          OR ISNULL(OptionC, '')           <> ISNULL(@OptC, '')
                          OR ISNULL(OptionD, '')           <> ISNULL(@OptD, '')
                          OR ISNULL(ArticleTitle, '')      <> ISNULL(@ArticleTitle, '')
                          OR ISNULL(ArticleContent, '')    <> ISNULL(@ArticleContent, '')
                          OR ISNULL(AudioUrl, '')          <> ISNULL(@AudioUrl, '')
                          OR ISNULL(GradingNote, '')       <> ISNULL(@GradingNote, '')
                          OR ISNULL(Topic, 0)              <> ISNULL(@Topic, 0)
                          OR ISNULL(Subtopic, 0)           <> ISNULL(@Subtopic, 0)
                          OR ISNULL(Genre, 0)              <> ISNULL(@Genre, 0)
                          OR ISNULL(Material, 0)           <> ISNULL(@Material, 0)
                          OR ISNULL(WritingMode, 0)        <> ISNULL(@WritingMode, 0)
                          OR ISNULL(AudioType, 0)          <> ISNULL(@AudioType, 0)
                          OR ISNULL(CoreAbility, 0)        <> ISNULL(@CoreAbility, 0)
                          OR ISNULL(DetailIndicator, 0)    <> ISNULL(@DetailIndicator, 0)
                        THEN SYSDATETIME()
                        ELSE UpdatedAt
                    END
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
            await UpsertSubQuestionsAsync(conn, tx, req.QuestionId, req.FormData, operatorUserId, projectId);

            // 4.5 附圖：DELETE + INSERT 全量覆寫（母題層 + 子題層，與 UpdateAsync 邏輯一致）
            await QuestionImagePersistence.UpsertMasterAsync(conn, tx, req.QuestionId, req.FormData.Images);
            await QuestionImagePersistence.UpsertSubAsync(conn, tx, req.QuestionId, GetSubQuestionDbIds(req.FormData), req.FormData.Images);

            // 5. INSERT MT_RevisionReplies — 純 append-only（每輪一筆獨立 row）
            //    Plan_014：總修階段（Stage=8）允許跨輪多次回覆，每輪退回後重新送出都產生新 row
            //    判別「本輪 reply」靠 CreatedAt > 上次總審退回 DecidedAt（見 ListAsync / GetRevisionDataAsync 等處）
            //    Stage=4/6 線性單輪，append-only 等同每題每階段最多一筆，行為不變
            //    Stage B-4-2：依 SubQuestionId 寫入（母題單元 = NULL；子題單元 = sub.Id）
            const string insertReplySql = """
                INSERT INTO dbo.MT_RevisionReplies (QuestionId, SubQuestionId, UserId, Stage, Content, CreatedAt)
                VALUES (@Qid, @SubId, @Uid, @Stage, @Content, SYSDATETIME());
                """;
            await conn.ExecuteAsync(insertReplySql, new
            {
                Qid     = req.QuestionId,
                SubId   = req.SubQuestionId,
                Uid     = operatorUserId,
                Stage   = phaseCode,
                Content = req.RevisionNote
            }, tx);

            // 6. AuditLog — Stage B-4-2：帶 SubQuestionId 供事後追溯哪個單元被修
            await WriteAuditLogAsync(conn, tx, operatorUserId, projectId,
                AuditLogAction.Modify, req.QuestionId,
                oldValue: new { UnitStatus = unitStatus, SubQuestionId = req.SubQuestionId },
                newValue: new { UnitStatus = unitStatus, SubQuestionId = req.SubQuestionId, Reason = "Revision", Stage = phaseCode });

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
        int parentId, QuestionFormData formData, byte masterStatus)
    {
        // Status 帶入：子題與母題同步起跑，後續階段升級才會抓到（避免子題卡在 Draft 永遠不顯示）
        const string sql = """
            INSERT INTO dbo.MT_SubQuestions (
                ParentQuestionId, SortOrder, Status,
                Stem, CorrectAnswer,
                OptionA, OptionB, OptionC, OptionD,
                Analysis, CoreAbility, Indicator, FixedDifficulty
            )
            OUTPUT INSERTED.Id
            VALUES (
                @ParentId, @SortOrder, @Status,
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
                sq.Id = await conn.QuerySingleAsync<int>(sql, BuildReadSubParams(parentId, i + 1, sq, masterStatus), tx);
            }
        }
        else if (formData.QuestionType == QuestionTypeCodes.ShortGroup)
        {
            for (var i = 0; i < formData.ShortSubQuestions.Count; i++)
            {
                var sq = formData.ShortSubQuestions[i];
                sq.Id = await conn.QuerySingleAsync<int>(sql, BuildShortSubParams(parentId, i + 1, sq, masterStatus), tx);
            }
        }
        else if (formData.QuestionType == QuestionTypeCodes.ListenGroup)
        {
            for (var i = 0; i < formData.ListenGroupSubQuestions.Count; i++)
            {
                var sq = formData.ListenGroupSubQuestions[i];
                sq.Id = await conn.QuerySingleAsync<int>(sql, BuildListenSubParams(parentId, i + 1, sq, masterStatus), tx);
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

        // 1.5 撈母題當前 Status — 新插入子題用此值（這次的 UPDATE 已先於本函式跑完，已是新值）
        var masterStatus = await conn.ExecuteScalarAsync<byte>(
            "SELECT Status FROM dbo.MT_Questions WHERE Id = @Id;",
            new { Id = parentId }, tx);

        // 2. 表單帶上來的子題（依題型）
        var formIds = new HashSet<int>();

        // INSERT 帶 Status；UPDATE SQL（下方）不引用 @Status，故 BuildXSubParams 的 Status 欄對 UPDATE 路徑無作用
        const string insertSql = """
            INSERT INTO dbo.MT_SubQuestions (
                ParentQuestionId, SortOrder, Status,
                Stem, CorrectAnswer,
                OptionA, OptionB, OptionC, OptionD,
                Analysis, CoreAbility, Indicator, FixedDifficulty
            )
            OUTPUT INSERTED.Id
            VALUES (
                @ParentId, @SortOrder, @Status,
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
                var p  = BuildReadSubParams(parentId, i + 1, sq, masterStatus);
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
                var p  = BuildShortSubParams(parentId, i + 1, sq, masterStatus);
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
                var p  = BuildListenSubParams(parentId, i + 1, sq, masterStatus);
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
    /// <summary>
    /// 抽出 formData 中子題的 DB Id 列表，依 form 位置順序排列。
    /// 必須在 InsertSubQuestionsAsync / UpsertSubQuestionsAsync 跑完之後呼叫，
    /// 屆時每個子題的 sq.Id 已經由 DB 回填。新增的子題若尚未寫入 DB，對應位置會是 0
    /// （由 QuestionImagePersistence.UpsertSubAsync 內部過濾掉）。
    /// 非題組題型回傳空陣列。
    /// </summary>
    private static IReadOnlyList<int> GetSubQuestionDbIds(QuestionFormData formData) =>
        formData.QuestionType switch
        {
            QuestionTypeCodes.ReadGroup    => formData.ReadSubQuestions.Select(s => s.Id).ToList(),
            QuestionTypeCodes.ShortGroup   => formData.ShortSubQuestions.Select(s => s.Id).ToList(),
            QuestionTypeCodes.ListenGroup  => formData.ListenGroupSubQuestions.Select(s => s.Id).ToList(),
            _                              => Array.Empty<int>()
        };

    // Status 參數同步：新插入的子題 Status 與母題一致，使 N+1 拆列在 [審修作業區] 立刻可見。
    private static object BuildReadSubParams(int parentId, int sortOrder, SubQuestionChoice sq, byte status) => new
    {
        ParentId        = parentId,
        SortOrder       = sortOrder,
        Status          = status,
        Stem            = NullIfEmpty(sq.Stem),
        Answer          = NullIfEmpty(sq.Answer),
        OptA            = SafeOption(sq.Options, 0),
        OptB            = SafeOption(sq.Options, 1),
        OptC            = SafeOption(sq.Options, 2),
        OptD            = SafeOption(sq.Options, 3),
        Analysis        = NullIfEmpty(sq.Analysis),
        sq.CoreAbility,           // 與短文題組共用對照表
        sq.Indicator,             // 同上
        FixedDifficulty = (byte?)null
    };

    private static object BuildShortSubParams(int parentId, int sortOrder, SubQuestionFreeResponse sq, byte status) => new
    {
        ParentId        = parentId,
        SortOrder       = sortOrder,
        Status          = status,
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

    private static object BuildListenSubParams(int parentId, int sortOrder, ListenGroupSubQuestion sq, byte status) => new
    {
        ParentId        = parentId,
        SortOrder       = sortOrder,
        Status          = status,
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

    // ====================================================================
    //  Stage B 共用：題組綁定式分配 helper
    //  - 設計原則：題組（QuestionTypeId 3/5/7）的母題與所有子題綁定給「同一位 reviewer」
    //  - DB 寫入：1 筆母題（SubQuestionId NULL）+ M 筆子題（SubQuestionId 各自填值），全部同 ReviewerId
    //  - 決策 / 計次 / 退回則各審題單元獨立（由後續 Modal / Decision 流程處理）
    // ====================================================================

    /// <summary>
    /// 批次撈一組母題 Id 的所有子題 Id（依 SortOrder 排序）。
    /// 非題組題（QuestionTypeId != 3/5/7）不會出現在 MT_SubQuestions，自然回空 list。
    /// </summary>
    private static async Task<Dictionary<int, List<int>>> LoadSubQuestionMapAsync(
        IDbConnection conn, IDbTransaction tx, IEnumerable<int> questionIds)
    {
        var ids = questionIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, List<int>>();

        var rows = await conn.QueryAsync<(int ParentQuestionId, int Id)>(
            """
            SELECT ParentQuestionId, Id
            FROM dbo.MT_SubQuestions
            WHERE ParentQuestionId IN @Ids
            ORDER BY ParentQuestionId, SortOrder;
            """,
            new { Ids = ids }, tx);

        return rows.GroupBy(x => x.ParentQuestionId)
                   .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());
    }

    /// <summary>
    /// 寫入「整組」審題分配（題組綁定）：母題 1 筆 + 子題 N 筆，全部分給同一 reviewer。
    ///
    /// 效能：用單一 INSERT…SELECT FROM (VALUES) 一次寫 N+1 筆（取代 N+1 個獨立 round-trip）。
    /// 防重：每筆都套用 NULL-safe NOT EXISTS（比對 QuestionId + SubQuestionId + Stage），既存列自動跳過。
    /// 回傳：本次實際新增的筆數（0 = 整組已存在；N+1 = 全新分配；中間值 = crash recovery 補殘缺）。
    /// </summary>
    private static async Task<int> InsertGroupAssignmentsAsync(
        IDbConnection conn, IDbTransaction tx,
        int projectId, int questionId, IReadOnlyList<int> subQuestionIds,
        int reviewerId, byte reviewStage)
    {
        // 構造 VALUES 列：母題 (NULL) + 各子題 Id
        // 注意：(VALUES (NULL)) 在 SQL Server 需明確 CAST 否則類型推斷會失敗
        var valueClauses = new List<string>(subQuestionIds.Count + 1)
        {
            "(CAST(NULL AS INT))"
        };
        var dp = new DynamicParameters();
        dp.Add("QuestionId", questionId);
        dp.Add("ProjectId",  projectId);
        dp.Add("ReviewerId", reviewerId);
        dp.Add("Stage",      reviewStage);
        for (var i = 0; i < subQuestionIds.Count; i++)
        {
            valueClauses.Add($"(@Sub{i})");
            dp.Add($"Sub{i}", subQuestionIds[i]);
        }

        // src.SubId NULL = 母題、非 NULL = 子題；NULL-safe NOT EXISTS 守門
        var sql = $"""
            INSERT INTO dbo.MT_ReviewAssignments
                (QuestionId, SubQuestionId, ProjectId, ReviewerId, ReviewStage, ReviewStatus, CreatedAt)
            SELECT @QuestionId, src.SubId, @ProjectId, @ReviewerId, @Stage, 0, SYSDATETIME()
            FROM (VALUES {string.Join(",", valueClauses)}) AS src(SubId)
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.MT_ReviewAssignments ra
                WHERE ra.QuestionId   = @QuestionId
                  AND ISNULL(ra.SubQuestionId, -1) = ISNULL(src.SubId, -1)
                  AND ra.ReviewStage  = @Stage
            );
            """;

        return await conn.ExecuteAsync(sql, dp, tx);
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

        // Stage A 新增：子題審題單元狀態（與母題對等）
        public byte Status { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? DecidedAt { get; set; }
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
        // Stage B-4-2：revision tab 子題列拆分用；母題列為 NULL
        public int? SubQuestionId { get; set; }
        public int? SubSortOrder { get; set; }
    }
}
