using System.Data;
using Dapper;
using Microsoft.AspNetCore.Http;
using MT.Models;

namespace MT.Services;

// ─── 介面 ───
public interface IAnnouncementService
{
    Task<List<AnnouncementListItem>> GetAnnouncementListAsync();
    Task<AnnouncementEditDto?> GetAnnouncementEditAsync(int id);
    Task<List<ProjectDropdownItem>> GetProjectDropdownAsync();
    Task<int> CreateAsync(AnnouncementFormModel model, int operatorId);
    Task UpdateAsync(int id, AnnouncementFormModel model, int operatorId);
    Task TogglePinAsync(int id, int operatorId);
    Task<int> AutoUnpinExpiredAsync(int operatorId);
    Task DeleteAsync(int id, int operatorId);
    Task<List<AnnouncementListItem>> GetHomeAnnouncementsAsync(int? projectId);
}

// ─── 實作 ───
public class AnnouncementService : IAnnouncementService
{
    private readonly IDatabaseService _db;
    private readonly ILogger<AnnouncementService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AnnouncementService(IDatabaseService db, ILogger<AnnouncementService> logger, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    // ─── 權限門鎖：寫入動作前必過 ───
    // 任一系統角色或當前梯次角色於 MT_RolePermissions 對 ModuleKey='Announcements' 啟用即放行
    // 注意：DB 實際存的 ModuleKey 為「首字大寫」（如 Announcements），比對採大小寫不敏感避免拼錯
    private static async Task EnsureCanEditAsync(IDbConnection conn, int operatorId)
    {
        if (operatorId <= 0)
            throw new UnauthorizedAccessException("尚未登入或登入逾期，請重新登入。");

        const string sql = """
            SELECT TOP 1 1
            FROM dbo.MT_RolePermissions rp
            INNER JOIN dbo.MT_Modules m ON m.Id = rp.ModuleId
            WHERE LOWER(m.ModuleKey) = 'announcements'
              AND rp.IsEnabled = 1
              AND rp.RoleId IN (
                  SELECT u.RoleId FROM dbo.MT_Users u WHERE u.Id = @UserId
                  UNION
                  SELECT pmr.RoleId
                  FROM dbo.MT_ProjectMembers pm
                  INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
                  WHERE pm.UserId = @UserId
              );
            """;

        var hasPermission = await conn.ExecuteScalarAsync<int?>(sql, new { UserId = operatorId });
        if (hasPermission != 1)
            throw new UnauthorizedAccessException("您沒有公告管理權限。");
    }

    // ─── 列表查詢 ───
    public async Task<List<AnnouncementListItem>> GetAnnouncementListAsync()
    {
        const string sql = """
            SELECT
                a.Id, a.Category, a.Status, a.ProjectId,
                ISNULL(p.Name, '') AS ProjectName,
                a.PublishDate, a.UnpublishDate, a.IsPinned,
                a.Title, a.Content, a.CreatedAt,
                u.DisplayName AS AuthorName
            FROM dbo.MT_Announcements a
            INNER JOIN dbo.MT_Users u ON a.AuthorId = u.Id
            LEFT JOIN dbo.MT_Projects p ON a.ProjectId = p.Id
            ORDER BY a.IsPinned DESC, a.PublishDate DESC;
            """;

        using var conn = _db.CreateConnection();
        var result = await conn.QueryAsync<AnnouncementListItem>(sql);
        return result.ToList();
    }

    // ─── 單筆編輯載入 ───
    public async Task<AnnouncementEditDto?> GetAnnouncementEditAsync(int id)
    {
        const string sql = """
            SELECT Id, Category, Status, ProjectId, PublishDate, UnpublishDate,
                   IsPinned, Title, Content
            FROM dbo.MT_Announcements
            WHERE Id = @Id;
            """;

        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<AnnouncementEditDto>(sql, new { Id = id });
    }

    // ─── 梯次下拉選項 ───
    public async Task<List<ProjectDropdownItem>> GetProjectDropdownAsync()
    {
        const string sql = """
            SELECT Id, Name
            FROM dbo.MT_Projects
            WHERE IsDeleted = 0
            ORDER BY Year DESC, Name;
            """;

        using var conn = _db.CreateConnection();
        var result = await conn.QueryAsync<ProjectDropdownItem>(sql);
        return result.ToList();
    }

    // ─── 新增公告 ───
    public async Task<int> CreateAsync(AnnouncementFormModel model, int operatorId)
    {
        const string insertSql = """
            INSERT INTO dbo.MT_Announcements
                (Category, Status, ProjectId, PublishDate, UnpublishDate, IsPinned, Title, Content, AuthorId)
            OUTPUT INSERTED.Id
            VALUES
                (@Category, @Status, @ProjectId, @PublishDate, @UnpublishDate, @IsPinned, @Title, @Content, @AuthorId);
            """;

        const string auditSql = """
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue, IpAddress)
            VALUES (@UserId, @Action, @TargetType, @TargetId, @NewValue, @IpAddress);
            """;

        using var conn = _db.CreateConnection();
        conn.Open();

        await EnsureCanEditAsync(conn, operatorId);

        using var tx = conn.BeginTransaction();

        try
        {
            var newId = await conn.QuerySingleAsync<int>(insertSql, new
            {
                model.Category,
                model.Status,
                model.ProjectId,
                model.PublishDate,
                model.UnpublishDate,
                model.IsPinned,
                model.Title,
                model.Content,
                AuthorId = operatorId
            }, tx);

            // NewValue 統一改為 JSON（含 targetDisplayName）：刪除後 SystemLogs 仍能 fallback 顯示
            await conn.ExecuteAsync(auditSql, new
            {
                UserId = operatorId,
                Action = (byte)AuditAction.Create,
                TargetType = (byte)AuditTargetType.Announcements,
                TargetId = newId,
                NewValue = AuditLogJsonHelper.Serialize(new
                {
                    title = model.Title,
                    category = model.Category,
                    isPinned = model.IsPinned,
                    projectId = model.ProjectId,           // 公告綁定的梯次（僅參考，不寫進 LOG 的 ProjectId 欄位）
                    targetDisplayName = model.Title
                }),
                IpAddress = ClientIpResolver.Resolve(_httpContextAccessor)
            }, tx);

            tx.Commit();
            return newId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ─── 更新公告 ───
    public async Task UpdateAsync(int id, AnnouncementFormModel model, int operatorId)
    {
        const string updateSql = """
            UPDATE dbo.MT_Announcements
            SET Category = @Category, Status = @Status, ProjectId = @ProjectId,
                PublishDate = @PublishDate, UnpublishDate = @UnpublishDate,
                IsPinned = @IsPinned, Title = @Title, Content = @Content,
                UpdatedAt = SYSDATETIME()
            WHERE Id = @Id;
            """;

        const string auditSql = """
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue, IpAddress)
            VALUES (@UserId, @Action, @TargetType, @TargetId, @NewValue, @IpAddress);
            """;

        using var conn = _db.CreateConnection();
        conn.Open();

        await EnsureCanEditAsync(conn, operatorId);

        using var tx = conn.BeginTransaction();

        try
        {
            await conn.ExecuteAsync(updateSql, new
            {
                Id = id,
                model.Category,
                model.Status,
                model.ProjectId,
                model.PublishDate,
                model.UnpublishDate,
                model.IsPinned,
                model.Title,
                model.Content
            }, tx);

            await conn.ExecuteAsync(auditSql, new
            {
                UserId = operatorId,
                Action = (byte)AuditAction.Update,
                TargetType = (byte)AuditTargetType.Announcements,
                TargetId = id,
                NewValue = AuditLogJsonHelper.Serialize(new
                {
                    title = model.Title,
                    category = model.Category,
                    isPinned = model.IsPinned,
                    projectId = model.ProjectId,
                    targetDisplayName = model.Title
                }),
                IpAddress = ClientIpResolver.Resolve(_httpContextAccessor)
            }, tx);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ─── 置頂切換 ───
    public async Task TogglePinAsync(int id, int operatorId)
    {
        // UPDATE 後用 OUTPUT 取得新的 IsPinned 與 Title，供 LOG 寫入時記錄切換後狀態
        const string toggleSql = """
            UPDATE dbo.MT_Announcements
            SET IsPinned = CASE WHEN IsPinned = 1 THEN 0 ELSE 1 END,
                UpdatedAt = SYSDATETIME()
            OUTPUT INSERTED.IsPinned AS IsPinned, INSERTED.Title AS Title
            WHERE Id = @Id;
            """;

        const string auditSql = """
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue, IpAddress)
            VALUES (@UserId, @Action, @TargetType, @TargetId, @NewValue, @IpAddress);
            """;

        using var conn = _db.CreateConnection();
        conn.Open();

        await EnsureCanEditAsync(conn, operatorId);

        using var tx = conn.BeginTransaction();

        try
        {
            var updated = await conn.QuerySingleAsync<(bool IsPinned, string Title)>(toggleSql, new { Id = id }, tx);

            await conn.ExecuteAsync(auditSql, new
            {
                UserId = operatorId,
                Action = (byte)AuditAction.Update,
                TargetType = (byte)AuditTargetType.Announcements,
                TargetId = id,
                NewValue = AuditLogJsonHelper.Serialize(new
                {
                    title = updated.Title,
                    action = updated.IsPinned ? "置頂" : "取消置頂",
                    isPinned = updated.IsPinned,
                    targetDisplayName = updated.Title
                }),
                IpAddress = ClientIpResolver.Resolve(_httpContextAccessor)
            }, tx);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ─── 自動取消草稿或已下架公告的置頂狀態 ───
    public async Task<int> AutoUnpinExpiredAsync(int operatorId)
    {
        const string sql = """
            DECLARE @Updated TABLE (Id INT NOT NULL);

            UPDATE dbo.MT_Announcements
            SET IsPinned = 0,
                UpdatedAt = SYSDATETIME()
            OUTPUT INSERTED.Id INTO @Updated(Id)
            WHERE IsPinned = 1
              AND (
                  Status = @DraftStatus
                  OR (
                      Status = @PublishedStatus
                      AND UnpublishDate IS NOT NULL
                      AND SYSDATETIME() > UnpublishDate
                  )
              );

            -- 為每筆被取消置頂的公告寫一筆 LOG；targetDisplayName 從 MT_Announcements.Title 取
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue, IpAddress)
            SELECT @UserId, @Action, @TargetType, u.Id,
                   N'{"action":"自動取消過期公告置頂","targetDisplayName":"' + REPLACE(ISNULL(a.Title, N''), N'"', N'\"') + N'"}',
                   @IpAddress
            FROM @Updated u
            LEFT JOIN dbo.MT_Announcements a ON a.Id = u.Id;

            SELECT COUNT(*) FROM @Updated;
            """;

        using var conn = _db.CreateConnection();
        conn.Open();

        await EnsureCanEditAsync(conn, operatorId);

        using var tx = conn.BeginTransaction();

        try
        {
            var affectedRows = await conn.ExecuteScalarAsync<int>(sql, new
            {
                UserId = operatorId,
                DraftStatus = (byte)AnnouncementStatus.Draft,
                PublishedStatus = (byte)AnnouncementStatus.Published,
                Action = (byte)AuditAction.Update,
                TargetType = (byte)AuditTargetType.Announcements,
                IpAddress = ClientIpResolver.Resolve(_httpContextAccessor)
            }, tx);

            tx.Commit();
            return affectedRows;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ─── 首頁公告查詢（僅已發佈且在上架期間） ───
    public async Task<List<AnnouncementListItem>> GetHomeAnnouncementsAsync(int? projectId)
    {
        const string sql = """
            SELECT
                a.Id, a.Category, a.Status, a.ProjectId,
                ISNULL(p.Name, '') AS ProjectName,
                a.PublishDate, a.UnpublishDate, a.IsPinned,
                a.Title, a.Content, a.CreatedAt,
                u.DisplayName AS AuthorName
            FROM dbo.MT_Announcements a
            INNER JOIN dbo.MT_Users u ON a.AuthorId = u.Id
            LEFT JOIN dbo.MT_Projects p ON a.ProjectId = p.Id
            WHERE a.Status = 1
              AND a.PublishDate <= SYSDATETIME()
              AND (a.UnpublishDate IS NULL OR a.UnpublishDate >= SYSDATETIME())
              AND (a.ProjectId IS NULL OR a.ProjectId = @ProjectId)
            ORDER BY a.IsPinned DESC, a.PublishDate DESC;
            """;

        using var conn = _db.CreateConnection();
        var result = await conn.QueryAsync<AnnouncementListItem>(sql, new { ProjectId = projectId });
        return result.ToList();
    }

    // ─── 刪除公告 ───
    public async Task DeleteAsync(int id, int operatorId)
    {
        const string selectSql = """
            SELECT Id, Title, Category, IsPinned, ProjectId
            FROM dbo.MT_Announcements WHERE Id = @Id;
            """;

        const string deleteSql = """
            DELETE FROM dbo.MT_Announcements WHERE Id = @Id;
            """;

        // Delete 用 OldValue 記錄被刪除前的內容，供 SystemLogs / Dashboard 反查 Title
        const string auditSql = """
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, OldValue, IpAddress)
            VALUES (@UserId, @Action, @TargetType, @TargetId, @OldValue, @IpAddress);
            """;

        using var conn = _db.CreateConnection();
        conn.Open();

        await EnsureCanEditAsync(conn, operatorId);

        using var tx = conn.BeginTransaction();

        try
        {
            // 先取得即將刪除的公告資料，作為 OldValue 寫入 audit
            var snapshot = await conn.QuerySingleOrDefaultAsync<AnnouncementDeleteSnapshot>(selectSql, new { Id = id }, tx);

            await conn.ExecuteAsync(auditSql, new
            {
                UserId = operatorId,
                Action = (byte)AuditAction.Delete,
                TargetType = (byte)AuditTargetType.Announcements,
                TargetId = id,
                OldValue = AuditLogJsonHelper.Serialize(new
                {
                    title = snapshot?.Title,
                    category = snapshot?.Category,
                    isPinned = snapshot?.IsPinned,
                    projectId = snapshot?.ProjectId,
                    targetDisplayName = snapshot?.Title
                }),
                IpAddress = ClientIpResolver.Resolve(_httpContextAccessor)
            }, tx);

            await conn.ExecuteAsync(deleteSql, new { Id = id }, tx);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>刪除公告前的快照（僅給 audit OldValue 用）。</summary>
    private sealed class AnnouncementDeleteSnapshot
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public byte Category { get; set; }
        public bool IsPinned { get; set; }
        public int? ProjectId { get; set; }
    }
}
