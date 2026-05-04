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

public class OverviewService(IQuestionService questionService, IReviewService reviewService, IDatabaseService db) : IOverviewService
{
    private readonly IQuestionService _questionService = questionService;
    private readonly IReviewService   _reviewService   = reviewService;
    private readonly IDatabaseService _db = db;

    public async Task<OverviewListResult> LoadAsync(int projectId, OverviewFilter filter)
    {
        // 命題總覽必須看到已刪除題目（紅色「命題刪除」標籤 + 復原按鈕）
        var listFilter = filter.ToListFilter(projectId);
        listFilter.IncludeDeleted = true;

        // 確保階段轉換已執行（idempotent；保證 Overview 看到的 Status 與當前階段對齊）
        // 命題階段結束 + 後續各階段（4/5/6/7/8）自動升級，避免使用者只進 Overview 不進 CwtList 卡狀態
        try
        {
            await _questionService.EnsureCompositionPhaseClosedAsync(projectId);
            await _questionService.EnsurePhaseTransitionAsync(projectId);
        }
        catch
        {
            // 階段轉換失敗不阻擋頁面載入；UI 端會顯示原狀態
        }

        var countsTask  = _questionService.GetStatusCountsAsync(projectId, creatorId: null);
        var listTask    = _questionService.ListAsync(listFilter);
        var pendingTask = GetPendingRevisionCountAsync(projectId);
        await Task.WhenAll(countsTask, listTask, pendingTask);

        var list = listTask.Result;
        return new OverviewListResult
        {
            Items                = list.Items,
            StatusCounts         = countsTask.Result,
            TotalCount           = list.TotalCount,
            Page                 = list.Page,
            PageSize             = list.PageSize,
            PendingRevisionCount = pendingTask.Result
        };
    }

    /// <summary>
    /// 計算「待修編」實際數量：Status IN (4,6,8) 且命題者尚未於該階段於 MT_RevisionReplies 留下紀錄。
    /// 與 QuestionService.GetListAsync 的 HasRepliedThisStage 子查詢同邏輯，差別在此為彙總計數。
    /// </summary>
    private async Task<int> GetPendingRevisionCountAsync(int projectId)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.MT_Questions q
            WHERE q.ProjectId = @ProjectId
              AND q.IsDeleted = 0
              AND q.Status IN (4, 6, 8)
              AND NOT EXISTS (
                    SELECT 1 FROM dbo.MT_RevisionReplies rr
                    WHERE rr.QuestionId = q.Id
                      AND rr.UserId     = q.CreatorId
                      AND rr.Stage      = q.Status
              );
            """;

        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql, new { ProjectId = projectId });
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
