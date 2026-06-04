namespace MT.Models;

// ======================================================================
// 教師管理系統
// ======================================================================

// ─── 左側列表項目 ───
public class TeacherListItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string TeacherCode { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public string School { get; set; } = "";
    public int Status { get; set; }

    /// <summary>該教師在當前梯次的角色名稱（可能多個）。</summary>
    public List<RoleTag> ProjectRoles { get; set; } = [];
}

// ─── 右側詳情 DTO ───
public class TeacherDetailDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string TeacherCode { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public int Status { get; set; }

    // 基本資料
    public int? Gender { get; set; }
    public string? Phone { get; set; }
    public string? IdNumber { get; set; }

    // 任教背景
    public string School { get; set; } = "";
    public string? Department { get; set; }
    public string? Title { get; set; }
    public string? Expertise { get; set; }
    public int? TeachingYears { get; set; }
    public int? Education { get; set; }

    // 帳號
    public string? Note { get; set; }
    public bool IsFirstLogin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // ─── 輔助方法 ───

    public string GenderText => Gender switch
    {
        1 => "男",
        2 => "女",
        _ => "未設定"
    };

    public string EducationText => Education switch
    {
        0 => "其它",
        1 => "專科",
        2 => "學士",
        3 => "碩士",
        4 => "博士",
        _ => "其它"
    };

    /// <summary>身分證遮罩（前3後3，中間 ****）。</summary>
    public string MaskedIdNumber
    {
        get
        {
            if (string.IsNullOrEmpty(IdNumber)) return "未設定";
            if (IdNumber.Length <= 6) return new string('*', IdNumber.Length);
            return string.Concat(IdNumber.AsSpan(0, 3), "****", IdNumber.AsSpan(IdNumber.Length - 3));
        }
    }
}

// ─── 統計卡片 DTO ───
public class TeacherStatsDto
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Inactive { get; set; }
    public int CurrentProject { get; set; }
}

// ─── 命題歷程統計 ───
public class TeacherComposeStats
{
    public int TotalCount { get; set; }
    public int AdoptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int ReviewingCount { get; set; }
}

// ─── 命題歷程列表項目 ───
public class TeacherComposeItem
{
    public int QuestionId { get; set; }
    public string QuestionCode { get; set; } = "";
    public int QuestionTypeId { get; set; }
    public string TypeName { get; set; } = "";
    public int? Level { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public int Status { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>0=CWT, 1=LCT，對應 MT_Projects.ProjectType。</summary>
    public byte ProjectType { get; set; }

    public string LevelText => ProjectType == 1
        ? (Level.HasValue ? QuestionConstants.ListenLevelLabels.GetValueOrDefault((byte)Level.Value, "—") : "—")
        : Level switch
        {
            0 => "初級",
            1 => "中級",
            2 => "中高級",
            3 => "高級",
            4 => "優級",
            _ => "—"
        };
}

// ─── 命題歷程分頁結果 ───
public class TeacherComposeHistoryResult
{
    public List<TeacherComposeItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int PageCount => PageSize > 0 ? (TotalCount + PageSize - 1) / PageSize : 0;
}

// ─── 審題歷程統計 ───
public class TeacherReviewStats
{
    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }
    public int PendingCount { get; set; }
}

// ─── 審題歷程列表項目 ───
public class TeacherReviewItem
{
    public int ReviewAssignmentId { get; set; }
    public string QuestionCode { get; set; } = "";
    public int QuestionTypeId { get; set; }
    public string TypeName { get; set; } = "";
    public int? Level { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public int ReviewStage { get; set; }
    public int? Decision { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>梯次結案時間；NULL 表示尚未結案。</summary>
    public DateTime? ProjectClosedAt { get; set; }

    /// <summary>結案後對應 MT_Questions.Status（12=採用、11=不採用）。</summary>
    public int FinalQuestionStatus { get; set; }

    /// <summary>0=CWT, 1=LCT，對應 MT_Projects.ProjectType。</summary>
    public byte ProjectType { get; set; }

    public string LevelText => ProjectType == 1
        ? (Level.HasValue ? QuestionConstants.ListenLevelLabels.GetValueOrDefault((byte)Level.Value, "—") : "—")
        : Level switch
        {
            0 => "初級",
            1 => "中級",
            2 => "中高級",
            3 => "高級",
            4 => "優級",
            _ => "—"
        };

    /// <summary>結案後顯示「已結案」，否則顯示審題階段名稱。</summary>
    public string StageText => ProjectClosedAt != null ? "已結案" : ReviewStage switch
    {
        1 => "互審",
        2 => "專審",
        3 => "總審",
        _ => "—"
    };

    /// <summary>本次審題決策（通過/修正/退回/未決）— 永遠顯示這一筆 assignment 當下的決策，不因結案覆蓋。</summary>
    public string ThisDecisionText => Decision switch
    {
        1 => "通過",
        2 => "修正",
        3 => "退回",
        _ => "未決"
    };

    /// <summary>題目最終結果（採用/不採用/—）— 僅梯次結案後有意義，未結案顯示 —。</summary>
    public string FinalResultText => ProjectClosedAt == null
        ? "—"
        : (FinalQuestionStatus == 12 ? "採用" : "不採用");

    /// <summary>向後相容，請改用 ThisDecisionText / FinalResultText。</summary>
    [Obsolete("請改用 ThisDecisionText（本次決策）與 FinalResultText（最終結果）")]
    public string DecisionText => ProjectClosedAt != null
        ? (FinalQuestionStatus == 12 ? "採用" : "不採用")
        : Decision switch
        {
            1 => "通過",
            2 => "修正",
            3 => "退回",
            _ => "未決"
        };
}

// ─── 審題歷程分頁結果 ───
public class TeacherReviewHistoryResult
{
    public List<TeacherReviewItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int PageCount => PageSize > 0 ? (TotalCount + PageSize - 1) / PageSize : 0;
}

// ─── 參與專案列表項目 ───
public class TeacherProjectItem
{
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public int ProjectYear { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? ClosedAt { get; set; }
    /// <summary>0=CWT, 1=LCT，對應 MT_Projects.ProjectType。</summary>
    public byte ProjectType { get; set; }
    public ProjectLifecycleStatus EffectiveStatus => ProjectStatusHelper.Resolve(StartDate, EndDate, ClosedAt);
    public List<RoleTag> Roles { get; set; } = [];
    public int QuestionCount { get; set; }
    public int AdoptedCount { get; set; }

    /// <summary>該教師在此專案是否有「可下載聘書」（FileName IS NOT NULL AND IsRevoked=0），給下載按鈕條件渲染用。</summary>
    public bool HasDownloadableCerts { get; set; }

    public string AdoptionRateText
    {
        get
        {
            // 進行中梯次的採用率沒有意義（多數題目尚在審），等結案後才呈現
            if (ClosedAt == null) return "進行中";
            if (QuestionCount == 0) return "0%";
            return $"{(double)AdoptedCount / QuestionCount:P0}";
        }
    }
}

// ─── 新增教師回傳結果 ───
public class CreateTeacherResult
{
    public int TeacherId { get; set; }

    /// <summary>
    /// 是否沿用既有 MT_Users 帳號（Email 已存在於系統時自動綁定，不新建帳號）。
    /// </summary>
    public bool ReusedExistingUser { get; set; }

    /// <summary>
    /// 沿用時，提示前端的既有使用者顯示名稱與帳號（讓 toast 告知使用者）。
    /// </summary>
    public string? ExistingDisplayName { get; set; }
    public string? ExistingUsername { get; set; }
}

// ─── 新增教師請求 ───
public class CreateTeacherRequest
{
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public int Gender { get; set; }
    public string? Phone { get; set; }
    public string? IdNumber { get; set; }
    public string School { get; set; } = "";
    public string? Department { get; set; }
    public string? Title { get; set; }
    public string? Expertise { get; set; }
    public int? TeachingYears { get; set; }
    public int? Education { get; set; }
    public int Status { get; set; } = 1;
    public string? Note { get; set; }
}

// ─── 編輯教師請求 ───
public class UpdateTeacherRequest
{
    public int TeacherId { get; set; }
    public string DisplayName { get; set; } = "";
    public int Gender { get; set; }
    public string? Phone { get; set; }
    public string? IdNumber { get; set; }
    public string School { get; set; } = "";
    public string? Department { get; set; }
    public string? Title { get; set; }
    public string? Expertise { get; set; }
    public int? TeachingYears { get; set; }
    public int? Education { get; set; }
    public int Status { get; set; }
    public string? Note { get; set; }
}

// ─── 加入梯次請求 ───
public class AssignProjectRequest
{
    public int TeacherUserId { get; set; }
    public int ProjectId { get; set; }
    public List<int> RoleIds { get; set; } = [];
}

// ─── 批次匯入 ───

/// <summary>批次匯入 — 每列資料（解析後）</summary>
public class BatchImportRow
{
    public int RowNumber { get; set; }                           // Excel 原始列號（從 2 開始）
    public CreateTeacherRequest Data { get; set; } = new();      // 復用既有 DTO，涵蓋全部 13 欄
    public BatchImportRowStatus Status { get; set; }
    public List<string> Errors { get; set; } = new();            // 紅色錯誤訊息（不可匯入）
    public List<string> Warnings { get; set; } = new();          // 橘色警告訊息（DB Email 已存在）
    public bool IsSelected { get; set; } = true;                 // 使用者勾選狀態
}

/// <summary>批次匯入 — 列的驗證狀態（三色 UI 對應）</summary>
public enum BatchImportRowStatus
{
    Valid   = 0,    // 驗證通過，可匯入（綠色）
    Warning = 1,    // DB Email 已存在，預設勾選可嘗試匯入（橘色）
    Error   = 2     // 格式/必填/檔內重複錯誤，強制排除（紅色）
}

/// <summary>批次匯入 — 每筆匯入結果明細</summary>
public class BatchImportRowResult
{
    public int RowNumber { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

// ─── EditForm 表單模型 ───
public class TeacherFormModel
{
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public int Gender { get; set; }
    public string? Phone { get; set; }
    public string? IdNumber { get; set; }
    public string School { get; set; } = "";
    public string? Department { get; set; }
    public string? Title { get; set; }
    public string? Expertise { get; set; }
    public int? TeachingYears { get; set; }
    public int? Education { get; set; }
    public int Status { get; set; } = 1;
    public string? Note { get; set; }
}

// ─── 匯出名單 ───

/// <summary>
/// 匯出報表中的單筆資料列（一位教師 × 一種身分）。
/// 所有 cell 一律以「渲染字串」呈現 — 可能是「X / Y」、純數字、或「－」（無分母）。
/// 由 Service 端依規格組裝完字串後交給 Razor，Razor 只負責原樣寫入 Excel。
/// </summary>
public class TeacherExportRow
{
    public string DisplayName { get; set; } = "";
    /// <summary>身分標籤，直接取自 MT_Roles.Name（「命題教師」/「審題委員」/「總召集人」/「計畫主持人」等）。</summary>
    public string RoleLabel { get; set; } = "";
    /// <summary>
    /// 6 個類型/難度欄的渲染字串，順序對應 <see cref="TeacherExportResult.CategoryHeaders"/>。
    /// 格式：「X / Y」（X=命/審題數，Y=分配數）；無分母時為「－」。
    /// </summary>
    public string[] CategoryCells { get; set; } = ["－", "－", "－", "－", "－", "－"];
    /// <summary>採用題數渲染字串（Status=12 數量；無資料來源則「－」）。</summary>
    public string AdoptedCell  { get; set; } = "－";
    /// <summary>不採用題數渲染字串（Status=11 數量；無資料來源則「－」）。</summary>
    public string RejectedCell { get; set; } = "－";
}

/// <summary>匯出服務的回傳結果，包含梯次名稱、等級、題型欄標題、與所有資料列。</summary>
public class TeacherExportResult
{
    public string ProjectName { get; set; } = "";
    /// <summary>0=CWT, 1=LCT，對應 MT_Projects.ProjectType。</summary>
    public byte ProjectType { get; set; }
    /// <summary>專案等級顯示字串（CWT：「初等/中等/中高等/高等/優等」；LCT：「－」）。</summary>
    public string ExamLevelLabel { get; set; } = "－";
    /// <summary>6 個類型/難度欄的標題，隨 ProjectType 切換。</summary>
    public string[] CategoryHeaders { get; set; } = new string[6];
    public List<TeacherExportRow> Rows { get; set; } = [];
    /// <summary>梯次層級結案採用題數（CWT + 已結案才填，否則為 null）。Status IN (9,12) 計。</summary>
    public int? ClosedAdopted  { get; set; }
    /// <summary>梯次層級結案不採用題數（CWT + 已結案才填，否則為 null）。Status IN (10,11) 計。</summary>
    public int? ClosedRejected { get; set; }
}