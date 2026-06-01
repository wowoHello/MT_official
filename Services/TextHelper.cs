namespace MT.Services;

/// <summary>
/// 跨 Service 共用的字串安全轉換工具。
/// 原本 QuestionService / ReviewService / RevisionService 各自有一份內容相同的
/// NullIfEmpty / SafeOption 私有 helper，現收斂於此（各服務以 <c>using static</c>
/// 引入，呼叫點維持不變）。
/// </summary>
public static class TextHelper
{
    /// <summary>空字串 / 純空白一律轉 NULL，避免 NVARCHAR 欄位儲存無意義的 ''。</summary>
    public static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>從選項陣列安全取出索引（不存在或空白回 NULL，避免 DB 存空字串）。</summary>
    public static string? SafeOption(string[] options, int index)
        => options.Length > index ? NullIfEmpty(options[index]) : null;
}
