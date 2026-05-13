using System;
using System.Collections.Generic;

namespace MT.Models;

/// <summary>
/// 建立專案時使用的請求 DTO。
/// </summary>
public class CreateProjectRequest
{
    public string Year { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? School { get; set; }
    public int CreatedBy { get; set; }
    public List<ProjectPhaseDto> Phases { get; set; } = new();
    public List<ProjectTargetDto> Targets { get; set; } = new();
    public List<ProjectMemberAllocationDto> MemberAllocations { get; set; } = new();
}

/// <summary>
/// 編輯專案時使用的請求 DTO。
/// </summary>
public class UpdateProjectRequest
{
    public int ProjectId { get; set; }
    public string Year { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? School { get; set; }
    public int UpdatedBy { get; set; }
    public List<ProjectPhaseDto> Phases { get; set; } = new();
    public List<ProjectTargetDto> Targets { get; set; } = new();
    public List<ProjectMemberAllocationDto> MemberAllocations { get; set; } = new();
}

public class ProjectPhaseDto
{
    public int PhaseCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class ProjectTargetDto
{
    public int QuestionTypeId { get; set; }
    public int TargetCount { get; set; }
}

public class ProjectMemberAllocationDto
{
    public int UserId { get; set; }
    public List<int> RoleIds { get; set; } = new();
    public List<ProjectMemberQuotaDto> Quotas { get; set; } = new();

    /// <summary>該人員在此專案已實際命題的題數（IsDeleted=0），編輯模式防呆用。</summary>
    public int CreatedQuestionCount { get; set; }
}

public class ProjectMemberQuotaDto
{
    public int QuestionTypeId { get; set; }
    public int QuotaCount { get; set; }
}

/// <summary>
/// 編輯專案時使用的完整回填資料。
/// </summary>
public class ProjectEditDto
{
    public int Id { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? School { get; set; }
    public DateTime? ClosedAt { get; set; }
    public List<ProjectPhaseDto> Phases { get; set; } = new();
    public List<ProjectTargetDto> Targets { get; set; } = new();
    public List<ProjectMemberAllocationDto> MemberAllocations { get; set; } = new();
}

/// <summary>
/// 可供專案指派的人員清單項目。
/// </summary>
public class ProjectTalentPoolItem
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
}

public class ProjectSwitcherItem
{
    public int Id { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Year { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? CompositionStartDate { get; set; }
    public ProjectLifecycleStatus EffectiveStatus => ProjectStatusHelper.Resolve(StartDate, EndDate, ClosedAt, CompositionStartDate);
    public bool IsClosed => EffectiveStatus == ProjectLifecycleStatus.Closed;
    public bool IsExpired => ProjectStatusHelper.IsExpired(EndDate, ClosedAt);
    public string DisplayName => $"{Year}年度 {Name}";
}

/// <summary>
/// 左側專案列表資料。
/// </summary>
public class ProjectListItem
{
    public int Id { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Year { get; set; }
    public string? School { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? CompositionStartDate { get; set; }
    public ProjectLifecycleStatus EffectiveStatus => ProjectStatusHelper.Resolve(StartDate, EndDate, ClosedAt, CompositionStartDate);
    public bool IsClosed => EffectiveStatus == ProjectLifecycleStatus.Closed;
    public bool IsExpired => ProjectStatusHelper.IsExpired(EndDate, ClosedAt);
    public string CreatorName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
}

/// <summary>
/// 右側專案明細資料。
/// </summary>
public class ProjectDetailDto
{
    public int Id { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Year { get; set; }
    public string? School { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? CompositionStartDate { get; set; }
    public ProjectLifecycleStatus EffectiveStatus => ProjectStatusHelper.Resolve(StartDate, EndDate, ClosedAt, CompositionStartDate);
    public bool IsClosed => EffectiveStatus == ProjectLifecycleStatus.Closed;
    public bool IsExpired => ProjectStatusHelper.IsExpired(EndDate, ClosedAt);
    public string CreatorName { get; set; } = string.Empty;
    public List<PhaseDetailDto> Phases { get; set; } = new();
    public List<TargetDetailDto> Targets { get; set; } = new();
    public List<MemberDetailDto> Members { get; set; } = new();
}

public class PhaseDetailDto
{
    public int PhaseCode { get; set; }
    public string PhaseName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class TargetDetailDto
{
    public int QuestionTypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int TargetCount { get; set; }
}

public class MemberDetailDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? TeacherCode { get; set; }
    public List<RoleTag> Roles { get; set; } = new();
}

public enum ProjectLifecycleStatus : byte
{
    Preparing = 0,
    Active = 1,
    Closed = 2
}

public static class ProjectStatusHelper
{
    public static ProjectLifecycleStatus Resolve(DateTime startDate, DateTime? endDate, DateTime? closedAt)
        => Resolve(startDate, endDate, closedAt, null);

    /// <summary>產學區間結束日已過但尚未手動結案：UI 應顯示提示引導管理員點擊「結案入庫」。</summary>
    public static bool IsExpired(DateTime endDate, DateTime? closedAt)
        => !closedAt.HasValue && endDate.Date < DateTime.Today;

    /// <summary>
    /// 完整版：以「命題階段 StartDate」作為 Preparing/Active 切換點。
    /// - 結案唯一來源：ClosedAt（手動點按「結案入庫」後才會寫入）
    ///   產學區間結束日 EndDate 已過但 ClosedAt 仍是 null 時，視為「進行中（待結案）」，
    ///   由 UI 端的 IsExpired 顯示提示橫幅引導管理員手動結案
    /// - 命題階段尚未開始 → Preparing（即使產學區間已起跑）
    /// - 命題階段已開始 → Active
    /// 若未提供 compositionStartDate，回退為以 startDate（產學區間）判斷。
    /// </summary>
    public static ProjectLifecycleStatus Resolve(DateTime startDate, DateTime? endDate, DateTime? closedAt, DateTime? compositionStartDate)
    {
        if (closedAt.HasValue)
        {
            return ProjectLifecycleStatus.Closed;
        }

        // 優先以命題階段 StartDate 判斷；無資料才退回產學區間
        var threshold = compositionStartDate?.Date ?? startDate.Date;
        return threshold <= DateTime.Today
            ? ProjectLifecycleStatus.Active
            : ProjectLifecycleStatus.Preparing;
    }
}
