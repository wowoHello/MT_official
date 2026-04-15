using System.Data.Common;
using Dapper;
using Microsoft.AspNetCore.Http;

namespace MT.Services;

public interface IPasswordResetService
{
    Task<(bool Success, string Message)> RequestPasswordResetAsync(string email, string baseUri);
    Task<(bool Success, string Message, int UserId, int TokenId)> ValidateTokenAsync(string token);
    Task<(bool Success, string Message)> ResetPasswordAsync(string token, string newPassword);

    /// <summary>直接變更密碼（無 token 流程，供強制改密碼或設定頁使用）</summary>
    Task ChangePasswordAsync(int userId, string newPassword);
}

public class PasswordResetService : IPasswordResetService
{
    private const int ResetTokenLifetimeMinutes = 10;
    private readonly IDatabaseService _db;
    private readonly IEmailService _emailService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PasswordResetService(IDatabaseService db, IEmailService emailService, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _emailService = emailService;
        _httpContextAccessor = httpContextAccessor;
    }

    private sealed class TokenRow
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsUsed { get; set; }
    }

    private sealed class UserResetRow
    {
        public int Id { get; set; }
        public string Email { get; set; } = "";
        public int Status { get; set; }
    }

    public async Task<(bool Success, string Message)> RequestPasswordResetAsync(string email, string baseUri)
    {
        using var conn = (DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        var normalizedEmail = email.Trim();
        var user = await conn.QueryFirstOrDefaultAsync<UserResetRow>(
            @"SELECT TOP 1 Id, Email, Status
              FROM dbo.MT_Users
              WHERE Email = @Email
              ORDER BY Id",
            new { Email = normalizedEmail });

        const string successMessage = "如果此電子信箱已註冊於系統，我們已寄出密碼重設連結，請留意收件匣與垃圾郵件資料夾。";
        if (user is null || user.Status != 1 || string.IsNullOrWhiteSpace(user.Email))
        {
            return (true, successMessage);
        }

        var token = Guid.NewGuid().ToString("N");
        var expiresAt = DateTime.Now.AddMinutes(ResetTokenLifetimeMinutes);
        var requestIp = ResolveIpAddress();
        int tokenId;

        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            await conn.ExecuteAsync(
                @"UPDATE dbo.MT_PasswordResetTokens
                  SET IsUsed = 1
                  WHERE UserId = @UserId
                    AND IsUsed = 0",
                new { UserId = user.Id }, tx);

            tokenId = await conn.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.MT_PasswordResetTokens (UserId, Token, RequestIp, ExpiresAt, IsUsed)
                  OUTPUT INSERTED.Id
                  VALUES (@UserId, @Token, @RequestIp, @ExpiresAt, 0)",
                new
                {
                    UserId = user.Id,
                    Token = token,
                    RequestIp = requestIp,
                    ExpiresAt = expiresAt
                },
                tx);

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            throw new InvalidOperationException($"建立密碼重設連結失敗：{ex.Message}", ex);
        }

        try
        {
            var resetPasswordUrl = new Uri(new Uri(baseUri), $"resetpassword?token={Uri.EscapeDataString(token)}").ToString();
            await _emailService.SendResetPWEmailAsync(
                user.Email,
                "CWT 命題工作平臺 - 密碼重設通知",
                baseUri,
                resetPasswordUrl);

            return (true, successMessage);
        }
        catch
        {
            await conn.ExecuteAsync(
                @"UPDATE dbo.MT_PasswordResetTokens
                  SET IsUsed = 1
                  WHERE Id = @TokenId",
                new { TokenId = tokenId });

            throw;
        }
    }

    public async Task<(bool Success, string Message, int UserId, int TokenId)> ValidateTokenAsync(string token)
    {
        using var conn = _db.CreateConnection();

        var row = await conn.QueryFirstOrDefaultAsync<TokenRow>(
            @"SELECT TOP 1 t.Id, t.UserId, t.ExpiresAt, t.IsUsed
              FROM dbo.MT_PasswordResetTokens t
              WHERE t.Token = @Token",
            new { Token = token });

        if (row is null)
            return (false, "通行證無效或已被使用。", 0, 0);

        if (row.IsUsed)
            return (false, "此重設連結已被使用，請重新申請。", 0, 0);

        if (row.ExpiresAt < DateTime.Now)
            return (false, "此重設連結已過期，請重新申請！", 0, 0);

        return (true, "", row.UserId, row.Id);
    }

    public async Task<(bool Success, string Message)> ResetPasswordAsync(string token, string newPassword)
    {
        using var conn = (DbConnection)_db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();

        try
        {
            // 驗證 token（交易內再次確認，防止併發）
            var row = await conn.QueryFirstOrDefaultAsync<TokenRow>(
                @"SELECT TOP 1 t.Id, t.UserId, t.ExpiresAt, t.IsUsed
                  FROM dbo.MT_PasswordResetTokens t
                  WHERE t.Token = @Token",
                new { Token = token }, tx);

            if (row is null)
                return (false, "通行證無效或已被使用。");

            if (row.IsUsed)
                return (false, "此重設連結已被使用，請重新申請。");

            if (row.ExpiresAt < DateTime.Now)
                return (false, "此重設連結已過期，請重新申請！");

            // 更新密碼（與登入驗證使用相同的 SHA256 UTF-16LE 雜湊）
            var passwordHash = AuthService.ComputePasswordHash(newPassword);

            await conn.ExecuteAsync(
                @"UPDATE dbo.MT_Users
                  SET PasswordHash = @PasswordHash,
                      IsFirstLogin = 0,
                      UpdatedAt = SYSDATETIME()
                  WHERE Id = @UserId",
                new { PasswordHash = passwordHash, UserId = row.UserId }, tx);

            // 作廢 token
            await conn.ExecuteAsync(
                @"UPDATE dbo.MT_PasswordResetTokens
                  SET IsUsed = 1
                  WHERE UserId = @UserId
                    AND IsUsed = 0",
                new { UserId = row.UserId }, tx);

            await tx.CommitAsync();
            return (true, "密碼已成功重設。");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task ChangePasswordAsync(int userId, string newPassword)
    {
        using var conn = _db.CreateConnection();

        var passwordHash = AuthService.ComputePasswordHash(newPassword);

        await conn.ExecuteAsync(
            @"UPDATE dbo.MT_Users
              SET PasswordHash = @PasswordHash,
                  IsFirstLogin = 0,
                  UpdatedAt = SYSDATETIME()
              WHERE Id = @UserId",
            new { PasswordHash = passwordHash, UserId = userId });
    }

    private string? ResolveIpAddress()
    {
        var context = _httpContextAccessor.HttpContext;
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
}
