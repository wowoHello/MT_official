using Dapper;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using MT.Models;

namespace MT.Services;

// ─── 介面 ───
public interface IUserGuideService
{
    /// <summary>管理端（Announcements）：登入頁槽位 + 每個角色一個槽位（已上傳帶檔案資訊，未上傳為空）。</summary>
    Task<IReadOnlyList<GuideSlotItem>> GetManagementSlotsAsync();

    /// <summary>上傳/替換：限 PDF + 30MB；舊 active 列 IsActive=0 + 刪舊檔 → 存 guid 檔 + INSERT。slotKey="login" 或 "role:{id}"。</summary>
    Task UploadAsync(string slotKey, IBrowserFile file, int operatorUserId);

    /// <summary>刪除：IsActive=0 + 刪物理檔。</summary>
    Task DeleteAsync(string slotKey, int operatorUserId);

    /// <summary>下載端（Home）：依使用者在當前梯次的角色聯集，過濾出可見且已上傳的角色手冊。</summary>
    Task<IReadOnlyList<GuideViewItem>> GetViewableAsync(int userId, int? projectId);

    /// <summary>登入頁專用：取 login 手冊（匿名，回 null 表示尚未上傳）。</summary>
    Task<GuideViewItem?> GetLoginGuideAsync();
}

// ─── 實作 ───
public class UserGuideService : IUserGuideService
{
    private readonly IDatabaseService _db;
    private readonly IMembershipService _membership;
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<UserGuideService> _logger;

    private const long MaxBytes = 30 * 1024 * 1024;   // 30MB
    private const string GuideSubDir = "guides";        // wwwroot/uploads/guides

    public UserGuideService(
        IDatabaseService db,
        IMembershipService membership,
        IWebHostEnvironment env,
        IHttpContextAccessor httpContextAccessor,
        ILogger<UserGuideService> logger)
    {
        _db = db;
        _membership = membership;
        _env = env;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    // SELECT active 列共用 SQL
    private const string SelectActiveSql =
        "SELECT PageKey, FileName, FilePath, FileSize FROM dbo.MT_UserGuideFiles WHERE IsActive = 1 AND PageKey IS NOT NULL;";

    private const string AuditInsertNewSql =
        "INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue, IpAddress) VALUES (@UserId, @Action, @TargetType, @TargetId, @Value, @IpAddress);";
    private const string AuditInsertOldSql =
        "INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, OldValue, IpAddress) VALUES (@UserId, @Action, @TargetType, @TargetId, @Value, @IpAddress);";

    // ─── 管理端：登入頁 + 每個角色一槽位 ───
    public async Task<IReadOnlyList<GuideSlotItem>> GetManagementSlotsAsync()
    {
        var byKey = await GetUploadedMapAsync();
        var roles = await GetAllRolesAsync();

        var list = new List<GuideSlotItem>(roles.Count + 1)
        {
            // 登入頁槽位固定置頂（頁面制、匿名）
            BuildSlot(GuideSlot.LoginKey, GuideSlot.LoginDisplayName, isLogin: true, byKey)
        };

        // 每個角色一個槽位（含自訂角色、含預設教師；管理員不上傳即不顯示）
        foreach (var r in roles)
            list.Add(BuildSlot(GuideSlot.RoleKey(r.Id), r.Name, isLogin: false, byKey));

        return list;
    }

    private GuideSlotItem BuildSlot(string slotKey, string displayName, bool isLogin, Dictionary<string, GuideFileRow> byKey)
    {
        if (byKey.TryGetValue(slotKey, out var row))
        {
            var (display, ticks) = ResolveFileMeta(row.FilePath);
            return new GuideSlotItem
            {
                PageKey = slotKey,
                DisplayName = displayName,
                IsLogin = isLogin,
                IsUploaded = true,
                FileName = row.FileName,
                FileSizeText = FormatSize(row.FileSize),
                UploadedDisplay = display,
                RelativeUrl = $"{row.FilePath}?v={ticks}"
            };
        }
        return new GuideSlotItem
        {
            PageKey = slotKey,
            DisplayName = displayName,
            IsLogin = isLogin,
            IsUploaded = false
        };
    }

    // ─── 上傳 / 替換 ───
    public async Task UploadAsync(string slotKey, IBrowserFile file, int operatorUserId)
    {
        // 驗證槽位 + 取顯示名稱（audit 用）。非法 slotKey 直接擋下。
        var displayName = await ResolveSlotDisplayNameAsync(slotKey);

        var ext = Path.GetExtension(file.Name);
        if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("僅接受 PDF 檔案。");
        if (file.Size > MaxBytes)
            throw new InvalidOperationException("檔案超過 30MB 上限。");

        // 1. 先寫實體檔（guid 命名，避免覆蓋衝突）
        var dir = Path.Combine(_env.WebRootPath, "uploads", GuideSubDir);
        Directory.CreateDirectory(dir);
        var guidName = $"{Guid.NewGuid():N}.pdf";
        var physical = Path.Combine(dir, guidName);
        var relative = $"uploads/{GuideSubDir}/{guidName}";

        await using (var input = file.OpenReadStream(MaxBytes))
        await using (var output = new FileStream(physical, FileMode.Create))
        {
            await input.CopyToAsync(output);
        }

        // 2. DB：舊列 IsActive=0 → INSERT 新列 → AuditLog（單一 transaction）
        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var oldPaths = (await conn.QueryAsync<string>(
                "SELECT FilePath FROM dbo.MT_UserGuideFiles WHERE PageKey = @PageKey AND IsActive = 1;",
                new { PageKey = slotKey }, tx)).ToList();

            await conn.ExecuteAsync(
                "UPDATE dbo.MT_UserGuideFiles SET IsActive = 0 WHERE PageKey = @PageKey AND IsActive = 1;",
                new { PageKey = slotKey }, tx);

            var newId = await conn.QuerySingleAsync<int>("""
                INSERT INTO dbo.MT_UserGuideFiles (FileName, FilePath, FileSize, UploadedBy, IsActive, PageKey)
                OUTPUT INSERTED.Id
                VALUES (@FileName, @FilePath, @FileSize, @UploadedBy, 1, @PageKey);
                """,
                new { FileName = file.Name, FilePath = relative, FileSize = file.Size, UploadedBy = operatorUserId, PageKey = slotKey }, tx);

            await conn.ExecuteAsync(AuditInsertNewSql, new
            {
                UserId = operatorUserId,
                Action = (byte)AuditAction.Create,
                TargetType = (byte)AuditTargetType.Announcements,   // 手冊就在系統公告/使用說明頁管理
                TargetId = newId,
                Value = AuditLogJsonHelper.Serialize(new
                {
                    slotKey,
                    fileName = file.Name,
                    targetDisplayName = GuideSlot.GuideTitle(displayName)
                }),
                IpAddress = ClientIpResolver.Resolve(_httpContextAccessor)
            }, tx);

            tx.Commit();

            // 取代成功後才刪舊實體檔（best-effort）
            DeletePhysicalFiles(oldPaths);
        }
        catch
        {
            tx.Rollback();
            TryDeletePhysical(physical);   // 新檔回收，避免孤兒
            throw;
        }
    }

    // ─── 刪除 ───
    public async Task DeleteAsync(string slotKey, int operatorUserId)
    {
        var displayName = await ResolveSlotDisplayNameAsync(slotKey);

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var rows = (await conn.QueryAsync<(int Id, string FilePath)>(
                "SELECT Id, FilePath FROM dbo.MT_UserGuideFiles WHERE PageKey = @PageKey AND IsActive = 1;",
                new { PageKey = slotKey }, tx)).ToList();

            if (rows.Count == 0)
            {
                tx.Commit();
                return;
            }

            await conn.ExecuteAsync(
                "UPDATE dbo.MT_UserGuideFiles SET IsActive = 0 WHERE PageKey = @PageKey AND IsActive = 1;",
                new { PageKey = slotKey }, tx);

            await conn.ExecuteAsync(AuditInsertOldSql, new
            {
                UserId = operatorUserId,
                Action = (byte)AuditAction.Delete,
                TargetType = (byte)AuditTargetType.Announcements,
                TargetId = rows[0].Id,
                Value = AuditLogJsonHelper.Serialize(new
                {
                    slotKey,
                    targetDisplayName = GuideSlot.GuideTitle(displayName)
                }),
                IpAddress = ClientIpResolver.Resolve(_httpContextAccessor)
            }, tx);

            tx.Commit();

            DeletePhysicalFiles(rows.Select(r => r.FilePath));
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ─── 下載端（Home）：依當前梯次角色聯集過濾 ───
    public async Task<IReadOnlyList<GuideViewItem>> GetViewableAsync(int userId, int? projectId)
    {
        // 全站角色 ∪ 該梯次內角色（projectId 為 null 時不過濾梯次）
        var roleIds = await _membership.GetEffectiveRoleIdsAsync(userId, projectId);
        if (roleIds.Count == 0) return [];

        var uploaded = await GetUploadedMapAsync();
        var roleNames = await GetRoleNamesAsync(roleIds);

        var list = new List<GuideViewItem>();
        foreach (var roleId in roleIds)
        {
            var slotKey = GuideSlot.RoleKey(roleId);
            if (!uploaded.TryGetValue(slotKey, out var row)) continue;   // 該角色未上傳手冊
            if (!roleNames.TryGetValue(roleId, out var name)) continue;  // 角色已刪除等 → 跳過

            list.Add(new GuideViewItem
            {
                PageKey = slotKey,
                DisplayTitle = GuideSlot.GuideTitle(name),
                RelativeUrl = $"{row.FilePath}?v={ResolveTicks(row.FilePath)}"
            });
        }
        return list;
    }

    // ─── 登入頁專用 ───
    public async Task<GuideViewItem?> GetLoginGuideAsync()
    {
        var uploaded = await GetUploadedMapAsync();
        if (!uploaded.TryGetValue(GuideSlot.LoginKey, out var row)) return null;

        return new GuideViewItem
        {
            PageKey = GuideSlot.LoginKey,
            DisplayTitle = GuideSlot.GuideTitle(GuideSlot.LoginDisplayName),
            RelativeUrl = $"{row.FilePath}?v={ResolveTicks(row.FilePath)}"
        };
    }

    // ─── 私有輔助 ───

    /// <summary>驗證槽位鍵並回顯示名稱（登入頁 / 角色名）；非法槽位拋例外。</summary>
    private async Task<string> ResolveSlotDisplayNameAsync(string slotKey)
    {
        if (slotKey == GuideSlot.LoginKey) return GuideSlot.LoginDisplayName;

        if (GuideSlot.TryParseRoleKey(slotKey, out var roleId))
        {
            using var conn = _db.CreateConnection();
            var name = await conn.QuerySingleOrDefaultAsync<string>(
                "SELECT Name FROM dbo.MT_Roles WHERE Id = @Id;", new { Id = roleId });
            if (!string.IsNullOrEmpty(name)) return name;
        }

        throw new ArgumentException($"未知的手冊槽位：{slotKey}", nameof(slotKey));
    }

    private async Task<List<RoleRow>> GetAllRolesAsync()
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<RoleRow>("SELECT Id, Name FROM dbo.MT_Roles ORDER BY Id;");
        return rows.AsList();
    }

    private async Task<Dictionary<int, string>> GetRoleNamesAsync(IReadOnlyCollection<int> roleIds)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<RoleRow>(
            "SELECT Id, Name FROM dbo.MT_Roles WHERE Id IN @Ids;", new { Ids = roleIds });
        return rows.ToDictionary(r => r.Id, r => r.Name);
    }

    private async Task<Dictionary<string, GuideFileRow>> GetUploadedMapAsync()
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<GuideFileRow>(SelectActiveSql);
        return rows.Where(r => !string.IsNullOrEmpty(r.PageKey))
                   .GroupBy(r => r.PageKey!)
                   .ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>取實體檔的「上傳日顯示」與快取破解 ticks（用 LastWriteTime，免 DB 加日期欄）。</summary>
    private (string? Display, long Ticks) ResolveFileMeta(string relativePath)
    {
        var physical = ToPhysical(relativePath);
        if (!File.Exists(physical)) return (null, 0);
        var info = new FileInfo(physical);
        return (info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"), info.LastWriteTimeUtc.Ticks);
    }

    private long ResolveTicks(string relativePath)
    {
        var physical = ToPhysical(relativePath);
        return File.Exists(physical) ? new FileInfo(physical).LastWriteTimeUtc.Ticks : 0;
    }

    private string ToPhysical(string relativePath) =>
        Path.Combine(_env.WebRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private void DeletePhysicalFiles(IEnumerable<string> relativePaths)
    {
        foreach (var p in relativePaths)
            TryDeletePhysical(ToPhysical(p));
    }

    private void TryDeletePhysical(string physical)
    {
        try
        {
            if (File.Exists(physical)) File.Delete(physical);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "刪除手冊實體檔失敗：{Path}", physical);
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024        => $"{bytes / 1024d:0} KB",
        _              => $"{bytes} B"
    };

    // Dapper 投影列
    private sealed class GuideFileRow
    {
        public string? PageKey { get; set; }
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public long FileSize { get; set; }
    }

    private sealed class RoleRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
