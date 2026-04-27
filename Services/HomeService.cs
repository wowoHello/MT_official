using Dapper;
using MT.Models;

namespace MT.Services;

public interface IHomeService
{
    /// <summary>取得首頁公告（已發佈 + 上架期間 + 全站或當前梯次）。</summary>
    Task<List<AnnouncementListItem>> GetAnnouncementsAsync(int? projectId);

    /// <summary>
    /// 取得首頁急件警示（階段倒數 ≤5 天，依使用者在該梯次的角色判斷）。
    /// 包含階段倒數提醒（Type A）與個人任務積壓警示（Type B）。
    /// </summary>
    Task<List<UrgentAlertItem>> GetUrgentAlertsAsync(int userId, int? projectId);
}

public class HomeService : IHomeService
{
    private const int AlertThresholdDays = 5;
    private const int CriticalThresholdDays = 2;

    // 對應的角色名稱（與 MT_Roles.Name 一致）
    private const string RoleProposer = "命題教師";
    private const string RoleExpert = "審題委員";
    private const string RoleConvener = "總召";

    private readonly IDatabaseService _db;
    private readonly IAnnouncementService _announcementService;
    private readonly ILogger<HomeService> _logger;

    public HomeService(IDatabaseService db, IAnnouncementService announcementService, ILogger<HomeService> logger)
    {
        _db = db;
        _announcementService = announcementService;
        _logger = logger;
    }

    public Task<List<AnnouncementListItem>> GetAnnouncementsAsync(int? projectId)
        => _announcementService.GetHomeAnnouncementsAsync(projectId);

    public async Task<List<UrgentAlertItem>> GetUrgentAlertsAsync(int userId, int? projectId)
    {
        // 沒選梯次或無 userId 則無法判斷
        if (!projectId.HasValue || userId <= 0) return [];

        try
        {
            using var conn = _db.CreateConnection();

            // 一次取回：當前進行中且 ≤5 天的階段、使用者在該梯次的角色名稱、命題配額/已完成、修題中題數、待審任務數
            const string sql = """
                -- 1) 倒數階段
                SELECT
                    PhaseCode,
                    PhaseName,
                    DATEDIFF(DAY, CAST(GETDATE() AS DATE), EndDate) AS DaysLeft
                FROM dbo.MT_ProjectPhases
                WHERE ProjectId = @ProjectId
                  AND PhaseCode > 1
                  AND CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
                  AND DATEDIFF(DAY, CAST(GETDATE() AS DATE), EndDate) BETWEEN 0 AND @Threshold
                ORDER BY PhaseCode;

                -- 2) 使用者在該梯次的角色名稱集合
                SELECT r.Name
                FROM dbo.MT_ProjectMembers pm
                INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
                INNER JOIN dbo.MT_Roles r ON r.Id = pmr.RoleId
                WHERE pm.UserId = @UserId AND pm.ProjectId = @ProjectId;

                -- 3) 命題配額未達標的數量（命題階段使用：總配額 - 已完成）
                SELECT
                    ISNULL(SUM(mq.QuotaCount), 0) AS Quota,
                    ISNULL(
                        (SELECT COUNT(*)
                         FROM dbo.MT_Questions q
                         WHERE q.ProjectId = @ProjectId
                           AND q.CreatorId = @UserId
                           AND q.IsDeleted = 0
                           AND q.Status >= 1), 0) AS Produced
                FROM dbo.MT_MemberQuotas mq
                INNER JOIN dbo.MT_ProjectMembers pm ON pm.Id = mq.ProjectMemberId
                WHERE pm.ProjectId = @ProjectId AND pm.UserId = @UserId;

                -- 4) 各修題階段中、未完成的題數（依 Status 分組）
                SELECT Status, COUNT(*) AS Cnt
                FROM dbo.MT_Questions
                WHERE ProjectId = @ProjectId
                  AND CreatorId = @UserId
                  AND IsDeleted = 0
                  AND Status IN (4, 6, 8)
                GROUP BY Status;

                -- 5) 待審任務（依 ReviewStage 分組）
                SELECT ReviewStage, COUNT(*) AS Cnt
                FROM dbo.MT_ReviewAssignments
                WHERE ProjectId = @ProjectId
                  AND ReviewerId = @UserId
                  AND ReviewStatus = 0
                GROUP BY ReviewStage;
                """;

            using var grid = await conn.QueryMultipleAsync(sql, new
            {
                UserId = userId,
                ProjectId = projectId.Value,
                Threshold = AlertThresholdDays
            });

            var phases = (await grid.ReadAsync<PhaseRow>()).ToList();
            if (phases.Count == 0) return [];

            var roles = (await grid.ReadAsync<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var quotaRow = await grid.ReadFirstOrDefaultAsync<QuotaRow>() ?? new QuotaRow();
            var editingByStatus = (await grid.ReadAsync<StatusCountRow>())
                .ToDictionary(r => r.Status, r => r.Cnt);
            var reviewByStage = (await grid.ReadAsync<StageCountRow>())
                .ToDictionary(r => r.ReviewStage, r => r.Cnt);

            var alerts = new List<UrgentAlertItem>();
            foreach (var phase in phases)
            {
                BuildAlertForPhase(phase, roles, quotaRow, editingByStatus, reviewByStage, alerts);
            }

            // 排序：個人任務優先 → 嚴重度 → 剩餘天數
            return alerts
                .OrderByDescending(a => a.AlertType == AlertType.PersonalBacklog)
                .ThenByDescending(a => a.Severity)
                .ThenBy(a => a.DaysLeft)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "載入首頁急件警示失敗 (UserId={UserId}, ProjectId={ProjectId})", userId, projectId);
            return [];
        }
    }

    /// <summary>依角色與資料庫狀態，為單一階段產出對應警示卡片。</summary>
    private static void BuildAlertForPhase(
        PhaseRow phase,
        HashSet<string> roles,
        QuotaRow quota,
        Dictionary<byte, int> editingByStatus,
        Dictionary<byte, int> reviewByStage,
        List<UrgentAlertItem> output)
    {
        // PhaseCode → (是否有角色資格、要看的個人任務數)
        // 2=命題, 3=互審, 4=互修, 5=專審, 6=專修, 7=總審, 8=總修
        var (eligible, pending, redirectUrl, taskLabel) = phase.PhaseCode switch
        {
            2 => (
                roles.Contains(RoleProposer),
                Math.Max(0, quota.Quota - quota.Produced),
                "/cwt-list?tab=compose",
                "未命題"),
            3 => (
                roles.Contains(RoleProposer),
                reviewByStage.GetValueOrDefault((byte)1, 0),
                "/reviews?tab=peer",
                "待審"),
            4 => (
                roles.Contains(RoleProposer),
                editingByStatus.GetValueOrDefault((byte)4, 0),
                "/cwt-list?tab=revision",
                "待修題"),
            5 => (
                roles.Contains(RoleExpert),
                reviewByStage.GetValueOrDefault((byte)2, 0),
                "/reviews?tab=expert",
                "待審"),
            6 => (
                roles.Contains(RoleProposer),
                editingByStatus.GetValueOrDefault((byte)6, 0),
                "/cwt-list?tab=revision",
                "待修題"),
            7 => (
                roles.Contains(RoleConvener),
                reviewByStage.GetValueOrDefault((byte)3, 0),
                "/reviews?tab=final",
                "待審"),
            8 => (
                roles.Contains(RoleProposer),
                editingByStatus.GetValueOrDefault((byte)8, 0),
                "/cwt-list?tab=revision",
                "待修題"),
            _ => (false, 0, string.Empty, string.Empty)
        };

        if (!eligible) return;

        var severity = phase.DaysLeft <= CriticalThresholdDays
            ? AlertSeverity.Critical
            : AlertSeverity.Warning;

        if (pending > 0)
        {
            // Type B：個人任務積壓（合併卡片）
            output.Add(new UrgentAlertItem
            {
                AlertType = AlertType.PersonalBacklog,
                Severity = AlertSeverity.Critical,
                PhaseCode = phase.PhaseCode,
                PhaseName = phase.PhaseName,
                DaysLeft = phase.DaysLeft,
                PendingCount = pending,
                Title = $"您還有 {pending} 題{taskLabel}",
                Subtitle = $"{phase.PhaseName}剩 {phase.DaysLeft} 天結束",
                RedirectUrl = redirectUrl
            });
        }
        else
        {
            // Type A：純階段倒數
            output.Add(new UrgentAlertItem
            {
                AlertType = AlertType.PhaseCountdown,
                Severity = severity,
                PhaseCode = phase.PhaseCode,
                PhaseName = phase.PhaseName,
                DaysLeft = phase.DaysLeft,
                PendingCount = 0,
                Title = $"{phase.PhaseName}剩 {phase.DaysLeft} 天結束",
                Subtitle = "目前任務皆已完成",
                RedirectUrl = redirectUrl
            });
        }
    }

    private sealed class PhaseRow
    {
        public int PhaseCode { get; set; }
        public string PhaseName { get; set; } = string.Empty;
        public int DaysLeft { get; set; }
    }

    private sealed class QuotaRow
    {
        public int Quota { get; set; }
        public int Produced { get; set; }
    }

    private sealed class StatusCountRow
    {
        public byte Status { get; set; }
        public int Cnt { get; set; }
    }

    private sealed class StageCountRow
    {
        public byte ReviewStage { get; set; }
        public int Cnt { get; set; }
    }
}
