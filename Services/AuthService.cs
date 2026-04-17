using Microsoft.Data.SqlClient;
using Dapper;
using MT.Models;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace MT.Services;

public interface IAuthService
{
    Task<(bool Success, string Message, UserInfo? User)> ValidateLoginAsync(string username, string password, string? userAgent = null);
    Task<List<ModulePermission>> GetUserPermissionsAsync(int roleId);
    Task LogLoginAttemptAsync(int? userId, string username, bool isSuccess, string? failReason, string? ip, string? userAgent);
    Task UpdateLastLoginAsync(int userId);

    /// <summary>暫存登入資料，回傳一次性 key，供 HTTP 端點完成 Cookie 寫入</summary>
    string PrepareSignIn(UserInfo user, bool rememberMe);

    /// <summary>由 HTTP 端點呼叫，使用一次性 key 完成 Cookie 寫入，回傳是否為首次登入以決定導向</summary>
    Task<(bool Success, bool IsFirstLogin)> CompleteSignInAsync(string key, HttpContext httpContext);

    /// <summary>寫入 MT_AuditLogs 稽核紀錄（登入/登出等）</summary>
    Task LogAuditAsync(int userId, AuditAction action, string? ipAddress = null);
}

public class AuthService : IAuthService
{
    private const int FailureWindowMinutes = 30;
    private const int MaxFailedAttempts = 3;
    private const int LockoutMinutes = 15;
    private const string InvalidCredentialMessage = "帳號或密碼錯誤。";
    private const string DisabledAccountMessage = "此帳號已停用，請聯繫管理員。";
    private const string ManualLockedAccountMessage = "此帳號已被鎖定，請聯繫管理員。";

    private readonly IDatabaseService _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // 一次性暫存：Blazor 元件驗證通過後暫存，HTTP 端點取用後立即移除
    private static readonly ConcurrentDictionary<string, (UserInfo User, bool RememberMe, DateTime CreatedAt)> _pendingLogins = new();

    public AuthService(IDatabaseService db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>SHA256 雜湊（UTF-16LE 編碼，與 MSSQL HASHBYTES('SHA2_256', N'...') 一致）</summary>
    public static byte[] ComputePasswordHash(string password)
        => SHA256.HashData(Encoding.Unicode.GetBytes(password));

    private sealed class UserAuthRow
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public byte[] PasswordHash { get; set; } = [];
        public int RoleId { get; set; }
        public int Status { get; set; }
        public bool IsFirstLogin { get; set; }
        public string RoleName { get; set; } = "";
        public DateTime? LockoutUntil { get; set; }
    }

    public async Task<(bool Success, string Message, UserInfo? User)> ValidateLoginAsync(string username, string password, string? userAgent = null)
    {
        using var conn = _db.CreateConnection();

        var normalizedUsername = username.Trim();
        var clientIp = ResolveIpAddress();
        var normalizedUserAgent = ResolveUserAgent(userAgent);
        var now = DateTime.Now;

        var authRow = await conn.QueryFirstOrDefaultAsync<UserAuthRow>(
            @"SELECT u.Id, u.Username, u.DisplayName, u.PasswordHash,
                     u.RoleId, u.Status, u.IsFirstLogin, r.Name AS RoleName,
                     u.LockoutUntil
              FROM dbo.MT_Users u
              INNER JOIN dbo.MT_Roles r ON r.Id = u.RoleId
              WHERE u.Username = @Username",
            new { Username = normalizedUsername });

        if (authRow is null)
        {
            await LogLoginAttemptAsync(null, normalizedUsername, false, InvalidCredentialMessage, clientIp, normalizedUserAgent);
            return (false, InvalidCredentialMessage, null);
        }

        if (authRow.Status == 0)
        {
            await LogLoginAttemptAsync(authRow.Id, authRow.Username, false, DisabledAccountMessage, clientIp, normalizedUserAgent);
            return (false, DisabledAccountMessage, null);
        }

        if (authRow.Status == 2)
        {
            await LogLoginAttemptAsync(authRow.Id, authRow.Username, false, ManualLockedAccountMessage, clientIp, normalizedUserAgent);
            return (false, ManualLockedAccountMessage, null);
        }

        if (authRow.LockoutUntil.HasValue && authRow.LockoutUntil.Value > now)
        {
            var lockoutMessage = BuildTemporaryLockoutMessage(authRow.LockoutUntil.Value);
            await LogLoginAttemptAsync(authRow.Id, authRow.Username, false, lockoutMessage, clientIp, normalizedUserAgent);
            return (false, lockoutMessage, null);
        }

        if (authRow.LockoutUntil.HasValue && authRow.LockoutUntil.Value <= now)
        {
            await ClearTemporaryLockoutAsync(authRow.Id);
        }

        var inputHash = ComputePasswordHash(password);

        if (!inputHash.SequenceEqual(authRow.PasswordHash))
        {
            await LogLoginAttemptAsync(authRow.Id, authRow.Username, false, InvalidCredentialMessage, clientIp, normalizedUserAgent);

            var recentFailedCount = await CountConsecutiveFailedAttemptsAsync(authRow.Id);
            if (recentFailedCount >= MaxFailedAttempts)
            {
                var lockoutUntil = now.AddMinutes(LockoutMinutes);
                await SetTemporaryLockoutAsync(authRow.Id, lockoutUntil);
                return (false, BuildTemporaryLockoutMessage(lockoutUntil), null);
            }

            return (false, BuildRemainingAttemptsMessage(recentFailedCount), null);
        }

        var userInfo = new UserInfo
        {
            Id = authRow.Id,
            Username = authRow.Username,
            DisplayName = authRow.DisplayName,
            RoleId = authRow.RoleId,
            Status = authRow.Status,
            IsFirstLogin = authRow.IsFirstLogin,
            RoleName = authRow.RoleName
        };

        return (true, "登入成功", userInfo);
    }

    public async Task<List<ModulePermission>> GetUserPermissionsAsync(int roleId)
    {
        using var conn = _db.CreateConnection();

        var permissions = await conn.QueryAsync<ModulePermission>(
            @"SELECT m.ModuleKey, m.Name, m.Icon, m.PageUrl,
                     m.Description, m.ColorClass, m.BgColorClass,
                     rp.IsEnabled, rp.AnnouncementPerm
              FROM dbo.MT_RolePermissions rp
              INNER JOIN dbo.MT_Modules m ON m.Id = rp.ModuleId
              WHERE rp.RoleId = @RoleId AND rp.IsEnabled = 1 AND m.IsActive = 1
              ORDER BY m.SortOrder",
            new { RoleId = roleId });

        return permissions.ToList();
    }

    public async Task LogLoginAttemptAsync(int? userId, string username, bool isSuccess, string? failReason, string? ip, string? userAgent)
    {
        using var conn = _db.CreateConnection();

        await conn.ExecuteAsync(
            @"INSERT INTO dbo.MT_LoginLogs (UserId, Username, IsSuccess, IpAddress, UserAgent, FailReason)
              VALUES (@UserId, @Username, @IsSuccess, @IpAddress, @UserAgent, @FailReason)",
            new
            {
                UserId = userId,
                Username = TrimToLength(username, 100) ?? string.Empty,
                IsSuccess = isSuccess,
                IpAddress = TrimToLength(ip ?? ResolveIpAddress(), 50),
                UserAgent = TrimToLength(userAgent ?? ResolveUserAgent(null), 500),
                FailReason = TrimToLength(failReason, 200)
            });
    }

    public async Task UpdateLastLoginAsync(int userId)
    {
        using var conn = _db.CreateConnection();

        await conn.ExecuteAsync(
            "UPDATE dbo.MT_Users SET LastLoginAt = SYSDATETIME() WHERE Id = @UserId",
            new { UserId = userId });
    }

    public string PrepareSignIn(UserInfo user, bool rememberMe)
    {
        // 清理超過 60 秒的過期暫存（防止累積）
        var cutoff = DateTime.UtcNow.AddSeconds(-60);
        foreach (var kvp in _pendingLogins)
        {
            if (kvp.Value.CreatedAt < cutoff)
                _pendingLogins.TryRemove(kvp.Key, out _);
        }

        var key = Guid.NewGuid().ToString("N");
        _pendingLogins[key] = (user, rememberMe, DateTime.UtcNow);
        return key;
    }

    public async Task<(bool Success, bool IsFirstLogin)> CompleteSignInAsync(string key, HttpContext httpContext)
    {
        if (!_pendingLogins.TryRemove(key, out var data))
            return (false, false);

        // 超過 60 秒視為過期
        if ((DateTime.UtcNow - data.CreatedAt).TotalSeconds > 60)
            return (false, false);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, data.User.Id.ToString()),
            new(ClaimTypes.Name, data.User.Username),
            new("DisplayName", data.User.DisplayName),
            new(ClaimTypes.Role, data.User.RoleName),
            new("RoleId", data.User.RoleId.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                // RememberMe=true  → Persistent Cookie，絕對 90 天（關瀏覽器回來仍登入）
                // RememberMe=false → Session Cookie，套用 ExpireTimeSpan（2 小時滑動）+ 關瀏覽器即失效
                IsPersistent = data.RememberMe,
                ExpiresUtc = data.RememberMe ? DateTimeOffset.UtcNow.AddDays(90) : null,
                AllowRefresh = !data.RememberMe
            });

        var clientIp = ResolveIpAddress(httpContext);

        await ClearTemporaryLockoutAsync(data.User.Id);
        await LogLoginAttemptAsync(
            data.User.Id,
            data.User.Username,
            true,
            null,
            clientIp,
            ResolveUserAgent(null, httpContext));
        await UpdateLastLoginAsync(data.User.Id);
        await LogAuditAsync(data.User.Id, AuditAction.Login, clientIp);

        return (true, data.User.IsFirstLogin);
    }

    private async Task<int> CountConsecutiveFailedAttemptsAsync(int userId)
    {
        using var conn = _db.CreateConnection();

        return await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*)
              FROM dbo.MT_LoginLogs
              WHERE UserId = @UserId
                AND IsSuccess = 0
                AND FailReason = @FailReason
                AND CreatedAt >= DATEADD(MINUTE, -@FailureWindowMinutes, SYSDATETIME())
                AND CreatedAt > ISNULL(
                    (
                        SELECT MAX(CreatedAt)
                        FROM dbo.MT_LoginLogs
                        WHERE UserId = @UserId
                          AND IsSuccess = 1
                    ),
                    CONVERT(datetime2, '1900-01-01')
                )",
            new
            {
                UserId = userId,
                FailReason = InvalidCredentialMessage,
                FailureWindowMinutes
            });
    }

    private async Task SetTemporaryLockoutAsync(int userId, DateTime lockoutUntil)
    {
        using var conn = _db.CreateConnection();

        await conn.ExecuteAsync(
            @"UPDATE dbo.MT_Users
              SET LockoutUntil = @LockoutUntil,
                  UpdatedAt = SYSDATETIME()
              WHERE Id = @UserId",
            new { UserId = userId, LockoutUntil = lockoutUntil });
    }

    private async Task ClearTemporaryLockoutAsync(int userId)
    {
        using var conn = _db.CreateConnection();

        await conn.ExecuteAsync(
            @"UPDATE dbo.MT_Users
              SET LockoutUntil = NULL,
                  UpdatedAt = SYSDATETIME()
              WHERE Id = @UserId
                AND LockoutUntil IS NOT NULL",
            new { UserId = userId });
    }

    private static string BuildTemporaryLockoutMessage(DateTime lockoutUntil)
        => $"30 分鐘內登入失敗已達 {MaxFailedAttempts} 次，帳號暫時鎖定至 {lockoutUntil:yyyy/MM/dd HH:mm}。";

    private static string BuildRemainingAttemptsMessage(int recentFailedCount)
    {
        var remainingAttempts = Math.Max(0, MaxFailedAttempts - recentFailedCount);
        var lockoutDurationText = LockoutMinutes % 60 == 0
            ? $"{LockoutMinutes / 60} 小時"
            : $"{LockoutMinutes} 分鐘";

        return $"{InvalidCredentialMessage} 若連續錯誤達到 {MaxFailedAttempts} 次將鎖定 {lockoutDurationText}，還能嘗試 {remainingAttempts} 次。";
    }

    private string? ResolveIpAddress(HttpContext? httpContext = null)
    {
        var context = httpContext ?? _httpContextAccessor.HttpContext;
        if (context is null)
        {
            return null;
        }

        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }

    private string? ResolveUserAgent(string? userAgent, HttpContext? httpContext = null)
    {
        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            return userAgent;
        }

        var context = httpContext ?? _httpContextAccessor.HttpContext;
        return context?.Request.Headers.UserAgent.ToString();
    }

    public async Task LogAuditAsync(int userId, AuditAction action, string? ipAddress = null)
    {
        using var conn = _db.CreateConnection();

        await conn.ExecuteAsync(
            @"INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, IpAddress)
              VALUES (@UserId, @Action, @TargetType, @TargetId, @IpAddress)",
            new
            {
                UserId = userId,
                Action = (byte)action,
                TargetType = (byte)AuditTargetType.Users,
                TargetId = userId,
                IpAddress = TrimToLength(ipAddress, 50)
            });
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
