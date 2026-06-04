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
    Task LogLoginAttemptAsync(int? userId, string username, bool isSuccess, string? failReason, string? ip, string? userAgent);
    Task UpdateLastLoginAsync(int userId);

    /// <summary>登出時寫入 MT_LoginLogs（EventType=2）。失敗不阻擋登出流程，由 caller 包 try/catch。</summary>
    Task LogLogoutAsync(int userId, string? ip);

    /// <summary>暫存登入資料，回傳一次性 key，供 HTTP 端點完成 Cookie 寫入</summary>
    string PrepareSignIn(UserInfo user);

    /// <summary>由 HTTP 端點呼叫，使用一次性 key 完成 Cookie 寫入，回傳是否為首次登入以決定導向</summary>
    Task<(bool Success, bool IsFirstLogin)> CompleteSignInAsync(string key, HttpContext httpContext);
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
    private static readonly ConcurrentDictionary<string, (UserInfo User, DateTime CreatedAt)> _pendingLogins = new();

    public AuthService(IDatabaseService db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    // === 密碼雜湊 ===========================================================
    // 新格式：PBKDF2-SHA256 + 16 byte salt + 100,000 iterations
    //   "PBKDF2.v1$<iter>$<salt-base64>$<hash-base64>"
    // 舊格式（向後相容）：純 Base64 編碼的 32 byte SHA256(UTF-16LE)
    //   驗證成功會自動升級到新格式（見 ValidateLoginAsync）

    private const string Pbkdf2Prefix = "PBKDF2.v1$";
    private const int Pbkdf2Iterations = 100_000;
    private const int Pbkdf2SaltSize = 16;
    private const int Pbkdf2HashSize = 32;

    /// <summary>產生新密碼雜湊（PBKDF2-SHA256，含密碼學安全 salt）。回傳字串長度約 90 字元。</summary>
    public static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(Pbkdf2SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, Pbkdf2HashSize);
        return $"{Pbkdf2Prefix}{Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// 驗證密碼。同時支援新（PBKDF2）與舊（裸 SHA256 Base64）格式。
    /// </summary>
    /// <returns>
    /// IsValid：密碼是否正確；
    /// NeedsUpgrade：true 表示驗證走的是舊格式，呼叫端應在驗證成功後重 hash 寫回 DB。
    /// </returns>
    public static (bool IsValid, bool NeedsUpgrade) VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
            return (false, false);

        if (storedHash.StartsWith(Pbkdf2Prefix, StringComparison.Ordinal))
            return (VerifyPbkdf2(password, storedHash), false);

        // 舊格式：純 Base64 編碼的 32 byte SHA256
        return (VerifyLegacySha256(password, storedHash), true);
    }

    private static bool VerifyPbkdf2(string password, string storedHash)
    {
        // 格式：PBKDF2.v1$<iter>$<salt-base64>$<hash-base64>
        var parts = storedHash.Split('$');
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[1], out int iterations) || iterations <= 0) return false;

        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expectedHash = Convert.FromBase64String(parts[3]);
            byte[] computedHash = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool VerifyLegacySha256(string password, string storedBase64)
    {
        try
        {
            byte[] storedHash = Convert.FromBase64String(storedBase64);
            if (storedHash.Length != 32) return false;
            byte[] computedHash = SHA256.HashData(Encoding.Unicode.GetBytes(password));
            return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class UserAuthRow
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string PasswordHash { get; set; } = "";
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

        // 支援以「帳號」或「信箱」登入（兩者在 MT_Users 都有 UNIQUE 過濾索引）
        var authRow = await conn.QueryFirstOrDefaultAsync<UserAuthRow>(
            @"SELECT u.Id, u.Username, u.DisplayName, u.PasswordHash,
                     u.RoleId, u.Status, u.IsFirstLogin, r.Name AS RoleName,
                     u.LockoutUntil
              FROM dbo.MT_Users u
              INNER JOIN dbo.MT_Roles r ON r.Id = u.RoleId
              WHERE u.Username = @Input OR u.Email = @Input",
            new { Input = normalizedUsername });

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

        var (isValid, needsUpgrade) = VerifyPassword(password, authRow.PasswordHash);

        if (!isValid)
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

        // 驗證成功且偵測到舊格式 hash → 自動升級為 PBKDF2 寫回 DB
        // 失敗不阻擋登入流程（最差就是下次登入再試一次升級）
        if (needsUpgrade)
        {
            try
            {
                await UpgradePasswordHashAsync(authRow.Id, password);
            }
            catch
            {
                // 升級失敗不影響登入；下次登入會再嘗試
            }
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

    /// <summary>把舊格式（裸 SHA256 Base64）的 hash 升級為 PBKDF2 寫回 DB。</summary>
    private async Task UpgradePasswordHashAsync(int userId, string plainPassword)
    {
        using var conn = _db.CreateConnection();
        var newHash = HashPassword(plainPassword);
        await conn.ExecuteAsync(
            @"UPDATE dbo.MT_Users
              SET PasswordHash = @PasswordHash,
                  UpdatedAt = SYSDATETIME()
              WHERE Id = @UserId",
            new { UserId = userId, PasswordHash = newHash });
    }

    public async Task LogLoginAttemptAsync(int? userId, string username, bool isSuccess, string? failReason, string? ip, string? userAgent)
    {
        using var conn = _db.CreateConnection();

        // EventType 顯式指定 1（Login）以對齊 MT_LoginLogs 的設計，雖然 DB 有 DEFAULT 1
        await conn.ExecuteAsync(
            @"INSERT INTO dbo.MT_LoginLogs (UserId, Username, EventType, IsSuccess, IpAddress, UserAgent, FailReason)
              VALUES (@UserId, @Username, 1, @IsSuccess, @IpAddress, @UserAgent, @FailReason)",
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

    public async Task LogLogoutAsync(int userId, string? ip)
    {
        using var conn = _db.CreateConnection();

        // 登出寫入 MT_LoginLogs（EventType=2, IsSuccess=1）
        // Username 由 MT_Users 反查（登出時 context 仍有效），UserAgent 由 HttpContext 取得
        await conn.ExecuteAsync(
            @"INSERT INTO dbo.MT_LoginLogs (UserId, Username, EventType, IsSuccess, IpAddress, UserAgent)
              SELECT @UserId, u.Username, 2, 1, @IpAddress, @UserAgent
              FROM dbo.MT_Users u
              WHERE u.Id = @UserId",
            new
            {
                UserId = userId,
                IpAddress = TrimToLength(ip ?? ResolveIpAddress(), 50),
                UserAgent = TrimToLength(ResolveUserAgent(null), 500)
            });
    }

    public async Task UpdateLastLoginAsync(int userId)
    {
        using var conn = _db.CreateConnection();

        await conn.ExecuteAsync(
            "UPDATE dbo.MT_Users SET LastLoginAt = SYSDATETIME() WHERE Id = @UserId",
            new { UserId = userId });
    }

    public string PrepareSignIn(UserInfo user)
    {
        // 清理超過 60 秒的過期暫存（防止累積）
        var cutoff = DateTime.UtcNow.AddSeconds(-60);
        foreach (var kvp in _pendingLogins)
        {
            if (kvp.Value.CreatedAt < cutoff)
                _pendingLogins.TryRemove(kvp.Key, out _);
        }

        var key = Guid.NewGuid().ToString("N");
        _pendingLogins[key] = (user, DateTime.UtcNow);
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
            new("RoleId", data.User.RoleId.ToString()),
            // 給 Login.razor 用：偵測首次登入按上一頁繞回首頁，強制導回改密碼頁
            new("IsFirstLogin", data.User.IsFirstLogin ? "true" : "false")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                // 一律 Session Cookie：關瀏覽器即失效 + 套用 ExpireTimeSpan（24 小時滑動安全網，見 Program.cs）
                IsPersistent = false,
                ExpiresUtc = null,
                AllowRefresh = true
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
        // Login 事件已由上方 LogLoginAttemptAsync 寫入 MT_LoginLogs，不再重複寫 MT_AuditLogs

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

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
