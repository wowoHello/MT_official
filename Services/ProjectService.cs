using Microsoft.Data.SqlClient;
using Dapper;

namespace MT.Services;

/// <summary>專案查詢結果模型</summary>
public class ProjectDto
{
    public int Id { get; set; }
    public string ProjectCode { get; set; } = "";
    public string Name { get; set; } = "";
    public int Year { get; set; }
    /// <summary>狀態：0=準備中, 1=進行中, 2=已結案</summary>
    public int Status { get; set; }

    /// <summary>狀態文字</summary>
    public string StatusText => Status switch
    {
        0 => "preparing",
        1 => "active",
        2 => "closed",
        _ => "unknown"
    };
}

public interface IProjectService
{
    /// <summary>取得所有專案列表</summary>
    Task<List<ProjectDto>> GetProjectsAsync();
}

public class ProjectService : IProjectService
{
    private readonly string _connectionString;

    public ProjectService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
    }

    public async Task<List<ProjectDto>> GetProjectsAsync()
    {
        if (string.IsNullOrEmpty(_connectionString))
            return new List<ProjectDto>();

        try
        {
            using var conn = new SqlConnection(_connectionString);

            const string sql = @"
                SELECT Id, ProjectCode, Name, Year, Status
                FROM dbo.MT_Projects
                ORDER BY Status ASC, Year DESC, Id DESC";

            var results = await conn.QueryAsync<ProjectDto>(sql);
            return results.ToList();
        }
        catch (SqlException)
        {
            // 資料表可能尚未建立
            return new List<ProjectDto>();
        }
        catch
        {
            return new List<ProjectDto>();
        }
    }
}
