namespace MT.Models;

// ============================================================
//  相似度分析（SimilarityAnalysis）專用 DTO 與常數 — Plan A 簡化版
//  對應資料表：MT_SimilarityChecks（v2 schema 保留，Plan A 不寫入）
//
//  使用場景（Plan A）：
//    教師 / 審題者在 Modal 內點【🔍 相似題】鈕
//      → ISimilarityService.ComputeOnDemandAsync
//      → 即時計算後彈窗顯示 Top-N（不寫 DB）
// ============================================================

/// <summary>
/// 查重結果判定（與 MT_SimilarityChecks.Determination 對齊，但 Plan A 多加 None=0 給「&lt; 40 分」用）
/// </summary>
public enum SimilarityDetermination : byte
{
    /// <summary>無疑慮：分數 &lt; 40，沒有顯著相似（僅 Plan A 即時計算用，DB 不寫入）</summary>
    None = 0,

    /// <summary>低度相似：40 ≤ 分數 &lt; 60，僅供參考</summary>
    Safe = 1,

    /// <summary>高度相似：60 ≤ 分數 &lt; 80，建議特別注意</summary>
    Risky = 2,

    /// <summary>確認重複：分數 ≥ 80，強烈建議不採用</summary>
    Duplicate = 3
}

/// <summary>
/// 相似度分析判定門檻與寫入規則常數
/// 設計理由：
///   - &lt; 40：噪音太多，不寫 DB 避免膨脹
///   - 40-59：黃色標籤，供命題者自查參考
///   - 60-79：橘色標籤，審題端 Banner 提示
///   - ≥ 80：紅色標籤，視同確認重複
/// </summary>
public static class SimilarityThresholds
{
    /// <summary>寫入 DB 的最低分數（&lt; 此分數不寫）</summary>
    public const decimal WriteThreshold = 40m;

    /// <summary>判定「相似度高」的下限</summary>
    public const decimal RiskyThreshold = 60m;

    /// <summary>判定「確認重複」的下限</summary>
    public const decimal DuplicateThreshold = 80m;

    /// <summary>當前演算法版本（4-gram Jaccard v1）</summary>
    public const byte CurrentAlgorithmVersion = 1;

    /// <summary>4-gram 切片大小</summary>
    public const int GramSize = 4;
}

// ============================================================
//  比對結果單筆（彈窗主表列）
// ============================================================

/// <summary>
/// 比對結果單筆 — Plan A 即時計算 Modal 顯示用
/// </summary>
public class SimilarityCompareResult
{
    /// <summary>被比對的母題 Id</summary>
    public int ComparedQuestionId { get; set; }

    /// <summary>被比對的題目代號（Q-115-00069 之類）</summary>
    public string ComparedQuestionCode { get; set; } = string.Empty;

    /// <summary>分數 0~100，已四捨五入至小數第 2 位</summary>
    public decimal Score { get; set; }

    /// <summary>判定 0=無疑慮 / 1=低度相似 / 2=高度相似 / 3=確認重複</summary>
    public SimilarityDetermination Determination { get; set; }

    /// <summary>命題者顯示名稱（供 UI 顯示「○○老師命的」）</summary>
    public string CreatorName { get; set; } = string.Empty;
}

// ============================================================
//  Plan A 即時計算 — 草稿快照（不要求題目已存 DB）
// ============================================================

/// <summary>
/// 編輯中題目的「快照」— Tier 2 即時比對的輸入
/// 設計理由：教師按【🔍 比對相似題】時，題目可能還沒按存檔，
///           因此不能用 QuestionId 從 DB 撈，必須由前端把當前編輯內容打包傳來
/// </summary>
public class QuestionDraftSnapshot
{
    /// <summary>當前編輯題目的 Id（用來在比對範圍內排除自己）；草稿全新建立時為 null</summary>
    public int? QuestionId { get; set; }

    /// <summary>當前題目所屬梯次（決定比對範圍）</summary>
    public int ProjectId { get; set; }

    /// <summary>題型 Id（決定加權公式 + 比對範圍）</summary>
    public int QuestionTypeId { get; set; }

    /// <summary>等級（決定比對範圍：初中高級不互比）</summary>
    public byte ExamLevel { get; set; }

    /// <summary>題幹（一般/精選/長文/聽力 使用）</summary>
    public string Stem { get; set; } = string.Empty;

    /// <summary>文章內容（長文/短文題組/閱讀題組 使用）</summary>
    public string? ArticleContent { get; set; }

    /// <summary>題目標題（長文用）</summary>
    public string? Title { get; set; }

    public string? OptionA { get; set; }
    public string? OptionB { get; set; }
    public string? OptionC { get; set; }
    public string? OptionD { get; set; }

    // 註：聽力題組（TypeId=7）的「語音內容（逐字稿）」實際存於 ArticleContent，
    //     聽力題（TypeId=6）DB 端無逐字稿欄位（命題端只有題幹+選項+音檔）。
    //     因此 Snapshot 不另設 AudioTranscript，由 Extractor 根據題型抽取對應欄位。

    /// <summary>子題清單（短文/閱讀/聽力題組用，TypeId 3/5/7）</summary>
    public List<SubQuestionSnapshot> SubQuestions { get; set; } = new();
}

/// <summary>子題快照（搭配 QuestionDraftSnapshot 使用）</summary>
public class SubQuestionSnapshot
{
    /// <summary>子題 Id（草稿新增的子題為 null）</summary>
    public int? SubQuestionId { get; set; }

    public string Stem { get; set; } = string.Empty;
    public string? OptionA { get; set; }
    public string? OptionB { get; set; }
    public string? OptionC { get; set; }
    public string? OptionD { get; set; }
}

// ============================================================
//  共用：QuestionFormData → QuestionDraftSnapshot 轉換
//  CwtList 命題端與 ReviewModal 審題端都用到 QuestionFormData，
//  避免兩處重複組裝邏輯 → 抽成 extension method
// ============================================================

public static class QuestionDraftSnapshotExtensions
{
    /// <summary>
    /// 把 QuestionFormData（命題編輯/審題展示用同一型別）轉換成
    /// 相似度比對所需的 QuestionDraftSnapshot
    /// </summary>
    public static QuestionDraftSnapshot ToDraftSnapshot(this QuestionFormData fd, int projectId)
    {
        var typeId = QuestionConstants.TypeKeyToId[fd.QuestionType];
        var snap = new QuestionDraftSnapshot
        {
            QuestionId     = fd.Id > 0 ? fd.Id : null,
            ProjectId      = projectId,
            QuestionTypeId = typeId,
            ExamLevel      = (byte)(fd.Level ?? 0),
            Stem           = fd.Stem ?? string.Empty,
            Title          = fd.ArticleTitle,
            ArticleContent = fd.ArticleContent,
            OptionA        = OptionAt(fd.Options, 0),
            OptionB        = OptionAt(fd.Options, 1),
            OptionC        = OptionAt(fd.Options, 2),
            OptionD        = OptionAt(fd.Options, 3)
        };

        // 題組類子題（注意 typeId 對應：3=短文 / 5=閱讀 / 7=聽力題組）
        if (typeId == 3) // 短文題組（自由作答，無選項）
        {
            foreach (var s in fd.ShortSubQuestions)
                snap.SubQuestions.Add(new SubQuestionSnapshot
                {
                    SubQuestionId = s.Id > 0 ? s.Id : null,
                    Stem = s.Stem ?? string.Empty
                });
        }
        else if (typeId == 5) // 閱讀題組（含 4 選項）
        {
            foreach (var s in fd.ReadSubQuestions)
                snap.SubQuestions.Add(new SubQuestionSnapshot
                {
                    SubQuestionId = s.Id > 0 ? s.Id : null,
                    Stem    = s.Stem ?? string.Empty,
                    OptionA = OptionAt(s.Options, 0),
                    OptionB = OptionAt(s.Options, 1),
                    OptionC = OptionAt(s.Options, 2),
                    OptionD = OptionAt(s.Options, 3)
                });
        }
        else if (typeId == 7) // 聽力題組（含 4 選項，相似度公式只比 Stem）
        {
            foreach (var s in fd.ListenGroupSubQuestions)
                snap.SubQuestions.Add(new SubQuestionSnapshot
                {
                    SubQuestionId = s.Id > 0 ? s.Id : null,
                    Stem = s.Stem ?? string.Empty
                });
        }

        return snap;
    }

    private static string? OptionAt(string[]? arr, int idx)
        => arr is not null && idx < arr.Length ? arr[idx] : null;
}
