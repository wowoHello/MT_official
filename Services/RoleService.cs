using System.Data;
using System.Text.Json;
using Dapper;
using MT.Models;

namespace MT.Services;

/// <summary>
/// 定義角色與權限管理頁面（人員帳號 + 角色權限）所需的服務契約。
/// </summary>
public interface IRoleService
{
    // === 人員帳號管理 (Tab A) ===
    Task<List<InternalAccountItem>> GetInternalAccountsAsync();
    Task<AccountDetailDto?> GetAccountDetailAsync(int userId);
    Task<int> CreateAccountAsync(CreateAccountRequest req, int operatorId);
    Task UpdateAccountAsync(UpdateAccountRequest req, int operatorId);
    Task ToggleAccountStatusAsync(int userId, int operatorId);
    Task ResetAccountPasswordAsync(int userId, int operatorId);

    // === 角色權限管理 (Tab B) ===
    Task<List<RoleCardItem>> GetRolesAsync();
    Task<RoleDetailDto?> GetRoleDetailAsync(int roleId);
    Task<int> CreateRoleAsync(CreateRoleRequest req, int operatorId);
    Task UpdateRoleAsync(UpdateRoleRequest req, int operatorId);
    Task DeleteRoleAsync(int roleId, int operatorId);

    // === 共用查詢 ===
    Task<List<RoleOption>> GetInternalRoleOptionsAsync();
    Task<List<ModuleItem>> GetActiveModulesAsync();

    /// <summary>
    /// 取得所有啟用模組並標記使用者在指定梯次下的存取權限。
    /// 權限 = 系統角色(MT_Users.RoleId) ∪ 梯次角色(MT_ProjectMemberRoles) 的聯集。
    /// </summary>
    Task<List<UserModuleCard>> GetUserModuleCardsAsync(int userId, int? projectId);
}

/// <summary>
/// 提供人員帳號與角色權限的查詢、建立、編輯、狀態切換與密碼重設服務。
/// </summary>
public class RoleService : IRoleService
{
    private const string DefaultInternalPassword = "01024304";
    private const string AnnouncementsModuleKey = "announcements";

    private readonly IDatabaseService _db;
    private readonly ILogger<RoleService> _logger;

    public RoleService(IDatabaseService db, ILogger<RoleService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ==================================================================
    // Tab A - 人員帳號管理
    // ==================================================================

    /// <summary>
    /// 取得所有內部人員（Category=0 角色）的帳號列表。
    /// </summary>
    public async Task<List<InternalAccountItem>> GetInternalAccountsAsync()
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                u.Id,
                u.Username,
                u.DisplayName,
                u.Email,
                u.RoleId,
                r.Name AS RoleName,
                r.IsDefault AS IsDefaultRole,
                u.Status,
                u.CompanyTitle
            FROM dbo.MT_Users u
            INNER JOIN dbo.MT_Roles r ON r.Id = u.RoleId
            WHERE r.Category = 0
            ORDER BY u.Status DESC, u.CreatedAt DESC;
            """;

        var rows = await conn.QueryAsync<InternalAccountItem>(sql);
        return rows.ToList();
    }

    /// <summary>
    /// 取得單一人員帳號詳情，含角色資訊與該角色已啟用的模組列表。
    /// </summary>
    public async Task<AccountDetailDto?> GetAccountDetailAsync(int userId)
    {
        using var conn = _db.CreateConnection();

        const string userSql = """
            SELECT
                u.Id,
                u.Username,
                u.DisplayName,
                u.Email,
                u.RoleId,
                r.Name AS RoleName,
                r.Category AS RoleCategory,
                r.IsDefault AS IsDefaultRole,
                u.Status,
                u.CompanyTitle,
                u.Note,
                u.IsFirstLogin,
                u.CreatedAt,
                u.LastLoginAt
            FROM dbo.MT_Users u
            INNER JOIN dbo.MT_Roles r ON r.Id = u.RoleId
            WHERE u.Id = @UserId;
            """;

        var detail = await conn.QuerySingleOrDefaultAsync<AccountDetailDto>(userSql, new { UserId = userId });
        if (detail is null) return null;

        const string moduleSql = """
            SELECT
                m.Id AS ModuleId,
                m.ModuleKey,
                m.Name,
                m.Icon,
                m.ColorClass,
                m.BgColorClass,
                m.SortOrder,
                rp.AnnouncementPerm
            FROM dbo.MT_RolePermissions rp
            INNER JOIN dbo.MT_Modules m ON m.Id = rp.ModuleId
            WHERE rp.RoleId = @RoleId
              AND rp.IsEnabled = 1
              AND m.IsActive = 1
            ORDER BY m.SortOrder;
            """;

        var modules = (await conn.QueryAsync(moduleSql, new { RoleId = detail.RoleId })).ToList();

        detail.EnabledModules = modules.Select(m => new RoleModuleBadge
        {
            ModuleId = (int)m.ModuleId,
            ModuleKey = (string)m.ModuleKey,
            Name = (string)m.Name,
            Icon = (string?)m.Icon ?? "",
            ColorClass = (string?)m.ColorClass ?? "",
            BgColorClass = (string?)m.BgColorClass ?? "",
            SortOrder = (int)m.SortOrder,
        }).ToList();

        var announcementsRow = modules.FirstOrDefault(m => (string)m.ModuleKey == AnnouncementsModuleKey);
        detail.AnnouncementPerm = announcementsRow is null ? 0 : (int)(byte)announcementsRow.AnnouncementPerm;

        return detail;
    }

    /// <summary>
    /// 新增內部人員帳號，預設密碼為公司統編，並標記首次登入。
    /// </summary>
    public async Task<int> CreateAccountAsync(CreateAccountRequest req, int operatorId)
    {
        ValidateAccountRequired(req.Username, req.DisplayName, req.RoleId);

        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        await EnsureUsernameUniqueAsync(conn, req.Username, excludeUserId: null);
        await EnsureRoleIsInternalAsync(conn, req.RoleId);

        var passwordHash = AuthService.ComputePasswordHash(DefaultInternalPassword);

        const string insertSql = """
            INSERT INTO dbo.MT_Users
                (Username, DisplayName, Email, PasswordHash, RoleId, Status, CompanyTitle, Note, IsFirstLogin)
            OUTPUT INSERTED.Id
            VALUES
                (@Username, @DisplayName, @Email, @PasswordHash, @RoleId, @Status, @CompanyTitle, @Note, 1);
            """;

        var newId = await conn.ExecuteScalarAsync<int>(insertSql, new
        {
            req.Username,
            req.DisplayName,
            req.Email,
            PasswordHash = passwordHash,
            req.RoleId,
            Status = NormalizeStatus(req.Status),
            req.CompanyTitle,
            req.Note,
        });

        await WriteAuditAsync(conn, operatorId, AuditAction.Create, AuditTargetType.Users, newId,
            newValue: JsonSerializer.Serialize(new { req.Username, req.DisplayName, req.RoleId }));

        return newId;
    }

    /// <summary>
    /// 編輯人員帳號資料（不更動登入帳號、密碼與首次登入狀態）。
    /// </summary>
    public async Task UpdateAccountAsync(UpdateAccountRequest req, int operatorId)
    {
        if (req.Id <= 0) throw new ArgumentException("Id 必填。");
        ValidateAccountRequired(username: "dummy", req.DisplayName, req.RoleId);

        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        await EnsureRoleIsInternalAsync(conn, req.RoleId);

        if (req.Id == operatorId && NormalizeStatus(req.Status) == 0)
            throw new InvalidOperationException("不可停用自己的帳號。");

        const string sql = """
            UPDATE dbo.MT_Users
            SET DisplayName   = @DisplayName,
                Email         = @Email,
                RoleId        = @RoleId,
                Status        = @Status,
                CompanyTitle  = @CompanyTitle,
                Note          = @Note,
                UpdatedAt     = SYSDATETIME()
            WHERE Id = @Id;
            """;

        var rows = await conn.ExecuteAsync(sql, new
        {
            req.Id,
            req.DisplayName,
            req.Email,
            req.RoleId,
            Status = NormalizeStatus(req.Status),
            req.CompanyTitle,
            req.Note,
        });

        if (rows == 0) throw new InvalidOperationException("找不到要更新的人員資料。");

        await WriteAuditAsync(conn, operatorId, AuditAction.Update, AuditTargetType.Users, req.Id,
            newValue: JsonSerializer.Serialize(new { req.DisplayName, req.RoleId, req.Status }));
    }

    /// <summary>
    /// 切換帳號啟用/停用狀態（鎖定狀態 2 視同停用，切換後回到啟用）。
    /// </summary>
    public async Task ToggleAccountStatusAsync(int userId, int operatorId)
    {
        if (userId == operatorId) throw new InvalidOperationException("不可停用或啟用自己的帳號。");

        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        const string readSql = "SELECT Status FROM dbo.MT_Users WHERE Id = @Id;";
        var current = await conn.QuerySingleOrDefaultAsync<int?>(readSql, new { Id = userId });
        if (current is null) throw new InvalidOperationException("找不到使用者。");

        var next = current.Value == 1 ? 0 : 1;

        const string updateSql = """
            UPDATE dbo.MT_Users
            SET Status = @Status,
                UpdatedAt = SYSDATETIME()
            WHERE Id = @Id;
            """;

        await conn.ExecuteAsync(updateSql, new { Id = userId, Status = next });

        await WriteAuditAsync(conn, operatorId, AuditAction.Update, AuditTargetType.Users, userId,
            newValue: JsonSerializer.Serialize(new { StatusChangedTo = next }));
    }

    /// <summary>
    /// 將密碼重設為公司統編預設值，並標記為首次登入（下次登入強制變更）。
    /// </summary>
    public async Task ResetAccountPasswordAsync(int userId, int operatorId)
    {
        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        var passwordHash = AuthService.ComputePasswordHash(DefaultInternalPassword);

        const string sql = """
            UPDATE dbo.MT_Users
            SET PasswordHash = @PasswordHash,
                IsFirstLogin = 1,
                LockoutUntil = NULL,
                UpdatedAt    = SYSDATETIME()
            WHERE Id = @Id;
            """;

        var rows = await conn.ExecuteAsync(sql, new { Id = userId, PasswordHash = passwordHash });
        if (rows == 0) throw new InvalidOperationException("找不到要重設密碼的人員。");

        await WriteAuditAsync(conn, operatorId, AuditAction.Update, AuditTargetType.Users, userId,
            newValue: "{\"PasswordReset\":true}");
    }

    // ==================================================================
    // Tab B - 角色權限管理
    // ==================================================================

    /// <summary>
    /// 取得所有角色卡片資料，含使用者數量與已啟用模組摘要。
    /// </summary>
    public async Task<List<RoleCardItem>> GetRolesAsync()
    {
        using var conn = _db.CreateConnection();

        const string roleSql = """
            SELECT
                r.Id,
                r.Name,
                r.Category,
                r.Description,
                r.IsDefault,
                (SELECT COUNT(*) FROM dbo.MT_Users u WHERE u.RoleId = r.Id) AS UserCount
            FROM dbo.MT_Roles r
            ORDER BY r.IsDefault DESC, r.Category, r.Id;
            """;

        var roles = (await conn.QueryAsync<RoleCardItem>(roleSql)).ToList();
        if (roles.Count == 0) return roles;

        const string permSql = """
            SELECT
                rp.RoleId,
                m.Id   AS ModuleId,
                m.ModuleKey,
                m.Name,
                m.Icon,
                m.ColorClass,
                m.BgColorClass,
                m.SortOrder
            FROM dbo.MT_RolePermissions rp
            INNER JOIN dbo.MT_Modules m ON m.Id = rp.ModuleId
            WHERE rp.IsEnabled = 1
              AND m.IsActive = 1
            ORDER BY m.SortOrder;
            """;

        var permRows = (await conn.QueryAsync(permSql)).ToList();

        foreach (var role in roles)
        {
            var badges = permRows
                .Where(p => (int)p.RoleId == role.Id)
                .Select(p => new RoleModuleBadge
                {
                    ModuleId = (int)p.ModuleId,
                    ModuleKey = (string)p.ModuleKey,
                    Name = (string)p.Name,
                    Icon = (string?)p.Icon ?? "",
                    ColorClass = (string?)p.ColorClass ?? "",
                    BgColorClass = (string?)p.BgColorClass ?? "",
                    SortOrder = (int)p.SortOrder,
                })
                .ToList();

            role.EnabledModules = badges;
            role.EnabledModuleCount = badges.Count;
        }

        return roles;
    }

    /// <summary>
    /// 取得單一角色的完整權限矩陣（含所有啟用中模組的 Toggle 狀態）。
    /// </summary>
    public async Task<RoleDetailDto?> GetRoleDetailAsync(int roleId)
    {
        using var conn = _db.CreateConnection();

        const string roleSql = """
            SELECT
                r.Id,
                r.Name,
                r.Category,
                r.Description,
                r.IsDefault,
                (SELECT COUNT(*) FROM dbo.MT_Users u WHERE u.RoleId = r.Id) AS UserCount
            FROM dbo.MT_Roles r
            WHERE r.Id = @Id;
            """;

        var detail = await conn.QuerySingleOrDefaultAsync<RoleDetailDto>(roleSql, new { Id = roleId });
        if (detail is null) return null;

        const string permSql = """
            SELECT
                m.Id         AS ModuleId,
                m.ModuleKey,
                m.Name,
                m.Icon,
                m.PageUrl,
                m.Description,
                m.ColorClass,
                m.BgColorClass,
                m.SortOrder,
                COALESCE(rp.IsEnabled, CAST(0 AS BIT)) AS IsEnabled,
                COALESCE(rp.AnnouncementPerm, 0)       AS AnnouncementPerm
            FROM dbo.MT_Modules m
            LEFT JOIN dbo.MT_RolePermissions rp
                ON rp.ModuleId = m.Id AND rp.RoleId = @RoleId
            WHERE m.IsActive = 1
            ORDER BY m.SortOrder;
            """;

        var toggles = await conn.QueryAsync<RolePermissionToggle>(permSql, new { RoleId = roleId });
        detail.Permissions = toggles.ToList();

        return detail;
    }

    /// <summary>
    /// 新增自訂角色並初始化該角色對所有功能模組的權限設定。
    /// </summary>
    public async Task<int> CreateRoleAsync(CreateRoleRequest req, int operatorId)
    {
        ValidateRoleRequired(req.Name, req.Category);

        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        await EnsureRoleNameUniqueAsync(conn, req.Name, excludeRoleId: null);

        using var trans = await conn.BeginTransactionAsync();
        try
        {
            const string insertRoleSql = """
                INSERT INTO dbo.MT_Roles (Name, Category, Description, IsDefault)
                OUTPUT INSERTED.Id
                VALUES (@Name, @Category, @Description, 0);
                """;

            var roleId = await conn.ExecuteScalarAsync<int>(insertRoleSql, new
            {
                req.Name,
                req.Category,
                req.Description,
            }, transaction: trans);

            await MergeRolePermissionsAsync(conn, trans, roleId, req.Permissions);

            await WriteAuditAsync(conn, operatorId, AuditAction.Create, AuditTargetType.Roles, roleId,
                newValue: JsonSerializer.Serialize(new { req.Name, req.Category }),
                transaction: trans);

            await trans.CommitAsync();
            return roleId;
        }
        catch
        {
            await trans.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 編輯自訂角色的資訊與權限矩陣（預設角色拒絕修改）。
    /// </summary>
    public async Task UpdateRoleAsync(UpdateRoleRequest req, int operatorId)
    {
        if (req.Id <= 0) throw new ArgumentException("Id 必填。");
        ValidateRoleRequired(req.Name, req.Category);

        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        await EnsureRoleEditableAsync(conn, req.Id);
        await EnsureRoleNameUniqueAsync(conn, req.Name, excludeRoleId: req.Id);

        using var trans = await conn.BeginTransactionAsync();
        try
        {
            const string updateSql = """
                UPDATE dbo.MT_Roles
                SET Name = @Name,
                    Category = @Category,
                    Description = @Description,
                    UpdatedAt = SYSDATETIME()
                WHERE Id = @Id AND IsDefault = 0;
                """;

            var affected = await conn.ExecuteAsync(updateSql, new
            {
                req.Id,
                req.Name,
                req.Category,
                req.Description,
            }, transaction: trans);

            if (affected == 0) throw new InvalidOperationException("找不到可修改的角色，或為系統預設角色。");

            await MergeRolePermissionsAsync(conn, trans, req.Id, req.Permissions);

            await WriteAuditAsync(conn, operatorId, AuditAction.Update, AuditTargetType.Roles, req.Id,
                newValue: JsonSerializer.Serialize(new { req.Name, req.Category }),
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
    /// 刪除自訂角色（預設角色拒絕、尚有使用者引用中拒絕）。
    /// </summary>
    public async Task DeleteRoleAsync(int roleId, int operatorId)
    {
        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        await EnsureRoleEditableAsync(conn, roleId);

        const string userCountSql = "SELECT COUNT(*) FROM dbo.MT_Users WHERE RoleId = @RoleId;";
        var userCount = await conn.ExecuteScalarAsync<int>(userCountSql, new { RoleId = roleId });
        if (userCount > 0) throw new InvalidOperationException($"此角色尚有 {userCount} 位使用者，請先改派後再刪除。");

        using var trans = await conn.BeginTransactionAsync();
        try
        {
            await conn.ExecuteAsync(
                "DELETE FROM dbo.MT_RolePermissions WHERE RoleId = @RoleId;",
                new { RoleId = roleId }, transaction: trans);

            var affected = await conn.ExecuteAsync(
                "DELETE FROM dbo.MT_Roles WHERE Id = @Id AND IsDefault = 0;",
                new { Id = roleId }, transaction: trans);

            if (affected == 0) throw new InvalidOperationException("找不到可刪除的角色，或為系統預設角色。");

            await WriteAuditAsync(conn, operatorId, AuditAction.Delete, AuditTargetType.Roles, roleId,
                newValue: null, transaction: trans);

            await trans.CommitAsync();
        }
        catch
        {
            await trans.RollbackAsync();
            throw;
        }
    }

    // ==================================================================
    // 共用查詢
    // ==================================================================

    /// <summary>
    /// 取得所有 Category=0（內部人員）角色，供新增人員時的身份別下拉使用。
    /// </summary>
    public async Task<List<RoleOption>> GetInternalRoleOptionsAsync()
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT Id, Name, Category, IsDefault
            FROM dbo.MT_Roles
            WHERE Category = 0
            ORDER BY IsDefault DESC, Id;
            """;

        var rows = await conn.QueryAsync<RoleOption>(sql);
        return rows.ToList();
    }

    /// <summary>
    /// 取得所有啟用中的功能模組，供角色權限 Toggle 清單渲染。
    /// </summary>
    public async Task<List<ModuleItem>> GetActiveModulesAsync()
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT Id, ModuleKey, Name, Icon, PageUrl, Description, ColorClass, BgColorClass, SortOrder
            FROM dbo.MT_Modules
            WHERE IsActive = 1
            ORDER BY SortOrder;
            """;

        var rows = await conn.QueryAsync<ModuleItem>(sql);
        return rows.ToList();
    }

    /// <summary>
    /// 取得所有啟用模組，並標記使用者在指定梯次下是否可存取。
    /// 權限判定：系統角色(MT_Users.RoleId) ∪ 梯次角色(MT_ProjectMemberRoles) 的聯集。
    /// 若 projectId 為 null，僅依系統角色判定。
    /// </summary>
    public async Task<List<UserModuleCard>> GetUserModuleCardsAsync(int userId, int? projectId)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                m.Id, m.ModuleKey, m.Name, m.Icon, m.PageUrl,
                m.Description, m.ColorClass, m.BgColorClass, m.SortOrder,
                -- 只要任一角色有啟用該模組即視為有權限
                CASE WHEN EXISTS (
                    SELECT 1
                    FROM dbo.MT_RolePermissions rp
                    WHERE rp.ModuleId = m.Id AND rp.IsEnabled = 1
                      AND rp.RoleId IN (
                          -- ① 系統角色
                          SELECT u.RoleId FROM dbo.MT_Users u WHERE u.Id = @UserId
                          UNION
                          -- ② 當前梯次的所有角色（projectId 為 NULL 時此段不回傳資料）
                          SELECT pmr.RoleId
                          FROM dbo.MT_ProjectMembers pm
                          INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
                          WHERE pm.UserId = @UserId AND pm.ProjectId = @ProjectId
                      )
                ) THEN 1 ELSE 0 END AS IsEnabled
            FROM dbo.MT_Modules m
            WHERE m.IsActive = 1
            ORDER BY m.SortOrder;
            """;

        var rows = await conn.QueryAsync<UserModuleCard>(sql, new { UserId = userId, ProjectId = projectId });
        return rows.ToList();
    }

    // ==================================================================
    // 私有輔助方法
    // ==================================================================

    private static void ValidateAccountRequired(string username, string displayName, int roleId)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("登入帳號必填。");
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("使用者姓名必填。");
        if (roleId <= 0) throw new ArgumentException("必須選擇身份別。");
    }

    private static void ValidateRoleRequired(string name, int category)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("角色名稱必填。");
        if (category is not (0 or 1)) throw new ArgumentException("角色分類僅允許 0:內部 或 1:外部。");
    }

    private static int NormalizeStatus(int status) => status is 0 or 1 or 2 ? status : 1;

    private static async Task EnsureUsernameUniqueAsync(IDbConnection conn, string username, int? excludeUserId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.MT_Users
            WHERE LOWER(Username) = LOWER(@Username)
              AND (@ExcludeId IS NULL OR Id <> @ExcludeId);
            """;
        var count = await conn.ExecuteScalarAsync<int>(sql, new { Username = username, ExcludeId = excludeUserId });
        if (count > 0) throw new InvalidOperationException($"登入帳號「{username}」已被使用。");
    }

    private static async Task EnsureRoleIsInternalAsync(IDbConnection conn, int roleId)
    {
        const string sql = "SELECT Category FROM dbo.MT_Roles WHERE Id = @Id;";
        var category = await conn.QuerySingleOrDefaultAsync<int?>(sql, new { Id = roleId });
        if (category is null) throw new InvalidOperationException("角色不存在。");
        if (category.Value != 0) throw new InvalidOperationException("僅可指派內部人員角色給帳號管理中的使用者。");
    }

    private static async Task EnsureRoleNameUniqueAsync(IDbConnection conn, string name, int? excludeRoleId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.MT_Roles
            WHERE Name = @Name
              AND (@ExcludeId IS NULL OR Id <> @ExcludeId);
            """;
        var count = await conn.ExecuteScalarAsync<int>(sql, new { Name = name, ExcludeId = excludeRoleId });
        if (count > 0) throw new InvalidOperationException($"角色名稱「{name}」已存在。");
    }

    private static async Task EnsureRoleEditableAsync(IDbConnection conn, int roleId)
    {
        const string sql = "SELECT IsDefault FROM dbo.MT_Roles WHERE Id = @Id;";
        var isDefault = await conn.QuerySingleOrDefaultAsync<bool?>(sql, new { Id = roleId });
        if (isDefault is null) throw new InvalidOperationException("角色不存在。");
        if (isDefault.Value) throw new InvalidOperationException("系統預設角色不可修改或刪除。");
    }

    /// <summary>
    /// 以「先清後寫」方式同步角色對每個模組的權限。呼叫端必須提供 Transaction 以維持原子性。
    /// </summary>
    private static async Task MergeRolePermissionsAsync(
        IDbConnection conn,
        IDbTransaction trans,
        int roleId,
        List<RolePermissionInput> permissions)
    {
        await conn.ExecuteAsync(
            "DELETE FROM dbo.MT_RolePermissions WHERE RoleId = @RoleId;",
            new { RoleId = roleId }, transaction: trans);

        if (permissions.Count == 0) return;

        // 讀取公告模組 Id，用於判斷是否保留 AnnouncementPerm；其他模組一律寫 0。
        var announcementsId = await conn.ExecuteScalarAsync<int?>(
            "SELECT Id FROM dbo.MT_Modules WHERE ModuleKey = @Key;",
            new { Key = AnnouncementsModuleKey }, transaction: trans);

        const string insertSql = """
            INSERT INTO dbo.MT_RolePermissions (RoleId, ModuleId, IsEnabled, AnnouncementPerm)
            VALUES (@RoleId, @ModuleId, @IsEnabled, @AnnouncementPerm);
            """;

        foreach (var p in permissions)
        {
            var announcementPerm = (p.ModuleId == announcementsId && p.IsEnabled)
                ? ClampAnnouncementPerm(p.AnnouncementPerm)
                : 0;

            await conn.ExecuteAsync(insertSql, new
            {
                RoleId = roleId,
                p.ModuleId,
                p.IsEnabled,
                AnnouncementPerm = announcementPerm,
            }, transaction: trans);
        }
    }

    private static int ClampAnnouncementPerm(int perm) => perm is 1 or 2 ? perm : 1;

    /// <summary>
    /// 統一寫入 MT_AuditLogs 的輔助方法。ProjectId 固定為 NULL（帳號/角色不綁定梯次）。
    /// </summary>
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
