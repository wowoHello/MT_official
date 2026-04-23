using System.Data;
using Dapper;
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

    public AnnouncementService(IDatabaseService db, ILogger<AnnouncementService> logger)
    {
        _db = db;
        _logger = logger;
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
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue)
            VALUES (@UserId, @Action, @TargetType, @TargetId, @NewValue);
            """;

        using var conn = _db.CreateConnection();
        conn.Open();
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

            await conn.ExecuteAsync(auditSql, new
            {
                UserId = operatorId,
                Action = (byte)AuditAction.Create,
                TargetType = (byte)AuditTargetType.Announcements,
                TargetId = newId,
                NewValue = model.Title
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
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue)
            VALUES (@UserId, @Action, @TargetType, @TargetId, @NewValue);
            """;

        using var conn = _db.CreateConnection();
        conn.Open();
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
                NewValue = model.Title
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
        const string toggleSql = """
            UPDATE dbo.MT_Announcements
            SET IsPinned = CASE WHEN IsPinned = 1 THEN 0 ELSE 1 END,
                UpdatedAt = SYSDATETIME()
            WHERE Id = @Id;
            """;

        const string auditSql = """
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue)
            VALUES (@UserId, @Action, @TargetType, @TargetId, @NewValue);
            """;

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        try
        {
            await conn.ExecuteAsync(toggleSql, new { Id = id }, tx);

            await conn.ExecuteAsync(auditSql, new
            {
                UserId = operatorId,
                Action = (byte)AuditAction.Update,
                TargetType = (byte)AuditTargetType.Announcements,
                TargetId = id,
                NewValue = "置頂切換"
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

            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue)
            SELECT @UserId, @Action, @TargetType, Id, @NewValue
            FROM @Updated;

            SELECT COUNT(*) FROM @Updated;
            """;

        using var conn = _db.CreateConnection();
        conn.Open();
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
                NewValue = "自動取消過期或草稿公告置頂"
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
        const string deleteSql = """
            DELETE FROM dbo.MT_Announcements WHERE Id = @Id;
            """;

        const string auditSql = """
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue)
            VALUES (@UserId, @Action, @TargetType, @TargetId, @NewValue);
            """;

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        try
        {
            await conn.ExecuteAsync(auditSql, new
            {
                UserId = operatorId,
                Action = (byte)AuditAction.Delete,
                TargetType = (byte)AuditTargetType.Announcements,
                TargetId = id,
                NewValue = "刪除公告"
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
}
