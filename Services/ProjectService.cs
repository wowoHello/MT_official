using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using MT.Models;

namespace MT.Services;

public interface IProjectService
{
    Task<int> CreateProjectAsync(CreateProjectRequest request);
    Task<List<ProjectListItem>> GetProjectListAsync();
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

    private static readonly IReadOnlyDictionary<string, byte> RoleCodeByName = new Dictionary<string, byte>(StringComparer.Ordinal)
    {
        ["命題教師"] = PropositionTeacherRoleCode,
        ["互審教師"] = CrossReviewTeacherRoleCode,
        ["專審委員"] = ExpertReviewerRoleCode,
        ["總召(專員)"] = ChiefCoordinatorRoleCode,
        ["專家學者"] = ExpertScholarRoleCode
    };

    private readonly IConfiguration _config;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(IConfiguration config, ILogger<ProjectService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<List<ProjectListItem>> GetProjectListAsync()
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

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
            LEFT JOIN dbo.MT_Users u ON u.UserId = p.CreatedBy
            WHERE p.IsDeleted = 0
            ORDER BY p.Year DESC, p.Id DESC;
            """;

        var result = await conn.QueryAsync<ProjectListItem>(sql);
        return result.ToList();
    }

    public async Task<ProjectDetailDto?> GetProjectDetailAsync(int projectId)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        var detailParams = new { ProjectId = projectId };

        const string mainSql = """
            SELECT
                p.Id,
                p.ProjectCode,
                p.Name,
                p.Year,
                p.School,
                p.Status,
                p.StartDate,
                p.EndDate,
                p.ClosedAt,
                ISNULL(u.DisplayName, N'系統') AS CreatorName
            FROM dbo.MT_Projects p
            LEFT JOIN dbo.MT_Users u ON u.UserId = p.CreatedBy
            WHERE p.Id = @ProjectId AND p.IsDeleted = 0;
            """;

        var detail = await conn.QueryFirstOrDefaultAsync<ProjectDetailDto>(mainSql, detailParams);
        if (detail is null)
        {
            return null;
        }

        try
        {
            const string phasesSql = """
                SELECT PhaseCode, PhaseName, StartDate, EndDate
                FROM dbo.MT_ProjectPhases
                WHERE ProjectId = @ProjectId
                ORDER BY SortOrder;
                """;

            detail.Phases = (await conn.QueryAsync<PhaseDetailDto>(phasesSql, detailParams)).ToList();

            const string targetsSql = """
                SELECT pt.QuestionTypeId, qt.Name AS TypeName, pt.TargetCount
                FROM dbo.MT_ProjectTargets pt
                INNER JOIN dbo.MT_QuestionTypes qt ON qt.Id = pt.QuestionTypeId
                WHERE pt.ProjectId = @ProjectId
                ORDER BY pt.QuestionTypeId;
                """;

            detail.Targets = (await conn.QueryAsync<TargetDetailDto>(targetsSql, detailParams)).ToList();

            const string membersSql = """
                SELECT
                    pm.UserId,
                    ISNULL(u.DisplayName, N'未知') AS DisplayName,
                    t.TeacherCode,
                    ISNULL(roleCodes.RoleCodes, N'') AS RoleCodes
                FROM dbo.MT_ProjectMembers pm
                LEFT JOIN dbo.MT_Users u ON u.UserId = pm.UserId
                LEFT JOIN dbo.MT_Teachers t ON t.UserId = u.UserId
                LEFT JOIN (
                    SELECT
                        pmr.ProjectMemberId,
                        STUFF((
                            SELECT N',' + CONVERT(NVARCHAR(10), pmr2.RoleCode)
                            FROM dbo.MT_ProjectMemberRoles pmr2
                            WHERE pmr2.ProjectMemberId = pmr.ProjectMemberId
                            ORDER BY pmr2.RoleCode
                            FOR XML PATH(''), TYPE
                        ).value('.', 'NVARCHAR(MAX)'), 1, 1, '') AS RoleCodes
                    FROM dbo.MT_ProjectMemberRoles pmr
                    GROUP BY pmr.ProjectMemberId
                ) roleCodes ON roleCodes.ProjectMemberId = pm.Id
                WHERE pm.ProjectId = @ProjectId
                ORDER BY pm.Id;
                """;

            var members = await conn.QueryAsync<ProjectMemberRow>(membersSql, detailParams);
            detail.Members = members.Select(MapMemberDetail).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入專案詳情子資料失敗 (Id={ProjectId})", projectId);
        }

        return detail;
    }

    public async Task<List<ProjectTalentPoolItem>> GetTalentPoolAsync()
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        const string sql = """
            SELECT
                u.UserId,
                u.DisplayName AS Name,
                t.TeacherCode AS Identifier
            FROM dbo.MT_Teachers t
            INNER JOIN dbo.MT_Users u ON u.UserId = t.UserId
            WHERE u.Status = 1
            ORDER BY t.TeacherCode, u.DisplayName;
            """;

        var result = await conn.QueryAsync<ProjectTalentPoolItem>(sql);
        return result.ToList();
    }

    public async Task SoftDeleteProjectAsync(int projectId, int deletedBy)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

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
    }

    public async Task<int> CreateProjectAsync(CreateProjectRequest req)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        await conn.OpenAsync();

        using var trans = conn.BeginTransaction(IsolationLevel.Serializable);

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

                var roleCode = ResolveRoleCode(alloc.RoleName);
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
        var seq = 1;
        if (!string.IsNullOrWhiteSpace(latestCode) &&
            latestCode.Length >= 8 &&
            int.TryParse(latestCode[^3..], out var lastSeq))
        {
            seq = lastSeq + 1;
        }

        return $"P{rocYear}{seq:D3}";
    }

    private static byte ResolveRoleCode(string roleName)
    {
        return RoleCodeByName.TryGetValue(roleName, out var roleCode)
            ? roleCode
            : (byte)0;
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

    private static MemberDetailDto MapMemberDetail(ProjectMemberRow row)
    {
        var roleNames = row.RoleCodes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => byte.TryParse(code, out var parsedCode) ? ResolveRoleName(parsedCode) : "未指定")
            .Distinct()
            .ToList();

        return new MemberDetailDto
        {
            UserId = row.UserId,
            DisplayName = row.DisplayName,
            TeacherCode = row.TeacherCode,
            RoleName = roleNames.Count > 0 ? string.Join(", ", roleNames) : "未指定"
        };
    }

    private sealed class ProjectMemberRow
    {
        public int UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? TeacherCode { get; set; }
        public string RoleCodes { get; set; } = string.Empty;
    }
}
