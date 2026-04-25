using Dapper;
using MT.Models;

namespace MT.Services;

public interface IQuestionService
{
    Task<List<QuotaProgressItem>> GetMyQuotaProgressAsync(int userId, int projectId);
    Task<ProjectPhaseInfo?> GetCurrentPhaseAsync(int projectId);
}

public class QuestionService(IDatabaseService db) : IQuestionService
{
    private readonly IDatabaseService _db = db;

    public async Task<List<QuotaProgressItem>> GetMyQuotaProgressAsync(int userId, int projectId)
    {
        const string sql = """
            SELECT
                qt.Id          AS QuestionTypeId,
                qt.Name        AS TypeName,
                mq.QuotaCount  AS Target,
                COUNT(q.Id)    AS Completed
            FROM dbo.MT_MemberQuotas mq
            INNER JOIN dbo.MT_ProjectMembers pm  ON pm.Id  = mq.ProjectMemberId
            INNER JOIN dbo.MT_QuestionTypes  qt  ON qt.Id  = mq.QuestionTypeId
            LEFT  JOIN dbo.MT_Questions      q   ON q.CreatorId      = pm.UserId
                                                AND q.ProjectId      = @ProjectId
                                                AND q.QuestionTypeId = mq.QuestionTypeId
                                                AND q.IsDeleted      = 0
                                                AND q.Status         >= 1
            WHERE pm.UserId    = @UserId
              AND pm.ProjectId = @ProjectId
            GROUP BY qt.Id, qt.Name, qt.SortOrder, mq.QuotaCount
            ORDER BY qt.SortOrder;
            """;

        using var conn = _db.CreateConnection();
        var result = await conn.QueryAsync<QuotaProgressItem>(sql, new { UserId = userId, ProjectId = projectId });
        return result.AsList();
    }

    public async Task<ProjectPhaseInfo?> GetCurrentPhaseAsync(int projectId)
    {
        const string sql = """
            SELECT TOP 1
                PhaseCode,
                PhaseName,
                StartDate,
                EndDate,
                DATEDIFF(DAY, CAST(GETDATE() AS DATE), EndDate) AS DaysLeft
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId
              AND CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
            ORDER BY SortOrder;
            """;

        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<ProjectPhaseInfo>(sql, new { ProjectId = projectId });
    }
}
