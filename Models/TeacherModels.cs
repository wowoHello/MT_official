namespace MT.Models;

// ======================================================================
// 教師管理系統（人才庫）
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
        1 => "學士",
        2 => "碩士",
        3 => "博士",
        _ => "未設定"
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

    public string LevelText => Level switch
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

    public string LevelText => Level switch
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

    /// <summary>
    /// 結案後依 FinalQuestionStatus 顯示最終結果（12=採用、其他=不採用）；
    /// 未結案則顯示審題中間決策。
    /// </summary>
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
