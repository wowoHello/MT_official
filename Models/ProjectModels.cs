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
    public byte RoleCode { get; set; }
    public List<ProjectMemberQuotaDto> Quotas { get; set; } = new();
}

public class ProjectMemberQuotaDto
{
    public int QuestionTypeId { get; set; }
    public int QuotaCount { get; set; }
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
    public byte Status { get; set; }
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
    public byte Status { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
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
    public byte Status { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime? ClosedAt { get; set; }
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
    public string RoleName { get; set; } = string.Empty;
}
