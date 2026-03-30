using System;
using System.Collections.Generic;

namespace MT.Models;

/// <summary>
/// 建立專案時的請求封裝 (Create Project DTO)
/// </summary>
public class CreateProjectRequest
{
    // 主檔資訊
    public string Year { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? School { get; set; }
    public int CreatedBy { get; set; }

    // 連動時程
    public List<ProjectPhaseDto> Phases { get; set; } = new();

    // 專案需求題數
    public List<ProjectTargetDto> Targets { get; set; } = new();

    // 人員指派與配額
    public List<ProjectMemberAllocationDto> MemberAllocations { get; set; } = new();
}

public class ProjectPhaseDto
{
    public int PhaseCode { get; set; } // 1~8
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
    public int UserId { get; set; } // 使用者 ID
    public string RoleName { get; set; } = string.Empty; // 主要角色名稱 (e.g. 命題教師)
    public List<ProjectMemberQuotaDto> Quotas { get; set; } = new(); // 若非命題教師通常為空
}

public class ProjectMemberQuotaDto
{
    public int QuestionTypeId { get; set; }
    public int QuotaCount { get; set; }
}

/// <summary>
/// 專案列表項目 DTO（左側列表顯示用）
/// </summary>
public class ProjectListItem
{
    public int Id { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Year { get; set; }
    public string? School { get; set; }
    public byte Status { get; set; } // 0:準備中, 1:進行中, 2:已結案
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string CreatorName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
}

/// <summary>
/// 專案詳情 DTO（右側面板顯示用，包含時程、題型目標、成員）
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
