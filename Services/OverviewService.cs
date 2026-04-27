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

    /// <summary>取單筆題目詳情（含子題）。</summary>
    Task<QuestionFormData?> GetDetailAsync(int questionId);

    /// <summary>依題型組成詳情面板用的標籤（主類/次類/文體/核心能力…）。</summary>
    Dictionary<string, string> BuildPreviewTags(QuestionFormData formData);

    /// <summary>依題型挑等級標籤字典（聽力 vs 一般題等級顯示不同）。</summary>
    string LevelLabel(string typeKey, byte? level);
}

public class OverviewService(IQuestionService questionService) : IOverviewService
{
    private readonly IQuestionService _questionService = questionService;

    public async Task<OverviewListResult> LoadAsync(int projectId, OverviewFilter filter)
    {
        var countsTask = _questionService.GetStatusCountsAsync(projectId, creatorId: null);
        var listTask   = _questionService.ListAsync(filter.ToListFilter(projectId));
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

    public Task<QuestionFormData?> GetDetailAsync(int questionId)
        => _questionService.GetByIdAsync(questionId);

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
