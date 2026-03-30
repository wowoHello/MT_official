using Dapper;
using Microsoft.Data.SqlClient;
using MT.Models;
using System.Data;
using System.Text.Json;

namespace MT.Services;

public interface IProjectService
{
    Task<int> CreateProjectAsync(CreateProjectRequest request);
    Task<List<ProjectListItem>> GetProjectListAsync();
    Task<ProjectDetailDto?> GetProjectDetailAsync(int projectId);
    Task SoftDeleteProjectAsync(int projectId, int deletedBy);
}

public class ProjectService : IProjectService
{
    private readonly IConfiguration _config;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(IConfiguration config, ILogger<ProjectService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>取得所有專案列表（含建立者名稱與參與人數）</summary>
    public async Task<List<ProjectListItem>> GetProjectListAsync()
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        var sql = @"
            SELECT
                p.Id, p.ProjectCode, p.Name, p.Year, p.School, p.Status,
                p.StartDate, p.EndDate,
                ISNULL(u.DisplayName, N'系統') AS CreatorName,
                (SELECT COUNT(*) FROM dbo.MT_ProjectMembers pm WHERE pm.ProjectId = p.Id) AS MemberCount
            FROM dbo.MT_Projects p
            LEFT JOIN dbo.MT_Users u ON u.UserId = p.CreatedBy
            WHERE p.IsDeleted = 0
            ORDER BY p.Year DESC, p.Id DESC";

        var result = await conn.QueryAsync<ProjectListItem>(sql);
        return result.ToList();
    }

    /// <summary>取得單一專案詳情（含時程、題型目標、成員）</summary>
    public async Task<ProjectDetailDto?> GetProjectDetailAsync(int projectId)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        // 主檔
        var mainSql = @"
            SELECT p.Id, p.ProjectCode, p.Name, p.Year, p.School, p.Status,
                   p.StartDate, p.EndDate, p.ClosedAt,
                   ISNULL(u.DisplayName, N'系統') AS CreatorName
            FROM dbo.MT_Projects p
            LEFT JOIN dbo.MT_Users u ON u.UserId = p.CreatedBy
            WHERE p.Id = @Id AND p.IsDeleted = 0";
        var detail = await conn.QueryFirstOrDefaultAsync<ProjectDetailDto>(mainSql, new { Id = projectId });
        if (detail == null) return null;

        // 時程
        var phasesSql = @"
            SELECT PhaseCode, PhaseName, StartDate, EndDate
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @Id
            ORDER BY SortOrder";
        detail.Phases = (await conn.QueryAsync<PhaseDetailDto>(phasesSql, new { Id = projectId })).ToList();

        // 題型目標數量
        var targetsSql = @"
            SELECT pt.QuestionTypeId, qt.Name AS TypeName, pt.TargetCount
            FROM dbo.MT_ProjectTargets pt
            INNER JOIN dbo.MT_QuestionTypes qt ON qt.Id = pt.QuestionTypeId
            WHERE pt.ProjectId = @Id
            ORDER BY pt.QuestionTypeId";
        detail.Targets = (await conn.QueryAsync<TargetDetailDto>(targetsSql, new { Id = projectId })).ToList();

        // 成員
        var membersSql = @"
            SELECT pm.UserId,
                   ISNULL(u.DisplayName, N'未知') AS DisplayName,
                   u.Account AS TeacherCode,
                   ISNULL(r.RoleName, N'未指定') AS RoleName
            FROM dbo.MT_ProjectMembers pm
            LEFT JOIN dbo.MT_Users u ON u.UserId = pm.UserId
            LEFT JOIN (
                SELECT pmr.ProjectMemberId,
                       STRING_AGG(ro.Name, N', ') AS RoleName
                FROM dbo.MT_ProjectMemberRoles pmr
                LEFT JOIN dbo.MT_Roles ro ON ro.Id = pmr.RoleCode
                GROUP BY pmr.ProjectMemberId
            ) r ON r.ProjectMemberId = pm.Id
            WHERE pm.ProjectId = @Id
            ORDER BY pm.Id";
        detail.Members = (await conn.QueryAsync<MemberDetailDto>(membersSql, new { Id = projectId })).ToList();

        return detail;
    }

    /// <summary>軟刪除專案（設 IsDeleted = 1）</summary>
    public async Task SoftDeleteProjectAsync(int projectId, int deletedBy)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        var sql = @"
            UPDATE dbo.MT_Projects
            SET IsDeleted = 1, DeletedAt = GETDATE()
            WHERE Id = @Id";
        await conn.ExecuteAsync(sql, new { Id = projectId });

        // 寫入稽核日誌
        var auditSql = @"
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue)
            VALUES (@UserId, 2, 2, @TargetId, N'軟刪除專案')";
        await conn.ExecuteAsync(auditSql, new { UserId = deletedBy, TargetId = projectId });
    }

    public async Task<int> CreateProjectAsync(CreateProjectRequest req)
    {
        // 取得連線
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        await conn.OpenAsync();
        
        using var trans = conn.BeginTransaction();
        try
        {
            // 1. 自動產生 ProjectCode (例如: P2026001)
            var latestCode = await conn.QueryFirstOrDefaultAsync<string>(
                "SELECT TOP 1 ProjectCode FROM dbo.MT_Projects WHERE Year = @Year ORDER BY Id DESC",
                new { Year = int.Parse(req.Year) }, transaction: trans);

            int seq = 1;
            if (!string.IsNullOrEmpty(latestCode) && latestCode.Length >= 8)
            {
                if (int.TryParse(latestCode.Substring(latestCode.Length - 3), out int lastSeq))
                    seq = lastSeq + 1;
            }
            string projectCode = $"P{req.Year}{seq:D3}";

            // 2. 新增 MT_Projects
            var projectSql = @"
                INSERT INTO dbo.MT_Projects (ProjectCode, Name, Year, School, Status, StartDate, EndDate, CreatedBy)
                OUTPUT INSERTED.Id
                VALUES (@ProjectCode, @Name, @Year, @School, 0, @StartDate, @EndDate, @CreatedBy)";

            DateTime projectStartDate = req.Phases.FirstOrDefault(p => p.PhaseCode == 1)?.StartDate ?? DateTime.Today;
            DateTime projectEndDate = req.Phases.OrderByDescending(p => p.PhaseCode).FirstOrDefault()?.EndDate ?? DateTime.Today.AddMonths(2);

            int projectId = await conn.QuerySingleAsync<int>(projectSql, new
            {
                ProjectCode = projectCode,
                Name = req.Name,
                Year = int.Parse(req.Year),
                School = req.School,
                StartDate = projectStartDate,
                EndDate = projectEndDate,
                CreatedBy = req.CreatedBy
            }, transaction: trans);

            // 3. 新增 MT_ProjectPhases
            var phaseSql = @"
                INSERT INTO dbo.MT_ProjectPhases (ProjectId, PhaseCode, PhaseName, StartDate, EndDate, SortOrder)
                VALUES (@ProjectId, @PhaseCode, @Name, @StartDate, @EndDate, @PhaseCode)";
            
            foreach (var phase in req.Phases)
            {
                await conn.ExecuteAsync(phaseSql, new
                {
                    ProjectId = projectId,
                    PhaseCode = phase.PhaseCode,
                    Name = phase.Name,
                    StartDate = phase.StartDate,
                    EndDate = phase.EndDate
                }, transaction: trans);
            }

            // 4. 新增 MT_ProjectTargets
            if (req.Targets.Any())
            {
                var targetSql = @"
                    INSERT INTO dbo.MT_ProjectTargets (ProjectId, QuestionTypeId, TargetCount)
                    VALUES (@ProjectId, @QuestionTypeId, @TargetCount)";
                
                foreach (var target in req.Targets)
                {
                    await conn.ExecuteAsync(targetSql, new
                    {
                        ProjectId = projectId,
                        QuestionTypeId = target.QuestionTypeId,
                        TargetCount = target.TargetCount
                    }, transaction: trans);
                }
            }

            // 5. 新增 MT_ProjectMembers 與關聯 Roles/Quotas
            var memberSql = @"
                INSERT INTO dbo.MT_ProjectMembers (ProjectId, UserId)
                OUTPUT INSERTED.Id
                VALUES (@ProjectId, @UserId)";
            
            var roleSql = @"
                INSERT INTO dbo.MT_ProjectMemberRoles (ProjectMemberId, RoleCode)
                VALUES (@ProjectMemberId, @RoleCode)";
            
            var quotaSql = @"
                INSERT INTO dbo.MT_MemberQuotas (ProjectMemberId, QuestionTypeId, QuotaCount)
                VALUES (@ProjectMemberId, @QuestionTypeId, @QuotaCount)";

            // 暫時的角色名稱轉代碼邏輯 (這應該參考 MT_Roles，為求此處順利運作先做 Mapping)
            byte MapRoleCode(string roleName)
            {
                return roleName switch
                {
                    "命題教師" => 1,
                    "互審教師" => 2,
                    "專家學者" => 3,
                    "總召(專員)" => 4,
                    _ => 0
                };
            }

            foreach (var alloc in req.MemberAllocations)
            {
                // 先插入 MT_ProjectMembers
                int memberId = await conn.QuerySingleAsync<int>(memberSql, new
                {
                    ProjectId = projectId,
                    UserId = alloc.UserId
                }, transaction: trans);

                // 插入 MT_ProjectMemberRoles
                byte roleCode = MapRoleCode(alloc.RoleName);
                if (roleCode > 0)
                {
                    await conn.ExecuteAsync(roleSql, new
                    {
                        ProjectMemberId = memberId,
                        RoleCode = roleCode
                    }, transaction: trans);
                }

                // 插入 MT_MemberQuotas
                foreach (var quota in alloc.Quotas)
                {
                    if (quota.QuotaCount > 0)
                    {
                        await conn.ExecuteAsync(quotaSql, new
                        {
                            ProjectMemberId = memberId,
                            QuestionTypeId = quota.QuestionTypeId,
                            QuotaCount = quota.QuotaCount
                        }, transaction: trans);
                    }
                }
            }

            // 6. 新增 AuditLog
            var jsonValue = JsonSerializer.Serialize(new { ProjectId = projectId, Name = req.Name, ProjectCode = projectCode });
            var auditSql = @"
                INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue)
                VALUES (@UserId, 0, 2, @TargetId, @NewValue)"; // Action 0:建立 = 0, TargetType 2:Projects = 2
            
            await conn.ExecuteAsync(auditSql, new
            {
                UserId = req.CreatedBy,
                TargetId = projectId,
                NewValue = jsonValue
            }, transaction: trans);

            trans.Commit();
            return projectId;
        }
        catch (Exception ex)
        {
            trans.Rollback();
            _logger.LogError(ex, "儲存專案發生錯誤");
            throw; // 讓前端接住錯誤並顯示
        }
    }
}
