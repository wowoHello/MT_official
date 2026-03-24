using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace MT.Services;

public interface IDatabaseService
{
    Task<(bool Success, string Message)> TestConnectionAsync();
    Task<IEnumerable<string>> GetTableListAsync();
}

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            return (false, "連線字串為空，請檢查 appsettings.json。");
        }

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // 嘗試執行一個簡單的查詢，例如取回當前資料庫名稱或版本
            var version = await conn.ExecuteScalarAsync<string>("SELECT @@VERSION");

            return (true, $"連線成功！SQL Server 版本：\n{version}");
        }
        catch (SqlException ex)
        {
            return (false, $"資料庫連線失敗 (SqlException): {ex.Message}");
        }
        catch (System.Exception ex)
        {
            return (false, $"發生其餘錯誤: {ex.Message}");
        }
    }
    public async Task<IEnumerable<string>> GetTableListAsync()
    {
        if (string.IsNullOrEmpty(_connectionString))
            throw new InvalidOperationException("連線字串為空，請檢查 appsettings.json。");

        using var conn = new SqlConnection(_connectionString);
        return await conn.QueryAsync<string>("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME");
    }
}
