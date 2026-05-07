using Dapper;
using MT.Models;

namespace MT.Services;

/// <summary>
/// 命題總覽（Overview.razor）的商業邏輯。
/// 對外封裝 IQuestionService 的細節，razor 端只需注入 IOverviewService。
/// </summary>
public interface IOverviewService
{
    /// <summary>合併「列表」+「status 分桶計數」一次回傳。</summary>
    Task<OverviewListResult> LoadAsync(int projectId, OverviewFilter filter);

    /// <summary>取此專案有命題紀錄的教師清單（給篩選下拉用）。</summary>
    Task<List<OverviewCreatorOption>> GetCreatorOptionsAsync(int projectId);

    /// <summary>取單筆題目詳情（含子題）。</summary>
    Task<QuestionFormData?> GetDetailAsync(int questionId);

    /// <summary>復原已軟刪除題目（包裝 IQuestionService.RestoreAsync）。</summary>
    Task<bool> RestoreAsync(int questionId, int operatorUserId);

    /// <summary>取此題的審題歷程（管理員監控用，不匿名）。委派 IReviewService。</summary>
    Task<List<ReviewHistoryEntry>> GetReviewHistoryAsync(int questionId);

    /// <summary>依題型組成詳情面板用的標籤（主類/次類/文體/核心能力…）。</summary>
    Dictionary<string, string> BuildPreviewTags(QuestionFormData formData);

    /// <summary>依題型挑等級標籤字典（聽力 vs 一般題等級顯示不同）。</summary>
    string LevelLabel(string typeKey, byte? level);
}

public class OverviewService(
    IQuestionService questionService,
    IReviewService reviewService,
    IDatabaseService db,
    IPhaseTransitionCoordinator phaseCoordinator) : IOverviewService
{
    private readonly IQuestionService _questionService = questionService;
    private readonly IReviewService   _reviewService   = reviewService;
    private readonly IDatabaseService _db = db;
    private readonly IPhaseTransitionCoordinator _phaseCoordinator = phaseCoordinator;

    public async Task<OverviewListResult> LoadAsync(int projectId, OverviewFilter filter)
    {
        // 命題總覽必須看到已刪除題目（紅色「命題刪除」標籤 + 復原按鈕）
        var listFilter = filter.ToListFilter(projectId);
        listFilter.IncludeDeleted = true;

        // 階段轉換統一委派 PhaseTransitionCoordinator（60 秒去重 + 統一 logging）
        await _phaseCoordinator.EnsureAsync(projectId);

        // countsTask、listTask、phaseTask 三者彼此獨立，並行執行
        var countsTask = _questionService.GetStatusCountsAsync(projectId, creatorId: null);
        var listTask   = _questionService.ListAsync(listFilter);
        var phaseTask  = _questionService.GetCurrentPhaseAsync(projectId);

        // pendingTask 需要 phaseTask 的結果（PhaseCode 由 MT_ProjectPhases 動態計算，
        // MT_Projects 沒有 PhaseCode 欄位），所以單獨先 await phaseTask 取得 PhaseCode 後再查詢
        var phase = await phaseTask;
        var phaseCode = phase is { PhaseCode: var pc } ? (byte?)pc : null;
        var pendingTask = GetPendingRevisionCountAsync(projectId, phaseCode);

        await Task.WhenAll(countsTask, listTask, pendingTask);

        var list = listTask.Result;

        // 「該題此審題階段所有被指派審題者皆已給意見」—— 只查當前頁列表，避免全表掃描
        var responded = await GetAllReviewersRespondedAsync(
            projectId, phaseCode, list.Items.Select(i => i.Id));

        return new OverviewListResult
        {
            Items                = list.Items,
            StatusCounts         = countsTask.Result,
            TotalCount           = list.TotalCount,
            Page                 = list.Page,
            PageSize             = list.PageSize,
            PendingRevisionCount = pendingTask.Result,
            // 梯次當前 PhaseCode（null = 不在任何進行中階段）；轉 byte? 讓 UI 端易於比對 QuestionStatus 常數
            CurrentPhaseCode     = phaseCode,
            AllReviewersResponded = responded
        };
    }

    /// <summary>
    /// 計算當前頁列表中，每筆題目「在當前審題階段是否全體被指派審題者皆已給意見」。
    /// PhaseCode 對應 ReviewStage：3→1（互審） / 5→2（專審） / 7→3（總審）。
    /// 判定：該題該階段被指派筆數 > 0，且所有筆數的 Comment 皆非空。
    /// 非審題階段（PhaseCode 不在 {3,5,7}）或列表為空 → 直接回空 dict，不打 DB。
    /// </summary>
    private async Task<Dictionary<int, bool>> GetAllReviewersRespondedAsync(
        int projectId, byte? phaseCode, IEnumerable<int> questionIds)
    {
        var ids = questionIds as IList<int> ?? questionIds.ToList();
        if (ids.Count == 0) return new();

        var stage = phaseCode switch
        {
            3 => (byte)1,   // 互審
            5 => (byte)2,   // 專審
            7 => (byte)3,   // 總審
            _ => (byte)0
        };
        if (stage == 0) return new();

        // GROUP BY QuestionId：當分配筆數 = 有效 Comment 筆數時，回傳該 QuestionId
        const string sql = """
            SELECT QuestionId
            FROM   dbo.MT_ReviewAssignments
            WHERE  ProjectId   = @ProjectId
              AND  ReviewStage = @Stage
              AND  QuestionId IN @Ids
            GROUP BY QuestionId
            HAVING COUNT(*) > 0
               AND COUNT(*) = SUM(CASE WHEN Comment IS NOT NULL
                                         AND LEN(LTRIM(RTRIM(Comment))) > 0
                                       THEN 1 ELSE 0 END);
            """;

        using var conn = _db.CreateConnection();
        var responded = (await conn.QueryAsync<int>(sql, new
        {
            ProjectId = projectId,
            Stage     = stage,
            Ids       = ids
        })).ToHashSet();

        return ids.ToDictionary(id => id, id => responded.Contains(id));
    }

    /// <summary>
    /// 計算「待修編」實際數量：Status = 梯次當前 PhaseCode（限修題階段 4/6/8）
    /// 且命題者尚未於該階段於 MT_RevisionReplies 留下紀錄。
    /// 用 PhaseCode 同時鎖「目前階段」與「修題狀態」（因修題階段 Status 與 PhaseCode 為 1:1 對齊）；
    /// PhaseCode 由 IQuestionService.GetCurrentPhaseAsync 統一計算（依 MT_ProjectPhases 日期區間）後傳入，
    /// 避免在 SQL 內重複實作日期比對邏輯，保持單一資料來源。
    /// 若 phaseCode 為 null 或不在 {4,6,8}，直接回 0（不打 DB）。
    /// </summary>
    private async Task<int> GetPendingRevisionCountAsync(int projectId, byte? phaseCode)
    {
        // 邊界守門：非修題階段直接回 0
        if (phaseCode is not (4 or 6 or 8)) return 0;

        const string sql = """
            SELECT COUNT(1)
            FROM dbo.MT_Questions q
            WHERE q.ProjectId = @ProjectId
              AND q.IsDeleted = 0
              AND q.Status    = @PhaseCode
              AND NOT EXISTS (
                    SELECT 1 FROM dbo.MT_RevisionReplies rr
                    WHERE rr.QuestionId = q.Id
                      AND rr.UserId     = q.CreatorId
                      AND rr.Stage      = q.Status
              );
            """;

        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql, new { ProjectId = projectId, PhaseCode = phaseCode.Value });
    }

    public async Task<List<OverviewCreatorOption>> GetCreatorOptionsAsync(int projectId)
    {
        // 只列實際有出題的命題教師（含已軟刪除的題目，避免老師全部草稿被刪後就消失於下拉）
        const string sql = """
            SELECT
                u.Id          AS Id,
                u.DisplayName AS DisplayName,
                COUNT(q.Id)   AS QuestionCount
            FROM dbo.MT_Questions q
            INNER JOIN dbo.MT_Users u ON u.Id = q.CreatorId
            WHERE q.ProjectId = @ProjectId
            GROUP BY u.Id, u.DisplayName
            ORDER BY u.DisplayName;
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<OverviewCreatorOption>(sql, new { ProjectId = projectId });
        return rows.AsList();
    }

    public Task<QuestionFormData?> GetDetailAsync(int questionId)
        => _questionService.GetByIdAsync(questionId);

    public Task<bool> RestoreAsync(int questionId, int operatorUserId)
        => _questionService.RestoreAsync(questionId, operatorUserId);

    public Task<List<ReviewHistoryEntry>> GetReviewHistoryAsync(int questionId)
        => _reviewService.GetHistoryByQuestionIdAsync(questionId);

    public Dictionary<string, string> BuildPreviewTags(QuestionFormData f)
    {
        var tags = new Dictionary<string, string>();
        const string fallback = "未設定";
        string Lab(byte? v, IReadOnlyDictionary<byte, string> map)
            => v is null ? fallback : map.GetValueOrDefault(v.Value, fallback);

        switch (f.QuestionType)
        {
            case QuestionTypeCodes.Single:
            case QuestionTypeCodes.Select:
                tags["主類"] = Lab(f.Topic, QuestionConstants.TopicLabels);
                tags["次類"] = Lab(f.Subtopic, QuestionConstants.SubtopicLabels);
                break;
            case QuestionTypeCodes.LongText:
                tags["寫作模式"] = Lab(f.WritingMode, QuestionConstants.WritingModeLabels);
                break;
            case QuestionTypeCodes.ReadGroup:
                tags["文體"] = Lab(f.Genre, QuestionConstants.GenreLabels);
                break;
            case QuestionTypeCodes.ShortGroup:
                tags["主類"] = "文意判讀";
                tags["次類"] = "篇章辨析";
                tags["文體"] = Lab(f.Genre, QuestionConstants.GenreLabels);
                break;
            case QuestionTypeCodes.Listen:
                tags["核心能力"] = Lab(f.CoreAbility, QuestionConstants.CoreAbilityLabels);
                tags["細目指標"] = Lab(f.DetailIndicator, QuestionConstants.DetailIndicatorLabels);
                tags["語音類型"] = Lab(f.AudioType, QuestionConstants.AudioTypeLabels);
                tags["素材分類"] = Lab(f.Material, QuestionConstants.MaterialLabels);
                break;
            case QuestionTypeCodes.ListenGroup:
                tags["語音類型"] = Lab(f.AudioType, QuestionConstants.AudioTypeLabels);
                tags["素材分類"] = Lab(f.Material, QuestionConstants.MaterialLabels);
                break;
        }
        return tags;
    }

    public string LevelLabel(string typeKey, byte? level)
    {
        if (level is null) return "";
        return typeKey is QuestionTypeCodes.Listen or QuestionTypeCodes.ListenGroup
            ? QuestionConstants.ListenLevelLabels.GetValueOrDefault(level.Value, "")
            : QuestionConstants.GeneralLevelLabels.GetValueOrDefault(level.Value, "");
    }
}
