namespace MT.Models;

/// <summary>
/// 題目附圖所屬欄位類型，對應 MT_QuestionImages.FieldType (TINYINT)。
/// 解析（Analysis）不支援圖片，故無對應 enum 值。
/// </summary>
public enum QuestionImageField : byte
{
    Stem           = 1,
    OptionA        = 2,
    OptionB        = 3,
    OptionC        = 4,
    OptionD        = 5,
    ArticleContent = 6,    // 僅 MT_Questions 母題有效，子題不允許
}
