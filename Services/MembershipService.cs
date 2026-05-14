using System.Collections.Concurrent;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using MT.Models;

namespace MT.Services;

/// <summary>
/// 集中管理「使用者 effective 角色 + 模組權限」的查詢，30 秒短 TTL Cache。
///
/// 服務的場景：MainLayout / Home / Announcements 編輯 / 任何需要快速判斷「user 在當前梯次能不能做 X」的呼叫點。
///
/// Cache 失效策略：
///   1. 30 秒 TTL 自然過期
///   2. RoleService 改完角色 / 權限 / 帳號 → 主動呼叫 InvalidateUser(userId) 或 InvalidateAll()
///   3. 客戶端會收 SignalR ReceiveRoleChanged 廣播，重整後 query 帶新權限
///
/// 生命週期：Scoped（注入 Scoped 的 IDatabaseService）
/// Cache backing store：IMemoryCache（Singleton，跨 user 跨 request 共享）
/// </summary>
public interface IMembershipService
{
    /// <summary>
    /// 取得使用者「系統角色 + 梯次內角色」union 後的 RoleId 集合。
    /// projectId 為 null 時不過濾梯次（回所有系統角色 + 全梯次內角色）。
    /// </summary>
    Task<IReadOnlyCollection<int>> GetEffectiveRoleIdsAsync(int userId, int? projectId);

    /// <summary>
    /// 取得使用者的 8 個模組權限卡片（含 IsEnabled flag）。
    /// </summary>
    Task<List<UserModuleCard>> GetUserModuleCardsAsync(int userId, int? projectId);

    /// <summary>
    /// 判斷使用者在指定 moduleKey 是否有啟用權限。
    /// 內部走 GetUserModuleCardsAsync 共享 cache，比另開 EXISTS SQL 更省。
    /// </summary>
    Task<bool> HasModulePermissionAsync(int userId, int? projectId, string moduleKey);

    /// <summary>清除單一使用者所有 cache（角色/權限/帳號變動時呼叫）。</summary>
    void InvalidateUser(int userId);

    /// <summary>清除所有使用者 cache（角色定義變動時呼叫，如 UpdateRole / DeleteRole）。</summary>
    void InvalidateAll();
}

public sealed class MembershipService : IMembershipService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IDatabaseService _db;
    private readonly IMemoryCache _cache;

    // 每個 user 一個 CTS：InvalidateUser cancel 該 token，cache 內所有掛此 token 的 entry 立即失效
    private static readonly ConcurrentDictionary<int, CancellationTokenSource> _userTokens = new();

    // 全域 CTS：InvalidateAll 替換為新 CTS 並 cancel 舊的，所有 cache entry 一次清空
    private static CancellationTokenSource _globalCts = new();

    public MembershipService(IDatabaseService db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyCollection<int>> GetEffectiveRoleIdsAsync(int userId, int? projectId)
    {
        var key = $"mem:roles:{userId}:{projectId ?? 0}";
        return (await _cache.GetOrCreateAsync(key, async entry =>
        {
            ConfigureExpiration(entry, userId);

            using var conn = _db.CreateConnection();
            const string sql = """
                SELECT u.RoleId FROM dbo.MT_Users u WHERE u.Id = @UserId
                UNION
                SELECT pmr.RoleId
                FROM dbo.MT_ProjectMembers pm
                INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
                WHERE pm.UserId = @UserId
                  AND (@ProjectId IS NULL OR pm.ProjectId = @ProjectId);
                """;
            var rows = await conn.QueryAsync<int>(sql, new { UserId = userId, ProjectId = projectId });
            return (IReadOnlyCollection<int>)rows.ToArray();
        }))!;
    }

    public async Task<List<UserModuleCard>> GetUserModuleCardsAsync(int userId, int? projectId)
    {
        var key = $"mem:cards:{userId}:{projectId ?? 0}";
        return (await _cache.GetOrCreateAsync(key, async entry =>
        {
            ConfigureExpiration(entry, userId);

            using var conn = _db.CreateConnection();
            const string sql = """
                SELECT
                    m.Id, m.ModuleKey, m.Name, m.Icon, m.PageUrl,
                    m.Description, m.ColorClass, m.BgColorClass, m.SortOrder,
                    -- 只要任一角色（系統角色 + 梯次內角色）有啟用該模組即視為有權限
                    CASE WHEN EXISTS (
                        SELECT 1
                        FROM dbo.MT_RolePermissions rp
                        WHERE rp.ModuleId = m.Id AND rp.IsEnabled = 1
                          AND rp.RoleId IN (
                              SELECT u.RoleId FROM dbo.MT_Users u WHERE u.Id = @UserId
                              UNION
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
        }))!;
    }

    public async Task<bool> HasModulePermissionAsync(int userId, int? projectId, string moduleKey)
    {
        var cards = await GetUserModuleCardsAsync(userId, projectId);
        return cards.Any(c => c.ModuleKey == moduleKey && c.IsEnabled);
    }

    public void InvalidateUser(int userId)
    {
        if (_userTokens.TryRemove(userId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public void InvalidateAll()
    {
        var old = Interlocked.Exchange(ref _globalCts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }

    // ─── 私有：設定 cache entry 的 30 秒 TTL + 失效 token ──────────────
    private static void ConfigureExpiration(ICacheEntry entry, int userId)
    {
        entry.AbsoluteExpirationRelativeToNow = CacheTtl;
        entry.AddExpirationToken(new CancellationChangeToken(GetUserCts(userId).Token));
        entry.AddExpirationToken(new CancellationChangeToken(_globalCts.Token));
    }

    // ─── 私有：取得 user 對應的 CTS，若已 cancel 過則替換新的 ──────────
    private static CancellationTokenSource GetUserCts(int userId)
    {
        while (true)
        {
            var cts = _userTokens.GetOrAdd(userId, _ => new CancellationTokenSource());
            if (!cts.IsCancellationRequested) return cts;

            var newCts = new CancellationTokenSource();
            if (_userTokens.TryUpdate(userId, newCts, cts))
            {
                cts.Dispose();
                return newCts;
            }
            newCts.Dispose();
        }
    }
}
