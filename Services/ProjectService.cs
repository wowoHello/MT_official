using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using MT.Hubs;
using MT.Models;

namespace MT.Services;

/// <summary>
/// 定義命題專案管理所需的查詢、建立與刪除服務契約。
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// 建立新專案，並一併寫入階段、題型目標、成員角色與配額資料。
    /// </summary>
    Task<int> CreateProjectAsync(CreateProjectRequest request);

    /// <summary>
    /// 取得指定專案的可編輯完整資料，供表單回填使用。
    /// </summary>
    Task<ProjectEditDto?> GetProjectEditAsync(int projectId);

    /// <summary>
    /// 取得專案管理頁左側使用的專案列表資料。
    /// </summary>
    Task<List<ProjectListItem>> GetProjectListAsync();

    /// <summary>
    /// 依登入者身分與參與紀錄，取得可切換與可見的專案清單。
    /// </summary>
    Task<List<ProjectSwitcherItem>> GetVisibleProjectsAsync(int userId);

    /// <summary>
    /// 取得單一專案的詳細資料，包含基本資訊、時程、題型目標與成員角色。
    /// </summary>
    Task<ProjectDetailDto?> GetProjectDetailAsync(int projectId);

    /// <summary>
    /// 取得建立或編輯專案時可選擇的人才池名單。
    /// </summary>
    Task<List<ProjectTalentPoolItem>> GetTalentPoolAsync();

    /// <summary>
    /// 取得專案指派人員可選用的外部角色清單。
    /// </summary>
    Task<List<RoleOption>> GetProjectRoleOptionsAsync();

    /// <summary>
    /// 將專案標記為軟刪除，從一般列表中隱藏並保留相關資料。
    /// </summary>
    Task SoftDeleteProjectAsync(int projectId, int deletedBy);

    /// <summary>
    /// 更新既有專案主檔與相關設定資料。
    /// </summary>
    Task UpdateProjectAsync(UpdateProjectRequest request);

    /// <summary>
    /// 取得指定專案的 7 個實作階段（PhaseCode &gt; 0），依排序回傳。
    /// </summary>
    Task<List<ProjectPhaseInfo>> GetPhasesAsync(int projectId);

    /// <summary>
    /// 取得指定專案目前進行中的階段（依今日落在 StartDate ~ EndDate 區間判定）。
    /// </summary>
    Task<ProjectPhaseInfo?> GetCurrentPhaseAsync(int projectId);
}

/// <summary>
/// 提供命題專案的查詢、建立、軟刪除與即時同步通知功能。
/// </summary>
public class ProjectService : IProjectService
{
    private readonly IDatabaseService _db;
    private readonly ILogger<ProjectService> _logger;
    private readonly IHubContext<ProjectsHub> _projectsHubContext;

    /// <summary>
    /// 初始化專案服務所需的資料庫、記錄器與即時同步依賴。
    /// </summary>
    public ProjectService(
        IDatabaseService db,
        ILogger<ProjectService> logger,
        IHubContext<ProjectsHub> projectsHubContext)
    {
        _db = db;
        _logger = logger;
        _projectsHubContext = projectsHubContext;
    }

    /// <summary>
    /// 取得專案管理頁顯示用的專案列表，排除已軟刪除資料。
    /// </summary>
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
                p.StartDate,
                p.EndDate,
                p.ClosedAt,
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

    /// <summary>
    /// 取得指定專案的可編輯完整資料，供專案編輯表單回填。
    /// </summary>
    public async Task<ProjectEditDto?> GetProjectEditAsync(int projectId)
    {
        using var conn = _db.CreateConnection();
        return await GetProjectEditAsync(conn, null, projectId);
    }

    /// <summary>
    /// 依使用者角色與參與關聯，取得上方專案切換器可見的專案清單。
    /// </summary>
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
                p.StartDate,
                p.ClosedAt
            FROM dbo.MT_Projects p
            INNER JOIN (
                SELECT DISTINCT ProjectId
                FROM VisibleProjectIds
            ) vp ON vp.ProjectId = p.Id
            WHERE p.IsDeleted = 0
            ORDER BY
                CASE WHEN p.ClosedAt IS NULL THEN 0 ELSE 1 END,
                p.Year DESC,
                p.Id DESC;
            """;

        var result = await conn.QueryAsync<ProjectSwitcherItem>(sql, new { UserId = userId });
        return result.ToList();
    }

    /// <summary>
    /// 取得單一專案的完整詳細資料，供專案管理頁右側詳情區顯示。
    /// </summary>
    public async Task<ProjectDetailDto?> GetProjectDetailAsync(int projectId)
    {
        using var conn = _db.CreateConnection();
        var detailParams = new { ProjectId = projectId };

        const string multipleSql = """
            -- 1. Project Detail
            SELECT
                p.Id, p.ProjectCode, p.Name, p.Year, p.School,
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

            -- 4. Members and Roles（帶 Category 供 UI 配色）
            SELECT
                pm.UserId,
                ISNULL(u.DisplayName, N'未知') AS DisplayName,
                t.TeacherCode,
                r.Name AS RoleName,
                r.Category AS RoleCategory
            FROM dbo.MT_ProjectMembers pm
            LEFT JOIN dbo.MT_Users u ON u.Id = pm.UserId
            LEFT JOIN dbo.MT_Teachers t ON t.UserId = u.Id
            LEFT JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
            LEFT JOIN dbo.MT_Roles r ON r.Id = pmr.RoleId
            WHERE pm.ProjectId = @ProjectId
            ORDER BY pm.Id, r.Id;
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
                    Roles = g.Where(x => !string.IsNullOrWhiteSpace(x.RoleName))
                        .Select(x => new RoleTag(x.RoleName!, x.RoleCategory ?? 0))
                        .GroupBy(r => r.Name)
                        .Select(grp => grp.First())
                        .ToList()
                }).ToList();
                
            return detail;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入專案詳情失敗 (Id={ProjectId})", projectId);
            throw;
        }
    }

    /// <summary>
    /// 取得目前可分派進專案的人才池教師清單。
    /// </summary>
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

    /// <summary>
    /// 取得專案指派人員可選用的外部角色清單。
    /// </summary>
    public async Task<List<RoleOption>> GetProjectRoleOptionsAsync()
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT Id, Name, Category, IsDefault
            FROM dbo.MT_Roles
            WHERE Category = 1
            ORDER BY IsDefault DESC, Id;
            """;

        var result = await conn.QueryAsync<RoleOption>(sql);
        return result.ToList();
    }

    /// <summary>
    /// 將指定專案標記為軟刪除，並記錄稽核資料與推播即時更新。
    /// </summary>
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
            INSERT INTO dbo.MT_AuditLogs (UserId, ProjectId, Action, TargetType, TargetId, NewValue)
            VALUES (@UserId, @ProjectId, @Action, @TargetType, @TargetId, N'刪除專案');
            """;

        await conn.ExecuteAsync(auditSql, new
        {
            UserId = deletedBy,
            ProjectId = projectId,
            Action = (byte)AuditAction.Delete,
            TargetType = (byte)AuditTargetType.Projects,
            TargetId = projectId
        });

        await BroadcastProjectChangedAsync(ProjectRealtimeChangeType.Deleted, projectId);
    }

    /// <summary>
    /// 建立專案主檔與其相關設定資料，完成後回傳新專案識別碼。
    /// </summary>
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
                INSERT INTO dbo.MT_Projects (ProjectCode, Name, Year, School, StartDate, EndDate, CreatedBy)
                OUTPUT INSERTED.Id
                VALUES (@ProjectCode, @Name, @Year, @School, @StartDate, @EndDate, @CreatedBy);
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

            await ReplaceProjectChildRecordsAsync(
                conn,
                trans,
                projectId,
                req.Phases,
                req.Targets,
                req.MemberAllocations,
                shouldClearExisting: false);

            var jsonValue = JsonSerializer.Serialize(new { ProjectId = projectId, Name = req.Name, ProjectCode = projectCode });

            const string auditSql = """
                INSERT INTO dbo.MT_AuditLogs (UserId, ProjectId, Action, TargetType, TargetId, NewValue)
                VALUES (@UserId, @ProjectId, @Action, @TargetType, @TargetId, @NewValue);
                """;

            await conn.ExecuteAsync(
                auditSql,
                new
                {
                    UserId = req.CreatedBy,
                    ProjectId = projectId,
                    Action = (byte)AuditAction.Create,
                    TargetType = (byte)AuditTargetType.Projects,
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

    /// <summary>
    /// 更新既有專案主檔與相關設定資料，完成後寫入稽核並推播即時同步。
    /// </summary>
    public async Task UpdateProjectAsync(UpdateProjectRequest req)
    {
        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        using var trans = await conn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var oldSnapshot = await GetProjectEditAsync(conn, trans, req.ProjectId);
            if (oldSnapshot is null)
            {
                throw new InvalidOperationException("找不到要編輯的專案，或該專案已被移除。");
            }

            var year = int.Parse(req.Year);
            var projectStartDate = req.Phases.FirstOrDefault(p => p.PhaseCode == 1)?.StartDate ?? DateTime.Today;
            var projectEndDate = req.Phases.OrderByDescending(p => p.PhaseCode).FirstOrDefault()?.EndDate ?? DateTime.Today.AddMonths(2);

            const string projectSql = """
                UPDATE dbo.MT_Projects
                SET Name = @Name,
                    Year = @Year,
                    School = @School,
                    StartDate = @StartDate,
                    EndDate = @EndDate,
                    UpdatedAt = SYSDATETIME()
                WHERE Id = @ProjectId
                  AND IsDeleted = 0;
                """;

            var affectedRows = await conn.ExecuteAsync(
                projectSql,
                new
                {
                    ProjectId = req.ProjectId,
                    Name = req.Name,
                    Year = year,
                    School = req.School,
                    StartDate = projectStartDate,
                    EndDate = projectEndDate
                },
                transaction: trans);

            if (affectedRows == 0)
            {
                throw new InvalidOperationException("找不到要更新的專案，或該專案已被移除。");
            }

            await ReplaceProjectChildRecordsAsync(
                conn,
                trans,
                req.ProjectId,
                req.Phases,
                req.Targets,
                req.MemberAllocations,
                shouldClearExisting: true);

            var newSnapshot = await GetProjectEditAsync(conn, trans, req.ProjectId)
                ?? throw new InvalidOperationException("專案更新後無法重新讀取資料。");

            const string auditSql = """
                INSERT INTO dbo.MT_AuditLogs (UserId, ProjectId, Action, TargetType, TargetId, OldValue, NewValue)
                VALUES (@UserId, @ProjectId, @Action, @TargetType, @TargetId, @OldValue, @NewValue);
                """;

            await conn.ExecuteAsync(
                auditSql,
                new
                {
                    UserId = req.UpdatedBy,
                    ProjectId = req.ProjectId,
                    Action = (byte)AuditAction.Update,
                    TargetType = (byte)AuditTargetType.Projects,
                    TargetId = req.ProjectId,
                    OldValue = JsonSerializer.Serialize(oldSnapshot),
                    NewValue = JsonSerializer.Serialize(newSnapshot)
                },
                transaction: trans);

            trans.Commit();

            await BroadcastProjectChangedAsync(ProjectRealtimeChangeType.Updated, req.ProjectId);
        }
        catch (Exception ex)
        {
            trans.Rollback();
            _logger.LogError(ex, "更新專案失敗 (Id={ProjectId})", req.ProjectId);
            throw;
        }
    }

    /// <summary>
    /// 依年度與目前最新流水號，產生下一個專案代碼。
    /// </summary>
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

    /// <summary>
    /// 透過 SignalR 廣播專案異動事件，通知前端同步更新畫面。
    /// </summary>
    private Task BroadcastProjectChangedAsync(ProjectRealtimeChangeType changeType, int projectId)
    {
        return _projectsHubContext.Clients.All.SendAsync(
            "ReceiveProjectChanged",
            new ProjectRealtimeSyncMessage(changeType, projectId));
    }

    private async Task<ProjectEditDto?> GetProjectEditAsync(IDbConnection conn, IDbTransaction? transaction, int projectId)
    {
        const string sql = """
            SELECT
                p.Id,
                p.ProjectCode,
                p.Year,
                p.Name,
                p.School,
                p.ClosedAt
            FROM dbo.MT_Projects p
            WHERE p.Id = @ProjectId
              AND p.IsDeleted = 0;

            SELECT
                PhaseCode,
                PhaseName AS Name,
                StartDate,
                EndDate
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId
            ORDER BY SortOrder;

            SELECT
                QuestionTypeId,
                TargetCount
            FROM dbo.MT_ProjectTargets
            WHERE ProjectId = @ProjectId
            ORDER BY QuestionTypeId;

            SELECT
                pm.Id AS ProjectMemberId,
                pm.UserId
            FROM dbo.MT_ProjectMembers pm
            WHERE pm.ProjectId = @ProjectId
            ORDER BY pm.Id;

            SELECT
                pmr.ProjectMemberId,
                pmr.RoleId
            FROM dbo.MT_ProjectMemberRoles pmr
            INNER JOIN dbo.MT_ProjectMembers pm ON pm.Id = pmr.ProjectMemberId
            WHERE pm.ProjectId = @ProjectId
            ORDER BY pmr.ProjectMemberId, pmr.RoleId;

            SELECT
                mq.ProjectMemberId,
                mq.QuestionTypeId,
                mq.QuotaCount
            FROM dbo.MT_MemberQuotas mq
            INNER JOIN dbo.MT_ProjectMembers pm ON pm.Id = mq.ProjectMemberId
            WHERE pm.ProjectId = @ProjectId
            ORDER BY mq.ProjectMemberId, mq.QuestionTypeId;
            """;

        using var multi = await conn.QueryMultipleAsync(sql, new { ProjectId = projectId }, transaction);

        var project = await multi.ReadFirstOrDefaultAsync<ProjectEditDto>();
        if (project is null)
        {
            return null;
        }

        project.Phases = (await multi.ReadAsync<ProjectPhaseDto>()).ToList();
        project.Targets = (await multi.ReadAsync<ProjectTargetDto>()).ToList();

        var memberRows = (await multi.ReadAsync<ProjectEditMemberRow>()).ToList();
        var roleRows = (await multi.ReadAsync<ProjectEditMemberRoleRow>()).ToList();
        var quotaRows = (await multi.ReadAsync<ProjectEditMemberQuotaRow>()).ToList();

        project.MemberAllocations = memberRows
            .Select(member => new ProjectMemberAllocationDto
            {
                UserId = member.UserId,
                RoleIds = roleRows
                    .Where(role => role.ProjectMemberId == member.ProjectMemberId)
                    .Select(role => role.RoleId)
                    .ToList(),
                Quotas = quotaRows
                    .Where(quota => quota.ProjectMemberId == member.ProjectMemberId)
                    .Select(quota => new ProjectMemberQuotaDto
                    {
                        QuestionTypeId = quota.QuestionTypeId,
                        QuotaCount = quota.QuotaCount
                    })
                    .ToList()
            })
            .ToList();

        return project;
    }

    private async Task ReplaceProjectChildRecordsAsync(
        IDbConnection conn,
        IDbTransaction transaction,
        int projectId,
        IEnumerable<ProjectPhaseDto> phases,
        IEnumerable<ProjectTargetDto> targets,
        IEnumerable<ProjectMemberAllocationDto> memberAllocations,
        bool shouldClearExisting)
    {
        if (shouldClearExisting)
        {
            const string deleteSql = """
                DELETE mq
                FROM dbo.MT_MemberQuotas mq
                INNER JOIN dbo.MT_ProjectMembers pm ON pm.Id = mq.ProjectMemberId
                WHERE pm.ProjectId = @ProjectId;

                DELETE pmr
                FROM dbo.MT_ProjectMemberRoles pmr
                INNER JOIN dbo.MT_ProjectMembers pm ON pm.Id = pmr.ProjectMemberId
                WHERE pm.ProjectId = @ProjectId;

                DELETE FROM dbo.MT_ProjectMembers
                WHERE ProjectId = @ProjectId;

                DELETE FROM dbo.MT_ProjectTargets
                WHERE ProjectId = @ProjectId;

                DELETE FROM dbo.MT_ProjectPhases
                WHERE ProjectId = @ProjectId;
                """;

            await conn.ExecuteAsync(deleteSql, new { ProjectId = projectId }, transaction: transaction);
        }

        const string phaseSql = """
            INSERT INTO dbo.MT_ProjectPhases (ProjectId, PhaseCode, PhaseName, StartDate, EndDate, SortOrder)
            VALUES (@ProjectId, @PhaseCode, @Name, @StartDate, @EndDate, @PhaseCode);
            """;

        foreach (var phase in phases)
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
                transaction: transaction);
        }

        const string targetSql = """
            INSERT INTO dbo.MT_ProjectTargets (ProjectId, QuestionTypeId, TargetCount)
            VALUES (@ProjectId, @QuestionTypeId, @TargetCount);
            """;

        foreach (var target in targets.Where(target => target.TargetCount > 0))
        {
            await conn.ExecuteAsync(
                targetSql,
                new
                {
                    ProjectId = projectId,
                    QuestionTypeId = target.QuestionTypeId,
                    TargetCount = target.TargetCount
                },
                transaction: transaction);
        }

        const string memberSql = """
            INSERT INTO dbo.MT_ProjectMembers (ProjectId, UserId)
            OUTPUT INSERTED.Id
            VALUES (@ProjectId, @UserId);
            """;

        const string roleSql = """
            INSERT INTO dbo.MT_ProjectMemberRoles (ProjectMemberId, RoleId)
            VALUES (@ProjectMemberId, @RoleId);
            """;

        const string quotaSql = """
            INSERT INTO dbo.MT_MemberQuotas (ProjectMemberId, QuestionTypeId, QuotaCount)
            VALUES (@ProjectMemberId, @QuestionTypeId, @QuotaCount);
            """;

        foreach (var alloc in memberAllocations.Where(allocation => allocation.UserId > 0))
        {
            var memberId = await conn.QuerySingleAsync<int>(
                memberSql,
                new
                {
                    ProjectId = projectId,
                    UserId = alloc.UserId
                },
                transaction: transaction);

            foreach (var roleId in alloc.RoleIds.Where(id => id > 0).Distinct())
            {
                await conn.ExecuteAsync(
                    roleSql,
                    new
                    {
                        ProjectMemberId = memberId,
                        RoleId = roleId
                    },
                    transaction: transaction);
            }

            foreach (var quota in alloc.Quotas.Where(quota => quota.QuotaCount > 0))
            {
                await conn.ExecuteAsync(
                    quotaSql,
                    new
                    {
                        ProjectMemberId = memberId,
                        QuestionTypeId = quota.QuestionTypeId,
                        QuotaCount = quota.QuotaCount
                    },
                    transaction: transaction);
            }
        }
    }

    /// <summary>
    /// 取得指定專案的 7 個實作階段（PhaseCode &gt; 0：命題 / 互審 / 互修 / 專審 / 專修 / 總審 / 總修）。
    /// 排除 PhaseCode = 0 的「產學計畫區間」框架。
    /// </summary>
    public async Task<List<ProjectPhaseInfo>> GetPhasesAsync(int projectId)
    {
        const string sql = """
            SELECT
                PhaseCode,
                PhaseName,
                StartDate,
                EndDate,
                DATEDIFF(DAY, CAST(GETDATE() AS DATE), EndDate) AS DaysLeft
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId
              AND PhaseCode > 0
            ORDER BY SortOrder;
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<ProjectPhaseInfo>(sql, new { ProjectId = projectId });
        return rows.AsList();
    }

    /// <summary>
    /// 取得指定專案目前進行中的階段；若今日未落在任何階段區間，回傳 null。
    /// </summary>
    public async Task<ProjectPhaseInfo?> GetCurrentPhaseAsync(int projectId)
    {
        const string sql = """
            SELECT TOP 1
                PhaseCode,
                PhaseName,
                StartDate,
                EndDate,
                DATEDIFF(DAY, CAST(GETDATE() AS DATE), EndDate) AS DaysLeft
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId
              AND PhaseCode > 0
              AND CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
            ORDER BY SortOrder;
            """;

        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<ProjectPhaseInfo>(sql, new { ProjectId = projectId });
    }

    private sealed class ProjectMemberRow
    {
        public int UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? TeacherCode { get; set; }
        public string? RoleName { get; set; }
        public int? RoleCategory { get; set; }
    }

    private sealed class ProjectEditMemberRow
    {
        public int ProjectMemberId { get; set; }
        public int UserId { get; set; }
    }

    private sealed class ProjectEditMemberRoleRow
    {
        public int ProjectMemberId { get; set; }
        public int RoleId { get; set; }
    }

    private sealed class ProjectEditMemberQuotaRow
    {
        public int ProjectMemberId { get; set; }
        public int QuestionTypeId { get; set; }
        public int QuotaCount { get; set; }
    }
}
