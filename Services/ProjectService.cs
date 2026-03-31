using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using MT.Hubs;
using MT.Models;

namespace MT.Services;

public interface IProjectService
{
    Task<int> CreateProjectAsync(CreateProjectRequest request);
    Task<List<ProjectListItem>> GetProjectListAsync();
    Task<List<ProjectSwitcherItem>> GetVisibleProjectsAsync(int userId);
    Task<ProjectDetailDto?> GetProjectDetailAsync(int projectId);
    Task<List<ProjectTalentPoolItem>> GetTalentPoolAsync();
    Task SoftDeleteProjectAsync(int projectId, int deletedBy);
}

public class ProjectService : IProjectService
{
    private const byte PropositionTeacherRoleCode = 1;
    private const byte CrossReviewTeacherRoleCode = 2;
    private const byte ExpertReviewerRoleCode = 3;
    private const byte ChiefCoordinatorRoleCode = 4;
    private const byte ExpertScholarRoleCode = 5;

    private readonly IDatabaseService _db;
    private readonly ILogger<ProjectService> _logger;
    private readonly IHubContext<ProjectsHub> _projectsHubContext;

    public ProjectService(
        IDatabaseService db,
        ILogger<ProjectService> logger,
        IHubContext<ProjectsHub> projectsHubContext)
    {
        _db = db;
        _logger = logger;
        _projectsHubContext = projectsHubContext;
    }

    public async Task<List<ProjectListItem>> GetProjectListAsync()
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                p.Id,
                p.ProjectCode,
                p.Name,
                p.Year,
                p.School,
                p.Status,
                p.StartDate,
                p.EndDate,
                ISNULL(u.DisplayName, N'系統') AS CreatorName,
                (SELECT COUNT(*) FROM dbo.MT_ProjectMembers pm WHERE pm.ProjectId = p.Id) AS MemberCount
            FROM dbo.MT_Projects p
            LEFT JOIN dbo.MT_Users u ON u.Id = p.CreatedBy
            WHERE p.IsDeleted = 0
            ORDER BY p.Year DESC, p.Id DESC;
            """;

        var result = await conn.QueryAsync<ProjectListItem>(sql);
        return result.ToList();
    }

    public async Task<List<ProjectSwitcherItem>> GetVisibleProjectsAsync(int userId)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            WITH UserContext AS (
                SELECT
                    u.Id AS UserId,
                    r.Category AS RoleCategory
                FROM dbo.MT_Users u
                INNER JOIN dbo.MT_Roles r ON r.Id = u.RoleId
                WHERE u.Id = @UserId
                  AND u.Status = 1
            ),
            VisibleProjectIds AS (
                SELECT p.Id AS ProjectId
                FROM dbo.MT_Projects p
                CROSS JOIN UserContext uc
                WHERE uc.RoleCategory = 0
                  AND p.IsDeleted = 0

                UNION

                SELECT pm.ProjectId
                FROM dbo.MT_ProjectMembers pm
                INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
                INNER JOIN UserContext uc ON uc.UserId = pm.UserId
                WHERE uc.RoleCategory = 1

                UNION

                SELECT q.ProjectId
                FROM dbo.MT_Questions q
                INNER JOIN UserContext uc ON uc.UserId = q.CreatorId
                WHERE uc.RoleCategory = 1
                  AND q.IsDeleted = 0

                UNION

                SELECT ra.ProjectId
                FROM dbo.MT_ReviewAssignments ra
                INNER JOIN UserContext uc ON uc.UserId = ra.ReviewerId
                WHERE uc.RoleCategory = 1
            )
            SELECT
                p.Id,
                p.ProjectCode,
                p.Name,
                p.Year,
                p.Status
            FROM dbo.MT_Projects p
            INNER JOIN (
                SELECT DISTINCT ProjectId
                FROM VisibleProjectIds
            ) vp ON vp.ProjectId = p.Id
            WHERE p.IsDeleted = 0
            ORDER BY
                CASE WHEN p.Status = 2 THEN 1 ELSE 0 END,
                p.Year DESC,
                p.Id DESC;
            """;

        var result = await conn.QueryAsync<ProjectSwitcherItem>(sql, new { UserId = userId });
        return result.ToList();
    }

    public async Task<ProjectDetailDto?> GetProjectDetailAsync(int projectId)
    {
        using var conn = _db.CreateConnection();
        var detailParams = new { ProjectId = projectId };

        const string multipleSql = """
            -- 1. Project Detail
            SELECT
                p.Id, p.ProjectCode, p.Name, p.Year, p.School, p.Status, 
                p.StartDate, p.EndDate, p.ClosedAt,
                ISNULL(u.DisplayName, N'系統') AS CreatorName
            FROM dbo.MT_Projects p
            LEFT JOIN dbo.MT_Users u ON u.Id = p.CreatedBy
            WHERE p.Id = @ProjectId AND p.IsDeleted = 0;

            -- 2. Phases
            SELECT PhaseCode, PhaseName, StartDate, EndDate
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId
            ORDER BY SortOrder;

            -- 3. Targets
            SELECT pt.QuestionTypeId, qt.Name AS TypeName, pt.TargetCount
            FROM dbo.MT_ProjectTargets pt
            INNER JOIN dbo.MT_QuestionTypes qt ON qt.Id = pt.QuestionTypeId
            WHERE pt.ProjectId = @ProjectId
            ORDER BY pt.QuestionTypeId;

            -- 4. Members and Roles
            SELECT
                pm.UserId,
                ISNULL(u.DisplayName, N'未知') AS DisplayName,
                t.TeacherCode,
                pmr.RoleCode
            FROM dbo.MT_ProjectMembers pm
            LEFT JOIN dbo.MT_Users u ON u.Id = pm.UserId
            LEFT JOIN dbo.MT_Teachers t ON t.UserId = u.Id
            LEFT JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
            WHERE pm.ProjectId = @ProjectId
            ORDER BY pm.Id, pmr.RoleCode;
            """;

        try
        {
            using var multi = await conn.QueryMultipleAsync(multipleSql, detailParams);
            
            var detail = await multi.ReadFirstOrDefaultAsync<ProjectDetailDto>();
            if (detail is null)
                return null;
                
            detail.Phases = (await multi.ReadAsync<PhaseDetailDto>()).ToList();
            detail.Targets = (await multi.ReadAsync<TargetDetailDto>()).ToList();
            
            var memberRows = await multi.ReadAsync<ProjectMemberRow>();
            detail.Members = memberRows
                .GroupBy(m => new { m.UserId, m.DisplayName, m.TeacherCode })
                .Select(g => new MemberDetailDto
                {
                    UserId = g.Key.UserId,
                    DisplayName = g.Key.DisplayName,
                    TeacherCode = g.Key.TeacherCode,
                    RoleName = g.Any(x => x.RoleCode.HasValue) 
                               ? string.Join(", ", g.Where(x => x.RoleCode.HasValue).Select(x => ResolveRoleName(x.RoleCode!.Value)).Distinct())
                               : "未指定"
                }).ToList();
                
            return detail;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入專案詳情失敗 (Id={ProjectId})", projectId);
            throw;
        }
    }

    public async Task<List<ProjectTalentPoolItem>> GetTalentPoolAsync()
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                u.Id AS UserId,
                u.DisplayName AS Name,
                t.TeacherCode AS Identifier
            FROM dbo.MT_Teachers t
            INNER JOIN dbo.MT_Users u ON u.Id = t.UserId
            WHERE u.Status = 1
            ORDER BY t.TeacherCode, u.DisplayName;
            """;

        var result = await conn.QueryAsync<ProjectTalentPoolItem>(sql);
        return result.ToList();
    }

    public async Task SoftDeleteProjectAsync(int projectId, int deletedBy)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            UPDATE dbo.MT_Projects
            SET IsDeleted = 1,
                DeletedAt = GETDATE()
            WHERE Id = @ProjectId;
            """;

        await conn.ExecuteAsync(sql, new { ProjectId = projectId });

        const string auditSql = """
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue)
            VALUES (@UserId, 2, 2, @TargetId, N'刪除專案');
            """;

        await conn.ExecuteAsync(auditSql, new { UserId = deletedBy, TargetId = projectId });

        await BroadcastProjectChangedAsync(ProjectRealtimeChangeType.Deleted, projectId);
    }

    public async Task<int> CreateProjectAsync(CreateProjectRequest req)
    {
        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        using var trans = await conn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            const string latestCodeSql = """
                SELECT TOP 1 ProjectCode
                FROM dbo.MT_Projects WITH (UPDLOCK, HOLDLOCK)
                WHERE Year = @Year
                ORDER BY Id DESC;
                """;

            var year = int.Parse(req.Year);
            var latestCode = await conn.QueryFirstOrDefaultAsync<string>(
                latestCodeSql,
                new { Year = year },
                transaction: trans);

            var projectCode = BuildNextProjectCode(req.Year, latestCode);

            const string projectSql = """
                INSERT INTO dbo.MT_Projects (ProjectCode, Name, Year, School, Status, StartDate, EndDate, CreatedBy)
                OUTPUT INSERTED.Id
                VALUES (@ProjectCode, @Name, @Year, @School, 0, @StartDate, @EndDate, @CreatedBy);
                """;

            var projectStartDate = req.Phases.FirstOrDefault(p => p.PhaseCode == 1)?.StartDate ?? DateTime.Today;
            var projectEndDate = req.Phases.OrderByDescending(p => p.PhaseCode).FirstOrDefault()?.EndDate ?? DateTime.Today.AddMonths(2);

            var projectId = await conn.QuerySingleAsync<int>(
                projectSql,
                new
                {
                    ProjectCode = projectCode,
                    Name = req.Name,
                    Year = year,
                    School = req.School,
                    StartDate = projectStartDate,
                    EndDate = projectEndDate,
                    CreatedBy = req.CreatedBy
                },
                transaction: trans);

            const string phaseSql = """
                INSERT INTO dbo.MT_ProjectPhases (ProjectId, PhaseCode, PhaseName, StartDate, EndDate, SortOrder)
                VALUES (@ProjectId, @PhaseCode, @Name, @StartDate, @EndDate, @PhaseCode);
                """;

            foreach (var phase in req.Phases)
            {
                await conn.ExecuteAsync(
                    phaseSql,
                    new
                    {
                        ProjectId = projectId,
                        PhaseCode = phase.PhaseCode,
                        Name = phase.Name,
                        StartDate = phase.StartDate,
                        EndDate = phase.EndDate
                    },
                    transaction: trans);
            }

            if (req.Targets.Any())
            {
                const string targetSql = """
                    INSERT INTO dbo.MT_ProjectTargets (ProjectId, QuestionTypeId, TargetCount)
                    VALUES (@ProjectId, @QuestionTypeId, @TargetCount);
                    """;

                foreach (var target in req.Targets)
                {
                    await conn.ExecuteAsync(
                        targetSql,
                        new
                        {
                            ProjectId = projectId,
                            QuestionTypeId = target.QuestionTypeId,
                            TargetCount = target.TargetCount
                        },
                        transaction: trans);
                }
            }

            const string memberSql = """
                INSERT INTO dbo.MT_ProjectMembers (ProjectId, UserId)
                OUTPUT INSERTED.Id
                VALUES (@ProjectId, @UserId);
                """;

            const string roleSql = """
                INSERT INTO dbo.MT_ProjectMemberRoles (ProjectMemberId, RoleCode)
                VALUES (@ProjectMemberId, @RoleCode);
                """;

            const string quotaSql = """
                INSERT INTO dbo.MT_MemberQuotas (ProjectMemberId, QuestionTypeId, QuotaCount)
                VALUES (@ProjectMemberId, @QuestionTypeId, @QuotaCount);
                """;

            foreach (var alloc in req.MemberAllocations.Where(x => x.UserId > 0))
            {
                var memberId = await conn.QuerySingleAsync<int>(
                    memberSql,
                    new
                    {
                        ProjectId = projectId,
                        UserId = alloc.UserId
                    },
                    transaction: trans);

                var roleCode = alloc.RoleCode;
                if (roleCode > 0)
                {
                    await conn.ExecuteAsync(
                        roleSql,
                        new
                        {
                            ProjectMemberId = memberId,
                            RoleCode = roleCode
                        },
                        transaction: trans);
                }

                foreach (var quota in alloc.Quotas.Where(x => x.QuotaCount > 0))
                {
                    await conn.ExecuteAsync(
                        quotaSql,
                        new
                        {
                            ProjectMemberId = memberId,
                            QuestionTypeId = quota.QuestionTypeId,
                            QuotaCount = quota.QuotaCount
                        },
                        transaction: trans);
                }
            }

            var jsonValue = JsonSerializer.Serialize(new { ProjectId = projectId, Name = req.Name, ProjectCode = projectCode });

            const string auditSql = """
                INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue)
                VALUES (@UserId, 0, 2, @TargetId, @NewValue);
                """;

            await conn.ExecuteAsync(
                auditSql,
                new
                {
                    UserId = req.CreatedBy,
                    TargetId = projectId,
                    NewValue = jsonValue
                },
                transaction: trans);

            trans.Commit();

            await BroadcastProjectChangedAsync(ProjectRealtimeChangeType.Created, projectId);

            return projectId;
        }
        catch (Exception ex)
        {
            trans.Rollback();
            _logger.LogError(ex, "建立專案失敗");
            throw;
        }
    }

    private static string BuildNextProjectCode(string rocYear, string? latestCode)
    {
        var normalizedYear = rocYear.Trim();
        var seq = 1;

        var expectedPrefix = $"P{normalizedYear}";
        if (!string.IsNullOrWhiteSpace(latestCode) &&
            latestCode.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) &&
            latestCode.Length >= expectedPrefix.Length + 3 &&
            int.TryParse(latestCode[^3..], out var lastSeq))
        {
            seq = lastSeq + 1;
        }

        return $"{expectedPrefix}{seq:D3}";
    }

    private Task BroadcastProjectChangedAsync(ProjectRealtimeChangeType changeType, int projectId)
    {
        return _projectsHubContext.Clients.All.SendAsync(
            "ReceiveProjectChanged",
            new ProjectRealtimeSyncMessage(changeType, projectId));
    }



    private static string ResolveRoleName(byte roleCode)
    {
        return roleCode switch
        {
            PropositionTeacherRoleCode => "命題教師",
            CrossReviewTeacherRoleCode => "互審教師",
            ExpertReviewerRoleCode => "專審委員",
            ChiefCoordinatorRoleCode => "總召(專員)",
            ExpertScholarRoleCode => "專家學者",
            _ => "未指定"
        };
    }

    private sealed class ProjectMemberRow
    {
        public int UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? TeacherCode { get; set; }
        public byte? RoleCode { get; set; }
    }
}
