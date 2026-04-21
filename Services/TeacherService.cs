using System.Data;
using System.Text.Json;
using Dapper;
using MT.Models;

namespace MT.Services;

/// <summary>
/// 定義教師管理系統（人才庫）的服務契約。
/// </summary>
public interface ITeacherService
{
    // === 列表與統計 ===
    Task<List<TeacherListItem>> GetTeacherListAsync(int? projectId);
    Task<TeacherStatsDto> GetTeacherStatsAsync(int? projectId);

    // === 詳情 ===
    Task<TeacherDetailDto?> GetTeacherDetailAsync(int teacherId);

    // === 命題歷程 (Tab 2a) ===
    Task<TeacherComposeStats> GetTeacherComposeStatsAsync(int teacherUserId, int? filterProjectId);
    Task<List<TeacherComposeItem>> GetTeacherComposeHistoryAsync(int teacherUserId, int? filterProjectId);

    // === 審題歷程 (Tab 2b) ===
    Task<TeacherReviewStats> GetTeacherReviewStatsAsync(int teacherUserId, int? filterProjectId);
    Task<List<TeacherReviewItem>> GetTeacherReviewHistoryAsync(int teacherUserId, int? filterProjectId);

    // === 參與專案 (Tab 3) ===
    Task<List<TeacherProjectItem>> GetTeacherProjectsAsync(int teacherUserId);
    Task<List<ProjectDropdownItem>> GetAvailableProjectsAsync(int teacherUserId);
    Task<List<RoleOption>> GetExternalRoleOptionsAsync();
    Task AssignToProjectAsync(AssignProjectRequest req, int operatorId);
    Task RemoveFromProjectAsync(int teacherUserId, int projectId, int operatorId);

    // === CRUD ===
    Task<int> CreateTeacherAsync(CreateTeacherRequest req, int operatorId);
    Task UpdateTeacherAsync(UpdateTeacherRequest req, int operatorId);
    Task ToggleTeacherStatusAsync(int teacherId, int operatorId);
    Task ResetTeacherPasswordAsync(int teacherId, int operatorId);
}

/// <summary>
/// 教師管理系統（人才庫）的查詢、建立、編輯、帳號操作與梯次指派服務。
/// </summary>
public class TeacherService : ITeacherService
{
    private const string DefaultTeacherPassword = "CSF@01024304";

    // 題目狀態碼常數（對應 MT_Questions.Status）
    private const int StatusAdopted = 12;
    private const int StatusRejected = 13;

    private readonly IDatabaseService _db;
    private readonly ILogger<TeacherService> _logger;

    public TeacherService(IDatabaseService db, ILogger<TeacherService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ==================================================================
    // 列表與統計
    // ==================================================================

    /// <summary>
    /// 取得所有教師列表，附帶在指定梯次的角色標籤。
    /// </summary>
    public async Task<List<TeacherListItem>> GetTeacherListAsync(int? projectId)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                t.Id,
                t.UserId,
                t.TeacherCode,
                u.DisplayName,
                u.Email,
                t.School,
                u.Status
            FROM dbo.MT_Teachers t
            INNER JOIN dbo.MT_Users u ON u.Id = t.UserId
            ORDER BY u.Status DESC, t.CreatedAt DESC;
            """;

        var teachers = (await conn.QueryAsync<TeacherListItem>(sql)).ToList();

        if (projectId.HasValue && teachers.Count > 0)
        {
            // 批次查詢所有教師在此梯次的角色（帶 Category 供 UI 配色）
            const string roleSql = """
                SELECT
                    pm.UserId,
                    r.Name AS RoleName,
                    r.Category
                FROM dbo.MT_ProjectMembers pm
                INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
                INNER JOIN dbo.MT_Roles r ON r.Id = pmr.RoleId
                WHERE pm.ProjectId = @ProjectId;
                """;

            var roleRows = (await conn.QueryAsync(roleSql, new { ProjectId = projectId.Value })).ToList();

            var roleMap = roleRows
                .GroupBy(r => (int)r.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(r => new RoleTag((string)r.RoleName, (int)(byte)r.Category)).ToList());

            foreach (var teacher in teachers)
            {
                if (roleMap.TryGetValue(teacher.UserId, out var roles))
                    teacher.ProjectRoles = roles;
            }
        }

        return teachers;
    }

    /// <summary>
    /// 取得統計卡片數字：總數、啟用、停用、本梯次參與。
    /// </summary>
    public async Task<TeacherStatsDto> GetTeacherStatsAsync(int? projectId)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                COUNT(*) AS Total,
                SUM(CASE WHEN u.Status = 1 THEN 1 ELSE 0 END) AS Active,
                SUM(CASE WHEN u.Status <> 1 THEN 1 ELSE 0 END) AS Inactive
            FROM dbo.MT_Teachers t
            INNER JOIN dbo.MT_Users u ON u.Id = t.UserId;
            """;

        var stats = await conn.QuerySingleAsync<TeacherStatsDto>(sql);

        if (projectId.HasValue)
        {
            const string projectSql = """
                SELECT COUNT(DISTINCT pm.UserId)
                FROM dbo.MT_ProjectMembers pm
                INNER JOIN dbo.MT_Teachers t ON t.UserId = pm.UserId
                WHERE pm.ProjectId = @ProjectId;
                """;

            stats.CurrentProject = await conn.ExecuteScalarAsync<int>(projectSql, new { ProjectId = projectId.Value });
        }

        return stats;
    }

    // ==================================================================
    // 詳情
    // ==================================================================

    /// <summary>
    /// 取得單一教師完整資料。
    /// </summary>
    public async Task<TeacherDetailDto?> GetTeacherDetailAsync(int teacherId)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                t.Id,
                t.UserId,
                t.TeacherCode,
                u.DisplayName,
                u.Email,
                u.Status,
                t.Gender,
                t.Phone,
                t.IdNumber,
                t.School,
                t.Department,
                t.Title,
                t.Expertise,
                t.TeachingYears,
                t.Education,
                t.Note,
                u.IsFirstLogin,
                u.CreatedAt,
                u.LastLoginAt
            FROM dbo.MT_Teachers t
            INNER JOIN dbo.MT_Users u ON u.Id = t.UserId
            WHERE t.Id = @Id;
            """;

        return await conn.QuerySingleOrDefaultAsync<TeacherDetailDto>(sql, new { Id = teacherId });
    }

    // ==================================================================
    // 命題歷程 (Tab 2a)
    // ==================================================================

    /// <summary>
    /// 取得教師命題歷程的統計數字。
    /// </summary>
    public async Task<TeacherComposeStats> GetTeacherComposeStatsAsync(int teacherUserId, int? filterProjectId)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                COUNT(*) AS TotalCount,
                SUM(CASE WHEN q.Status = @Adopted THEN 1 ELSE 0 END) AS AdoptedCount,
                SUM(CASE WHEN q.Status = @Rejected THEN 1 ELSE 0 END) AS RejectedCount,
                SUM(CASE WHEN q.Status NOT IN (@Adopted, @Rejected) AND q.Status > 0 THEN 1 ELSE 0 END) AS ReviewingCount
            FROM dbo.MT_Questions q
            WHERE q.CreatorId = @UserId
              AND q.IsDeleted = 0
              AND (@ProjectId IS NULL OR q.ProjectId = @ProjectId);
            """;

        return await conn.QuerySingleAsync<TeacherComposeStats>(sql, new
        {
            UserId = teacherUserId,
            ProjectId = filterProjectId,
            Adopted = StatusAdopted,
            Rejected = StatusRejected
        });
    }

    /// <summary>
    /// 取得教師命題歷程列表，可依梯次篩選。
    /// </summary>
    public async Task<List<TeacherComposeItem>> GetTeacherComposeHistoryAsync(int teacherUserId, int? filterProjectId)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                q.Id AS QuestionId,
                q.QuestionCode,
                qt.Name AS TypeName,
                q.Level,
                q.ProjectId,
                p.Name AS ProjectName,
                q.Status,
                q.UpdatedAt
            FROM dbo.MT_Questions q
            INNER JOIN dbo.MT_QuestionTypes qt ON qt.Id = q.QuestionTypeId
            INNER JOIN dbo.MT_Projects p ON p.Id = q.ProjectId
            WHERE q.CreatorId = @UserId
              AND q.IsDeleted = 0
              AND (@ProjectId IS NULL OR q.ProjectId = @ProjectId)
            ORDER BY q.UpdatedAt DESC;
            """;

        var rows = await conn.QueryAsync<TeacherComposeItem>(sql, new
        {
            UserId = teacherUserId,
            ProjectId = filterProjectId
        });
        return rows.ToList();
    }

    // ==================================================================
    // 審題歷程 (Tab 2b)
    // ==================================================================

    /// <summary>
    /// 取得教師審題歷程的統計數字。
    /// </summary>
    public async Task<TeacherReviewStats> GetTeacherReviewStatsAsync(int teacherUserId, int? filterProjectId)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                COUNT(*) AS TotalCount,
                SUM(CASE WHEN ra.ReviewStatus = 2 THEN 1 ELSE 0 END) AS CompletedCount,
                SUM(CASE WHEN ra.ReviewStatus IN (0, 1) THEN 1 ELSE 0 END) AS PendingCount
            FROM dbo.MT_ReviewAssignments ra
            WHERE ra.ReviewerId = @UserId
              AND (@ProjectId IS NULL OR ra.ProjectId = @ProjectId);
            """;

        return await conn.QuerySingleAsync<TeacherReviewStats>(sql, new
        {
            UserId = teacherUserId,
            ProjectId = filterProjectId
        });
    }

    /// <summary>
    /// 取得教師審題歷程列表，可依梯次篩選。
    /// </summary>
    public async Task<List<TeacherReviewItem>> GetTeacherReviewHistoryAsync(int teacherUserId, int? filterProjectId)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                ra.Id AS ReviewAssignmentId,
                q.QuestionCode,
                qt.Name AS TypeName,
                q.Level,
                ra.ProjectId,
                p.Name AS ProjectName,
                ra.ReviewStage,
                ra.Decision,
                ra.DecidedAt,
                ra.CreatedAt
            FROM dbo.MT_ReviewAssignments ra
            INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
            INNER JOIN dbo.MT_QuestionTypes qt ON qt.Id = q.QuestionTypeId
            INNER JOIN dbo.MT_Projects p ON p.Id = ra.ProjectId
            WHERE ra.ReviewerId = @UserId
              AND (@ProjectId IS NULL OR ra.ProjectId = @ProjectId)
            ORDER BY ra.CreatedAt DESC;
            """;

        var rows = await conn.QueryAsync<TeacherReviewItem>(sql, new
        {
            UserId = teacherUserId,
            ProjectId = filterProjectId
        });
        return rows.ToList();
    }

    // ==================================================================
    // 參與專案 (Tab 3)
    // ==================================================================

    /// <summary>
    /// 取得教師參與的所有梯次列表，含角色與命題統計。
    /// </summary>
    public async Task<List<TeacherProjectItem>> GetTeacherProjectsAsync(int teacherUserId)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                p.Id AS ProjectId,
                p.ProjectCode,
                p.Name AS ProjectName,
                p.Year AS ProjectYear,
                ISNULL(pp.StartDate, SYSDATETIME()) AS StartDate,
                p.ClosedAt
            FROM dbo.MT_ProjectMembers pm
            INNER JOIN dbo.MT_Projects p ON p.Id = pm.ProjectId
            LEFT JOIN (
                SELECT ProjectId, MIN(StartDate) AS StartDate
                FROM dbo.MT_ProjectPhases
                GROUP BY ProjectId
            ) pp ON pp.ProjectId = p.Id
            WHERE pm.UserId = @UserId
              AND (p.IsDeleted = 0 OR p.IsDeleted IS NULL)
            ORDER BY p.Year DESC, p.Id DESC;
            """;

        var projects = (await conn.QueryAsync<TeacherProjectItem>(sql, new { UserId = teacherUserId })).ToList();

        if (projects.Count == 0) return projects;

        // 批次查角色
        const string roleSql = """
            SELECT
                pm.ProjectId,
                r.Name AS RoleName,
                r.Category
            FROM dbo.MT_ProjectMembers pm
            INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
            INNER JOIN dbo.MT_Roles r ON r.Id = pmr.RoleId
            WHERE pm.UserId = @UserId;
            """;

        var roleRows = (await conn.QueryAsync(roleSql, new { UserId = teacherUserId })).ToList();
        var roleMap = roleRows
            .GroupBy(r => (int)r.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new RoleTag((string)r.RoleName, (int)(byte)r.Category)).ToList());

        // 批次查命題數 + 採用數
        const string questionSql = """
            SELECT
                q.ProjectId,
                COUNT(*) AS QuestionCount,
                SUM(CASE WHEN q.Status = @Adopted THEN 1 ELSE 0 END) AS AdoptedCount
            FROM dbo.MT_Questions q
            WHERE q.CreatorId = @UserId
              AND q.IsDeleted = 0
            GROUP BY q.ProjectId;
            """;

        var questionRows = (await conn.QueryAsync(questionSql, new { UserId = teacherUserId, Adopted = StatusAdopted })).ToList();
        var questionMap = questionRows.ToDictionary(r => (int)r.ProjectId);

        foreach (var project in projects)
        {
            if (roleMap.TryGetValue(project.ProjectId, out var roles))
                project.Roles = roles;

            if (questionMap.TryGetValue(project.ProjectId, out var qs))
            {
                project.QuestionCount = (int)qs.QuestionCount;
                project.AdoptedCount = (int)qs.AdoptedCount;
            }
        }

        return projects;
    }

    /// <summary>
    /// 取得教師尚未參與且非已結案的梯次，供「加入梯次」Modal 用。
    /// </summary>
    public async Task<List<ProjectDropdownItem>> GetAvailableProjectsAsync(int teacherUserId)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT p.Id, p.Name
            FROM dbo.MT_Projects p
            WHERE p.ClosedAt IS NULL
              AND (p.IsDeleted = 0 OR p.IsDeleted IS NULL)
              AND p.Id NOT IN (
                  SELECT pm.ProjectId FROM dbo.MT_ProjectMembers pm WHERE pm.UserId = @UserId
              )
            ORDER BY p.Id DESC;
            """;

        var rows = await conn.QueryAsync<ProjectDropdownItem>(sql, new { UserId = teacherUserId });
        return rows.ToList();
    }

    /// <summary>
    /// 取得 Category=1（外部人員）的角色選項。
    /// </summary>
    public async Task<List<RoleOption>> GetExternalRoleOptionsAsync()
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT Id, Name, Category, IsDefault
            FROM dbo.MT_Roles
            WHERE Category = 1
            ORDER BY IsDefault DESC, Id;
            """;

        var rows = await conn.QueryAsync<RoleOption>(sql);
        return rows.ToList();
    }

    /// <summary>
    /// 將教師加入指定梯次並設定角色。
    /// </summary>
    public async Task AssignToProjectAsync(AssignProjectRequest req, int operatorId)
    {
        if (req.RoleIds.Count == 0) throw new ArgumentException("請至少選擇一個角色。");

        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();
        using var trans = await conn.BeginTransactionAsync();

        try
        {
            // 檢查是否已在此梯次
            const string checkSql = """
                SELECT Id FROM dbo.MT_ProjectMembers
                WHERE ProjectId = @ProjectId AND UserId = @UserId;
                """;
            var existingId = await conn.QuerySingleOrDefaultAsync<int?>(checkSql,
                new { req.ProjectId, UserId = req.TeacherUserId }, transaction: trans);

            if (existingId.HasValue)
                throw new InvalidOperationException("此教師已在該梯次中。");

            // 新增 ProjectMembers
            const string insertMemberSql = """
                INSERT INTO dbo.MT_ProjectMembers (ProjectId, UserId)
                OUTPUT INSERTED.Id
                VALUES (@ProjectId, @UserId);
                """;
            var memberId = await conn.ExecuteScalarAsync<int>(insertMemberSql,
                new { req.ProjectId, UserId = req.TeacherUserId }, transaction: trans);

            // 新增各角色
            const string insertRoleSql = """
                INSERT INTO dbo.MT_ProjectMemberRoles (ProjectMemberId, RoleId)
                VALUES (@ProjectMemberId, @RoleId);
                """;
            foreach (var roleId in req.RoleIds)
            {
                await conn.ExecuteAsync(insertRoleSql,
                    new { ProjectMemberId = memberId, RoleId = roleId }, transaction: trans);
            }

            await WriteAuditAsync(conn, operatorId, AuditAction.Update, AuditTargetType.Teachers,
                req.TeacherUserId,
                JsonSerializer.Serialize(new { Action = "AssignProject", req.ProjectId, req.RoleIds }),
                transaction: trans);

            await trans.CommitAsync();
        }
        catch
        {
            await trans.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 從梯次移除教師（僅限非結案梯次）。
    /// </summary>
    public async Task RemoveFromProjectAsync(int teacherUserId, int projectId, int operatorId)
    {
        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        // 檢查梯次是否已結案
        const string checkSql = "SELECT ClosedAt FROM dbo.MT_Projects WHERE Id = @Id;";
        var closedAt = await conn.QuerySingleOrDefaultAsync<DateTime?>(checkSql, new { Id = projectId });
        if (closedAt.HasValue)
            throw new InvalidOperationException("已結案的梯次不可移除成員。");

        using var trans = await conn.BeginTransactionAsync();
        try
        {
            // 查 ProjectMemberId
            const string memberSql = """
                SELECT Id FROM dbo.MT_ProjectMembers
                WHERE ProjectId = @ProjectId AND UserId = @UserId;
                """;
            var memberId = await conn.QuerySingleOrDefaultAsync<int?>(memberSql,
                new { ProjectId = projectId, UserId = teacherUserId }, transaction: trans);

            if (!memberId.HasValue)
                throw new InvalidOperationException("此教師不在該梯次中。");

            // CASCADE 刪除：配額 → 角色 → 成員
            await conn.ExecuteAsync(
                "DELETE FROM dbo.MT_MemberQuotas WHERE ProjectMemberId = @Id;",
                new { Id = memberId.Value }, transaction: trans);

            await conn.ExecuteAsync(
                "DELETE FROM dbo.MT_ProjectMemberRoles WHERE ProjectMemberId = @Id;",
                new { Id = memberId.Value }, transaction: trans);

            await conn.ExecuteAsync(
                "DELETE FROM dbo.MT_ProjectMembers WHERE Id = @Id;",
                new { Id = memberId.Value }, transaction: trans);

            await WriteAuditAsync(conn, operatorId, AuditAction.Delete, AuditTargetType.Teachers,
                teacherUserId,
                JsonSerializer.Serialize(new { Action = "RemoveFromProject", ProjectId = projectId }),
                transaction: trans);

            await trans.CommitAsync();
        }
        catch
        {
            await trans.RollbackAsync();
            throw;
        }
    }

    // ==================================================================
    // CRUD
    // ==================================================================

    /// <summary>
    /// 新增教師：Transaction 內建立 MT_Users + MT_Teachers。
    /// </summary>
    public async Task<int> CreateTeacherAsync(CreateTeacherRequest req, int operatorId)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName)) throw new ArgumentException("教師姓名必填。");
        if (string.IsNullOrWhiteSpace(req.Email)) throw new ArgumentException("電子信箱必填。");
        if (string.IsNullOrWhiteSpace(req.School)) throw new ArgumentException("任教學校必填。");

        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        // 檢查 Email 是否已被使用（Email = Username）
        const string checkSql = """
            SELECT COUNT(*) FROM dbo.MT_Users WHERE LOWER(Username) = LOWER(@Email);
            """;
        var exists = await conn.ExecuteScalarAsync<int>(checkSql, new { req.Email });
        if (exists > 0) throw new InvalidOperationException($"信箱「{req.Email}」已被使用。");

        using var trans = await conn.BeginTransactionAsync();
        try
        {
            // 取得「新創教師」角色（所有新建教師在未分配梯次前統一使用此身份）
            var defaultRoleId = await conn.QuerySingleOrDefaultAsync<int?>(
                "SELECT TOP 1 Id FROM dbo.MT_Roles WHERE Name = N'新創教師';",
                transaction: trans);
            if (!defaultRoleId.HasValue)
                throw new InvalidOperationException("系統尚未建立「新創教師」角色，請先至角色管理建立。");

            // 建立 MT_Users
            var passwordHash = AuthService.ComputePasswordHash(DefaultTeacherPassword);
            const string insertUserSql = """
                INSERT INTO dbo.MT_Users
                    (Username, DisplayName, Email, PasswordHash, RoleId, Status, IsFirstLogin)
                OUTPUT INSERTED.Id
                VALUES (@Username, @DisplayName, @Email, @PasswordHash, @RoleId, @Status, 1);
                """;
            var userId = await conn.ExecuteScalarAsync<int>(insertUserSql, new
            {
                Username = req.Email,
                req.DisplayName,
                req.Email,
                PasswordHash = passwordHash,
                RoleId = defaultRoleId.Value,
                Status = req.Status == 0 ? 0 : 1,
            }, transaction: trans);

            // 生成 TeacherCode：T + 民國年 + 3碼流水號（如 T115001）
            var rocYear = DateTime.Now.Year - 1911;
            var prefix = $"T{rocYear}";
            const string codeSql = """
                SELECT ISNULL(MAX(CAST(RIGHT(TeacherCode, 3) AS INT)), 0)
                FROM dbo.MT_Teachers
                WHERE TeacherCode LIKE @Prefix + '%' AND LEN(TeacherCode) = @ExpectedLen;
                """;
            var maxSeq = await conn.ExecuteScalarAsync<int>(codeSql,
                new { Prefix = prefix, ExpectedLen = prefix.Length + 3 }, transaction: trans);
            var teacherCode = $"{prefix}{(maxSeq + 1):D3}";

            // 建立 MT_Teachers
            const string insertTeacherSql = """
                INSERT INTO dbo.MT_Teachers
                    (UserId, TeacherCode, Gender, Phone, IdNumber, School, Department, Title, Expertise, TeachingYears, Education, Note)
                OUTPUT INSERTED.Id
                VALUES (@UserId, @TeacherCode, @Gender, @Phone, @IdNumber, @School, @Department, @Title, @Expertise, @TeachingYears, @Education, @Note);
                """;
            var teacherId = await conn.ExecuteScalarAsync<int>(insertTeacherSql, new
            {
                UserId = userId,
                TeacherCode = teacherCode,
                req.Gender,
                req.Phone,
                req.IdNumber,
                req.School,
                req.Department,
                req.Title,
                req.Expertise,
                req.TeachingYears,
                req.Education,
                req.Note,
            }, transaction: trans);

            await WriteAuditAsync(conn, operatorId, AuditAction.Create, AuditTargetType.Teachers, teacherId,
                JsonSerializer.Serialize(new { req.DisplayName, req.Email, TeacherCode = teacherCode }),
                transaction: trans);

            await trans.CommitAsync();
            return teacherId;
        }
        catch
        {
            await trans.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 編輯教師資料（不更動帳號與密碼）。
    /// </summary>
    public async Task UpdateTeacherAsync(UpdateTeacherRequest req, int operatorId)
    {
        if (req.TeacherId <= 0) throw new ArgumentException("TeacherId 必填。");
        if (string.IsNullOrWhiteSpace(req.DisplayName)) throw new ArgumentException("教師姓名必填。");
        if (string.IsNullOrWhiteSpace(req.School)) throw new ArgumentException("任教學校必填。");

        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        // 查 UserId
        const string userIdSql = "SELECT UserId FROM dbo.MT_Teachers WHERE Id = @Id;";
        var userId = await conn.QuerySingleOrDefaultAsync<int?>(userIdSql, new { Id = req.TeacherId });
        if (!userId.HasValue) throw new InvalidOperationException("找不到教師資料。");

        using var trans = await conn.BeginTransactionAsync();
        try
        {
            // 更新 MT_Users（DisplayName + Status）
            const string updateUserSql = """
                UPDATE dbo.MT_Users
                SET DisplayName = @DisplayName,
                    Status      = @Status,
                    UpdatedAt   = SYSDATETIME()
                WHERE Id = @Id;
                """;
            await conn.ExecuteAsync(updateUserSql, new
            {
                Id = userId.Value,
                req.DisplayName,
                Status = req.Status == 0 ? 0 : 1,
            }, transaction: trans);

            // 更新 MT_Teachers
            const string updateTeacherSql = """
                UPDATE dbo.MT_Teachers
                SET Gender        = @Gender,
                    Phone         = @Phone,
                    IdNumber      = @IdNumber,
                    School        = @School,
                    Department    = @Department,
                    Title         = @Title,
                    Expertise     = @Expertise,
                    TeachingYears = @TeachingYears,
                    Education     = @Education,
                    Note          = @Note,
                    UpdatedAt     = SYSDATETIME()
                WHERE Id = @Id;
                """;
            await conn.ExecuteAsync(updateTeacherSql, new
            {
                Id = req.TeacherId,
                req.Gender,
                req.Phone,
                req.IdNumber,
                req.School,
                req.Department,
                req.Title,
                req.Expertise,
                req.TeachingYears,
                req.Education,
                req.Note,
            }, transaction: trans);

            await WriteAuditAsync(conn, operatorId, AuditAction.Update, AuditTargetType.Teachers,
                req.TeacherId,
                JsonSerializer.Serialize(new { req.DisplayName, req.School }),
                transaction: trans);

            await trans.CommitAsync();
        }
        catch
        {
            await trans.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 切換教師帳號啟用/停用狀態。
    /// </summary>
    public async Task ToggleTeacherStatusAsync(int teacherId, int operatorId)
    {
        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        const string sql = """
            SELECT u.Id, u.Status
            FROM dbo.MT_Teachers t
            INNER JOIN dbo.MT_Users u ON u.Id = t.UserId
            WHERE t.Id = @Id;
            """;
        var row = await conn.QuerySingleOrDefaultAsync(sql, new { Id = teacherId });
        if (row is null) throw new InvalidOperationException("找不到教師資料。");

        int userId = (int)row.Id;
        int next = (int)row.Status == 1 ? 0 : 1;

        const string updateSql = """
            UPDATE dbo.MT_Users
            SET Status = @Status, UpdatedAt = SYSDATETIME()
            WHERE Id = @Id;
            """;
        await conn.ExecuteAsync(updateSql, new { Id = userId, Status = next });

        await WriteAuditAsync(conn, operatorId, AuditAction.Update, AuditTargetType.Teachers, teacherId,
            JsonSerializer.Serialize(new { StatusChangedTo = next }));
    }

    /// <summary>
    /// 重設教師密碼為 Cwt2026!，標記首次登入。
    /// </summary>
    public async Task ResetTeacherPasswordAsync(int teacherId, int operatorId)
    {
        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        const string userIdSql = "SELECT UserId FROM dbo.MT_Teachers WHERE Id = @Id;";
        var userId = await conn.QuerySingleOrDefaultAsync<int?>(userIdSql, new { Id = teacherId });
        if (!userId.HasValue) throw new InvalidOperationException("找不到教師資料。");

        var passwordHash = AuthService.ComputePasswordHash(DefaultTeacherPassword);

        const string sql = """
            UPDATE dbo.MT_Users
            SET PasswordHash = @PasswordHash,
                IsFirstLogin = 1,
                LockoutUntil = NULL,
                UpdatedAt    = SYSDATETIME()
            WHERE Id = @Id;
            """;
        await conn.ExecuteAsync(sql, new { Id = userId.Value, PasswordHash = passwordHash });

        await WriteAuditAsync(conn, operatorId, AuditAction.Update, AuditTargetType.Teachers, teacherId,
            "{\"PasswordReset\":true}");
    }

    // ==================================================================
    // 私有輔助方法
    // ==================================================================

    private static async Task WriteAuditAsync(
        IDbConnection conn,
        int operatorId,
        AuditAction action,
        AuditTargetType targetType,
        int targetId,
        string? newValue,
        IDbTransaction? transaction = null)
    {
        const string sql = """
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue)
            VALUES (@UserId, @Action, @TargetType, @TargetId, @NewValue);
            """;

        await conn.ExecuteAsync(sql, new
        {
            UserId = operatorId,
            Action = (byte)action,
            TargetType = (byte)targetType,
            TargetId = targetId,
            NewValue = newValue,
        }, transaction: transaction);
    }
}
