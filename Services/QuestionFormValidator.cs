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

    /// <summary>一般 / 精選單選題。題幹與選項：文字或圖片至少要有一個（支援純圖題、對話截圖選項題等）。</summary>
    private static void ValidateSingleOrSelect(QuestionFormData data, List<ValidationError> errors)
    {
        // 屬性面板：主類 / 次類 / 難易度
        if (data.Topic is null)
            errors.Add(new(nameof(QuestionFormData.Topic), "請選擇主類"));
        if (data.Subtopic is null)
            errors.Add(new(nameof(QuestionFormData.Subtopic), "請選擇次類"));
        if (data.Difficulty is null)
            errors.Add(new(nameof(QuestionFormData.Difficulty), "請選擇難易度"));

        // 題幹：文字或圖片至少要有一個
        if (IsRichTextEmpty(data.Stem) && !HasMasterImage(data.Images, QuestionImageField.Stem))
            errors.Add(new(nameof(QuestionFormData.Stem), "請填寫題幹文字或上傳題幹圖片"));

        // 四選項：逐項檢查「文字或圖片至少一個」；保留舊邏輯只回報第一個漏的選項
        for (int i = 0; i < 4; i++)
        {
            var hasText  = !IsRichTextEmpty(i < data.Options.Length ? data.Options[i] : "");
            var hasImage = HasMasterImage(data.Images, (QuestionImageField)((byte)QuestionImageField.OptionA + i));
            if (!hasText && !hasImage)
            {
                var letter = "ABCD"[i];
                errors.Add(new($"{nameof(QuestionFormData.Options)}_{i}",
                    $"選項 ({letter}) 請輸入文字或上傳圖片"));
                break;
            }
        }

        // 答案必選（沿用既有邏輯）
        if (string.IsNullOrWhiteSpace(data.Answer)
            || (data.Answer != "A" && data.Answer != "B" && data.Answer != "C" && data.Answer != "D"))
        {
            errors.Add(new(nameof(QuestionFormData.Answer), "請選擇正確答案"));
        }

        // 試題解析為選填，不再強制驗證（2026-06：解析/批閱說明改為非必填）
    }

    /// <summary>檢查母題層級指定 FieldType 是否至少有一張有效圖片。</summary>
    private static bool HasMasterImage(List<QuestionImage> images, QuestionImageField fieldType) =>
        images.Any(i => i.FieldType == (byte)fieldType
                     && i.SubQuestionIndex is null
                     && !string.IsNullOrWhiteSpace(i.ImagePath));

    /// <summary>長文題目。文章內容：文字或圖片擇一；批閱說明：選填。</summary>
    private static void ValidateLongText(QuestionFormData data, List<ValidationError> errors)
    {
        if (data.WritingMode is null)
            errors.Add(new(nameof(QuestionFormData.WritingMode), "請選擇寫作模式"));
        if (data.Difficulty is null)
            errors.Add(new(nameof(QuestionFormData.Difficulty), "請選擇難易度"));

        // 題目（Stem）在 razor 中沒標必填、不檢查
        if (IsRichTextEmpty(data.ArticleContent) && !HasMasterImage(data.Images, QuestionImageField.ArticleContent))
            errors.Add(new(nameof(QuestionFormData.ArticleContent), "請填寫文章內容或上傳文章圖片"));
        // 批閱說明為選填，不再強制驗證（2026-06：解析/批閱說明改為非必填）
    }

    /// <summary>閱讀題組。</summary>
    private static void ValidateReadGroup(QuestionFormData data, List<ValidationError> errors)
    {
        // 屬性面板
        if (data.Genre is null)
            errors.Add(new(nameof(QuestionFormData.Genre), "請選擇文體"));
        if (data.Difficulty is null)
            errors.Add(new(nameof(QuestionFormData.Difficulty), "請選擇難易度"));

        // 母題：只有文章內容（文字或圖片擇一）。實際考卷不需要標題，故移除 Stem 檢查
        if (IsRichTextEmpty(data.ArticleContent) && !HasMasterImage(data.Images, QuestionImageField.ArticleContent))
            errors.Add(new(nameof(QuestionFormData.ArticleContent), "請填寫文章內容或上傳文章圖片"));

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
            var subIdx = i;   // 0-based form 位置（對應 QuestionImage.SubQuestionIndex）

            // 主向度 / 能力指標（與短文題組對齊）
            if (sub.CoreAbility is null)
                errors.Add(new($"ReadSub_{n}_CoreAbility", $"子題 {n}：請選擇主向度", n));
            if (sub.Indicator is null)
                errors.Add(new($"ReadSub_{n}_Indicator", $"子題 {n}：請選擇能力指標", n));

            // 題目內容：文字或圖片擇一
            if (IsRichTextEmpty(sub.Stem) && !HasSubImage(data.Images, QuestionImageField.Stem, subIdx))
                errors.Add(new($"ReadSub_{n}_Stem", $"子題 {n}：請填寫題目內容或上傳題目圖片", n));

            // 四選項：逐項「文字或圖片擇一」；只回報第一個漏的
            for (int j = 0; j < 4; j++)
            {
                var hasText  = !IsRichTextEmpty(j < sub.Options.Length ? sub.Options[j] : "");
                var hasImage = HasSubImage(data.Images, (QuestionImageField)((byte)QuestionImageField.OptionA + j), subIdx);
                if (!hasText && !hasImage)
                {
                    var letter = "ABCD"[j];
                    errors.Add(new($"ReadSub_{n}_Options_{j}",
                        $"子題 {n}：選項 ({letter}) 請輸入文字或上傳圖片", n));
                    break;
                }
            }

            // 答案必選
            if (string.IsNullOrWhiteSpace(sub.Answer)
                || (sub.Answer != "A" && sub.Answer != "B" && sub.Answer != "C" && sub.Answer != "D"))
            {
                errors.Add(new($"ReadSub_{n}_Answer", $"子題 {n}：請選擇正確答案", n));
            }

            // 子題試題解析為選填，不再強制驗證（2026-06：解析改為非必填）
        }
    }

    /// <summary>檢查子題層級指定 FieldType + SubQuestionIndex 是否至少有一張有效圖片。</summary>
    private static bool HasSubImage(List<QuestionImage> images, QuestionImageField fieldType, int subIdx) =>
        images.Any(i => i.FieldType == (byte)fieldType
                     && i.SubQuestionIndex == subIdx
                     && !string.IsNullOrWhiteSpace(i.ImagePath));

    /// <summary>短文題組。文章內容與子題題幹：文字或圖片擇一。</summary>
    private static void ValidateShortGroup(QuestionFormData data, List<ValidationError> errors)
    {
        if (data.Genre is null)
            errors.Add(new(nameof(QuestionFormData.Genre), "請選擇文體"));
        if (data.Difficulty is null)
            errors.Add(new(nameof(QuestionFormData.Difficulty), "請選擇難易度"));

        // 實際考卷不需要「題目」欄位，故只檢查文章內容（文字或圖片擇一）
        if (IsRichTextEmpty(data.ArticleContent) && !HasMasterImage(data.Images, QuestionImageField.ArticleContent))
            errors.Add(new(nameof(QuestionFormData.ArticleContent), "請填寫文章內容或上傳文章圖片"));

        if (data.ShortSubQuestions.Count == 0)
        {
            errors.Add(new("ShortSubQuestions", "短文題組至少需要一個子題"));
            return;
        }

        for (int i = 0; i < data.ShortSubQuestions.Count; i++)
        {
            var sub = data.ShortSubQuestions[i];
            var n = i + 1;

            // 子題題目內容：文字或圖片擇一（與單選題、長文邏輯一致）
            if (IsRichTextEmpty(sub.Stem) && !HasSubImage(data.Images, QuestionImageField.Stem, i))
                errors.Add(new($"ShortSub_{n}_Stem", $"子題 {n}：請填寫題目內容或上傳圖片", n));

            if (sub.CoreAbility is null)
                errors.Add(new($"ShortSub_{n}_CoreAbility", $"子題 {n}：請選擇主向度", n));
            if (sub.Indicator is null)
                errors.Add(new($"ShortSub_{n}_Indicator", $"子題 {n}：請選擇能力指標", n));

            // 子題試題解析為選填，不再強制驗證（2026-06：解析改為非必填）
        }
    }

    /// <summary>聽力測驗。題幹必填文字；選項：文字或圖片擇一（聽圖辨識題型）；解析選填。</summary>
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

        // 題幹維持文字必填（聽力題的題幹通常是提示語，較少配圖）
        if (IsRichTextEmpty(data.Stem))
            errors.Add(new(nameof(QuestionFormData.Stem), "請填寫題幹"));

        // 內容（語音逐字稿）：文字或圖片擇一必填（與長文題目 ArticleContent 一致）
        if (IsRichTextEmpty(data.ArticleContent) && !HasMasterImage(data.Images, QuestionImageField.ArticleContent))
            errors.Add(new(nameof(QuestionFormData.ArticleContent), "請填寫內容（語音逐字稿）或上傳補充圖片"));

        // 四選項：逐項檢查「文字或圖片至少一個」；只回報第一個漏的（與 Single 邏輯一致）
        for (int i = 0; i < 4; i++)
        {
            var hasText  = !IsRichTextEmpty(i < data.Options.Length ? data.Options[i] : "");
            var hasImage = HasMasterImage(data.Images, (QuestionImageField)((byte)QuestionImageField.OptionA + i));
            if (!hasText && !hasImage)
            {
                var letter = "ABCD"[i];
                errors.Add(new($"{nameof(QuestionFormData.Options)}_{i}",
                    $"選項 ({letter}) 請輸入文字或上傳圖片"));
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(data.Answer)
            || (data.Answer != "A" && data.Answer != "B" && data.Answer != "C" && data.Answer != "D"))
        {
            errors.Add(new(nameof(QuestionFormData.Answer), "請選擇正確答案"));
        }

        // 試題解析為選填，不再強制驗證（2026-06：解析/批閱說明改為非必填）
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

            // 子題題目內容：文字或圖片擇一
            if (IsRichTextEmpty(sub.Stem) && !HasSubImage(data.Images, QuestionImageField.Stem, i))
                errors.Add(new($"ListenSub_{n}_Stem", $"子題 {n}：請填寫題目內容或上傳圖片", n));

            // 子題四選項：逐項檢查「文字或圖片擇一」；只回報第一個漏的（與 ReadGroup 一致）
            var firstMissing = -1;
            for (int j = 0; j < 4; j++)
            {
                var hasText  = !IsRichTextEmpty(j < sub.Options.Length ? sub.Options[j] : "");
                var hasImage = HasSubImage(data.Images, (QuestionImageField)((byte)QuestionImageField.OptionA + j), i);
                if (!hasText && !hasImage) { firstMissing = j; break; }
            }
            if (firstMissing >= 0)
            {
                var letter = "ABCD"[firstMissing];
                errors.Add(new($"ListenSub_{n}_Options_{firstMissing}",
                    $"子題 {n}：選項 ({letter}) 請輸入文字或上傳圖片", n));
            }

            // 答案必選
            if (string.IsNullOrWhiteSpace(sub.Answer)
                || (sub.Answer != "A" && sub.Answer != "B" && sub.Answer != "C" && sub.Answer != "D"))
            {
                errors.Add(new($"ListenSub_{n}_Answer", $"子題 {n}：請選擇正確答案", n));
            }

            // 子題試題解析為選填，不再強制驗證（2026-06：解析改為非必填）
        }
    }

    // ------------------------------------------------------------------
    //  共用 helper
    // ------------------------------------------------------------------

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
