using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
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
        _connectionString = ResolveConnectionString(configuration);
    }

    private static string ResolveConnectionString(IConfiguration configuration)
    {
        var server = configuration["MT_SQL_Server"];
        var database = configuration["MT_SQL_Database"];
        var userId = configuration["MT_SQL_UserId"];
        var password = configuration["MT_SQL_UserPassword"];

        if (!string.IsNullOrWhiteSpace(server) &&
            !string.IsNullOrWhiteSpace(database) &&
            !string.IsNullOrWhiteSpace(userId) &&
            !string.IsNullOrWhiteSpace(password))
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                UserID = userId,
                Password = password,
                TrustServerCertificate = true
            };

            return builder.ConnectionString;
        }

        return configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
    }

    public IDbConnection CreateConnection()
    {
        return new SqlConnection(_connectionString);
    }
 }
