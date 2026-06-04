namespace MT.Models;

// ======================================================================
//  審題劃記評語（Inline Annotation）— MT_ReviewAnnotations
//  - 對應 ReviewAssignment（隱含 Q/Sub/Stage/Reviewer 四維度）
//  - 標記題目特定文字段落 + 評語 + 命題者回應狀態
// ======================================================================

/// <summary>命題者對劃記的回應狀態（對應 MT_ReviewAnnotations.ResponseState）</summary>
public enum AnnotationResponseState : byte
{
    /// <summary>確認修改 — 命題者接受審題者意見</summary>
    Accepted = 1,

    /// <summary>不修改 — 命題者堅持原內容，需附理由</summary>
    Rejected = 2
}

/// <summary>
/// 審題劃記評語 DTO（讀取用）。
/// 與 MT_ReviewAnnotations schema 對應；QuestionId / SubQuestionId / ReviewStage 為冗餘直接欄位
/// （免 JOIN Assignment 即可查），由 Service 寫入時自動填入。
/// </summary>
public class ReviewAnnotation
{
    public int Id { get; set; }
    public int AssignmentId { get; set; }

    /// <summary>試題 Id（冗餘直接欄位）</summary>
    public int QuestionId { get; set; }

    /// <summary>子題 Id（冗餘直接欄位）；NULL = 母題單元</summary>
    public int? SubQuestionId { get; set; }

    /// <summary>審題階段（冗餘直接欄位；對應 ReviewAssignment.ReviewStage）</summary>
    public ReviewStage Stage { get; set; }

    /// <summary>欄位識別字串（如 stem / optionA / subStem.0 …）</summary>
    public string FieldKey { get; set; } = string.Empty;

    public int AnchorStart { get; set; }
    public int AnchorEnd { get; set; }

    /// <summary>劃記原文（容錯：題目被改寫後 JS 端用 indexOf 比對定位）</summary>
    public string SelectedText { get; set; } = string.Empty;

    /// <summary>劃記評語（純文字）</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>命題者回應狀態（NULL = 未回覆）</summary>
    public AnnotationResponseState? ResponseState { get; set; }

    /// <summary>命題者選擇「不修改」時的理由</summary>
    public string? NoChangeReason { get; set; }

    public DateTime? ResponseAt { get; set; }
    public int? ResponseByUserId { get; set; }
    public string? ResponseByName { get; set; }

    public DateTime CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }

    /// <summary>劃記建立人顯示名稱</summary>
    public string CreatorName { get; set; } = string.Empty;

    /// <summary>是否已回覆</summary>
    public bool IsResponded => ResponseState.HasValue;
}

// ============================================================
//  寫入 API 請求 DTO
// ============================================================

/// <summary>建立新劃記（審題者）— Service 端會由 AssignmentId 反查 QuestionId/SubQuestionId/Stage 填入</summary>
public class SaveAnnotationRequest
{
    public int AssignmentId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public int AnchorStart { get; set; }
    public int AnchorEnd { get; set; }
    public string SelectedText { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
}

/// <summary>命題者回應劃記（確認修改 / 不修改 + 理由）</summary>
public class RespondAnnotationRequest
{
    public int AnnotationId { get; set; }
    public AnnotationResponseState State { get; set; }

    /// <summary>State=Rejected 時必填的不修改理由</summary>
    public string? NoChangeReason { get; set; }
}

// ============================================================
//  審題者匿名顯示規則
// ============================================================

/// <summary>
/// 把劃記建立者依 ReviewStage 匿名化為「命題教師 / 審題委員 / 總召集人」。
/// 用於修題端 / 審題端的 UI 顯示；LOG 與儀表板維持真實姓名（DisplayName）。
///   ReviewStage.Mutual → 互審 = 命題教師（互審者本身亦是命題身分）
///   ReviewStage.Expert → 專審 = 審題委員
///   ReviewStage.Final  → 總審 = 總召集人
/// 未知 stage 回 "審題者"。
/// </summary>
public static class AnnotationActorLabel
{
    public static string Anonymize(ReviewStage? stage) => stage switch
    {
        ReviewStage.Mutual => "命題教師",
        ReviewStage.Expert => "審題委員",
        ReviewStage.Final  => "總召集人",
        _                  => "審題者"
    };
}

// ============================================================
// 轉換描述
// ============================================================

/// <summary>
/// 把 annotation.js / DB 內部使用的 fieldKey（如 "subOptionA.0"）轉成中文友善描述（「子題 1 選項 A」）。
/// 全站審題 / 修題介面顯示劃記時統一走這個 helper。
/// </summary>
public static class AnnotationFieldLabel
{
    private static readonly Dictionary<string, string> Master = new()
    {
        ["stem"]        = "題幹",
        ["optionA"]     = "選項 A",
        ["optionB"]     = "選項 B",
        ["optionC"]     = "選項 C",
        ["optionD"]     = "選項 D",
        ["analysis"]    = "試題解析",
        ["article"]     = "文章",
        ["gradingNote"] = "批閱說明"
    };

    private static readonly Dictionary<string, string> SubPrefix = new()
    {
        ["subStem"]     = "題幹",
        ["subOptionA"]  = "選項 A",
        ["subOptionB"]  = "選項 B",
        ["subOptionC"]  = "選項 C",
        ["subOptionD"]  = "選項 D",
        ["subAnalysis"] = "試題解析"
    };

    /// <summary>
    /// 解析範例：
    ///   "stem"          → "題幹"
    ///   "optionA"       → "選項 A"
    ///   "subOptionA.0"  → "第 1 題 選項 A"
    ///   "subStem.2"     → "第 3 題 題幹"
    /// 未知 key 回傳原字串。
    /// </summary>
    public static string Describe(string? fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey)) return "";

        // 子題：subXxx.{idx}
        var dotIdx = fieldKey.IndexOf('.');
        if (dotIdx > 0)
        {
            var prefix = fieldKey[..dotIdx];
            var suffix = fieldKey[(dotIdx + 1)..];
            if (SubPrefix.TryGetValue(prefix, out var label)
                && int.TryParse(suffix, out var idx))
            {
                return $"第 {idx + 1} 題 {label}";
            }
        }

        // 母題
        return Master.TryGetValue(fieldKey, out var name) ? name : fieldKey;
    }
}
