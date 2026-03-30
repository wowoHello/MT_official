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
