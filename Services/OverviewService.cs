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

    /// <summary>依題型組成詳情面板用的標籤（主類/次類/文體/核心能力…）。</summary>
    Dictionary<string, string> BuildPreviewTags(QuestionFormData formData);

    /// <summary>依題型挑等級標籤字典（聽力 vs 一般題等級顯示不同）。</summary>
    string LevelLabel(string typeKey, byte? level);
}

public class OverviewService(IQuestionService questionService, IDatabaseService db) : IOverviewService
{
    private readonly IQuestionService _questionService = questionService;
    private readonly IDatabaseService _db = db;

    public async Task<OverviewListResult> LoadAsync(int projectId, OverviewFilter filter)
    {
        // 命題總覽必須看到已刪除題目（紅色「命題刪除」標籤 + 復原按鈕）
        var listFilter = filter.ToListFilter(projectId);
        listFilter.IncludeDeleted = true;

        var countsTask = _questionService.GetStatusCountsAsync(projectId, creatorId: null);
        var listTask   = _questionService.ListAsync(listFilter);
        await Task.WhenAll(countsTask, listTask);

        var list = listTask.Result;
        return new OverviewListResult
        {
            Items        = list.Items,
            StatusCounts = countsTask.Result,
            TotalCount   = list.TotalCount,
            Page         = list.Page,
            PageSize     = list.PageSize
        };
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
