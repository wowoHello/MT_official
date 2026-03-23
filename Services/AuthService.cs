using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.AspNetCore.Identity;

namespace MT.Services;

/// <summary>認證結果模型</summary>
public class AuthResult
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string RoleName { get; set; } = "";
    public int RoleCode { get; set; }
    public string CompanyTitle { get; set; } = "";
    public bool IsFirstLogin { get; set; }
}

public interface IAuthService
{
    /// <summary>驗證帳號密碼並回傳使用者資訊</summary>
    Task<(AuthResult? User, string? ErrorMessage)> LoginAsync(string username, string password);
}

public class AuthService : IAuthService
{
    private readonly string _connectionString;
    private readonly PasswordHasher<object> _hasher = new();

    public AuthService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
    }

    public async Task<(AuthResult? User, string? ErrorMessage)> LoginAsync(string username, string password)
    {
        if (string.IsNullOrEmpty(_connectionString))
            return (null, "系統連線設定錯誤，請聯絡管理員。");

        try
        {
            using var conn = new SqlConnection(_connectionString);

            // 查詢使用者 + 角色資訊
            const string sql = @"
                SELECT 
                    u.Id AS UserId,
                    u.Username,
                    u.DisplayName,
                    u.Email,
                    u.PasswordHash,
                    u.Status,
                    u.IsFirstLogin,
                    u.CompanyTitle,
                    r.Name AS RoleName,
                    r.Code AS RoleCode
                FROM dbo.MT_Users u
                INNER JOIN dbo.MT_Roles r ON u.RoleId = r.Id
                WHERE u.Username = @Username";

            var row = await conn.QueryFirstOrDefaultAsync(sql, new { Username = username.Trim() });

            if (row == null)
                return (null, "帳號或密碼錯誤。");

            // 檢查帳號狀態 (Status: 0=停用, 1=啟用, 2=鎖定)
            int status = (int)row.Status;
            if (status == 0)
                return (null, "此帳號已被停用，請聯絡管理員。");
            if (status == 2)
                return (null, "此帳號已被鎖定，請聯絡管理員。");

            // 驗證密碼 Hash
            string storedHash = (string)row.PasswordHash;
            var verifyResult = _hasher.VerifyHashedPassword(new object(), storedHash, password);

            if (verifyResult == PasswordVerificationResult.Failed)
                return (null, "帳號或密碼錯誤。");

            // 更新最後登入時間
            const string updateSql = "UPDATE dbo.MT_Users SET LastLoginAt = SYSDATETIME() WHERE Id = @Id";
            await conn.ExecuteAsync(updateSql, new { Id = (int)row.UserId });

            return (new AuthResult
            {
                UserId = (int)row.UserId,
                Username = (string)row.Username,
                DisplayName = (string)row.DisplayName,
                Email = (string)(row.Email ?? ""),
                RoleName = (string)row.RoleName,
                RoleCode = (int)row.RoleCode,
                CompanyTitle = (string)(row.CompanyTitle ?? ""),
                IsFirstLogin = (bool)row.IsFirstLogin
            }, null);
        }
        catch (SqlException ex)
        {
            return (null, $"資料庫連線失敗：{ex.Message}");
        }
        catch (Exception ex)
        {
            return (null, $"系統錯誤：{ex.Message}");
        }
    }
}
