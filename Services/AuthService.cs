using Microsoft.Data.SqlClient;
using Dapper;
using MT.Models;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MT.Services;

public interface IAuthService
{
    Task<(bool Success, string Message, UserInfo? User)> ValidateLoginAsync(string username, string password);
    Task<List<ModulePermission>> GetUserPermissionsAsync(int roleId);
    Task LogLoginAttemptAsync(int? userId, string username, bool isSuccess, string? failReason, string? ip, string? userAgent);
    Task UpdateLastLoginAsync(int userId);

    /// <summary>暫存登入資料，回傳一次性 key，供 HTTP 端點完成 Cookie 寫入</summary>
    string PrepareSignIn(UserInfo user, bool rememberMe);

    /// <summary>由 HTTP 端點呼叫，使用一次性 key 完成 Cookie 寫入</summary>
    Task<bool> CompleteSignInAsync(string key, HttpContext httpContext);
}

public class AuthService : IAuthService
{
    private readonly IDatabaseService _db;

    // 一次性暫存：Blazor 元件驗證通過後暫存，HTTP 端點取用後立即移除
    private static readonly ConcurrentDictionary<string, (UserInfo User, bool RememberMe, DateTime CreatedAt)> _pendingLogins = new();

    public AuthService(IDatabaseService db)
    {
        _db = db;
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
    }

    public async Task<(bool Success, string Message, UserInfo? User)> ValidateLoginAsync(string username, string password)
    {
        using var conn = _db.CreateConnection();

        var authRow = await conn.QueryFirstOrDefaultAsync<UserAuthRow>(
            @"SELECT u.Id, u.Username, u.DisplayName, u.PasswordHash,
                     u.RoleId, u.Status, u.IsFirstLogin, r.Name AS RoleName
              FROM dbo.MT_Users u
              INNER JOIN dbo.MT_Roles r ON r.Id = u.RoleId
              WHERE u.Username = @Username",
            new { Username = username });

        if (authRow is null)
            return (false, "帳號或密碼錯誤。", null);

        if (authRow.Status == 0)
            return (false, "此帳號已停用，請聯繫管理員。", null);

        if (authRow.Status == 2)
            return (false, "此帳號已被鎖定，請聯繫管理員。", null);

        var inputHash = ComputePasswordHash(password);

        if (!inputHash.SequenceEqual(authRow.PasswordHash))
            return (false, "帳號或密碼錯誤。", null);

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
            new { UserId = userId, Username = username, IsSuccess = isSuccess, IpAddress = ip, UserAgent = userAgent, FailReason = failReason });
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

    public async Task<bool> CompleteSignInAsync(string key, HttpContext httpContext)
    {
        if (!_pendingLogins.TryRemove(key, out var data))
            return false;

        // 超過 60 秒視為過期
        if ((DateTime.UtcNow - data.CreatedAt).TotalSeconds > 60)
            return false;

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
                IsPersistent = data.RememberMe,
                ExpiresUtc = data.RememberMe ? DateTimeOffset.UtcNow.AddDays(90) : null
            });

        return true;
    }
}
