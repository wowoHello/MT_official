using System.Data.Common;
using Dapper;

namespace MT.Services;

public interface IPasswordResetService
{
    Task<(bool Success, string Message, int UserId, int TokenId)> ValidateTokenAsync(string token);
    Task<(bool Success, string Message)> ResetPasswordAsync(string token, string newPassword);
}

public class PasswordResetService : IPasswordResetService
{
    private readonly IDatabaseService _db;

    public PasswordResetService(IDatabaseService db)
    {
        _db = db;
    }

    private sealed class TokenRow
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsUsed { get; set; }
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
                  WHERE Id = @TokenId",
                new { TokenId = row.Id }, tx);

            await tx.CommitAsync();
            return (true, "密碼已成功重設。");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
