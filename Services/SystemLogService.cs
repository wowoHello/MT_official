using Dapper;
using MT.Models;

namespace MT.Services;

public interface ISystemLogService
{
    /// <summary>取得當前 Tab 的列表（依 Tab 決定資料源：MT_LoginLogs 或 MT_AuditLogs）。</summary>
    Task<SystemLogPage> GetLogsAsync(SystemLogQuery query);
    /// <summary>取得登入活動 4 張 KPI（僅 Login Tab 用）。</summary>
    Task<SystemLogKpiDto> GetKpiAsync();
}

/// <summary>
/// 系統活動記錄頁面（SystemLogs.razor）的資料服務。
/// 統一 4 個 Tab 的資料來源：
///   Login        → MT_LoginLogs
///   Members/Project/Announcement → MT_AuditLogs WHERE ProjectId IS NULL
/// </summary>
public class SystemLogService : ISystemLogService
{
    private readonly IDatabaseService _db;

    public SystemLogService(IDatabaseService db) => _db = db;

    public Task<SystemLogPage> GetLogsAsync(SystemLogQuery query)
        => query.Tab == SystemLogTab.Login
            ? GetLoginLogsAsync(query)
            : GetAuditLogsAsync(query);

    // ── Login Tab：查 MT_LoginLogs ─────────────────────────────────────

    private async Task<SystemLogPage> GetLoginLogsAsync(SystemLogQuery query)
    {
        using var conn = _db.CreateConnection();

        var clauses = new List<string>();
        var p = new DynamicParameters();

        if (query.EventType.HasValue)
        {
            clauses.Add("ll.EventType = @EventType");
            p.Add("EventType", query.EventType.Value);
        }
        if (query.IsSuccess.HasValue)
        {
            clauses.Add("ll.IsSuccess = @IsSuccess");
            p.Add("IsSuccess", query.IsSuccess.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            clauses.Add("ll.Username LIKE @Keyword");
            p.Add("Keyword", $"%{query.Keyword.Trim()}%");
        }
        ApplyDateRange(clauses, p, query, "ll.CreatedAt");

        var whereSql = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";
        var offset = (query.Page - 1) * query.PageSize;
        p.Add("Skip", offset);
        p.Add("Take", query.PageSize);

        var items = (await conn.QueryAsync<SystemLogItem>($@"
            SELECT ll.Id, ll.UserId,
                   ISNULL(u.DisplayName, ll.Username) AS OperatorName,
                   ll.Username,
                   ll.EventType, ll.IsSuccess, ll.IpAddress, ll.UserAgent, ll.FailReason,
                   ll.CreatedAt
            FROM dbo.MT_LoginLogs ll
            LEFT JOIN dbo.MT_Users u ON u.Id = ll.UserId
            {whereSql}
            ORDER BY ll.CreatedAt DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", p)).ToList();

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.MT_LoginLogs ll {whereSql}", p);

        return BuildPage(items, total, query.PageSize);
    }

    // ── Audit Tab：查 MT_AuditLogs ─────────────────────────────────────

    private async Task<SystemLogPage> GetAuditLogsAsync(SystemLogQuery query)
    {
        using var conn = _db.CreateConnection();

        // ⚠️ 必須用 int[]，不能用 byte[]：
        //    Dapper 對 byte[] 預設綁定為 varbinary（單一 binary 參數），
        //    使得 `TargetType IN @typeCodes` 永遠 0 筆。
        //    DashboardService 也是用 int[]，這裡保持一致。
        int[] typeCodes = query.Tab switch
        {
            SystemLogTab.Members      => [0, 1, 5],   // Users / Roles / Teachers
            SystemLogTab.Project      => [2],
            SystemLogTab.Announcement => [4],
            _                         => []
        };

        var clauses = new List<string>
        {
            "al.ProjectId IS NULL",                  // 強制：只看全站活動
            "al.Action IN (0, 1, 2)",                // 不再包含 Login/Logout（已分離至 MT_LoginLogs）
            "al.TargetType IN @typeCodes"
        };
        var p = new DynamicParameters();
        p.Add("typeCodes", typeCodes);

        if (query.Action.HasValue)
        {
            clauses.Add("al.Action = @Action");
            p.Add("Action", query.Action.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            clauses.Add("u.DisplayName LIKE @Keyword");
            p.Add("Keyword", $"%{query.Keyword.Trim()}%");
        }
        ApplyDateRange(clauses, p, query, "al.CreatedAt");

        var whereSql = "WHERE " + string.Join(" AND ", clauses);
        var offset = (query.Page - 1) * query.PageSize;
        p.Add("Skip", offset);
        p.Add("Take", query.PageSize);

        // 用 OUTER APPLY 一次帶回 TargetName（依 TargetType 走不同表）
        // 主表反查失敗時（目標已刪除）：C# 端從 OldValue / NewValue JSON 解析 targetDisplayName
        var items = (await conn.QueryAsync<SystemLogItem>($@"
            SELECT al.Id, al.UserId,
                   ISNULL(u.DisplayName, N'系統') AS OperatorName,
                   al.Action, al.TargetType, al.TargetId,
                   COALESCE(usr_t.n, rol_t.n, prj_t.n, ann_t.n, tch_t.n) AS TargetName,
                   al.IpAddress, al.CreatedAt,
                   al.OldValue, al.NewValue
            FROM dbo.MT_AuditLogs al
            LEFT JOIN dbo.MT_Users u ON u.Id = al.UserId
            OUTER APPLY (SELECT DisplayName n FROM dbo.MT_Users      WHERE al.TargetType = 0 AND Id = al.TargetId) usr_t
            OUTER APPLY (SELECT Name        n FROM dbo.MT_Roles      WHERE al.TargetType = 1 AND Id = al.TargetId) rol_t
            OUTER APPLY (SELECT Name        n FROM dbo.MT_Projects   WHERE al.TargetType = 2 AND Id = al.TargetId) prj_t
            OUTER APPLY (SELECT Title       n FROM dbo.MT_Announcements WHERE al.TargetType = 4 AND Id = al.TargetId) ann_t
            OUTER APPLY (
                SELECT u2.DisplayName n
                FROM dbo.MT_Teachers t JOIN dbo.MT_Users u2 ON u2.Id = t.UserId
                WHERE al.TargetType = 5 AND t.Id = al.TargetId
            ) tch_t
            {whereSql}
            ORDER BY al.CreatedAt DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", p)).ToList();

        // 目標已刪除時 fallback：從 JSON 抽 targetDisplayName（Delete 優先看 OldValue，其餘看 NewValue）
        foreach (var item in items.Where(x => string.IsNullOrWhiteSpace(x.TargetName)))
        {
            var jsonPriority = item.Action == 2 /* Delete */
                ? new[] { item.OldValue, item.NewValue }
                : new[] { item.NewValue, item.OldValue };

            foreach (var json in jsonPriority)
            {
                var extracted = AuditLogJsonHelper.TryExtractTargetName(item.TargetType ?? 0, json);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    item.TargetName = extracted;
                    break;
                }
            }

            // UI 不需要原始 JSON，清掉減少傳輸量
            item.OldValue = null;
            item.NewValue = null;
        }

        var total = await conn.ExecuteScalarAsync<int>($@"
            SELECT COUNT(*)
            FROM dbo.MT_AuditLogs al
            LEFT JOIN dbo.MT_Users u ON u.Id = al.UserId
            {whereSql}", p);

        return BuildPage(items, total, query.PageSize);
    }

    // ── KPI（沿用舊 LoginLogService 邏輯）─────────────────────────────

    public async Task<SystemLogKpiDto> GetKpiAsync()
    {
        using var conn = _db.CreateConnection();

        // 一次 SQL 撈四個 KPI，避免來回 round trip
        return await conn.QuerySingleAsync<SystemLogKpiDto>(@"
            DECLARE @today    DATE      = CAST(SYSDATETIME() AS DATE);
            DECLARE @weekAgo  DATETIME2 = DATEADD(DAY, -7, SYSDATETIME());
            DECLARE @dayAgo   DATETIME2 = DATEADD(HOUR, -24, SYSDATETIME());

            SELECT
                (SELECT COUNT(*) FROM dbo.MT_LoginLogs
                 WHERE EventType = 1 AND IsSuccess = 1
                   AND CAST(CreatedAt AS DATE) = @today) AS TodayLoginCount,

                (SELECT COUNT(*) FROM dbo.MT_LoginLogs
                 WHERE EventType = 1 AND IsSuccess = 0
                   AND CAST(CreatedAt AS DATE) = @today) AS TodayFailedCount,

                (SELECT COUNT(DISTINCT UserId) FROM dbo.MT_LoginLogs
                 WHERE EventType = 1 AND IsSuccess = 1
                   AND UserId IS NOT NULL
                   AND CreatedAt >= @weekAgo) AS WeeklyActiveUserCount,

                (SELECT COUNT(*) FROM (
                    SELECT Username
                    FROM dbo.MT_LoginLogs
                    WHERE EventType = 1 AND IsSuccess = 0 AND CreatedAt >= @dayAgo
                    GROUP BY Username
                    HAVING COUNT(*) >= 3
                 ) AS suspicious) AS SuspiciousLoginCount;
        ");
    }

    // ── Helper ─────────────────────────────────────────────────────────

    /// <summary>共用日期區間條件套用（避免兩個 Tab 重複寫）。</summary>
    private static void ApplyDateRange(List<string> clauses, DynamicParameters p, SystemLogQuery query, string columnRef)
    {
        if (query.StartDate.HasValue)
        {
            clauses.Add($"{columnRef} >= @StartDate");
            p.Add("StartDate", query.StartDate.Value.Date);
        }
        if (query.EndDate.HasValue)
        {
            // 含當日：以「< 隔日 00:00」表達，避免 DATETIME2 精度造成邊界遺漏
            clauses.Add($"{columnRef} < DATEADD(DAY, 1, @EndDate)");
            p.Add("EndDate", query.EndDate.Value.Date);
        }
    }

    private static SystemLogPage BuildPage(List<SystemLogItem> items, int total, int pageSize)
        => new()
        {
            Items = items,
            TotalCount = total,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize)
        };
}
