using Dapper;
using Microsoft.Data.SqlClient;
using MT.Models;
using System.Data;
using System.Text.Json;

namespace MT.Services;

public interface IProjectService
{
    Task<int> CreateProjectAsync(CreateProjectRequest request);
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
