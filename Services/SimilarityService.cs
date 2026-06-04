using System.Data;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using MT.Models;

namespace MT.Services;

// ============================================================
//  SimilarityService — Plan A 簡化版
//
//  範圍（決策日 2026-05-20）：
//    ✅ ComputeOnDemandAsync — 教師/審題者按【相似題】鈕即時計算
//                              不寫 DB，只回 Top-N 結果
//    ❌ 砍掉自動寫入、批次掃描、Dashboard KPI、並排檢視
//
//  演算法核心保留：NGramJaccard / TextNormalizer / QuestionTextExtractor
//  （Phase A 實作 ComputeOnDemandAsync 時會直接使用）
// ============================================================

public interface ISimilarityService
{
    /// <summary>
    /// 即時計算相似度（Plan A 主菜）
    /// caller 提供 QuestionDraftSnapshot（命題教師當前編輯內容 / 審題端讀到的題目）
    /// 後端撈同梯次同型同級候選並算 Jaccard，回傳 Top-N（不寫 DB）
    /// </summary>
    Task<IReadOnlyList<SimilarityCompareResult>> ComputeOnDemandAsync(
        QuestionDraftSnapshot draft, int topN = 5);
}

// ============================================================
//  SimilarityService 實作（Plan A 實作中）
// ============================================================

public class SimilarityService(
    IDatabaseService db,
    ILogger<SimilarityService> logger) : ISimilarityService
{
    private readonly IDatabaseService _db = db;
    private readonly ILogger<SimilarityService> _logger = logger;

    public async Task<IReadOnlyList<SimilarityCompareResult>> ComputeOnDemandAsync(
        QuestionDraftSnapshot draft, int topN = 5)
    {
        // 0. 基本守門
        if (draft is null) return Array.Empty<SimilarityCompareResult>();
        if (draft.ProjectId <= 0 || draft.QuestionTypeId <= 0)
            return Array.Empty<SimilarityCompareResult>();

        // 1. 抽取來源 grams
        var sourceBundle = QuestionTextExtractor.Extract(draft);
        if (sourceBundle.MasterFields.All(f => f.Grams.Count == 0))
        {
            _logger.LogInformation("[SimOnDemand] 來源內容過短：ProjectId={Pid} TypeId={Tid}",
                draft.ProjectId, draft.QuestionTypeId);
            return Array.Empty<SimilarityCompareResult>();
        }

        // 2. 撈候選（跨所有梯次 + 同題型 + 同等級 + 排除自己 + 排除無效題目）
        //    Plan A 設計：所有梯次都納入，讓教師看到跨年度可能重複題；
        //    排除 Status 0(草稿) / 10(不採用) / 11(結案未採用) 與已刪除題目
        using var conn = (SqlConnection)_db.CreateConnection();
        await conn.OpenAsync();

        var selfFilter = draft.QuestionId.HasValue ? "AND q.Id <> @SelfId" : string.Empty;
        var candidatesSql = $"""
            SELECT q.Id, q.ProjectId, p.Name AS ProjectName,
                   q.QuestionTypeId, q.Level,
                   q.QuestionCode, q.CreatorId, u.DisplayName AS CreatorName,
                   q.Stem, q.ArticleTitle, q.ArticleContent,
                   q.OptionA, q.OptionB, q.OptionC, q.OptionD
            FROM dbo.MT_Questions q
            INNER JOIN dbo.MT_Projects p ON p.Id = q.ProjectId
            LEFT JOIN dbo.MT_Users u ON u.Id = q.CreatorId
            WHERE q.QuestionTypeId = @TypeId
              AND q.Level          = @Level
              AND q.IsDeleted      = 0
              AND q.DeletedAt     IS NULL
              AND q.Status NOT IN (0, 10, 11)
              {selfFilter};
            """;
        var rows = (await conn.QueryAsync<CandidateRow>(candidatesSql, new
        {
            draft.ProjectId,
            TypeId = draft.QuestionTypeId,
            Level  = draft.ExamLevel,
            SelfId = draft.QuestionId ?? 0
        })).ToList();

        if (rows.Count == 0)
        {
            _logger.LogInformation("[SimOnDemand] 無候選：ProjectId={Pid} TypeId={Tid} Level={Lv}",
                draft.ProjectId, draft.QuestionTypeId, draft.ExamLevel);
            return Array.Empty<SimilarityCompareResult>();
        }

        // 3. 題組類（3/5/7）一次撈全部候選的子題（套同樣過濾條件）
        var subsByParent = new Dictionary<int, List<SubQuestionLoadRow>>();
        if (draft.QuestionTypeId is 3 or 5 or 7)
        {
            var ids = rows.Select(r => r.Id).ToList();
            const string subSql = """
                SELECT Id, ParentQuestionId, Stem, OptionA, OptionB, OptionC, OptionD
                FROM dbo.MT_SubQuestions
                WHERE ParentQuestionId IN @Ids
                  AND IsDeleted   = 0
                  AND DeletedAt  IS NULL
                  AND Status     NOT IN (0, 10, 11)
                ORDER BY ParentQuestionId, SortOrder;
                """;
            var allSubs = await conn.QueryAsync<SubQuestionLoadRow>(subSql, new { Ids = ids });
            subsByParent = allSubs.GroupBy(s => s.ParentQuestionId)
                                  .ToDictionary(g => g.Key, g => g.ToList());
        }

        // 4. 對每個候選計算 Jaccard
        var scored = new List<(decimal Score, SimilarityCompareResult Item)>(rows.Count);
        foreach (var row in rows)
        {
            var candSnap = new QuestionDraftSnapshot
            {
                QuestionId     = row.Id,
                ProjectId      = row.ProjectId,
                QuestionTypeId = row.QuestionTypeId,
                ExamLevel      = row.Level,
                Stem           = row.Stem ?? string.Empty,
                Title          = row.ArticleTitle,
                ArticleContent = row.ArticleContent,
                OptionA        = row.OptionA,
                OptionB        = row.OptionB,
                OptionC        = row.OptionC,
                OptionD        = row.OptionD
            };
            if (subsByParent.TryGetValue(row.Id, out var subs))
            {
                foreach (var s in subs)
                {
                    candSnap.SubQuestions.Add(new SubQuestionSnapshot
                    {
                        SubQuestionId = s.Id,
                        Stem          = s.Stem ?? string.Empty,
                        OptionA       = s.OptionA,
                        OptionB       = s.OptionB,
                        OptionC       = s.OptionC,
                        OptionD       = s.OptionD
                    });
                }
            }

            var candBundle = QuestionTextExtractor.Extract(candSnap);
            if (candBundle.MasterFields.Count != sourceBundle.MasterFields.Count) continue;

            var score = NGramJaccard.WeightedJaccard(
                sourceBundle.MasterFields, candBundle.MasterFields);

            var det = score >= SimilarityThresholds.DuplicateThreshold ? SimilarityDetermination.Duplicate
                    : score >= SimilarityThresholds.RiskyThreshold      ? SimilarityDetermination.Risky
                    : score >= SimilarityThresholds.WriteThreshold      ? SimilarityDetermination.Safe
                    : SimilarityDetermination.None;

            scored.Add((score, new SimilarityCompareResult
            {
                ComparedQuestionId   = row.Id,
                ComparedQuestionCode = row.QuestionCode ?? string.Empty,
                ProjectName          = row.ProjectName ?? string.Empty,
                Score                = score,
                Determination        = det,
                CreatorName          = row.CreatorName ?? string.Empty
            }));
        }

        // 5. 排序取 Top-N（高分在前）
        var top = scored.OrderByDescending(p => p.Score).Take(topN).Select(p => p.Item).ToList();
        _logger.LogInformation("[SimOnDemand] 完成：候選 {Total} 道，回傳 Top-{N}", rows.Count, top.Count);
        return top;
    }

    // ====================================================================
    //  Plan A 內部 DB 載入用 record（不對外公開）
    // ====================================================================

    private record CandidateRow(
        int Id, int ProjectId, string? ProjectName,
        int QuestionTypeId, byte Level,
        string? QuestionCode, int CreatorId, string? CreatorName,
        string? Stem, string? ArticleTitle, string? ArticleContent,
        string? OptionA, string? OptionB, string? OptionC, string? OptionD);

    private record SubQuestionLoadRow(
        int Id, int ParentQuestionId,
        string? Stem,
        string? OptionA, string? OptionB, string? OptionC, string? OptionD);
}

// ============================================================
//  Internal Helper 1：TextNormalizer
//  把 HTML 髒污文字洗成乾淨字串，給 N-gram 切分用
//
//  範例輸入：「<p>下列何者為 <strong>臺灣</strong>最高峰？</p>」
//  範例輸出：「下列何者為臺灣最高峰?」
// ============================================================

internal static class TextNormalizer
{
    /// <summary>對應 HTML tag 的正規式（緩存編譯結果加速重複呼叫）</summary>
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

    /// <summary>對應多餘空白的正規式（轉成空字串移除）</summary>
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// 移除 HTML tag + 解碼 HTML entity（&amp; &nbsp; 等）
    /// 不處理 <script> </script> 或惡意內容（Service 層輸入來自Quill）
    /// </summary>
    public static string StripHtml(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var noTags = HtmlTagRegex.Replace(raw, string.Empty);
        return WebUtility.HtmlDecode(noTags);
    }

    /// <summary>
    /// 完整正規化：StripHtml → 全形轉半形 → 移除所有空白
    /// 移除空白是為了避免「下列 何者」與「下列何者」算成兩個不同字串
    /// </summary>
    public static string Normalize(string? raw)
    {
        var stripped = StripHtml(raw);
        if (stripped.Length == 0) return string.Empty;
        var halfWidth = ToHalfWidth(stripped);
        return WhitespaceRegex.Replace(halfWidth, string.Empty);
    }

    /// <summary>
    /// 全形英數標點 → 半形（Ａ→A、！→!）
    /// 全形空白 U+3000 → 半形空白
    /// 中文字（Unicode CJK 區段）不動
    /// </summary>
    private static string ToHalfWidth(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c >= '!' && c <= '~')
                sb.Append((char)(c - 0xFEE0));
            else if (c == '　')
                sb.Append(' ');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}

// ============================================================
//  Internal Helper 2：NGramJaccard
//  把字串切成 4-gram 集合，並計算兩集合的 Jaccard 相似度
//
//  範例：
//    A = "下列何者為臺灣"      → {下列何者, 列何者為, 何者為臺, 者為臺灣}
//    B = "下列何者是臺灣"      → {下列何者, 列何者是, 何者是臺, 者是臺灣}
//    交集 = {下列何者} 1 個
//    聯集 = 7 個
//    Jaccard = 1/7 ≈ 0.143 ×100 ≈ 14.29 分
// ============================================================

internal static class NGramJaccard
{
    /// <summary>
    /// 切 N-gram。預設 N=4，從計畫書定案的「4-gram 對中文檢定題目最平衡」
    /// 太短（< N）的字串回空集合（避免異常），分數計算自動為 0
    /// </summary>
    public static HashSet<string> ToGrams(string text, int n = 4)
    {
        var result = new HashSet<string>();
        if (string.IsNullOrEmpty(text) || text.Length < n)
            return result;

        for (int i = 0; i <= text.Length - n; i++)
            result.Add(text.Substring(i, n));

        return result;
    }

    /// <summary>
    /// 兩集合的 Jaccard 係數 ×100（轉成 decimal 百分比）
    /// 公式：|A ∩ B| / |A ∪ B|
    /// 兩空集合視為 0 分（避免 0/0 例外）
    /// </summary>
    public static decimal Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0m;

        int intersect = a.Intersect(b).Count();
        int union = a.Count + b.Count - intersect;
        if (union == 0) return 0m;

        return Math.Round((decimal)intersect / union * 100m, 2);
    }

    /// <summary>
    /// 加權平均：多個欄位（題幹/選項/文章…）各算 Jaccard 後依權重平均
    /// 兩邊欄位數量必須一致（一一對應）
    /// </summary>
    public static decimal WeightedJaccard(
        IReadOnlyList<(HashSet<string> Grams, decimal Weight)> a,
        IReadOnlyList<(HashSet<string> Grams, decimal Weight)> b)
    {
        if (a.Count != b.Count)
            throw new ArgumentException($"欄位數量不一致：a={a.Count}, b={b.Count}");

        decimal sumWeighted = 0m;
        decimal sumWeight = 0m;
        for (int i = 0; i < a.Count; i++)
        {
            var fieldScore = Jaccard(a[i].Grams, b[i].Grams);
            sumWeighted += fieldScore * a[i].Weight;
            sumWeight += a[i].Weight;
        }

        return sumWeight == 0m ? 0m : Math.Round(sumWeighted / sumWeight, 2);
    }
}

// ============================================================
//  Internal Helper 3：QuestionTextExtractor
//  依題型抽取對應欄位 + 套用權重，輸出可直接餵給 NGramJaccard 的結構
//
//  為什麼要分題型抽取？
//    不同題型的「資訊密度」分布不同：
//    - 一般題：題幹是重點 (70%)，選項輔助 (30%)
//    - 長文題：文章佔絕大資訊量 (90%)
//    - 題組類：母題文章 + 子題 都重要
// ============================================================

/// <summary>母題+子題抽取結果（給 Service 層拿去比對）</summary>
internal class QuestionGramBundle
{
    /// <summary>母題層欄位（每筆 = 一個欄位的 grams + 該欄位權重）</summary>
    public List<(HashSet<string> Grams, decimal Weight)> MasterFields { get; set; } = new();

    /// <summary>子題層（僅 TypeId 3/5/7 填）</summary>
    public List<SubQuestionGramBundle> SubQuestions { get; set; } = new();
}

internal class SubQuestionGramBundle
{
    public int? SubQuestionId { get; set; }
    public List<(HashSet<string> Grams, decimal Weight)> Fields { get; set; } = new();
}

internal static class QuestionTextExtractor
{
    /// <summary>
    /// 把 DraftSnapshot 抽成可比對的 grams（母題+子題）
    /// 依題型 dispatch 到不同的抽取函式
    /// </summary>
    public static QuestionGramBundle Extract(QuestionDraftSnapshot draft)
    {
        return draft.QuestionTypeId switch
        {
            1 or 2 => ExtractGeneralOrPicked(draft),  // 一般 / 精選 — 共用表單邏輯
            3      => ExtractShortGroup(draft),        // 短文題組
            4      => ExtractLongText(draft),          // 長文題目
            5      => ExtractReadingGroup(draft),      // 閱讀題組
            6      => ExtractListening(draft),         // 聽力題目
            7      => ExtractListeningGroup(draft),    // 聽力題組
            _      => throw new ArgumentException(
                          $"未知題型 TypeId={draft.QuestionTypeId}")
        };
    }

    // ========== 各題型抽取邏輯 ==========

    /// <summary>一般 / 精選：Stem 70% + 四選項合併 30%</summary>
    private static QuestionGramBundle ExtractGeneralOrPicked(QuestionDraftSnapshot d)
    {
        var stemGrams = ToGramsNormalized(d.Stem);
        var optionsGrams = ToGramsNormalized(
            string.Join(' ', d.OptionA, d.OptionB, d.OptionC, d.OptionD));

        return new QuestionGramBundle
        {
            MasterFields =
            {
                (stemGrams,    70m),
                (optionsGrams, 30m)
            }
        };
    }

    /// <summary>長文：文章 90% + 標題 10%（沒有選項）</summary>
    private static QuestionGramBundle ExtractLongText(QuestionDraftSnapshot d)
    {
        return new QuestionGramBundle
        {
            MasterFields =
            {
                (ToGramsNormalized(d.ArticleContent), 90m),
                (ToGramsNormalized(d.Title),          10m)
            }
        };
    }

    /// <summary>短文題組：母題文章 60% + 子題題幹拼接 40%；子題層額外各自比對</summary>
    private static QuestionGramBundle ExtractShortGroup(QuestionDraftSnapshot d)
    {
        var subStemsConcat = string.Join(' ', d.SubQuestions.Select(s => s.Stem));

        var bundle = new QuestionGramBundle
        {
            MasterFields =
            {
                (ToGramsNormalized(d.ArticleContent), 60m),
                (ToGramsNormalized(subStemsConcat),   40m)
            }
        };

        // 子題層：每個子題獨立切片（Stem 70% + 選項 30%）
        foreach (var sub in d.SubQuestions)
        {
            bundle.SubQuestions.Add(new SubQuestionGramBundle
            {
                SubQuestionId = sub.SubQuestionId,
                Fields =
                {
                    (ToGramsNormalized(sub.Stem),                                70m),
                    (ToGramsNormalized(JoinOptions(sub.OptionA, sub.OptionB,
                                                   sub.OptionC, sub.OptionD)),  30m)
                }
            });
        }
        return bundle;
    }

    /// <summary>閱讀題組：母題文章 50% + 子題題幹 30% + 子題選項 20%</summary>
    private static QuestionGramBundle ExtractReadingGroup(QuestionDraftSnapshot d)
    {
        var subStemsConcat = string.Join(' ', d.SubQuestions.Select(s => s.Stem));
        var subOptsConcat = string.Join(' ', d.SubQuestions.SelectMany(s =>
            new[] { s.OptionA, s.OptionB, s.OptionC, s.OptionD }));

        var bundle = new QuestionGramBundle
        {
            MasterFields =
            {
                (ToGramsNormalized(d.ArticleContent), 50m),
                (ToGramsNormalized(subStemsConcat),   30m),
                (ToGramsNormalized(subOptsConcat),    20m)
            }
        };

        foreach (var sub in d.SubQuestions)
        {
            bundle.SubQuestions.Add(new SubQuestionGramBundle
            {
                SubQuestionId = sub.SubQuestionId,
                Fields =
                {
                    (ToGramsNormalized(sub.Stem), 70m),
                    (ToGramsNormalized(JoinOptions(sub.OptionA, sub.OptionB,
                                                   sub.OptionC, sub.OptionD)), 30m)
                }
            });
        }
        return bundle;
    }

    /// <summary>
    /// 聽力題（TypeId=6）：ArticleContent 70% + Stem 20% + 四選項 10%
    /// 註：「內容」欄位（語音逐字稿/補充說明）對應 MT_Questions.ArticleContent，
    ///     設計上與聽力題組的語音逐字稿語意一致，故權重結構同 ExtractListeningGroup。
    /// </summary>
    private static QuestionGramBundle ExtractListening(QuestionDraftSnapshot d)
    {
        return new QuestionGramBundle
        {
            MasterFields =
            {
                (ToGramsNormalized(d.ArticleContent), 70m),
                (ToGramsNormalized(d.Stem),           20m),
                (ToGramsNormalized(JoinOptions(d.OptionA, d.OptionB,
                                               d.OptionC, d.OptionD)), 10m)
            }
        };
    }

    /// <summary>
    /// 聽力題組（TypeId=7）：ArticleContent 70% + 2 固定子題 Stem 拼接 30%
    /// 註：「語音內容（逐字稿）」實際存於 ArticleContent 欄位（QuestionFormListenGroup 表單設計）。
    /// </summary>
    private static QuestionGramBundle ExtractListeningGroup(QuestionDraftSnapshot d)
    {
        var subStemsConcat = string.Join(' ', d.SubQuestions.Select(s => s.Stem));

        var bundle = new QuestionGramBundle
        {
            MasterFields =
            {
                (ToGramsNormalized(d.ArticleContent), 70m),
                (ToGramsNormalized(subStemsConcat),   30m)
            }
        };

        // 聽力題組子題固定 2 題，只比 Stem（無選項變化空間）
        foreach (var sub in d.SubQuestions)
        {
            bundle.SubQuestions.Add(new SubQuestionGramBundle
            {
                SubQuestionId = sub.SubQuestionId,
                Fields =
                {
                    (ToGramsNormalized(sub.Stem), 100m)
                }
            });
        }
        return bundle;
    }

    // ========== 共用工具 ==========

    /// <summary>正規化文字後切 N-gram 的一條龍 helper</summary>
    private static HashSet<string> ToGramsNormalized(string? raw)
    {
        var normalized = TextNormalizer.Normalize(raw);
        return NGramJaccard.ToGrams(normalized, SimilarityThresholds.GramSize);
    }

    /// <summary>四選項串接成單一字串（空白分隔避免相鄰兩選項頭尾相連造成假 gram）</summary>
    private static string JoinOptions(string? a, string? b, string? c, string? d)
        => string.Join(' ', a ?? string.Empty, b ?? string.Empty,
                            c ?? string.Empty, d ?? string.Empty);
}
