namespace MT.Models;

// ======================================================================
//  既有：配額進度與階段資訊
// ======================================================================
public class QuotaProgressItem
{
    public int QuestionTypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int Target { get; set; }
    public int Completed { get; set; }
    public int Percent => Target > 0 ? Math.Min(100, Completed * 100 / Target) : 0;
}

public class ProjectPhaseInfo
{
    public int PhaseCode { get; set; }
    public string PhaseName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int DaysLeft { get; set; }
    public bool IsUrgent => DaysLeft >= 0 && DaysLeft <= 5;
}

// ======================================================================
//  題型代碼（共用 string key）
// ======================================================================
public static class QuestionTypeCodes
{
    public const string Single      = "single";
    public const string Select      = "select";
    public const string ReadGroup   = "readGroup";
    public const string LongText    = "longText";
    public const string ShortGroup  = "shortGroup";
    public const string Listen      = "listen";
    public const string ListenGroup = "listenGroup";
}

// ======================================================================
//  題型常數聚合：標籤 / 等級 / 屬性 enum 對照 / 級聯映射 / 解碼 helper
//  集中所有題型相關的對照表，razor 與 Service 統一從此引用
// ======================================================================
public static class QuestionConstants
{
    // ----- 題型 → 中文標籤 -----
    public static readonly Dictionary<string, string> TypeLabels = new()
    {
        [QuestionTypeCodes.Single]      = "一般單選題",
        [QuestionTypeCodes.Select]      = "精選單選題",
        [QuestionTypeCodes.ReadGroup]   = "閱讀題組",
        [QuestionTypeCodes.LongText]    = "長文題目",
        [QuestionTypeCodes.ShortGroup]  = "短文題組",
        [QuestionTypeCodes.Listen]      = "聽力測驗",
        [QuestionTypeCodes.ListenGroup] = "聽力題組"
    };

    // ----- 題型 → 可選等級 byte 陣列 -----
    public static readonly Dictionary<string, byte[]> TypeLevels = new()
    {
        [QuestionTypeCodes.Single]      = [0, 1, 2],
        [QuestionTypeCodes.Select]      = [0, 1, 2],
        [QuestionTypeCodes.LongText]    = [0, 1, 2, 4],
        [QuestionTypeCodes.ReadGroup]   = [0, 1, 2, 3, 4],
        [QuestionTypeCodes.ShortGroup]  = [0, 1, 2, 3, 4],  // 現況以網站為主
        [QuestionTypeCodes.Listen]      = [1, 2, 3, 4, 5],
        [QuestionTypeCodes.ListenGroup] = []                // 母題無等級
    };

    // ----- 等級 byte → 標籤（依題型不同呈現方式）-----
    public static readonly Dictionary<byte, string> GeneralLevelLabels = new()
    {
        [0] = "初等", [1] = "中等", [2] = "中高等", [3] = "高等", [4] = "優等"
    };
    public static readonly Dictionary<byte, string> ListenLevelLabels = new()
    {
        [1] = "難度一", [2] = "難度二", [3] = "難度三", [4] = "難度四", [5] = "難度五"
    };

    // ----- 難易度 -----
    public static readonly Dictionary<byte, string> DifficultyLabels = new()
    {
        [1] = "易", [2] = "中", [3] = "難"
    };

    // ----- Topic（主類）-----
    public static readonly Dictionary<byte, string> TopicLabels = new()
    {
        [0] = "文字", [1] = "語詞", [2] = "成語短語", [3] = "造句標點",
        [4] = "修辭技巧", [5] = "語文知識", [6] = "文意判讀"
    };

    // ----- Subtopic（次類，扁平化共 18 種）-----
    public static readonly Dictionary<byte, string> SubtopicLabels = new()
    {
        [0]  = "字音",      [1]  = "字型",      [2]  = "造字原則",
        [3]  = "辭義辨識",  [4]  = "詞彙辨析",  [5]  = "詞性分辨",  [6]  = "語詞應用",
        [7]  = "短語辨識",  [8]  = "語詞使用",  [9]  = "文義取得",
        [10] = "句義",      [11] = "句法辨析",  [12] = "標點符號",
        [13] = "修辭類型",  [14] = "語態變化",
        [15] = "語文知識",
        [16] = "段義辨析",
        [17] = "篇章辨析"
    };

    // ----- 主類 byte → 該主類下可選的 Subtopic byte 陣列 -----
    public static readonly Dictionary<byte, byte[]> TopicSubtopicMap = new()
    {
        [0] = [0, 1, 2],
        [1] = [3, 4, 5, 6],
        [2] = [7, 8, 9],
        [3] = [10, 11, 12],
        [4] = [13, 14],
        [5] = [15],
        [6] = [16]
        // 短文題組固定 Topic=6 / Subtopic=17（篇章辨析）不放在級聯，前端寫死
    };

    // ----- Genre / Material / WritingMode / AudioType -----
    public static readonly Dictionary<byte, string> GenreLabels       = new()
    {
        [0] = "文言文", [1] = "應用文", [2] = "語體文"
    };
    public static readonly Dictionary<byte, string> MaterialLabels    = new()
    {
        [0] = "生活", [1] = "教育", [2] = "職場", [3] = "專業"
    };
    public static readonly Dictionary<byte, string> WritingModeLabels = new()
    {
        [0] = "引導寫作", [1] = "資訊整合"
    };
    public static readonly Dictionary<byte, string> AudioTypeLabels   = new()
    {
        [0] = "對話", [1] = "情境", [2] = "陳述"
    };

    // ----- 聽力測驗 CoreAbility（核心能力）-----
    public static readonly Dictionary<byte, string> CoreAbilityLabels = new()
    {
        [0] = "提取訊息",
        [1] = "理解訊息",
        [2] = "推斷訊息",
        [3] = "歸納分析訊息",
        [4] = "區辨詞語多義性",
        [5] = "統整、闡述或評鑑訊息",
        [6] = "思辨、推衍訊息"
    };

    // ----- 聽力測驗等級 → 該等級可選的 CoreAbility byte 陣列 -----
    public static readonly Dictionary<byte, byte[]> ListenLevelCoreAbilityMap = new()
    {
        [1] = [0],
        [2] = [1],
        [3] = [2],
        [4] = [3, 4],
        [5] = [5, 6]
    };

    // ----- 聽力測驗 DetailIndicator（細目指標，扁平化 15 種）-----
    public static readonly Dictionary<byte, string> DetailIndicatorLabels = new()
    {
        [0]  = "提取對話與訊息主旨",
        [1]  = "回應訊息內容",
        [2]  = "轉述訊息內容",
        [3]  = "理解訊息意圖或細節",
        [4]  = "理解說話者語氣或態度變化",
        [5]  = "理解慣用語的意義",
        [6]  = "推斷訊息邏輯性",
        [7]  = "能掌握語意轉折",
        [8]  = "能推斷語意變化",
        [9]  = "歸納或總結訊息內容",
        [10] = "分解或辨析訊息內容",
        [11] = "區辨詞語的多義性",
        [12] = "摘要、條列、統整訊息關鍵字、要點、主旨",
        [13] = "闡述訊息涵義或評鑑訊息適切性",
        [14] = "思辨、推衍訊息言外意、抽象義"
    };

    // ----- CoreAbility byte → 該核心能力下的 DetailIndicator byte 陣列 -----
    //  注意：4 (區辨詞語多義性) 共用 9~11；6 (思辨、推衍訊息) 共用 12~14
    public static readonly Dictionary<byte, byte[]> CoreAbilityIndicatorMap = new()
    {
        [0] = [0, 1, 2],
        [1] = [3, 4, 5],
        [2] = [6, 7, 8],
        [3] = [9, 10, 11],
        [4] = [9, 10, 11],
        [5] = [12, 13, 14],
        [6] = [12, 13, 14]
    };

    // ----- 短文題組子題：主向度（獨立編碼 0~2）-----
    public static readonly Dictionary<byte, string> ShortGroupCoreAbilityLabels = new()
    {
        [0] = "條列敘述", [1] = "歸納統整", [2] = "分析推理"
    };

    // ----- 短文題組子題：能力指標（獨立編碼 0~13）-----
    public static readonly Dictionary<byte, string> ShortGroupIndicatorLabels = new()
    {
        [0]  = "1-1 條列敘述人、事、物特徵與特質",
        [1]  = "1-2 條列敘述起始原因、發生情況、結論等時空先後順序",
        [2]  = "1-3 條列敘述人、事、物的差異",
        [3]  = "2-1 歸納作者主張",
        [4]  = "2-2 歸納文章主旨",
        [5]  = "2-3 歸納共同特點",
        [6]  = "3-1 分析線索",
        [7]  = "3-2 推論緣由",
        [8]  = "3-3 判斷結果",
        [9]  = "3-4 判斷詞性、主語",
        [10] = "3-5 判斷字句的解釋、文意說明是否正確",
        [11] = "3-6 推測行為的原因或用意",
        [12] = "3-7 推測寫作手法的目的",
        [13] = "3-8 判斷文體、格律、風格"
    };

    // ----- 短文題組子題：主向度 → 能力指標 byte 陣列 -----
    public static readonly Dictionary<byte, byte[]> ShortGroupCoreAbilityIndicatorMap = new()
    {
        [0] = [0, 1, 2],
        [1] = [3, 4, 5],
        [2] = [6, 7, 8, 9, 10, 11, 12, 13]
    };

    // ----- 聽力題組子題：固定難度 → 可選 CoreAbility（沿用 CoreAbilityLabels 全表碼）-----
    public static readonly Dictionary<byte, byte[]> ListenGroupSubFixedDifficultyCoreAbilityMap = new()
    {
        [3] = [2],            // 難度三 → 推斷訊息 (2)
        [4] = [3, 4]          // 難度四 → 歸納分析訊息 (3) / 區辨詞語多義性 (4)
    };

    // ======================================================================
    //  解碼 Helper：依母題題型決定子題 CoreAbility / Indicator 的中文
    // ======================================================================
    public static string DecodeSubCoreAbility(byte? value, string parentTypeKey)
    {
        if (value is null) return "";
        return parentTypeKey switch
        {
            QuestionTypeCodes.ShortGroup  => ShortGroupCoreAbilityLabels.GetValueOrDefault(value.Value, ""),
            QuestionTypeCodes.ListenGroup => CoreAbilityLabels.GetValueOrDefault(value.Value, ""),
            _ => ""
        };
    }

    public static string DecodeSubIndicator(byte? value, string parentTypeKey)
    {
        if (value is null) return "";
        return parentTypeKey switch
        {
            QuestionTypeCodes.ShortGroup  => ShortGroupIndicatorLabels.GetValueOrDefault(value.Value, ""),
            QuestionTypeCodes.ListenGroup => DetailIndicatorLabels.GetValueOrDefault(value.Value, ""),
            _ => ""
        };
    }
}

// ======================================================================
//  子題資料類別（從 razor 內嵌搬出，集中管理）
// ======================================================================
public class SubQuestionChoice           // 用於閱讀題組
{
    public string Stem { get; set; } = "";
    public string[] Options { get; set; } = ["", "", "", ""];
    public string Answer { get; set; } = "";
    public string Analysis { get; set; } = "";
}

public class SubQuestionFreeResponse     // 用於短文題組
{
    public string Stem { get; set; } = "";
    public byte? CoreAbility { get; set; }   // 主向度（ShortGroupCoreAbilityLabels）
    public byte? Indicator { get; set; }     // 能力指標（ShortGroupIndicatorLabels）
    public string Analysis { get; set; } = "";
}

public class ListenGroupSubQuestion      // 用於聽力題組
{
    public byte FixedDifficulty { get; set; }    // 3=難度三 / 4=難度四
    public string Stem { get; set; } = "";
    public string[] Options { get; set; } = ["", "", "", ""];
    public string Answer { get; set; } = "";
    public string Analysis { get; set; } = "";
    public byte? CoreAbility { get; set; }       // 沿用 CoreAbilityLabels 全表碼
    public byte? DetailIndicator { get; set; }   // 沿用 DetailIndicatorLabels 全表碼
}

// ======================================================================
//  共用表單資料模型（為 P2 鋪路：CwtList 散裝欄位收攏的目標）
// ======================================================================
public class QuestionFormData
{
    public string QuestionType { get; set; } = QuestionTypeCodes.Single;
    public byte? Level { get; set; }
    public byte? Difficulty { get; set; }

    // 屬性 enum（對應 MT_Questions 8 個 TINYINT 欄位）
    public byte? Topic { get; set; }
    public byte? Subtopic { get; set; }
    public byte? Genre { get; set; }
    public byte? Material { get; set; }
    public byte? WritingMode { get; set; }
    public byte? AudioType { get; set; }
    public byte? CoreAbility { get; set; }
    public byte? DetailIndicator { get; set; }

    // 主題內容
    public string Stem { get; set; } = "";
    public string ArticleTitle { get; set; } = "";
    public string ArticleContent { get; set; } = "";
    public string Analysis { get; set; } = "";
    public string GradingNote { get; set; } = "";
    public string[] Options { get; set; } = ["", "", "", ""];
    public string Answer { get; set; } = "";
    public string AudioUrl { get; set; } = "";

    // 子題（依題型擇一使用）
    public List<SubQuestionChoice> ReadSubQuestions { get; set; } = [new()];
    public List<SubQuestionFreeResponse> ShortSubQuestions { get; set; } = [new()];
    public List<ListenGroupSubQuestion> ListenGroupSubQuestions { get; set; } =
        [new() { FixedDifficulty = 3 }, new() { FixedDifficulty = 4 }];
}

// ======================================================================
//  列表項類別（為 P3/P4 鋪路：題目列表查詢結果）
// ======================================================================
public class QuestionListItem
{
    public int Id { get; set; }
    public string QuestionCode { get; set; } = "";
    public string TypeKey { get; set; } = "";
    public byte? Level { get; set; }
    public byte? Difficulty { get; set; }
    public byte Status { get; set; }
    public string SummaryHtml { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
