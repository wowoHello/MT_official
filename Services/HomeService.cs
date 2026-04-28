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

    // 預設角色名稱（IsDefault=1，前端鎖定改名，可信賴的「任務語意」標識）
    private const string RoleProposer = "命題教師";
    private const string RoleExpert = "審題委員";
    private const string RoleConvener = "總召";

    // MT_Roles.Category：0 = 內部人員（管理員視角）、1 = 外部人員
    private const byte CategoryInternal = 0;

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

            // 一次取回 8 個結果集：倒數階段 / 梯次角色 / 個人配額 / 修題中 / 待審 / 系統角色 / 配額缺口 / 逾期階段
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

                -- 6) 系統角色分類（Category：0=內部人員、1=外部人員，用於判斷管理員視角）
                SELECT r.Category
                FROM dbo.MT_Users u
                INNER JOIN dbo.MT_Roles r ON r.Id = u.RoleId
                WHERE u.Id = @UserId;

                -- 7) 全梯次配額缺口（命題階段倒數時用）
                SELECT
                    ISNULL((SELECT SUM(TargetCount) FROM dbo.MT_ProjectTargets WHERE ProjectId = @ProjectId), 0) AS TargetTotal,
                    ISNULL((SELECT COUNT(*) FROM dbo.MT_Questions
                            WHERE ProjectId = @ProjectId AND IsDeleted = 0 AND Status >= 1), 0) AS ProducedTotal;

                -- 8) 逾期階段（最新一個 EndDate < 今日 且下一階段尚未開始）
                SELECT TOP 1 p.PhaseCode, p.PhaseName,
                    DATEDIFF(DAY, p.EndDate, CAST(GETDATE() AS DATE)) AS DaysOverdue
                FROM dbo.MT_ProjectPhases p
                LEFT JOIN dbo.MT_ProjectPhases pNext
                    ON pNext.ProjectId = p.ProjectId AND pNext.PhaseCode = p.PhaseCode + 1
                WHERE p.ProjectId = @ProjectId
                  AND p.PhaseCode > 1
                  AND p.EndDate < CAST(GETDATE() AS DATE)
                  AND (pNext.StartDate IS NULL OR pNext.StartDate > CAST(GETDATE() AS DATE))
                ORDER BY p.PhaseCode DESC;
                """;

            using var grid = await conn.QueryMultipleAsync(sql, new
            {
                UserId = userId,
                ProjectId = projectId.Value,
                Threshold = AlertThresholdDays
            });

            var phases = (await grid.ReadAsync<PhaseRow>()).ToList();
            var roles = (await grid.ReadAsync<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var quotaRow = await grid.ReadFirstOrDefaultAsync<QuotaRow>() ?? new QuotaRow();
            var editingByStatus = (await grid.ReadAsync<StatusCountRow>())
                .ToDictionary(r => r.Status, r => r.Cnt);
            var reviewByStage = (await grid.ReadAsync<StageCountRow>())
                .ToDictionary(r => r.ReviewStage, r => r.Cnt);
            var systemRoleCategory = await grid.ReadFirstOrDefaultAsync<byte?>();
            var quotaGap = await grid.ReadFirstOrDefaultAsync<QuotaGapRow>() ?? new QuotaGapRow();
            var overdue = await grid.ReadFirstOrDefaultAsync<OverdueRow>();

            // 管理員視角 = 系統角色屬內部人員（Category=0）OR 梯次角色為總召
            var isAdmin = systemRoleCategory == CategoryInternal
                       || roles.Contains(RoleConvener);

            var alerts = new List<UrgentAlertItem>();

            // [警示一] 階段逾期（任何角色，固定紅 + 跳閃）
            if (overdue is not null)
            {
                alerts.Add(new UrgentAlertItem
                {
                    AlertType = AlertType.PhaseOverdue,
                    Severity = AlertSeverity.Critical,
                    PhaseCode = overdue.PhaseCode,
                    PhaseName = overdue.PhaseName,
                    DaysLeft = -overdue.DaysOverdue,
                    Title = $"{overdue.PhaseName}已逾期 {overdue.DaysOverdue} 天",
                    Subtitle = isAdmin ? "請盡速推進階段時程" : "請聯絡專案管理員協助處理",
                    RedirectUrl = isAdmin ? "/projects" : "/overview"
                });
            }

            // [警示二/三] 各階段倒數（個人積壓 + 純倒數 + 配額缺口）
            foreach (var phase in phases)
            {
                BuildAlertForPhase(phase, roles, quotaRow, editingByStatus, reviewByStage, alerts);

                // 管理員：對該階段補上純倒數提醒（若尚未因個人任務加入）
                if (isAdmin && !alerts.Any(a => a.PhaseCode == phase.PhaseCode
                    && (a.AlertType == AlertType.PhaseCountdown || a.AlertType == AlertType.PersonalBacklog)))
                {
                    alerts.Add(BuildAdminPhaseCountdown(phase));
                }

                // 管理員專屬：命題階段倒數時，加入「配額缺口」警示
                if (isAdmin && phase.PhaseCode == 2
                    && quotaGap.TargetTotal > 0
                    && quotaGap.ProducedTotal < quotaGap.TargetTotal)
                {
                    alerts.Add(BuildQuotaGapAlert(phase, quotaGap));
                }
            }

            // 排序：逾期 > 配額缺口 > 個人積壓 > 純倒數，再依嚴重度與剩餘天數
            return alerts
                .OrderBy(a => a.AlertType switch
                {
                    AlertType.PhaseOverdue => 0,
                    AlertType.QuotaGap => 1,
                    AlertType.PersonalBacklog => 2,
                    _ => 3
                })
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

    /// <summary>管理員視角：純階段倒數提醒（不論是否有任務）。</summary>
    private static UrgentAlertItem BuildAdminPhaseCountdown(PhaseRow phase)
    {
        var severity = phase.DaysLeft <= CriticalThresholdDays
            ? AlertSeverity.Critical
            : AlertSeverity.Warning;

        return new UrgentAlertItem
        {
            AlertType = AlertType.PhaseCountdown,
            Severity = severity,
            PhaseCode = phase.PhaseCode,
            PhaseName = phase.PhaseName,
            DaysLeft = phase.DaysLeft,
            Title = $"{phase.PhaseName}{FormatPhaseDeadline(phase.DaysLeft)}",
            Subtitle = "點擊查看梯次總覽",
            RedirectUrl = "/overview"
        };
    }

    /// <summary>管理員視角：命題階段倒數時的配額缺口警示。</summary>
    private static UrgentAlertItem BuildQuotaGapAlert(PhaseRow phase, QuotaGapRow gap)
    {
        var shortage = gap.TargetTotal - gap.ProducedTotal;
        var ratio = gap.TargetTotal > 0 ? gap.ProducedTotal * 100 / gap.TargetTotal : 0;
        var severity = ratio < 70 ? AlertSeverity.Critical : AlertSeverity.Warning;

        return new UrgentAlertItem
        {
            AlertType = AlertType.QuotaGap,
            Severity = severity,
            PhaseCode = phase.PhaseCode,
            PhaseName = phase.PhaseName,
            DaysLeft = phase.DaysLeft,
            PendingCount = shortage,
            Title = $"梯次還缺 {shortage} 題未產出",
            Subtitle = $"達成率 {ratio}%，命題階段{FormatPhaseRemaining(phase.DaysLeft)}",
            RedirectUrl = "/overview"
        };
    }

    /// <summary>格式化「階段結束」描述；當天截止時顯示「今天過後截止」。</summary>
    private static string FormatPhaseDeadline(int daysLeft)
        => daysLeft <= 0 ? "今天過後截止" : $"剩 {daysLeft} 天結束";

    /// <summary>格式化「剩餘天數」描述；當天截止時顯示「今天過後截止」。</summary>
    private static string FormatPhaseRemaining(int daysLeft)
        => daysLeft <= 0 ? "今天過後截止" : $"剩 {daysLeft} 天";

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
                Subtitle = $"{phase.PhaseName}{FormatPhaseDeadline(phase.DaysLeft)}",
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
                Title = $"{phase.PhaseName}{FormatPhaseDeadline(phase.DaysLeft)}",
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

    private sealed class QuotaGapRow
    {
        public int TargetTotal { get; set; }
        public int ProducedTotal { get; set; }
    }

    private sealed class OverdueRow
    {
        public int PhaseCode { get; set; }
        public string PhaseName { get; set; } = string.Empty;
        public int DaysOverdue { get; set; }
    }
}
