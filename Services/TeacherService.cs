using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
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
    Task<TeacherComposeHistoryResult> GetTeacherComposeHistoryAsync(int teacherUserId, int? filterProjectId, int page = 1, int pageSize = 10);

    // === 審題歷程 (Tab 2b) ===
    Task<TeacherReviewStats> GetTeacherReviewStatsAsync(int teacherUserId, int? filterProjectId);
    Task<TeacherReviewHistoryResult> GetTeacherReviewHistoryAsync(int teacherUserId, int? filterProjectId, int page = 1, int pageSize = 10);

    // === 參與專案 (Tab 3) ===
    Task<List<TeacherProjectItem>> GetTeacherProjectsAsync(int teacherUserId);
    Task<List<ProjectDropdownItem>> GetAvailableProjectsAsync(int teacherUserId);
    Task<List<RoleOption>> GetExternalRoleOptionsAsync();
    Task AssignToProjectAsync(AssignProjectRequest req, int operatorId);
    Task RemoveFromProjectAsync(int teacherUserId, int projectId, int operatorId);

    // === CRUD ===
    Task<CreateTeacherResult> CreateTeacherAsync(CreateTeacherRequest req, int operatorId);
    Task UpdateTeacherAsync(UpdateTeacherRequest req, int operatorId);
    Task ToggleTeacherStatusAsync(int teacherId, int operatorId);
    Task ResetTeacherPasswordAsync(int teacherId, int operatorId);

    // === 批次匯入 ===
    Task<(List<BatchImportRow> Rows, string? ParseError)> ParseExcelAsync(Stream fileStream);
    Task<List<BatchImportRowResult>> ImportTeachersAsync(IReadOnlyList<BatchImportRow> rows, int operatorId);

    // === 匯出名單 ===
    Task<TeacherExportResult> ExportProjectTeachersAsync(int projectId, string projectName);
}

/// <summary>
/// 教師管理系統（人才庫）的查詢、建立、編輯、帳號操作與梯次指派服務。
/// </summary>
public class TeacherService : ITeacherService
{
    private const string DefaultTeacherPassword = "CSF@01024304";

    // 與 QuestionStatus.Adopted=9 / Rejected=10 命名區分，避免混淆
    private const int StatusClosedAdopted      = 12;   // 結案入庫
    private const int StatusClosedNotAdopted   = 11;   // 結案未採用

    private readonly IDatabaseService _db;
    private readonly ILogger<TeacherService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IQuestionTypeCatalog _typeCatalog;
    private readonly IAppointmentService _appointmentSvc;

    public TeacherService(
        IDatabaseService db,
        ILogger<TeacherService> logger,
        IHttpContextAccessor httpContextAccessor,
        IQuestionTypeCatalog typeCatalog,
        IAppointmentService appointmentSvc)
    {
        _db = db;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _typeCatalog = typeCatalog;
        _appointmentSvc = appointmentSvc;
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
                SUM(CASE WHEN q.Status IN (9, 12) THEN 1 ELSE 0 END) AS AdoptedCount,
                SUM(CASE WHEN q.Status IN (10, 11) THEN 1 ELSE 0 END) AS RejectedCount,
                SUM(CASE WHEN q.Status BETWEEN 3 AND 8 THEN 1 ELSE 0 END) AS ReviewingCount
            FROM dbo.MT_Questions q
            WHERE q.CreatorId = @UserId
              AND q.IsDeleted = 0
              AND (@ProjectId IS NULL OR q.ProjectId = @ProjectId);
            """;

        return await conn.QuerySingleAsync<TeacherComposeStats>(sql, new
        {
            UserId = teacherUserId,
            ProjectId = filterProjectId
        });
    }

    /// <summary>
    /// 取得教師命題歷程分頁列表，可依梯次篩選。
    /// 一次連線跑兩段 SQL（COUNT + OFFSET FETCH），由分頁器導頁。
    /// </summary>
    public async Task<TeacherComposeHistoryResult> GetTeacherComposeHistoryAsync(
        int teacherUserId, int? filterProjectId, int page = 1, int pageSize = 10)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;

        using var conn = _db.CreateConnection();

        const string countSql = """
            SELECT COUNT(*)
            FROM dbo.MT_Questions q
            WHERE q.CreatorId = @UserId
              AND q.IsDeleted = 0
              AND (@ProjectId IS NULL OR q.ProjectId = @ProjectId);
            """;

        const string listSql = """
            SELECT
                q.Id AS QuestionId,
                q.QuestionCode,
                q.QuestionTypeId,
                q.Level,
                q.ProjectId,
                p.Name AS ProjectName,
                p.ProjectType,
                q.Status,
                q.UpdatedAt
            FROM dbo.MT_Questions q
            INNER JOIN dbo.MT_Projects p ON p.Id = q.ProjectId
            WHERE q.CreatorId = @UserId
              AND q.IsDeleted = 0
              AND (@ProjectId IS NULL OR q.ProjectId = @ProjectId)
            ORDER BY q.UpdatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        var args = new
        {
            UserId = teacherUserId,
            ProjectId = filterProjectId,
            Offset = (page - 1) * pageSize,
            PageSize = pageSize
        };

        var total = await conn.ExecuteScalarAsync<int>(countSql, args);
        var rows = (await conn.QueryAsync<TeacherComposeItem>(listSql, args)).ToList();

        foreach (var row in rows)
        {
            row.TypeName = _typeCatalog.GetName(row.QuestionTypeId);
        }

        return new TeacherComposeHistoryResult
        {
            Items = rows,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    // ==================================================================
    // 審題歷程 (Tab 2b)
    // ==================================================================

    /// <summary>
    /// 取得教師審題歷程的統計數字。
    /// 只計算母題層級 assignment（SubQuestionId IS NULL），避免題組類題目的子題 assignment 造成重複計數。
    /// 題組類題目（短文/閱讀/聽力題組）一定有母題 assignment，用它作為「一道題」的代表單元。
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
              AND ra.SubQuestionId IS NULL
              AND (@ProjectId IS NULL OR ra.ProjectId = @ProjectId);
            """;

        return await conn.QuerySingleAsync<TeacherReviewStats>(sql, new
        {
            UserId = teacherUserId,
            ProjectId = filterProjectId
        });
    }

    /// <summary>
    /// 取得教師審題歷程分頁列表，可依梯次篩選。
    /// 一次連線跑兩段 SQL（COUNT + OFFSET FETCH），由分頁器導頁。
    /// 只列出母題層級 assignment（SubQuestionId IS NULL），避免題組類子題 assignment 讓同一道題重複出現。
    /// </summary>
    public async Task<TeacherReviewHistoryResult> GetTeacherReviewHistoryAsync(
        int teacherUserId, int? filterProjectId, int page = 1, int pageSize = 10)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;

        using var conn = _db.CreateConnection();

        const string countSql = """
            SELECT COUNT(*)
            FROM dbo.MT_ReviewAssignments ra
            WHERE ra.ReviewerId = @UserId
              AND ra.SubQuestionId IS NULL
              AND (@ProjectId IS NULL OR ra.ProjectId = @ProjectId);
            """;

        const string listSql = """
            SELECT
                ra.Id AS ReviewAssignmentId,
                q.QuestionCode,
                q.QuestionTypeId,
                q.Level,
                ra.ProjectId,
                p.Name AS ProjectName,
                p.ProjectType,
                ra.ReviewStage,
                ra.Decision,
                ra.DecidedAt,
                ra.CreatedAt,
                p.ClosedAt AS ProjectClosedAt,
                q.Status   AS FinalQuestionStatus
            FROM dbo.MT_ReviewAssignments ra
            INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
            INNER JOIN dbo.MT_Projects p ON p.Id = ra.ProjectId
            WHERE ra.ReviewerId = @UserId
              AND ra.SubQuestionId IS NULL
              AND (@ProjectId IS NULL OR ra.ProjectId = @ProjectId)
            ORDER BY ra.CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        var args = new
        {
            UserId = teacherUserId,
            ProjectId = filterProjectId,
            Offset = (page - 1) * pageSize,
            PageSize = pageSize
        };

        var total = await conn.ExecuteScalarAsync<int>(countSql, args);
        var rows = (await conn.QueryAsync<TeacherReviewItem>(listSql, args)).ToList();

        foreach (var row in rows)
        {
            row.TypeName = _typeCatalog.GetName(row.QuestionTypeId);
        }

        return new TeacherReviewHistoryResult
        {
            Items = rows,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
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
                p.EndDate,
                p.ClosedAt,
                p.ProjectType
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
                SUM(CASE WHEN q.Status IN (9, 12) THEN 1 ELSE 0 END) AS AdoptedCount
            FROM dbo.MT_Questions q
            WHERE q.CreatorId = @UserId
              AND q.IsDeleted = 0
            GROUP BY q.ProjectId;
            """;

        var questionRows = (await conn.QueryAsync(questionSql, new { UserId = teacherUserId })).ToList();
        var questionMap = questionRows.ToDictionary(r => (int)r.ProjectId);

        // 批次查「有可下載聘書」的 ProjectId 集合（給下載按鈕條件渲染用，避免 404）
        var downloadableProjectIds = await _appointmentSvc.GetDownloadableProjectIdsForUserAsync(teacherUserId);

        foreach (var project in projects)
        {
            if (roleMap.TryGetValue(project.ProjectId, out var roles))
                project.Roles = roles;

            if (questionMap.TryGetValue(project.ProjectId, out var qs))
            {
                project.QuestionCount = (int)qs.QuestionCount;
                project.AdoptedCount = (int)qs.AdoptedCount;
            }

            project.HasDownloadableCerts = downloadableProjectIds.Contains(project.ProjectId);
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

            // 同步聘書 metadata（每個新身份 INSERT 占位；FileName 由 client 繪製後上傳補上）
            await _appointmentSvc.SyncCertificatesAsync(req.ProjectId, conn, trans);

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

            // 同步聘書 metadata（移除身份 → 撤銷對應聘書 IsRevoked=1，保留檔案以便日後恢復）
            await _appointmentSvc.SyncCertificatesAsync(projectId, conn, trans);

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
    /// 新增教師：
    ///  1. 若 Email 已對應到既有 MT_Users → 自動沿用該帳號（不新建 MT_Users），僅插入 MT_Teachers。
    ///  2. 否則 → Transaction 內同時建立 MT_Users + MT_Teachers。
    /// 沿用模式下不更動既有 MT_Users 的 DisplayName / Status / 密碼等欄位。
    /// </summary>
    public async Task<CreateTeacherResult> CreateTeacherAsync(CreateTeacherRequest req, int operatorId)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName)) throw new ArgumentException("教師姓名必填。");
        if (string.IsNullOrWhiteSpace(req.Email)) throw new ArgumentException("電子信箱必填。");
        if (string.IsNullOrWhiteSpace(req.School)) throw new ArgumentException("任教學校必填。");

        // 服務層正規化：避免使用者複製貼上帶到前後空白導致 Email 比對 miss、UNIQUE 衝突誤判
        req.DisplayName = req.DisplayName.Trim();
        req.Email       = req.Email.Trim();
        req.School      = req.School.Trim();
        req.Phone       = req.Phone?.Trim();
        req.IdNumber    = req.IdNumber?.Trim();
        req.Department  = req.Department?.Trim();
        req.Title       = req.Title?.Trim();
        req.Expertise   = req.Expertise?.Trim();
        req.Note        = req.Note?.Trim();

        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        // 1. 查 Email 是否已存在於 MT_Users（同時比對 Username 與 Email，因兩者皆有 UNIQUE 過濾索引）
        // SQL Server 預設 collation 為 CI（case-insensitive），無需 LOWER() 包欄位，索引才能命中
        const string lookupSql = """
            SELECT TOP 1 Id, Username, DisplayName
            FROM dbo.MT_Users
            WHERE Username = @Email OR Email = @Email;
            """;
        var existing = await conn.QueryFirstOrDefaultAsync<(int Id, string Username, string DisplayName)?>(
            lookupSql, new { req.Email });

        // 2. 沿用模式：檢查既有使用者是否已在教師人才庫中
        if (existing.HasValue)
        {
            var alreadyTeacher = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.MT_Teachers WHERE UserId = @UserId;",
                new { UserId = existing.Value.Id });
            if (alreadyTeacher > 0)
                throw new InvalidOperationException("此信箱對應的使用者已存在於教師人才庫，請改用編輯。");
        }

        using var trans = await conn.BeginTransactionAsync();
        try
        {
            int userId;
            bool reused;

            if (existing.HasValue)
            {
                // 沿用既有帳號，不動 MT_Users
                userId = existing.Value.Id;
                reused = true;
            }
            else
            {
                // 取得「新創教師」角色（所有新建教師在未分配梯次前統一使用此身份）
                var defaultRoleId = await conn.QuerySingleOrDefaultAsync<int?>(
                    "SELECT TOP 1 Id FROM dbo.MT_Roles WHERE Name = N'預設教師';",
                    transaction: trans);
                if (!defaultRoleId.HasValue)
                    throw new InvalidOperationException("系統尚未建立「預設教師」角色，請先至角色管理建立。");

                // 建立 MT_Users
                var passwordHash = AuthService.HashPassword(DefaultTeacherPassword);
                const string insertUserSql = """
                    INSERT INTO dbo.MT_Users
                        (Username, DisplayName, Email, PasswordHash, RoleId, Status, IsFirstLogin)
                    OUTPUT INSERTED.Id
                    VALUES (@Username, @DisplayName, @Email, @PasswordHash, @RoleId, @Status, 1);
                    """;
                try
                {
                    userId = await conn.ExecuteScalarAsync<int>(insertUserSql, new
                    {
                        Username = req.Email,
                        req.DisplayName,
                        req.Email,
                        PasswordHash = passwordHash,
                        RoleId = defaultRoleId.Value,
                        Status = req.Status == 0 ? 0 : 1,
                    }, transaction: trans);
                }
                catch (SqlException ex) when (ex.Number is 2601 or 2627)
                {
                    // 理論上前面 lookup 已避開，這是極端併發保險
                    throw new InvalidOperationException("Email 信箱已存在");
                }
                reused = false;
            }

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

            // 建立 MT_Teachers（不論是否沿用，都會新增一筆教師資料）
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
                JsonSerializer.Serialize(new { req.DisplayName, req.Email, TeacherCode = teacherCode, ReusedExistingUser = reused }),
                transaction: trans);

            await trans.CommitAsync();

            return new CreateTeacherResult
            {
                TeacherId = teacherId,
                ReusedExistingUser = reused,
                ExistingDisplayName = reused ? existing!.Value.DisplayName : null,
                ExistingUsername = reused ? existing!.Value.Username : null,
            };
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

        // 與 Create 對齊：服務層統一 trim，避免前端漏帶或外部入口直呼造成資料不一致
        req.DisplayName = req.DisplayName.Trim();
        req.School      = req.School.Trim();
        req.Phone       = req.Phone?.Trim();
        req.IdNumber    = req.IdNumber?.Trim();
        req.Department  = req.Department?.Trim();
        req.Title       = req.Title?.Trim();
        req.Expertise   = req.Expertise?.Trim();
        req.Note        = req.Note?.Trim();

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
    /// 重設教師密碼為 CSF@01024304，標記首次登入。
    /// </summary>
    public async Task ResetTeacherPasswordAsync(int teacherId, int operatorId)
    {
        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        const string userIdSql = "SELECT UserId FROM dbo.MT_Teachers WHERE Id = @Id;";
        var userId = await conn.QuerySingleOrDefaultAsync<int?>(userIdSql, new { Id = teacherId });
        if (!userId.HasValue) throw new InvalidOperationException("找不到教師資料。");

        var passwordHash = AuthService.HashPassword(DefaultTeacherPassword);

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
    // 批次匯入
    // ==================================================================

    private static readonly Regex EmailPattern =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    /// <summary>
    /// 解析 .xlsx 檔案，回傳逐列驗證結果（含 DB 重複 Email 偵測）。
    /// ParseError 不為 null 表示工作表層級錯誤，此時 Rows 為空清單。
    /// </summary>
    public async Task<(List<BatchImportRow> Rows, string? ParseError)> ParseExcelAsync(Stream fileStream)
    {
        // ── 1. 開檔 + 讀工作表（NPOI 細節封裝在 ExcelHelper）──
        var (excelRows, error) = ExcelHelper.ReadSheet(
            fileStream,
            sheetName:   "教師資料匯入區",
            startColIdx: 1,   // B 欄
            endColIdx:   13,  // N 欄
            startRowIdx: 1    // 跳過第 1 列標題
        );
        if (error is not null) return ([], error);

        var rows = new List<BatchImportRow>();
        // 追蹤檔內 Email 首次出現的列號（key: lowercase email, value: RowNumber）
        var seenEmails = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // ── 2. 逐列驗證 ──
        for (var i = 0; i < excelRows.Count; i++)
        {
            var cells = excelRows[i];

            // ── 3. 讀取 13 個欄位（cells[0]=B … cells[12]=N）──
            var displayName   = cells[0];   // B
            var genderRaw     = cells[1];   // C
            var email         = cells[2];   // D
            var phone         = cells[3];   // E
            var idNumber      = cells[4];   // F
            var school        = cells[5];   // G
            var department    = cells[6];   // H
            var title         = cells[7];   // I
            var expertise     = cells[8];   // J
            var yearsRaw      = cells[9];   // K
            var educationRaw  = cells[10];  // L
            var statusRaw     = cells[11];  // M
            var note          = cells[12];  // N

            var importRow = new BatchImportRow { RowNumber = i + 2 }; // Excel 顯示列號 = 資料列 index + 2（跳過標題）

            // ── 6. 驗證 ──
            // 必填：姓名
            if (displayName == "")
                importRow.Errors.Add("姓名為必填欄位");

            // 必填 + 格式：Email
            if (email == "")
            {
                importRow.Errors.Add("信箱格式不正確");
            }
            else if (!EmailPattern.IsMatch(email) || email.Length > 200)
            {
                importRow.Errors.Add("信箱格式不正確");
            }

            // 必填：學校
            if (school == "")
                importRow.Errors.Add("任教學校為必填欄位");

            // 必填：職稱
            if (title == "")
                importRow.Errors.Add("職稱為必填欄位");

            // 長度檢查
            if (displayName.Length > 50)
                importRow.Errors.Add("姓名長度不可超過 50 字元");
            if (title.Length > 20)
                importRow.Errors.Add("職稱長度不可超過 20 字元（影響正式聘書）");
            if (note.Length > 500)
                importRow.Errors.Add("備註長度不可超過 500 字元");

            // 教學年資
            int? teachingYears = null;
            if (yearsRaw != "")
            {
                if (int.TryParse(yearsRaw, out var y) && y >= 0 && y <= 99)
                    teachingYears = y;
                else
                    importRow.Errors.Add("教學年資必須為 0~99 的整數");
            }

            // 帳號狀態（必填）
            var status = ParseStatus(statusRaw);
            if (status == -1)
                importRow.Errors.Add("帳號狀態必須填寫「啟用」或「停用」");

            // 性別（警告不擋）
            var gender = ParseGender(genderRaw, importRow.Warnings);

            // 最高學歷（警告不擋）
            var education = ParseEducation(educationRaw, importRow.Warnings);

            // ── 7. 檔內 Email 重複偵測 ──
            if (email != "" && EmailPattern.IsMatch(email))
            {
                var emailKey = email.ToLowerInvariant();
                if (seenEmails.TryGetValue(emailKey, out var firstRow))
                {
                    importRow.Errors.Add($"與檔案第 {firstRow} 列 Email 重複");
                }
                else
                {
                    seenEmails[emailKey] = importRow.RowNumber;
                }
            }

            // ── 8. 設定 Status ──
            if (importRow.Errors.Count > 0)
                importRow.Status = BatchImportRowStatus.Error;

            // ── 9. 填入 Data（即使有錯誤也填，方便前端預覽顯示） ──
            importRow.Data = new CreateTeacherRequest
            {
                DisplayName   = displayName,
                Email         = email,
                Gender        = gender,
                Phone         = phone == "" ? null : phone,
                IdNumber      = idNumber == "" ? null : idNumber,
                School        = school,
                Department    = department == "" ? null : department,
                Title         = title == "" ? null : title,
                Expertise     = expertise == "" ? null : expertise,
                TeachingYears = teachingYears,
                Education     = education,
                Status        = status == -1 ? 1 : status,  // 無效時暫設 1（錯誤列不可匯入）
                Note          = note == "" ? null : note,
            };

            rows.Add(importRow);
        }

        // ── 10. DB 重複 Email 批次偵測（僅對非 Error 列） ──
        var emailsToCheck = rows
            .Where(r => r.Status != BatchImportRowStatus.Error && r.Data.Email != "")
            .Select(r => r.Data.Email)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (emailsToCheck.Count > 0)
        {
            using var conn = _db.CreateConnection();
            var existingEmails = (await conn.QueryAsync<string>(
                "SELECT Email FROM dbo.MT_Users WHERE Email IN @Emails",
                new { Emails = emailsToCheck }
            )).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var r in rows)
            {
                if (r.Status == BatchImportRowStatus.Error) continue;
                if (existingEmails.Contains(r.Data.Email))
                {
                    // 已存在於 DB：升為 Warning（若原本是 Valid）
                    if (r.Status == BatchImportRowStatus.Valid)
                        r.Status = BatchImportRowStatus.Warning;
                    r.Warnings.Add("此 Email 已存在於系統，若保留勾選匯入將被自動排除。");
                }
            }
        }

        return (rows, null);
    }

    /// <summary>
    /// 逐筆匯入已通過前端驗證的教師資料列。
    /// 每筆各自開小 Transaction，失敗不影響其他筆（不整批回滾）。
    /// AuditLog 由內部呼叫的 CreateTeacherAsync 寫入（成功才寫）。
    /// </summary>
    public async Task<List<BatchImportRowResult>> ImportTeachersAsync(
        IReadOnlyList<BatchImportRow> rows,
        int operatorId)
    {
        var results = new List<BatchImportRowResult>();

        // 過濾：使用者勾選中、且非 Error 的列（含 Valid 與 Warning）
        var eligible = rows.Where(r => r.IsSelected && r.Status != BatchImportRowStatus.Error);

        foreach (var row in eligible)
        {
            try
            {
                // 直接呼叫既有方法（內含 MT_Users + MT_Teachers + AuditLog 完整寫入邏輯）
                await CreateTeacherAsync(row.Data, operatorId);

                results.Add(new BatchImportRowResult
                {
                    RowNumber = row.RowNumber,
                    IsSuccess = true
                });
            }
            catch (InvalidOperationException ex)
            {
                // 預期類錯誤（Email 撞名、已是教師、預設角色不存在）→ 直接顯示人話
                results.Add(new BatchImportRowResult
                {
                    RowNumber = row.RowNumber,
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                });
            }
            catch (Exception ex)
            {
                // 非預期錯誤（DB 連線中斷、SQL 語法錯誤等）
                results.Add(new BatchImportRowResult
                {
                    RowNumber = row.RowNumber,
                    IsSuccess = false,
                    ErrorMessage = $"匯入失敗：{ex.Message}"
                });
            }
        }

        return results;
    }

    // ==================================================================
    // 私有輔助方法
    // ==================================================================

    /// <summary>
    /// 解析性別中文字串：「男」→ 1，「女」→ 2，空白 → 0（未知），其他 → 0 並加 Warning。
    /// </summary>
    private static int ParseGender(string? raw, List<string> warnings)
    {
        return raw switch
        {
            "男" => 1,
            "女" => 2,
            null or "" => 0,
            _ => AddWarningAndReturn("性別格式不正確，已設為未知", warnings, 0)
        };
    }

    /// <summary>
    /// 解析最高學歷：「其它」→ 0，「專科」→ 1，「學士」→ 2，「碩士」→ 3，「博士」→ 4，空白 → null，其他 → null 並加 Warning。
    /// </summary>
    private static int? ParseEducation(string? raw, List<string> warnings)
    {
        return raw switch
        {
            "其它" => 0,
            "專科" => 1,
            "學士" => 2,
            "碩士" => 3,
            "博士" => 4,
            null or "" => null,
            _ => (int?)AddWarningAndReturn("最高學歷格式不正確，已忽略", warnings, (int?)null)
        };
    }

    /// <summary>
    /// 解析帳號狀態：「啟用」→ 1，「停用」→ 0，其他（含空白）→ -1（表示無效，呼叫端負責加錯誤）。
    /// </summary>
    private static int ParseStatus(string? raw)
    {
        return raw switch
        {
            "啟用" => 1,
            "停用" => 0,
            _ => -1
        };
    }

    /// <summary>加入警告訊息後回傳預設值（供 switch expression 使用的輔助方法）。</summary>
    private static T AddWarningAndReturn<T>(string message, List<string> warnings, T defaultValue)
    {
        warnings.Add(message);
        return defaultValue;
    }

    // ==================================================================
    // 匯出名單
    // ==================================================================

    /// <summary>
    /// 查詢指定梯次的成員（命題教師 / 審題類 / 管理身分），依身分各產生一列。
    /// 依 MT_Projects.ProjectType 分流 CWT (0) / LCT (1) 計數規則。
    /// 題型欄渲染為「X / Y」（X=匯出當下命/審題數、Y=被分配任務數）；分母為 0 時顯示「－」。
    /// 採用 / 不採用以 Status=12 / Status=11 計（結案後的最終狀態）。
    /// </summary>
    public async Task<TeacherExportResult> ExportProjectTeachersAsync(int projectId, string projectName)
    {
        using var conn = _db.CreateConnection();
        var param = new { ProjectId = projectId };

        // ── 0. 查 Project meta（ProjectType + ExamLevel）──
        var projectMeta = await conn.QuerySingleAsync<ExportProjectMeta>(
            "SELECT ProjectType, ExamLevel FROM dbo.MT_Projects WHERE Id = @ProjectId;", param);

        // 等級字串（CWT 用 GeneralLevelLabels；LCT 為「－」）
        var examLevelLabel = projectMeta.ProjectType == 0 && projectMeta.ExamLevel.HasValue
            && QuestionConstants.GeneralLevelLabels.TryGetValue(projectMeta.ExamLevel.Value, out var lv)
            ? lv
            : "－";

        // ── 1. 撈成員身分（兩種報表共用）──
        const string memberSql = """
            SELECT
                u.Id           AS UserId,
                u.DisplayName  AS DisplayName,
                r.Name         AS RoleName
            FROM dbo.MT_ProjectMembers pm
            INNER JOIN dbo.MT_Users u ON u.Id = pm.UserId
            INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
            INNER JOIN dbo.MT_Roles r ON r.Id = pmr.RoleId
            WHERE pm.ProjectId = @ProjectId;
            """;
        var memberRows = (await conn.QueryAsync<ExportMemberRow>(memberSql, param)).AsList();

        // ── 2. 依 ProjectType 計算各人 cells（含 X/Y 字串與採用/不採用）──
        Dictionary<int, ExportUserCells> composeCells;
        Dictionary<int, ExportUserCells> reviewCells;
        string[] categoryHeaders;

        if (projectMeta.ProjectType == 0)
        {
            composeCells    = await BuildCwtComposeCellsAsync(conn, projectId);
            reviewCells     = await BuildCwtReviewCellsAsync(conn, projectId);
            categoryHeaders = new[]
            {
                "一般單選題數", "閱讀題組母題數", "閱讀題組子題數",
                "長文題目數",   "短文題組母題數", "短文題組子題數"
            };
        }
        else
        {
            composeCells    = await BuildLctComposeCellsAsync(conn, projectId);
            reviewCells     = await BuildLctReviewCellsAsync(conn, projectId);
            categoryHeaders = new[]
            {
                "難度一題數", "難度二題數", "難度三題數",
                "難度四題數", "難度五題數", "聽力題組題數"
            };
        }

        // ── 3. 身分歸屬 ──
        static bool IsReviewerRole(string name) =>
            name == "總召集人" ||
            name.StartsWith("審題", StringComparison.Ordinal);

        var memberInfo = memberRows
            .GroupBy(r => r.UserId)
            .ToDictionary(
                g => g.Key,
                g => new ExportMemberInfo
                {
                    DisplayName       = g.First().DisplayName,
                    ProposerRoleName  = g.FirstOrDefault(r => r.RoleName == "命題教師")?.RoleName,
                    ReviewerRoleNames = g.Where(r => IsReviewerRole(r.RoleName))
                                         .Select(r => r.RoleName)
                                         .Distinct()
                                         .OrderBy(n => n, StringComparer.Ordinal)
                                         .ToList(),
                    AdminRoles        = g.Where(r => r.RoleName != "命題教師" && !IsReviewerRole(r.RoleName))
                                         .Select(r => r.RoleName)
                                         .Distinct()
                                         .OrderBy(n => n, StringComparer.Ordinal)
                                         .ToList(),
                });

        var exportRows = new List<TeacherExportRow>();

        // ── 4. 命題教師列 ──
        foreach (var kv in memberInfo
                     .Where(kv => kv.Value.ProposerRoleName != null)
                     .OrderBy(kv => kv.Value.DisplayName, StringComparer.Ordinal))
        {
            var cells = composeCells.TryGetValue(kv.Key, out var c) ? c : ExportUserCells.AllDashes;
            exportRows.Add(new TeacherExportRow
            {
                DisplayName    = kv.Value.DisplayName,
                RoleLabel      = kv.Value.ProposerRoleName!,
                CategoryCells  = cells.CategoryCells,
                AdoptedCell    = cells.AdoptedCell,
                RejectedCell   = cells.RejectedCell,
            });
        }

        // ── 5. 審題類列（審題委員 / 總召集人）──
        var reviewerEntries = memberInfo
            .SelectMany(kv => kv.Value.ReviewerRoleNames.Select(role => (
                UserId: kv.Key,
                DisplayName: kv.Value.DisplayName,
                RoleName: role)))
            .OrderBy(t => t.RoleName,    StringComparer.Ordinal)
            .ThenBy (t => t.DisplayName, StringComparer.Ordinal);

        foreach (var (userId, displayName, roleName) in reviewerEntries)
        {
            var cells = reviewCells.TryGetValue(userId, out var c) ? c : ExportUserCells.AllDashes;
            exportRows.Add(new TeacherExportRow
            {
                DisplayName    = displayName,
                RoleLabel      = roleName,
                CategoryCells  = cells.CategoryCells,
                AdoptedCell    = cells.AdoptedCell,
                RejectedCell   = cells.RejectedCell,
            });
        }

        // ── 6. 管理身分列（全欄「－」）──
        var adminEntries = memberInfo
            .SelectMany(kv => kv.Value.AdminRoles.Select(role => (
                DisplayName: kv.Value.DisplayName,
                RoleName: role)))
            .OrderBy(t => t.RoleName,    StringComparer.Ordinal)
            .ThenBy (t => t.DisplayName, StringComparer.Ordinal);

        foreach (var (displayName, roleName) in adminEntries)
        {
            exportRows.Add(new TeacherExportRow
            {
                DisplayName    = displayName,
                RoleLabel      = roleName,
                CategoryCells  = ExportUserCells.AllDashes.CategoryCells,
                AdoptedCell    = "－",
                RejectedCell   = "－",
            });
        }

        return new TeacherExportResult
        {
            ProjectName     = projectName,
            ProjectType     = projectMeta.ProjectType,
            ExamLevelLabel  = examLevelLabel,
            CategoryHeaders = categoryHeaders,
            Rows            = exportRows,
        };
    }

    // ============================================================
    //  CWT 統計載入：命題側（X = 實際命題數、Y = MT_MemberQuotas）
    //  6 欄：[0]一般單選 [1]閱讀母題 [2]閱讀子題 [3]長文 [4]短文母題 [5]短文子題
    //  採用/不採用：Status=12 / Status=11（母+子加總）
    // ============================================================
    private static async Task<Dictionary<int, ExportUserCells>> BuildCwtComposeCellsAsync(
        IDbConnection conn, int projectId)
    {
        var param = new { ProjectId = projectId };

        // 分母：MT_MemberQuotas，依 (UserId, TypeId, Granularity) 聚合
        const string quotaSql = """
            SELECT pm.UserId       AS UserId,
                   mq.QuestionTypeId AS TypeId,
                   mq.Granularity   AS Granularity,
                   SUM(mq.QuotaCount) AS Quota
            FROM dbo.MT_MemberQuotas mq
            INNER JOIN dbo.MT_ProjectMembers pm ON pm.Id = mq.ProjectMemberId
            WHERE pm.ProjectId = @ProjectId
              AND mq.QuestionTypeId IN (1, 3, 4, 5)
            GROUP BY pm.UserId, mq.QuestionTypeId, mq.Granularity;
            """;

        // 分子（母題）：MT_Questions
        const string masterSql = """
            SELECT q.CreatorId      AS UserId,
                   q.QuestionTypeId AS TypeId,
                   COUNT(*)         AS Cnt,
                   SUM(CASE WHEN q.Status = 12 THEN 1 ELSE 0 END) AS Adopted,
                   SUM(CASE WHEN q.Status = 11 THEN 1 ELSE 0 END) AS Rejected
            FROM dbo.MT_Questions q
            WHERE q.ProjectId      = @ProjectId
              AND q.IsDeleted      = 0
              AND q.Status         <> 0
              AND q.QuestionTypeId IN (1, 3, 4, 5)
            GROUP BY q.CreatorId, q.QuestionTypeId;
            """;

        // 分子（子題）：MT_SubQuestions
        const string subSql = """
            SELECT q.CreatorId      AS UserId,
                   q.QuestionTypeId AS TypeId,
                   COUNT(*)         AS Cnt,
                   SUM(CASE WHEN sq.Status = 12 THEN 1 ELSE 0 END) AS Adopted,
                   SUM(CASE WHEN sq.Status = 11 THEN 1 ELSE 0 END) AS Rejected
            FROM dbo.MT_SubQuestions sq
            INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
            WHERE q.ProjectId      = @ProjectId
              AND q.IsDeleted      = 0
              AND q.Status         <> 0
              AND q.QuestionTypeId IN (3, 5)
              AND sq.IsDeleted     = 0
            GROUP BY q.CreatorId, q.QuestionTypeId;
            """;

        var quotas    = (await conn.QueryAsync<CwtQuotaRow >(quotaSql,  param)).AsList();
        var masters   = (await conn.QueryAsync<CwtBucketRow>(masterSql, param)).AsList();
        var subs      = (await conn.QueryAsync<CwtBucketRow>(subSql,    param)).AsList();
        return AssembleCwtCells(quotas, masters, subs);
    }

    private static async Task<Dictionary<int, ExportUserCells>> BuildCwtReviewCellsAsync(
        IDbConnection conn, int projectId)
    {
        var param = new { ProjectId = projectId };

        // 審題：依 (UserId, TypeId, Granularity=母題/子題) 聚合
        //   完成 = ReviewStatus=2 OR (Comment IS NOT NULL AND Comment <> '')
        //   採用/不採用：以 master Status 為準（DISTINCT QuestionId）
        const string reviewSql = """
            SELECT ra.ReviewerId AS UserId,
                   q.QuestionTypeId AS TypeId,
                   CASE WHEN ra.SubQuestionId IS NULL THEN 0 ELSE 1 END AS Granularity,
                   COUNT(*) AS Total,
                   SUM(CASE
                       WHEN ra.ReviewStatus = 2
                            OR (ra.Comment IS NOT NULL AND LTRIM(RTRIM(ra.Comment)) <> '')
                       THEN 1 ELSE 0
                   END) AS Completed
            FROM dbo.MT_ReviewAssignments ra
            INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
            WHERE ra.ProjectId     = @ProjectId
              AND q.IsDeleted      = 0
              AND q.QuestionTypeId IN (1, 3, 4, 5)
            GROUP BY ra.ReviewerId, q.QuestionTypeId, CASE WHEN ra.SubQuestionId IS NULL THEN 0 ELSE 1 END;
            """;

        // 採用/不採用：該 user 審過的母題 + 子題，依各自 Status
        const string masterAdoptionSql = """
            SELECT dq.ReviewerId AS UserId,
                   SUM(CASE WHEN q.Status = 12 THEN 1 ELSE 0 END) AS Adopted,
                   SUM(CASE WHEN q.Status = 11 THEN 1 ELSE 0 END) AS Rejected
            FROM (
                SELECT DISTINCT ra.ReviewerId, q.Id AS QuestionId
                FROM dbo.MT_ReviewAssignments ra
                INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
                WHERE ra.ProjectId     = @ProjectId
                  AND q.IsDeleted      = 0
                  AND q.QuestionTypeId IN (1, 3, 4, 5)
                  AND ra.SubQuestionId IS NULL
            ) AS dq
            INNER JOIN dbo.MT_Questions q ON q.Id = dq.QuestionId
            GROUP BY dq.ReviewerId;
            """;

        const string subAdoptionSql = """
            SELECT dq.ReviewerId AS UserId,
                   SUM(CASE WHEN sq.Status = 12 THEN 1 ELSE 0 END) AS Adopted,
                   SUM(CASE WHEN sq.Status = 11 THEN 1 ELSE 0 END) AS Rejected
            FROM (
                SELECT DISTINCT ra.ReviewerId, ra.SubQuestionId AS SubId
                FROM dbo.MT_ReviewAssignments ra
                INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
                WHERE ra.ProjectId     = @ProjectId
                  AND q.IsDeleted      = 0
                  AND q.QuestionTypeId IN (3, 5)
                  AND ra.SubQuestionId IS NOT NULL
            ) AS dq
            INNER JOIN dbo.MT_SubQuestions sq ON sq.Id = dq.SubId
            WHERE sq.IsDeleted = 0
            GROUP BY dq.ReviewerId;
            """;

        var reviewRows     = (await conn.QueryAsync<CwtReviewRow   >(reviewSql,         param)).AsList();
        var masterAdoption = (await conn.QueryAsync<AdoptionRow    >(masterAdoptionSql, param)).AsList();
        var subAdoption    = (await conn.QueryAsync<AdoptionRow    >(subAdoptionSql,    param)).AsList();
        return AssembleCwtReviewCells(reviewRows, masterAdoption, subAdoption);
    }

    /// <summary>把 CWT 命題側資料攤平成 6 個 X/Y cell + 採用/不採用。無分母（quota=0）→「－」。</summary>
    private static Dictionary<int, ExportUserCells> AssembleCwtCells(
        List<CwtQuotaRow> quotas,
        List<CwtBucketRow> masters,
        List<CwtBucketRow> subs)
    {
        // 收集所有出現過的 UserId（quota / 實際命題的 superset）
        var allUserIds = new HashSet<int>(quotas.Select(q => q.UserId)
            .Concat(masters.Select(m => m.UserId))
            .Concat(subs.Select(s => s.UserId)));

        var result = new Dictionary<int, ExportUserCells>();

        // 6 欄對應 (TypeId, Granularity)：
        // 0: (1, 0)、1: (5, 0)、2: (5, 1)、3: (4, 0)、4: (3, 0)、5: (3, 1)
        var colMapMaster = new Dictionary<int, int> { { 1, 0 }, { 5, 1 }, { 4, 3 }, { 3, 4 } };
        var colMapSub    = new Dictionary<int, int> {           { 5, 2 },           { 3, 5 } };

        foreach (var uid in allUserIds)
        {
            var quotaPerCol = new int[6];
            foreach (var q in quotas.Where(x => x.UserId == uid))
            {
                int? col = q.Granularity == 0
                    ? (colMapMaster.TryGetValue(q.TypeId, out var c) ? c : (int?)null)
                    : (colMapSub.TryGetValue(q.TypeId, out var c2) ? c2 : (int?)null);
                if (col.HasValue) quotaPerCol[col.Value] += q.Quota;
            }

            var actualPerCol = new int[6];
            int adoptedTotal = 0, rejectedTotal = 0;
            foreach (var m in masters.Where(x => x.UserId == uid))
            {
                if (colMapMaster.TryGetValue(m.TypeId, out var col))
                    actualPerCol[col] += m.Cnt;
                adoptedTotal  += m.Adopted;
                rejectedTotal += m.Rejected;
            }
            foreach (var s in subs.Where(x => x.UserId == uid))
            {
                if (colMapSub.TryGetValue(s.TypeId, out var col))
                    actualPerCol[col] += s.Cnt;
                adoptedTotal  += s.Adopted;
                rejectedTotal += s.Rejected;
            }

            var cells = new string[6];
            bool hasAnyQuota = false;
            for (int i = 0; i < 6; i++)
            {
                if (quotaPerCol[i] > 0)
                {
                    cells[i] = $"{actualPerCol[i]} / {quotaPerCol[i]}";
                    hasAnyQuota = true;
                }
                else
                {
                    cells[i] = "－";
                }
            }

            result[uid] = new ExportUserCells
            {
                CategoryCells = cells,
                AdoptedCell   = hasAnyQuota ? adoptedTotal .ToString() : "－",
                RejectedCell  = hasAnyQuota ? rejectedTotal.ToString() : "－",
            };
        }

        return result;
    }

    /// <summary>把 CWT 審題側資料攤平成 6 個 X/Y cell + 採用/不採用。無分配 → 整列「－」。</summary>
    private static Dictionary<int, ExportUserCells> AssembleCwtReviewCells(
        List<CwtReviewRow> reviewRows,
        List<AdoptionRow>  masterAdoption,
        List<AdoptionRow>  subAdoption)
    {
        var allUserIds = new HashSet<int>(reviewRows.Select(r => r.UserId)
            .Concat(masterAdoption.Select(a => a.UserId))
            .Concat(subAdoption   .Select(a => a.UserId)));

        var colMapMaster = new Dictionary<int, int> { { 1, 0 }, { 5, 1 }, { 4, 3 }, { 3, 4 } };
        var colMapSub    = new Dictionary<int, int> {           { 5, 2 },           { 3, 5 } };

        var result = new Dictionary<int, ExportUserCells>();

        foreach (var uid in allUserIds)
        {
            var totalPerCol     = new int[6];
            var completedPerCol = new int[6];
            foreach (var r in reviewRows.Where(x => x.UserId == uid))
            {
                int? col = r.Granularity == 0
                    ? (colMapMaster.TryGetValue(r.TypeId, out var c) ? c : (int?)null)
                    : (colMapSub.TryGetValue(r.TypeId, out var c2) ? c2 : (int?)null);
                if (col.HasValue)
                {
                    totalPerCol[col.Value]     += r.Total;
                    completedPerCol[col.Value] += r.Completed;
                }
            }

            var cells = new string[6];
            bool hasAny = false;
            for (int i = 0; i < 6; i++)
            {
                if (totalPerCol[i] > 0)
                {
                    cells[i] = $"{completedPerCol[i]} / {totalPerCol[i]}";
                    hasAny = true;
                }
                else
                {
                    cells[i] = "－";
                }
            }

            int adopted  = masterAdoption.Where(a => a.UserId == uid).Sum(a => a.Adopted)
                         + subAdoption   .Where(a => a.UserId == uid).Sum(a => a.Adopted);
            int rejected = masterAdoption.Where(a => a.UserId == uid).Sum(a => a.Rejected)
                         + subAdoption   .Where(a => a.UserId == uid).Sum(a => a.Rejected);

            result[uid] = new ExportUserCells
            {
                CategoryCells = cells,
                AdoptedCell   = hasAny ? adopted .ToString() : "－",
                RejectedCell  = hasAny ? rejected.ToString() : "－",
            };
        }

        return result;
    }

    // ============================================================
    //  LCT 統計載入：命題側（X = 實際命題、Y = MT_MemberQuotas）
    //  6 欄：[0~4]難度1~5（TypeId=6 + Level=N）[5]聽力題組（TypeId=7 + Level NULL）
    //  採用：TypeId=6 Status=12 + TypeId=7 整組採用
    //  不採用：TypeId=6 Status=11（聽力題組不入庫不計入）
    // ============================================================
    private static async Task<Dictionary<int, ExportUserCells>> BuildLctComposeCellsAsync(
        IDbConnection conn, int projectId)
    {
        var param = new { ProjectId = projectId };

        // 分母：MT_MemberQuotas，按 (UserId, TypeId, Level, Granularity)
        const string quotaSql = """
            SELECT pm.UserId         AS UserId,
                   mq.QuestionTypeId AS TypeId,
                   mq.[Level]        AS Lvl,
                   SUM(mq.QuotaCount) AS Quota
            FROM dbo.MT_MemberQuotas mq
            INNER JOIN dbo.MT_ProjectMembers pm ON pm.Id = mq.ProjectMemberId
            WHERE pm.ProjectId = @ProjectId
              AND mq.QuestionTypeId IN (6, 7)
              AND mq.Granularity = 0
            GROUP BY pm.UserId, mq.QuestionTypeId, mq.[Level];
            """;

        // 分子（單題聽力 TypeId=6 by Level）：MT_Questions
        const string singleSql = """
            SELECT q.CreatorId AS UserId,
                   q.[Level]   AS Lvl,
                   COUNT(*)    AS Cnt,
                   SUM(CASE WHEN q.Status = 12 THEN 1 ELSE 0 END) AS Adopted,
                   SUM(CASE WHEN q.Status = 11 THEN 1 ELSE 0 END) AS Rejected
            FROM dbo.MT_Questions q
            WHERE q.ProjectId      = @ProjectId
              AND q.IsDeleted      = 0
              AND q.Status         <> 0
              AND q.QuestionTypeId = 6
              AND q.[Level] IN (1, 2, 3, 4, 5)
            GROUP BY q.CreatorId, q.[Level];
            """;

        // 分子（聽力題組 TypeId=7）：MT_Questions 母題計數
        const string groupSql = """
            SELECT q.CreatorId AS UserId,
                   COUNT(*)    AS Cnt
            FROM dbo.MT_Questions q
            WHERE q.ProjectId      = @ProjectId
              AND q.IsDeleted      = 0
              AND q.Status         <> 0
              AND q.QuestionTypeId = 7
            GROUP BY q.CreatorId;
            """;

        // 聽力題組整組採用：母題 Status=12 AND 所有子題 Status=12
        const string groupAdoptionSql = """
            SELECT q.CreatorId AS UserId,
                   COUNT(*)    AS Cnt
            FROM dbo.MT_Questions q
            WHERE q.ProjectId      = @ProjectId
              AND q.IsDeleted      = 0
              AND q.QuestionTypeId = 7
              AND q.Status = 12
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.MT_SubQuestions sq
                  WHERE sq.ParentQuestionId = q.Id
                    AND sq.IsDeleted = 0
                    AND sq.Status <> 12
              )
            GROUP BY q.CreatorId;
            """;

        var quotas        = (await conn.QueryAsync<LctQuotaRow >(quotaSql,         param)).AsList();
        var singles       = (await conn.QueryAsync<LctSingleRow>(singleSql,        param)).AsList();
        var groups        = (await conn.QueryAsync<LctGroupRow >(groupSql,         param)).AsList();
        var groupAdoption = (await conn.QueryAsync<LctGroupRow >(groupAdoptionSql, param)).AsList();

        return AssembleLctComposeCells(quotas, singles, groups, groupAdoption);
    }

    // ============================================================
    //  LCT 統計載入：審題側（X = 完成數、Y = 該人指派數）
    // ============================================================
    private static async Task<Dictionary<int, ExportUserCells>> BuildLctReviewCellsAsync(
        IDbConnection conn, int projectId)
    {
        var param = new { ProjectId = projectId };

        // 單題聽力（TypeId=6）by Level：依 ReviewerId × Level
        // 完成 = ReviewStatus=2 OR Comment 非空
        const string singleAssignSql = """
            SELECT ra.ReviewerId AS UserId,
                   q.[Level]     AS Lvl,
                   COUNT(*)      AS Total,
                   SUM(CASE
                       WHEN ra.ReviewStatus = 2
                            OR (ra.Comment IS NOT NULL AND LTRIM(RTRIM(ra.Comment)) <> '')
                       THEN 1 ELSE 0
                   END) AS Completed
            FROM dbo.MT_ReviewAssignments ra
            INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
            WHERE ra.ProjectId     = @ProjectId
              AND q.IsDeleted      = 0
              AND q.QuestionTypeId = 6
              AND q.[Level] IN (1, 2, 3, 4, 5)
              AND ra.SubQuestionId IS NULL
            GROUP BY ra.ReviewerId, q.[Level];
            """;

        // 聽力題組（TypeId=7）DISTINCT QuestionId by Reviewer（母題、子題指派合併計）
        const string groupAssignSql = """
            SELECT dq.ReviewerId AS UserId,
                   COUNT(*)      AS Total,
                   SUM(CASE WHEN dq.AnyCompleted = 1 THEN 1 ELSE 0 END) AS Completed
            FROM (
                SELECT ra.ReviewerId,
                       ra.QuestionId,
                       MAX(CASE
                           WHEN ra.ReviewStatus = 2
                                OR (ra.Comment IS NOT NULL AND LTRIM(RTRIM(ra.Comment)) <> '')
                           THEN 1 ELSE 0
                       END) AS AnyCompleted
                FROM dbo.MT_ReviewAssignments ra
                INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
                WHERE ra.ProjectId     = @ProjectId
                  AND q.IsDeleted      = 0
                  AND q.QuestionTypeId = 7
                GROUP BY ra.ReviewerId, ra.QuestionId
            ) AS dq
            GROUP BY dq.ReviewerId;
            """;

        // 採用/不採用（單題聽力）：DISTINCT master where Status=12 / Status=11
        const string singleAdoptionSql = """
            SELECT dq.ReviewerId AS UserId,
                   SUM(CASE WHEN q.Status = 12 THEN 1 ELSE 0 END) AS Adopted,
                   SUM(CASE WHEN q.Status = 11 THEN 1 ELSE 0 END) AS Rejected
            FROM (
                SELECT DISTINCT ra.ReviewerId, q.Id AS QuestionId
                FROM dbo.MT_ReviewAssignments ra
                INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
                WHERE ra.ProjectId     = @ProjectId
                  AND q.IsDeleted      = 0
                  AND q.QuestionTypeId = 6
                  AND ra.SubQuestionId IS NULL
            ) AS dq
            INNER JOIN dbo.MT_Questions q ON q.Id = dq.QuestionId
            GROUP BY dq.ReviewerId;
            """;

        // 聽力題組整組採用：DISTINCT master where整組採用
        const string groupAdoptionSql = """
            SELECT dq.ReviewerId AS UserId,
                   COUNT(*)      AS AdoptedGroupCount
            FROM (
                SELECT DISTINCT ra.ReviewerId, q.Id AS QuestionId
                FROM dbo.MT_ReviewAssignments ra
                INNER JOIN dbo.MT_Questions q ON q.Id = ra.QuestionId
                WHERE ra.ProjectId     = @ProjectId
                  AND q.IsDeleted      = 0
                  AND q.QuestionTypeId = 7
            ) AS dq
            INNER JOIN dbo.MT_Questions q ON q.Id = dq.QuestionId
            WHERE q.Status = 12
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.MT_SubQuestions sq
                  WHERE sq.ParentQuestionId = q.Id
                    AND sq.IsDeleted = 0
                    AND sq.Status <> 12
              )
            GROUP BY dq.ReviewerId;
            """;

        var singles        = (await conn.QueryAsync<LctReviewSingleRow>(singleAssignSql,   param)).AsList();
        var groups         = (await conn.QueryAsync<LctReviewGroupRow >(groupAssignSql,    param)).AsList();
        var singleAdoption = (await conn.QueryAsync<AdoptionRow       >(singleAdoptionSql, param)).AsList();
        var groupAdoption  = (await conn.QueryAsync<LctGroupRow       >(groupAdoptionSql,  param)).AsList();

        return AssembleLctReviewCells(singles, groups, singleAdoption, groupAdoption);
    }

    /// <summary>把 LCT 命題側資料攤平成 6 cells + 採用/不採用。</summary>
    private static Dictionary<int, ExportUserCells> AssembleLctComposeCells(
        List<LctQuotaRow> quotas, List<LctSingleRow> singles,
        List<LctGroupRow> groups, List<LctGroupRow> groupAdoption)
    {
        var allUserIds = new HashSet<int>(quotas.Select(q => q.UserId)
            .Concat(singles.Select(s => s.UserId))
            .Concat(groups .Select(g => g.UserId))
            .Concat(groupAdoption.Select(g => g.UserId)));

        var result = new Dictionary<int, ExportUserCells>();

        foreach (var uid in allUserIds)
        {
            var quotaPerCol = new int[6];
            foreach (var q in quotas.Where(x => x.UserId == uid))
            {
                int col = q.TypeId == 7 ? 5 :
                    (q.Lvl is >= 1 and <= 5 ? q.Lvl.Value - 1 : -1);
                if (col >= 0) quotaPerCol[col] += q.Quota;
            }

            var actualPerCol = new int[6];
            int adoptedTotal = 0, rejectedTotal = 0;
            foreach (var s in singles.Where(x => x.UserId == uid))
            {
                if (s.Lvl is >= 1 and <= 5)
                {
                    actualPerCol[s.Lvl - 1] += s.Cnt;
                    adoptedTotal  += s.Adopted;
                    rejectedTotal += s.Rejected;
                }
            }
            foreach (var g in groups.Where(x => x.UserId == uid))
                actualPerCol[5] += g.Cnt;

            int adoptedGroups = groupAdoption.Where(x => x.UserId == uid).Sum(x => x.Cnt);
            adoptedTotal += adoptedGroups;   // 聽力題組整組採用納入 採用總計（不採用不計入）

            var cells = new string[6];
            bool hasAnyQuota = false;
            for (int i = 0; i < 6; i++)
            {
                if (quotaPerCol[i] > 0)
                {
                    cells[i] = $"{actualPerCol[i]} / {quotaPerCol[i]}";
                    hasAnyQuota = true;
                }
                else
                {
                    cells[i] = "－";
                }
            }

            result[uid] = new ExportUserCells
            {
                CategoryCells = cells,
                AdoptedCell   = hasAnyQuota ? adoptedTotal .ToString() : "－",
                RejectedCell  = hasAnyQuota ? rejectedTotal.ToString() : "－",
            };
        }

        return result;
    }

    /// <summary>把 LCT 審題側資料攤平成 6 cells + 採用/不採用。</summary>
    private static Dictionary<int, ExportUserCells> AssembleLctReviewCells(
        List<LctReviewSingleRow> singles, List<LctReviewGroupRow> groups,
        List<AdoptionRow> singleAdoption, List<LctGroupRow> groupAdoption)
    {
        var allUserIds = new HashSet<int>(singles.Select(s => s.UserId)
            .Concat(groups        .Select(g => g.UserId))
            .Concat(singleAdoption.Select(a => a.UserId))
            .Concat(groupAdoption .Select(g => g.UserId)));

        var result = new Dictionary<int, ExportUserCells>();

        foreach (var uid in allUserIds)
        {
            var totalPerCol     = new int[6];
            var completedPerCol = new int[6];
            foreach (var s in singles.Where(x => x.UserId == uid))
            {
                if (s.Lvl is >= 1 and <= 5)
                {
                    totalPerCol[s.Lvl - 1]     += s.Total;
                    completedPerCol[s.Lvl - 1] += s.Completed;
                }
            }
            foreach (var g in groups.Where(x => x.UserId == uid))
            {
                totalPerCol[5]     += g.Total;
                completedPerCol[5] += g.Completed;
            }

            var cells = new string[6];
            bool hasAny = false;
            for (int i = 0; i < 6; i++)
            {
                if (totalPerCol[i] > 0)
                {
                    cells[i] = $"{completedPerCol[i]} / {totalPerCol[i]}";
                    hasAny = true;
                }
                else
                {
                    cells[i] = "－";
                }
            }

            int adopted  = singleAdoption.Where(a => a.UserId == uid).Sum(a => a.Adopted)
                         + groupAdoption .Where(g => g.UserId == uid).Sum(g => g.Cnt);
            int rejected = singleAdoption.Where(a => a.UserId == uid).Sum(a => a.Rejected);
            // LCT 聽力題組不入庫不計入 不採用

            result[uid] = new ExportUserCells
            {
                CategoryCells = cells,
                AdoptedCell   = hasAny ? adopted .ToString() : "－",
                RejectedCell  = hasAny ? rejected.ToString() : "－",
            };
        }

        return result;
    }

    /// <summary>單一 UserId 的渲染字串結果（新版主流，CWT 用）。</summary>
    private sealed class ExportUserCells
    {
        public string[] CategoryCells { get; set; } = ["－", "－", "－", "－", "－", "－"];
        public string   AdoptedCell   { get; set; } = "－";
        public string   RejectedCell  { get; set; } = "－";

        public static readonly ExportUserCells AllDashes = new();
    }

    /// <summary>MT_Projects 查 ProjectType + ExamLevel 的 DTO。</summary>
    private sealed class ExportProjectMeta
    {
        public byte  ProjectType { get; set; }
        public byte? ExamLevel   { get; set; }
    }

    /// <summary>CWT 命題側 quota DTO（依 UserId × TypeId × Granularity）。</summary>
    private sealed class CwtQuotaRow
    {
        public int UserId      { get; set; }
        public int TypeId      { get; set; }
        public int Granularity { get; set; }
        public int Quota       { get; set; }
    }

    /// <summary>CWT 命題側「實際命題數 + 採用/不採用」DTO（依 UserId × TypeId，母題或子題）。</summary>
    private sealed class CwtBucketRow
    {
        public int UserId    { get; set; }
        public int TypeId    { get; set; }
        public int Cnt       { get; set; }
        public int Adopted   { get; set; }
        public int Rejected  { get; set; }
    }

    /// <summary>CWT 審題側「指派數 + 完成數」DTO。</summary>
    private sealed class CwtReviewRow
    {
        public int UserId      { get; set; }
        public int TypeId      { get; set; }
        public int Granularity { get; set; }
        public int Total       { get; set; }
        public int Completed   { get; set; }
    }

    /// <summary>採用/不採用對應 DTO（兩個欄位）。</summary>
    private sealed class AdoptionRow
    {
        public int UserId   { get; set; }
        public int Adopted  { get; set; }
        public int Rejected { get; set; }
    }

    /// <summary>LCT 命題側 quota DTO（依 UserId × TypeId × Level；Level 可為 NULL 表聽力題組）。</summary>
    private sealed class LctQuotaRow
    {
        public int   UserId { get; set; }
        public int   TypeId { get; set; }
        public byte? Lvl    { get; set; }   // NULL = 聽力題組
        public int   Quota  { get; set; }
    }

    /// <summary>LCT 命題側「單題聽力」依 Level 分桶（含採用/不採用）。</summary>
    private sealed class LctSingleRow
    {
        public int  UserId    { get; set; }
        public byte Lvl       { get; set; }
        public int  Cnt       { get; set; }
        public int  Adopted   { get; set; }
        public int  Rejected  { get; set; }
    }

    /// <summary>LCT 聽力題組計數（per UserId 單欄，Cnt 視情境是總數或整組採用數）。</summary>
    private sealed class LctGroupRow
    {
        public int UserId { get; set; }
        public int Cnt    { get; set; }
    }

    /// <summary>LCT 審題側 單題聽力分配/完成數，依 Level 分桶。</summary>
    private sealed class LctReviewSingleRow
    {
        public int  UserId    { get; set; }
        public byte Lvl       { get; set; }
        public int  Total     { get; set; }
        public int  Completed { get; set; }
    }

    /// <summary>LCT 審題側 聽力題組分配/完成數（DISTINCT QuestionId）。</summary>
    private sealed class LctReviewGroupRow
    {
        public int UserId    { get; set; }
        public int Total     { get; set; }
        public int Completed { get; set; }
    }

    private sealed class ExportMemberRow
    {
        public int    UserId      { get; set; }
        public string DisplayName { get; set; } = "";
        public string RoleName    { get; set; } = "";
    }

    private sealed class ExportMemberInfo
    {
        public string  DisplayName       { get; set; } = "";
        /// <summary>命題身分 RoleName（直接帶 MT_Roles.Name 的「命題教師」）；null 表示無此身分。</summary>
        public string? ProposerRoleName  { get; set; }
        /// <summary>
        /// 審題相關身分清單：含「審題委員」(LIKE '審題%') 與「總召集人」。
        /// 每個 RoleName 在報表中產生一列、套用相同 review stats（DISTINCT QuestionId）。
        /// 該人同時掛多個審題身分 → 產生多列。
        /// </summary>
        public List<string> ReviewerRoleNames { get; set; } = new();
        /// <summary>純管理身分（計畫主持人、系統管理員 等），題數欄全部「－」。</summary>
        public List<string> AdminRoles       { get; set; } = new();
    }

    private async Task WriteAuditAsync(
        IDbConnection conn,
        int operatorId,
        AuditAction action,
        AuditTargetType targetType,
        int targetId,
        string? newValue,
        IDbTransaction? transaction = null)
    {
        const string sql = """
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue, IpAddress)
            VALUES (@UserId, @Action, @TargetType, @TargetId, @NewValue, @IpAddress);
            """;

        await conn.ExecuteAsync(sql, new
        {
            UserId = operatorId,
            Action = (byte)action,
            TargetType = (byte)targetType,
            TargetId = targetId,
            NewValue = newValue,
            IpAddress = ClientIpResolver.Resolve(_httpContextAccessor),
        }, transaction: transaction);
    }
}
