using System.Data;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Http;
using MT.Models;

namespace MT.Services;

// ─── 介面 ───
public interface IAnnouncementService
{
    Task<List<AnnouncementListItem>> GetAnnouncementListAsync();
    Task<AnnouncementEditDto?> GetAnnouncementEditAsync(int id);
    /// <summary>梯次下拉：依 LifecycleStatus 分三組（進行中 / 準備中 / 已結案）。</summary>
    Task<ProjectGroupedDropdown> GetProjectDropdownAsync();
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
    private readonly IMembershipService _membership;

    public AnnouncementService(
        IDatabaseService db,
        ILogger<AnnouncementService> logger,
        IHttpContextAccessor httpContextAccessor,
        IMembershipService membership)
    {
        _db = db;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _membership = membership;
    }

    // ─── 權限門鎖：寫入動作前必過 ───
    // 任一系統角色或梯次角色於 MT_RolePermissions 對 ModuleKey='announcements' 啟用即放行
    // 走 IMembershipService 30 秒短 TTL Cache，連續編輯多筆公告時免重複查 DB
    private async Task EnsureCanEditAsync(int operatorId)
    {
        if (operatorId <= 0)
            throw new UnauthorizedAccessException("尚未登入或登入逾期，請重新登入。");

        var canEdit = await _membership.HasModulePermissionAsync(operatorId, null, "announcements");
        if (!canEdit)
            throw new UnauthorizedAccessException("您沒有公告管理權限。");
    }

    // ─── 列表查詢 ───
    // STUFF + FOR XML 把 junction 表的 ProjectIds 與 Names 聚合成 CSV，C# 端 split 回 List
    // 全站廣播（junction 0 列）→ CSV NULL → 前端 ProjectIds 取得空 list
    public async Task<List<AnnouncementListItem>> GetAnnouncementListAsync()
    {
        const string sql = """
            SELECT
                a.Id, a.Category, a.Status,
                a.PublishDate, a.UnpublishDate, a.IsPinned,
                a.Title, a.Content, a.CreatedAt,
                u.DisplayName AS AuthorName,
                STUFF((
                    SELECT N',' + CAST(ap.ProjectId AS NVARCHAR(20))
                    FROM dbo.MT_AnnouncementProjects ap
                    WHERE ap.AnnouncementId = a.Id
                    ORDER BY ap.Id
                    FOR XML PATH(''), TYPE
                ).value('.', 'NVARCHAR(MAX)'), 1, 1, '') AS ProjectIdsCsv,
                STUFF((
                    SELECT N',' + ISNULL(p.Name, N'（已刪除梯次）')
                    FROM dbo.MT_AnnouncementProjects ap
                    LEFT JOIN dbo.MT_Projects p ON p.Id = ap.ProjectId
                    WHERE ap.AnnouncementId = a.Id
                    ORDER BY ap.Id
                    FOR XML PATH(''), TYPE
                ).value('.', 'NVARCHAR(MAX)'), 1, 1, '') AS ProjectNamesCsv
            FROM dbo.MT_Announcements a
            INNER JOIN dbo.MT_Users u ON a.AuthorId = u.Id
            ORDER BY a.IsPinned DESC, a.PublishDate DESC;
            """;

        using var conn = _db.CreateConnection();
        var result = await conn.QueryAsync<AnnouncementListItem>(sql);
        return result.ToList();
    }

    // ─── 單筆編輯載入 ───
    // QueryMultiple 兩段：① 主檔欄位（無 ProjectId）② junction 表的 ProjectIds
    public async Task<AnnouncementEditDto?> GetAnnouncementEditAsync(int id)
    {
        const string sql = """
            SELECT Id, Category, Status, PublishDate, UnpublishDate,
                   IsPinned, Title, Content
            FROM dbo.MT_Announcements
            WHERE Id = @Id;

            SELECT ProjectId
            FROM dbo.MT_AnnouncementProjects
            WHERE AnnouncementId = @Id
            ORDER BY Id;
            """;

        using var conn = _db.CreateConnection();
        using var multi = await conn.QueryMultipleAsync(sql, new { Id = id });

        var dto = await multi.ReadFirstOrDefaultAsync<AnnouncementEditDto>();
        if (dto is null) return null;

        dto.ProjectIds = (await multi.ReadAsync<int>()).ToList();
        return dto;
    }

    // ─── 梯次下拉選項（分組 + 元資料） ───
    // LifecycleStatus 邏輯：
    //   ClosedAt 非 NULL → 3 已結案
    //   命題階段 StartDate <= 今天 → 2 進行中
    //   否則 → 1 準備中
    public async Task<ProjectGroupedDropdown> GetProjectDropdownAsync()
    {
        const string sql = """
            SELECT
                p.Id, p.Name,
                ISNULL(p.ProjectType, 0) AS ProjectType,
                p.Year,
                CAST(CASE
                    WHEN p.ClosedAt IS NOT NULL THEN 3
                    WHEN ISNULL(comp.StartDate, p.StartDate) <= CAST(SYSDATETIME() AS DATE) THEN 2
                    ELSE 1
                END AS TINYINT) AS LifecycleStatus
            FROM dbo.MT_Projects p
            OUTER APPLY (
                SELECT TOP 1 StartDate FROM dbo.MT_ProjectPhases
                WHERE ProjectId = p.Id AND PhaseCode = 2
            ) comp
            WHERE p.IsDeleted = 0
            ORDER BY LifecycleStatus, p.Year DESC, p.Name;
            """;

        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<ProjectDropdownItem>(sql)).ToList();

        return new ProjectGroupedDropdown
        {
            Active    = rows.Where(r => r.LifecycleStatus == (byte)ProjectLifecycleGroup.Active).ToList(),
            Preparing = rows.Where(r => r.LifecycleStatus == (byte)ProjectLifecycleGroup.Preparing).ToList(),
            Closed    = rows.Where(r => r.LifecycleStatus == (byte)ProjectLifecycleGroup.Closed).ToList()
        };
    }

    // ─── 新增公告 ───
    // 主檔 INSERT 不帶 ProjectId（DB 欄位保留但停用，新資料一律經 junction）
    public async Task<int> CreateAsync(AnnouncementFormModel model, int operatorId)
    {
        const string insertSql = """
            INSERT INTO dbo.MT_Announcements
                (Category, Status, PublishDate, UnpublishDate, IsPinned, Title, Content, AuthorId)
            OUTPUT INSERTED.Id
            VALUES
                (@Category, @Status, @PublishDate, @UnpublishDate, @IsPinned, @Title, @Content, @AuthorId);
            """;

        const string auditSql = """
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue, IpAddress)
            VALUES (@UserId, @Action, @TargetType, @TargetId, @NewValue, @IpAddress);
            """;

        using var conn = _db.CreateConnection();
        conn.Open();

        await EnsureCanEditAsync(operatorId);

        using var tx = conn.BeginTransaction();

        try
        {
            var newId = await conn.QuerySingleAsync<int>(insertSql, new
            {
                model.Category,
                model.Status,
                model.PublishDate,
                model.UnpublishDate,
                model.IsPinned,
                model.Title,
                model.Content,
                AuthorId = operatorId
            }, tx);

            // junction 表寫入（空 list = 全站廣播，跳過 INSERT）
            await InsertJunctionAsync(conn, tx, newId, model.ProjectIds);

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
                    projectIds = model.ProjectIds,     // 空陣列 = 全站廣播
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
    // 主檔 UPDATE 不帶 ProjectId；junction 先全刪後重建（簡單且原子）
    public async Task UpdateAsync(int id, AnnouncementFormModel model, int operatorId)
    {
        const string updateSql = """
            UPDATE dbo.MT_Announcements
            SET Category = @Category, Status = @Status,
                PublishDate = @PublishDate, UnpublishDate = @UnpublishDate,
                IsPinned = @IsPinned, Title = @Title, Content = @Content,
                UpdatedAt = SYSDATETIME()
            WHERE Id = @Id;
            """;

        const string clearJunctionSql = """
            DELETE FROM dbo.MT_AnnouncementProjects WHERE AnnouncementId = @Id;
            """;

        const string auditSql = """
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue, IpAddress)
            VALUES (@UserId, @Action, @TargetType, @TargetId, @NewValue, @IpAddress);
            """;

        using var conn = _db.CreateConnection();
        conn.Open();

        await EnsureCanEditAsync(operatorId);

        using var tx = conn.BeginTransaction();

        try
        {
            await conn.ExecuteAsync(updateSql, new
            {
                Id = id,
                model.Category,
                model.Status,
                model.PublishDate,
                model.UnpublishDate,
                model.IsPinned,
                model.Title,
                model.Content
            }, tx);

            // junction 重建：先清再寫
            await conn.ExecuteAsync(clearJunctionSql, new { Id = id }, tx);
            await InsertJunctionAsync(conn, tx, id, model.ProjectIds);

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
                    projectIds = model.ProjectIds,
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

    // ─── 切換置頂 ───
    public async Task TogglePinAsync(int id, int operatorId)
    {
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

        await EnsureCanEditAsync(operatorId);

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

        await EnsureCanEditAsync(operatorId);

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

    // ─── 首頁公告查詢（教師可見性 = 全站廣播 OR 公告綁定包含當前梯次） ───
    // 全站廣播判定：junction 0 列（NOT EXISTS）
    // 指定梯次判定：junction 含 @ProjectId 列
    public async Task<List<AnnouncementListItem>> GetHomeAnnouncementsAsync(int? projectId)
    {
        const string sql = """
            SELECT
                a.Id, a.Category, a.Status,
                a.PublishDate, a.UnpublishDate, a.IsPinned,
                a.Title, a.Content, a.CreatedAt,
                u.DisplayName AS AuthorName,
                STUFF((
                    SELECT N',' + CAST(ap.ProjectId AS NVARCHAR(20))
                    FROM dbo.MT_AnnouncementProjects ap
                    WHERE ap.AnnouncementId = a.Id
                    ORDER BY ap.Id
                    FOR XML PATH(''), TYPE
                ).value('.', 'NVARCHAR(MAX)'), 1, 1, '') AS ProjectIdsCsv,
                STUFF((
                    SELECT N',' + ISNULL(p.Name, N'（已刪除梯次）')
                    FROM dbo.MT_AnnouncementProjects ap
                    LEFT JOIN dbo.MT_Projects p ON p.Id = ap.ProjectId
                    WHERE ap.AnnouncementId = a.Id
                    ORDER BY ap.Id
                    FOR XML PATH(''), TYPE
                ).value('.', 'NVARCHAR(MAX)'), 1, 1, '') AS ProjectNamesCsv
            FROM dbo.MT_Announcements a
            INNER JOIN dbo.MT_Users u ON a.AuthorId = u.Id
            WHERE a.Status = 1
              AND a.PublishDate <= SYSDATETIME()
              AND (a.UnpublishDate IS NULL OR a.UnpublishDate >= SYSDATETIME())
              AND (
                  -- 全站廣播：junction 0 列
                  NOT EXISTS (SELECT 1 FROM dbo.MT_AnnouncementProjects
                              WHERE AnnouncementId = a.Id)
                  OR
                  -- 公告綁定包含當前梯次
                  EXISTS (SELECT 1 FROM dbo.MT_AnnouncementProjects
                          WHERE AnnouncementId = a.Id AND ProjectId = @ProjectId)
              )
            ORDER BY a.IsPinned DESC, a.PublishDate DESC;
            """;

        using var conn = _db.CreateConnection();
        var result = await conn.QueryAsync<AnnouncementListItem>(sql, new { ProjectId = projectId });
        return result.ToList();
    }

    // ─── 刪除公告 ───
    // SELECT old projectIds 給 AuditLog OldValue 用；junction cascade 由 application 端處理
    public async Task DeleteAsync(int id, int operatorId)
    {
        const string selectSql = """
            SELECT Id, Title, Category, IsPinned
            FROM dbo.MT_Announcements WHERE Id = @Id;

            SELECT ProjectId
            FROM dbo.MT_AnnouncementProjects WHERE AnnouncementId = @Id
            ORDER BY Id;
            """;

        const string deleteJunctionSql = """
            DELETE FROM dbo.MT_AnnouncementProjects WHERE AnnouncementId = @Id;
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

        await EnsureCanEditAsync(operatorId);

        using var tx = conn.BeginTransaction();

        try
        {
            // 先取得即將刪除的公告資料與綁定梯次，作為 OldValue 寫入 audit
            using var multi = await conn.QueryMultipleAsync(selectSql, new { Id = id }, tx);
            var snapshot = await multi.ReadFirstOrDefaultAsync<AnnouncementDeleteSnapshot>();
            var oldProjectIds = (await multi.ReadAsync<int>()).ToList();

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
                    projectIds = oldProjectIds,
                    targetDisplayName = snapshot?.Title
                }),
                IpAddress = ClientIpResolver.Resolve(_httpContextAccessor)
            }, tx);

            // junction 先刪，再刪主檔（順序對非 FK schema 不重要，但邏輯清楚）
            await conn.ExecuteAsync(deleteJunctionSql, new { Id = id }, tx);
            await conn.ExecuteAsync(deleteSql, new { Id = id }, tx);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ─── 批次寫入 junction：多列 VALUES INSERT（單一 round-trip） ───
    // 仿 ProjectService.BulkInsert* / RoleService.MergeRolePermissionsAsync pattern
    private static async Task InsertJunctionAsync(
        IDbConnection conn, IDbTransaction tx,
        int announcementId, IReadOnlyList<int> projectIds)
    {
        if (projectIds is null || projectIds.Count == 0) return;

        var sb = new StringBuilder("INSERT INTO dbo.MT_AnnouncementProjects (AnnouncementId, ProjectId) VALUES ");
        var parameters = new DynamicParameters();
        parameters.Add("@AnnouncementId", announcementId);

        for (int i = 0; i < projectIds.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var pName = $"@P{i}";
            sb.Append("(@AnnouncementId, ").Append(pName).Append(')');
            parameters.Add(pName, projectIds[i]);
        }
        sb.Append(';');

        await conn.ExecuteAsync(sb.ToString(), parameters, tx);
    }

    /// <summary>刪除公告前的快照（僅給 audit OldValue 主檔欄位用）。</summary>
    private sealed class AnnouncementDeleteSnapshot
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public byte Category { get; set; }
        public bool IsPinned { get; set; }
    }
}
