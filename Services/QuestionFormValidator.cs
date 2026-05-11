using MT.Models;
using System.Text.RegularExpressions;

namespace MT.Services;

// ======================================================================
//  QuestionFormValidator — 命題表單欄位驗證 utility（純 static、無依賴）
//  --------------------------------------------------------------
//  目的：
//    在 CwtList.razor 的 SaveAsync 流程中，於進入 Service 層前先做
//    UI 端的欄位完整性檢查，回傳缺失欄位清單供前端：
//      1. 用 SweetAlert2 條列顯示
//      2. 把 FieldKey 餵回各題型表單元件畫紅框
//      3. 透過 JS interop 將第一個 FieldKey 滾入視野
//
//  設計準則：
//    - 純驗證、不改資料；不做網路 / DB / DI；不做 server-side 第二道防線
//    - 草稿（ValidateForDraft）只擋題型 + 等級兩條最基本的線
//    - 命題完成 / 送審（ValidateForCompletion）依 TypeKey 嚴格驗證
//    - 子題集合採「逐筆驗證」並在 Message / FieldKey 標明「第 N 題」
//    - FieldKey 命名規則參考檔頭 docstring，盡量 1:1 對應前端欄位 anchor
// ======================================================================

/// <summary>單筆驗證錯誤（前端用來條列訊息 + 畫紅框 + 滾動）。</summary>
/// <param name="FieldKey">前端 anchor 名稱（對應 data-field 屬性），用於畫紅框與 scrollIntoView。</param>
/// <param name="Message">人類可讀的錯誤訊息，將顯示於 Swal 條列項。</param>
/// <param name="SubIndex">子題索引（題組型才用），1-based；母題或非題組為 null。</param>
public sealed record ValidationError(string FieldKey, string Message, int? SubIndex = null);

public static class QuestionFormValidator
{
    // ------------------------------------------------------------------
    //  公開 API
    // ------------------------------------------------------------------

    /// <summary>「命題完成」/「送審」前的嚴格驗證：依題型逐欄檢查。</summary>
    public static List<ValidationError> ValidateForCompletion(QuestionFormData data)
    {
        var errors = new List<ValidationError>();

        // 共用必填：題型 + 等級（聽力題組母題無等級，例外處理）
        ValidateCommonAttributes(data, errors, strict: true);
        if (errors.Count > 0 && string.IsNullOrWhiteSpace(data.QuestionType))
        {
            // 連題型都沒有就直接回傳；其他驗證沒有意義
            return errors;
        }

        switch (data.QuestionType)
        {
            case QuestionTypeCodes.Single:
            case QuestionTypeCodes.Select:
                ValidateSingleOrSelect(data, errors);
                break;
            case QuestionTypeCodes.LongText:
                ValidateLongText(data, errors);
                break;
            case QuestionTypeCodes.ReadGroup:
                ValidateReadGroup(data, errors);
                break;
            case QuestionTypeCodes.ShortGroup:
                ValidateShortGroup(data, errors);
                break;
            case QuestionTypeCodes.Listen:
                ValidateListen(data, errors);
                break;
            case QuestionTypeCodes.ListenGroup:
                ValidateListenGroup(data, errors);
                break;
        }

        return errors;
    }

    /// <summary>「存為草稿」的寬鬆驗證：只擋題型 + 等級。</summary>
    public static List<ValidationError> ValidateForDraft(QuestionFormData data)
    {
        var errors = new List<ValidationError>();
        ValidateCommonAttributes(data, errors, strict: false);
        return errors;
    }

    // ------------------------------------------------------------------
    //  共用必填（題型 + 等級）
    // ------------------------------------------------------------------

    private static void ValidateCommonAttributes(QuestionFormData data, List<ValidationError> errors, bool strict)
    {
        if (string.IsNullOrWhiteSpace(data.QuestionType))
        {
            errors.Add(new(nameof(QuestionFormData.QuestionType), "請選擇題型"));
            return;
        }

        // 聽力題組母題不需要等級（其等級在子題 FixedDifficulty 上）
        if (data.QuestionType == QuestionTypeCodes.ListenGroup)
        {
            return;
        }

        if (data.Level is null)
        {
            errors.Add(new(nameof(QuestionFormData.Level), "請選擇等級"));
        }

        // 草稿驗證到此為止
        if (!strict) return;
    }

    // ------------------------------------------------------------------
    //  各題型驗證
    // ------------------------------------------------------------------

    /// <summary>一般 / 精選單選題。</summary>
    private static void ValidateSingleOrSelect(QuestionFormData data, List<ValidationError> errors)
    {
        // 屬性面板：主類 / 次類 / 難易度
        if (data.Topic is null)
            errors.Add(new(nameof(QuestionFormData.Topic), "請選擇主類"));
        if (data.Subtopic is null)
            errors.Add(new(nameof(QuestionFormData.Subtopic), "請選擇次類"));
        if (data.Difficulty is null)
            errors.Add(new(nameof(QuestionFormData.Difficulty), "請選擇難易度"));

        // 主要內容：題幹 / 四選項 / 答案
        if (IsRichTextEmpty(data.Stem))
            errors.Add(new(nameof(QuestionFormData.Stem), "請填寫題幹"));

        ValidateOptionsAndAnswer(data.Options, data.Answer, errors,
            optionPrefix: nameof(QuestionFormData.Options),
            answerKey: nameof(QuestionFormData.Answer));

        if (IsRichTextEmpty(data.Analysis))
            errors.Add(new(nameof(QuestionFormData.Analysis), "請填寫試題解析"));
    }

    /// <summary>長文題目。</summary>
    private static void ValidateLongText(QuestionFormData data, List<ValidationError> errors)
    {
        if (data.WritingMode is null)
            errors.Add(new(nameof(QuestionFormData.WritingMode), "請選擇寫作模式"));
        if (data.Difficulty is null)
            errors.Add(new(nameof(QuestionFormData.Difficulty), "請選擇難易度"));

        // 題目（Stem）在 razor 中沒標必填，但文章內容、批閱說明標 *
        if (IsRichTextEmpty(data.ArticleContent))
            errors.Add(new(nameof(QuestionFormData.ArticleContent), "請填寫文章內容"));
        if (IsRichTextEmpty(data.GradingNote))
            errors.Add(new(nameof(QuestionFormData.GradingNote), "請填寫批閱說明"));
    }

    /// <summary>閱讀題組。</summary>
    private static void ValidateReadGroup(QuestionFormData data, List<ValidationError> errors)
    {
        // 屬性面板
        if (data.Genre is null)
            errors.Add(new(nameof(QuestionFormData.Genre), "請選擇文體"));
        if (data.Difficulty is null)
            errors.Add(new(nameof(QuestionFormData.Difficulty), "請選擇難易度"));

        // 母題：標題 + 文章內容
        if (IsRichTextEmpty(data.Stem))
            errors.Add(new(nameof(QuestionFormData.Stem), "請填寫標題"));
        if (IsRichTextEmpty(data.ArticleContent))
            errors.Add(new(nameof(QuestionFormData.ArticleContent), "請填寫文章內容"));

        // 子題逐筆
        if (data.ReadSubQuestions.Count == 0)
        {
            errors.Add(new("ReadSubQuestions", "閱讀題組至少需要一個子題"));
            return;
        }

        for (int i = 0; i < data.ReadSubQuestions.Count; i++)
        {
            var sub = data.ReadSubQuestions[i];
            var n = i + 1;

            if (IsRichTextEmpty(sub.Stem))
                errors.Add(new($"ReadSub_{n}_Stem", $"子題 {n}：請填寫題目內容", n));

            ValidateOptionsAndAnswer(sub.Options, sub.Answer, errors,
                optionPrefix: $"ReadSub_{n}_Options",
                answerKey: $"ReadSub_{n}_Answer",
                subIndex: n,
                subLabel: $"子題 {n}：");

            if (IsRichTextEmpty(sub.Analysis))
                errors.Add(new($"ReadSub_{n}_Analysis", $"子題 {n}：請填寫試題解析", n));
        }
    }

    /// <summary>短文題組。</summary>
    private static void ValidateShortGroup(QuestionFormData data, List<ValidationError> errors)
    {
        if (data.Genre is null)
            errors.Add(new(nameof(QuestionFormData.Genre), "請選擇文體"));
        if (data.Difficulty is null)
            errors.Add(new(nameof(QuestionFormData.Difficulty), "請選擇難易度"));

        if (IsRichTextEmpty(data.Stem))
            errors.Add(new(nameof(QuestionFormData.Stem), "請填寫題目"));
        if (IsRichTextEmpty(data.ArticleContent))
            errors.Add(new(nameof(QuestionFormData.ArticleContent), "請填寫文章內容"));

        if (data.ShortSubQuestions.Count == 0)
        {
            errors.Add(new("ShortSubQuestions", "短文題組至少需要一個子題"));
            return;
        }

        for (int i = 0; i < data.ShortSubQuestions.Count; i++)
        {
            var sub = data.ShortSubQuestions[i];
            var n = i + 1;

            if (IsRichTextEmpty(sub.Stem))
                errors.Add(new($"ShortSub_{n}_Stem", $"子題 {n}：請填寫題目內容", n));

            if (sub.CoreAbility is null)
                errors.Add(new($"ShortSub_{n}_CoreAbility", $"子題 {n}：請選擇主向度", n));
            if (sub.Indicator is null)
                errors.Add(new($"ShortSub_{n}_Indicator", $"子題 {n}：請選擇能力指標", n));

            if (IsRichTextEmpty(sub.Analysis))
                errors.Add(new($"ShortSub_{n}_Analysis", $"子題 {n}：請填寫試題解析", n));
        }
    }

    /// <summary>聽力測驗。</summary>
    private static void ValidateListen(QuestionFormData data, List<ValidationError> errors)
    {
        if (data.CoreAbility is null)
            errors.Add(new(nameof(QuestionFormData.CoreAbility), "請選擇核心能力"));
        if (data.DetailIndicator is null)
            errors.Add(new(nameof(QuestionFormData.DetailIndicator), "請選擇細目指標"));
        if (data.AudioType is null)
            errors.Add(new(nameof(QuestionFormData.AudioType), "請選擇語音類型"));
        if (data.Material is null)
            errors.Add(new(nameof(QuestionFormData.Material), "請選擇素材分類"));
        if (data.Difficulty is null)
            errors.Add(new(nameof(QuestionFormData.Difficulty), "請選擇難易度"));

        if (IsRichTextEmpty(data.Stem))
            errors.Add(new(nameof(QuestionFormData.Stem), "請填寫題幹"));

        ValidateOptionsAndAnswer(data.Options, data.Answer, errors,
            optionPrefix: nameof(QuestionFormData.Options),
            answerKey: nameof(QuestionFormData.Answer));

        if (IsRichTextEmpty(data.Analysis))
            errors.Add(new(nameof(QuestionFormData.Analysis), "請填寫試題解析"));
    }

    /// <summary>聽力題組（母題無等級無難度，固定 2 子題）。</summary>
    private static void ValidateListenGroup(QuestionFormData data, List<ValidationError> errors)
    {
        if (data.AudioType is null)
            errors.Add(new(nameof(QuestionFormData.AudioType), "請選擇語音類型"));
        if (data.Material is null)
            errors.Add(new(nameof(QuestionFormData.Material), "請選擇素材分類"));

        if (IsRichTextEmpty(data.ArticleContent))
            errors.Add(new(nameof(QuestionFormData.ArticleContent), "請填寫語音內容"));

        if (data.ListenGroupSubQuestions.Count == 0)
        {
            errors.Add(new("ListenGroupSubQuestions", "聽力題組需要兩個子題"));
            return;
        }

        for (int i = 0; i < data.ListenGroupSubQuestions.Count; i++)
        {
            var sub = data.ListenGroupSubQuestions[i];
            var n = i + 1;

            if (sub.CoreAbility is null)
                errors.Add(new($"ListenSub_{n}_CoreAbility", $"子題 {n}：請選擇核心能力", n));
            if (sub.DetailIndicator is null)
                errors.Add(new($"ListenSub_{n}_Indicator", $"子題 {n}：請選擇細目指標", n));

            if (IsRichTextEmpty(sub.Stem))
                errors.Add(new($"ListenSub_{n}_Stem", $"子題 {n}：請填寫題目內容", n));

            ValidateOptionsAndAnswer(sub.Options, sub.Answer, errors,
                optionPrefix: $"ListenSub_{n}_Options",
                answerKey: $"ListenSub_{n}_Answer",
                subIndex: n,
                subLabel: $"子題 {n}：");

            if (IsRichTextEmpty(sub.Analysis))
                errors.Add(new($"ListenSub_{n}_Analysis", $"子題 {n}：請填寫試題解析", n));
        }
    }

    // ------------------------------------------------------------------
    //  共用 helper
    // ------------------------------------------------------------------

    /// <summary>
    /// 驗證 ABCD 四選項與正確答案：
    ///   1. 四個選項皆需填寫（避免送出空選項）
    ///   2. Answer 必須是 "A"/"B"/"C"/"D" 之一
    /// </summary>
    private static void ValidateOptionsAndAnswer(
        string[] options,
        string answer,
        List<ValidationError> errors,
        string optionPrefix,
        string answerKey,
        int? subIndex = null,
        string subLabel = "")
    {
        // 四個選項都必須有內容
        var emptyIdx = -1;
        for (int i = 0; i < 4; i++)
        {
            var v = i < options.Length ? options[i] : "";
            if (IsRichTextEmpty(v))
            {
                emptyIdx = i;
                break;
            }
        }
        if (emptyIdx >= 0)
        {
            var letter = "ABCD"[emptyIdx];
            errors.Add(new($"{optionPrefix}_{emptyIdx}",
                $"{subLabel}請填寫選項 {letter} 的內容", subIndex));
        }

        if (string.IsNullOrWhiteSpace(answer)
            || (answer != "A" && answer != "B" && answer != "C" && answer != "D"))
        {
            errors.Add(new(answerKey, $"{subLabel}請選擇正確答案", subIndex));
        }
    }

    /// <summary>
    /// 判斷 Quill 富文本內容是否為空。Quill 空內容常見格式：
    ///   ""、"<p><br></p>"、"<p></p>"、純空白字元、純 HTML 標籤無文字。
    /// 規則：剝除所有標籤後若為空白即視為空。
    /// </summary>
    private static bool IsRichTextEmpty(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return true;

        // 移除 HTML 標籤後檢查純文字（Quill 空段落會殘留 <p><br></p>）
        var stripped = TagRegex.Replace(html, string.Empty);
        stripped = System.Net.WebUtility.HtmlDecode(stripped);
        // 移除常見的不可見空白：&nbsp; 解碼後是  、Quill 也可能殘留 ​ 零寬空白
        stripped = stripped.Replace(' ', ' ').Replace("​", string.Empty);
        return string.IsNullOrWhiteSpace(stripped);
    }

    private static readonly Regex TagRegex = new("<.*?>",
        RegexOptions.Singleline | RegexOptions.Compiled);
}
