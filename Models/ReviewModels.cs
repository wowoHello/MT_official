namespace MT.Models;

// ============================================================
//  審題任務（Reviews.razor）專用 DTO 與 Enum
//  對應資料表：MT_ReviewAssignments、MT_ReviewReturnCounts、
//             MT_RevisionReplies、MT_SimilarityChecks、MT_AuditLogs
// ============================================================

/// <summary>審題階段（對應 MT_ReviewAssignments.ReviewStage）</summary>
public enum ReviewStage : byte
{
    /// <summary>互審（一審）— 命題教師之間互評，僅給意見、無決策按鈕</summary>
    Mutual = 1,

    /// <summary>專審（二審）— 專家學者，可採用 / 改後再審（無不採用）</summary>
    Expert = 2,

    /// <summary>總審（三審）— 總召，可採用 / 改後再審 / 不採用</summary>
    Final  = 3
}

/// <summary>審題任務狀態（對應 MT_ReviewAssignments.ReviewStatus）</summary>
public enum ReviewTaskStatus : byte
{
    Pending  = 0,
    Reviewing = 1,
    Completed = 2
}

/// <summary>審題決策（對應 MT_ReviewAssignments.Decision）</summary>
public enum ReviewDecision : byte
{
    /// <summary>採用 / 通過</summary>
    Approve = 1,

    /// <summary>改後再審 / 修正</summary>
    Revise  = 2,

    /// <summary>不採用 / 退回（僅總審可用）</summary>
    Reject  = 3
}

/// <summary>歷程軌跡項目來源（決定渲染顏色與排版）</summary>
public enum ReviewHistoryKind : byte
{
    /// <summary>命題建立 / 修改 / 完成（來自 MT_AuditLogs Action=Create/Modify, TargetType=Questions）</summary>
    QuestionEvent = 1,

    /// <summary>審題意見（來自 MT_ReviewAssignments.Comment + Decision）</summary>
    ReviewComment = 2,

    /// <summary>修題說明（來自 MT_RevisionReplies）</summary>
    RevisionReply = 3
}

// ============================================================
//  Modal 開啟所需的完整 DTO
// ============================================================

/// <summary>審題 Modal 開啟時一次拉取的完整資料包</summary>
public class ReviewModalData
{
    /// <summary>當前登入者對此題目的 Assignment（可能 null —— 表示尚未分配給此人）</summary>
    public ReviewAssignmentInfo? MyAssignment { get; set; }

    /// <summary>題目本體（複用 QuestionFormData，唯讀展示）</summary>
    public QuestionFormData Question { get; set; } = new();

    /// <summary>命題者顯示名稱（給 Header 與歷程顯示用）</summary>
    public string CreatorName { get; set; } = string.Empty;

    /// <summary>歷程軌跡（時序排列）</summary>
    public List<ReviewHistoryEntry> History { get; set; } = new();

    /// <summary>相似題比對清單（無相似題則為空 List）</summary>
    public List<ReviewSimilarityEntry> SimilarQuestions { get; set; } = new();

    /// <summary>總召退回次數（僅總審階段需要）</summary>
    public int FinalReturnCount { get; set; }

    /// <summary>是否已解鎖總召自行修題（總召退回 ≥ 2 次後）</summary>
    public bool CanFinalReviewerEdit { get; set; }
}

/// <summary>當前使用者對某題目的 Assignment 紀錄</summary>
public class ReviewAssignmentInfo
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public ReviewStage Stage { get; set; }
    public ReviewTaskStatus Status { get; set; }
    public ReviewDecision? Decision { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>是否已決策（Decision != null）—— UI 用來決定顯示按鈕或唯讀提示</summary>
    public bool IsDecided => Decision.HasValue;

    /// <summary>是否已完成（包含互審只儲存意見即視為完成）</summary>
    public bool IsCompleted => Status == ReviewTaskStatus.Completed;
}

/// <summary>歷程軌跡單筆（時間軸顯示用）</summary>
public class ReviewHistoryEntry
{
    public ReviewHistoryKind Kind { get; set; }
    public DateTime At { get; set; }
    public string ActorName { get; set; } = string.Empty;

    /// <summary>顯示用的事件標籤（如「命題完成」「互審意見」「修題說明」）</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>內容（HTML 已淨化），可能為空（純事件無內文）</summary>
    public string? ContentHtml { get; set; }
}

/// <summary>相似題比對單筆</summary>
public class ReviewSimilarityEntry
{
    public int ComparedQuestionId { get; set; }
    public string ComparedQuestionCode { get; set; } = string.Empty;
    public decimal SimilarityScore { get; set; }

    /// <summary>1=安全、2=相似度高、3=確認重複（對應 MT_SimilarityChecks.Determination）</summary>
    public byte Determination { get; set; }

    /// <summary>對比題的題幹摘要（已去 HTML 標籤、限長）</summary>
    public string SummaryText { get; set; } = string.Empty;
}

// ============================================================
//  Reviews.razor 列表行 DTO（Phase 3.2 才會用到，先定義）
// ============================================================

/// <summary>審題作業區列表單列</summary>
public class ReviewListItem
{
    public int AssignmentId { get; set; }
    public int QuestionId { get; set; }
    public string QuestionCode { get; set; } = string.Empty;
    public string TypeKey { get; set; } = string.Empty;
    public byte? Level { get; set; }
    public byte? Difficulty { get; set; }
    public string SummaryText { get; set; } = string.Empty;
    public ReviewStage Stage { get; set; }
    public ReviewDecision? Decision { get; set; }
    public ReviewTaskStatus Status { get; set; }
    public DateTime AssignedAt { get; set; }

    /// <summary>
    /// 是否已完成（任務完成 = 互審意見已存或專/總審決策已下）。
    /// 用 ReviewStatus 而非 Decision，因互審無決策但 Comment 儲存後即視為完成。
    /// </summary>
    public bool IsCompleted => Status == ReviewTaskStatus.Completed;
}

/// <summary>審核結果與歷史 Tab 的單列（採用 / 不採用）</summary>
public class ReviewHistoryItem
{
    public int QuestionId { get; set; }
    public string QuestionCode { get; set; } = string.Empty;
    public string TypeKey { get; set; } = string.Empty;
    public byte? Level { get; set; }
    public byte? Difficulty { get; set; }
    public string SummaryText { get; set; } = string.Empty;
    /// <summary>最終狀態（採用 / 不採用）— 用 QuestionStatus 的 Adopted / Rejected / ClosedNotAdopted</summary>
    public byte FinalStatus { get; set; }
    public DateTime FinalDecidedAt { get; set; }
}

// ============================================================
//  寫入 API 用的請求 DTO
// ============================================================

/// <summary>儲存審題意見草稿（不做決策）</summary>
public class SaveReviewCommentRequest
{
    public int AssignmentId { get; set; }
    public string Comment { get; set; } = string.Empty;
}

/// <summary>提交審題決策（含意見一併存入）</summary>
public class SubmitReviewDecisionRequest
{
    public int AssignmentId { get; set; }
    public string Comment { get; set; } = string.Empty;
    public ReviewDecision Decision { get; set; }
}
