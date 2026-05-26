namespace MT.Models;

// ======================================================================
//  審後修訂（Post-Decision Revision）— 由 RevisionService / RevisionHistory.razor 使用
//
//  本檔案的 Models 服務於「管理員在三審結束 / 結案後再編輯題目並調整決策」功能。
//  與 QuestionService.SaveRevisionAsync（命題教師於修題階段內存檔）完全不同：
//    - SaveRevisionAsync     → 教師、修題期間、寫 MT_RevisionReplies、有 PhaseCode 防呆
//    - 本檔對應的 RevisionService → 管理員、決策後、寫 MT_AuditLogs.revisionReason、跳過防呆
// ======================================================================

/// <summary>
/// 審後修訂的儲存請求。母題或子題擇一（SubQuestionId 非 NULL 即為子題修訂）。
/// 修訂後 Status 由 NewDecision + 專案結案狀態自動對應：
///   未結案：Adopt → 9, Reject → 10
///   已結案：Adopt → 12, Reject → 11
/// </summary>
public class AdminReviseRequest
{
    public int QuestionId { get; set; }

    /// <summary>NULL = 修訂母題單元；非 NULL = 修訂該子題單元（與母題 QuestionId 必須對應）。</summary>
    public int? SubQuestionId { get; set; }

    /// <summary>樂觀鎖：載入時記下的 UpdatedAt，儲存時與 DB 比對；不符表示被他人改過。</summary>
    public DateTime ExpectedUpdatedAt { get; set; }

    /// <summary>修訂後的內容（重用既有 QuestionFormData，只取相關欄位寫入）。</summary>
    public QuestionFormData FormData { get; set; } = new();

    /// <summary>修訂後的最終決策（採用 / 不採用）。</summary>
    public AdminReviseDecision NewDecision { get; set; }

    /// <summary>修訂原因（必填，會寫入 MT_AuditLogs.NewValue 的 revisionReason 欄位）。</summary>
    public string RevisionReason { get; set; } = "";
}

/// <summary>審後修訂的決策二選一。內部會自動對應 Status 9/10（未結案）或 12/11（已結案）。</summary>
public enum AdminReviseDecision : byte
{
    Adopt  = 0,
    Reject = 1,
}

/// <summary>RevisionService.SaveAsync 的回傳結果，包含寫入後的新 Status（供 UI 即時更新）。</summary>
public class AdminReviseResult
{
    public bool Success { get; set; }
    public byte NewStatus { get; set; }   // 9 / 10 / 11 / 12
    public string? ErrorMessage { get; set; }

    /// <summary>樂觀鎖衝突時為 true，UI 應提示「此題已被他人修改，請重新載入」。</summary>
    public bool ConflictDetected { get; set; }
}

// ======================================================================
//  RevisionHistory.razor 列表頁用 DTO
// ======================================================================

/// <summary>修訂歷史列表的篩選條件。所有條件皆 optional，可任意組合。</summary>
public class RevisionListFilter
{
    public int? ProjectId { get; set; }            // 梯次篩選；null = 全梯次
    public int? OperatorId { get; set; }           // 修訂者 UserId 篩選
    public string? Keyword { get; set; }           // 題號模糊搜尋（QuestionCode）
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }

    /// <summary>決策變更篩選：null = 全部、true = 僅有改決策、false = 僅改內容未改決策。</summary>
    public bool? OnlyDecisionChanged { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>修訂歷史列表的單筆顯示項。</summary>
public class RevisionListItem
{
    public long AuditLogId { get; set; }
    public DateTime RevisedAt { get; set; }
    public int OperatorId { get; set; }
    public string OperatorName { get; set; } = "";

    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";

    public int QuestionId { get; set; }
    public int? SubQuestionId { get; set; }
    public string QuestionCode { get; set; } = "";    // 母題顯示 Q-115-00013；子題顯示 Q-115-00013-01
    public string TypeKey { get; set; } = "";

    /// <summary>原決策（採用 / 不採用）— 來自 OldValue.statusLabel。</summary>
    public string OldStatusLabel { get; set; } = "";

    /// <summary>新決策（採用 / 不採用）— 來自 NewValue.statusLabel。</summary>
    public string NewStatusLabel { get; set; } = "";

    /// <summary>決策是否變更（Old != New）。UI 端用紅色強調。</summary>
    public bool DecisionChanged { get; set; }

    /// <summary>修訂原因摘要（取前 60 字 + 省略號）。</summary>
    public string RevisionReasonSummary { get; set; } = "";
}

/// <summary>分頁回傳結果。</summary>
public class RevisionListResult
{
    public List<RevisionListItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int PageCount => PageSize <= 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
}

/// <summary>單筆修訂的完整 diff（accordion 展開時顯示）。</summary>
public class RevisionDiffDetail
{
    public long AuditLogId { get; set; }

    /// <summary>修訂原因全文（不截斷）。</summary>
    public string RevisionReason { get; set; } = "";

    /// <summary>各欄位的 Before / After 對照清單。只列出有變動的欄位（內容相同的不出現）。</summary>
    public List<RevisionFieldDiff> Fields { get; set; } = [];
}

/// <summary>單一欄位的 Before / After 對照。OldValue / NewValue 已 StripHtml 為純文字以便比對顯示。</summary>
public class RevisionFieldDiff
{
    public string FieldLabel { get; set; } = "";   // 「題目」「選項 A」「最終決策」…
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";

    /// <summary>true 代表「決策」這個特別欄位（UI 用紅色強調）。</summary>
    public bool IsDecisionField { get; set; }
}
