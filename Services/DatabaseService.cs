using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace MT.Services;

public interface IDatabaseService
{
    IDbConnection CreateConnection();
}

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("appsettings.json 缺少 ConnectionStrings:DefaultConnection 設定。");
    }

    public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
}
