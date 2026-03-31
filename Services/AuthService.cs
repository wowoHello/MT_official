using Microsoft.Data.SqlClient;
using Dapper;
using MT.Models;
using System.Security.Cryptography;
using System.Text;

namespace MT.Services;

public interface IAuthService
{
    Task<(bool Success, string Message, UserInfo? User)> ValidateLoginAsync(string username, string password);
    Task<List<ModulePermission>> GetUserPermissionsAsync(int roleId);
    Task LogLoginAttemptAsync(int? userId, string username, bool isSuccess, string? failReason, string? ip, string? userAgent);
    Task UpdateLastLoginAsync(int userId);
}

public class AuthService : IAuthService
{
    private readonly IDatabaseService _db;

    public AuthService(IDatabaseService db)
    {
        _db = db;
    }

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

        // SHA256 雜湊比對（MSSQL HASHBYTES('SHA2_256', N'...') 使用 UTF-16LE 編碼）
        var inputHash = SHA256.HashData(Encoding.Unicode.GetBytes(password));

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
}
