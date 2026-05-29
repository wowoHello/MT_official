using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using MT.Hubs;
using MT.Models;

namespace MT.Services;

/// <summary>
/// 定義命題專案管理所需的查詢、建立與刪除服務契約。
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// 建立新專案，並一併寫入階段、題型目標、成員角色與配額資料。
    /// </summary>
    Task<int> CreateProjectAsync(CreateProjectRequest request);

    /// <summary>
    /// 取得指定專案的可編輯完整資料，供表單回填使用。
    /// </summary>
    Task<ProjectEditDto?> GetProjectEditAsync(int projectId);

    /// <summary>
    /// 取得專案管理頁左側使用的專案列表資料。
    /// </summary>
    Task<List<ProjectListItem>> GetProjectListAsync();

    /// <summary>
    /// 依登入者身分與參與紀錄，取得可切換與可見的專案清單。
    /// </summary>
    Task<List<ProjectSwitcherItem>> GetVisibleProjectsAsync(int userId);

    /// <summary>
    /// 取得單一專案的詳細資料，包含基本資訊、時程、題型目標與成員角色。
    /// </summary>
    Task<ProjectDetailDto?> GetProjectDetailAsync(int projectId);

    /// <summary>
    /// 取得建立或編輯專案時可選擇的人才池名單。
    /// </summary>
    Task<List<ProjectTalentPoolItem>> GetTalentPoolAsync();

    /// <summary>
    /// 取得專案指派人員可選用的外部角色清單。
    /// </summary>
    Task<List<RoleOption>> GetProjectRoleOptionsAsync();

    /// <summary>
    /// 將專案標記為軟刪除，從一般列表中隱藏並保留相關資料。
    /// </summary>
    Task SoftDeleteProjectAsync(int projectId, int deletedBy);

    /// <summary>
    /// 提前結案：將梯次設為已結案、非「採用」題目一律改為「不採用」入庫。
    /// </summary>
    Task CloseProjectAsync(int projectId, int closedBy);

    /// <summary>
    /// 更新既有專案主檔與相關設定資料。
    /// </summary>
    Task UpdateProjectAsync(UpdateProjectRequest request);

    /// <summary>
    /// 取得指定專案的 7 個實作階段（PhaseCode &gt; 0），依排序回傳。
    /// </summary>
    Task<List<ProjectPhaseInfo>> GetPhasesAsync(int projectId);

    /// <summary>
    /// 取得指定專案目前進行中的階段（依今日落在 StartDate ~ EndDate 區間判定）。
    /// </summary>
    Task<ProjectPhaseInfo?> GetCurrentPhaseAsync(int projectId);

    /// <summary>
    /// 取得「下載結案資料」EXCEL 所需資料；僅已結案梯次 (ClosedAt IS NOT NULL) 才有資料，
    /// 否則回傳 Rows 為空。題目過濾條件：Status IN (9,10,11,12) 即已有最終結局者。
    /// </summary>
    Task<ClosedProjectExportData?> GetClosedProjectExportDataAsync(int projectId);
}

/// <summary>
/// 提供命題專案的查詢、建立、軟刪除與即時同步通知功能。
/// </summary>
public class ProjectService : IProjectService
{
    private readonly IDatabaseService _db;
    private readonly ILogger<ProjectService> _logger;
    private readonly IHubContext<ProjectsHub> _projectsHubContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IQuestionTypeCatalog _typeCatalog;
    private readonly IAppointmentService _appointmentSvc;

    /// <summary>
    /// 初始化專案服務所需的資料庫、記錄器與即時同步依賴。
    /// </summary>
    public ProjectService(
        IDatabaseService db,
        ILogger<ProjectService> logger,
        IHubContext<ProjectsHub> projectsHubContext,
        IHttpContextAccessor httpContextAccessor,
        IQuestionTypeCatalog typeCatalog,
        IAppointmentService appointmentSvc)
    {
        _db = db;
        _logger = logger;
        _projectsHubContext = projectsHubContext;
        _httpContextAccessor = httpContextAccessor;
        _typeCatalog = typeCatalog;
        _appointmentSvc = appointmentSvc;
    }

    /// <summary>
    /// 取得專案管理頁顯示用的專案列表，排除已軟刪除資料。
    /// </summary>
    public async Task<List<ProjectListItem>> GetProjectListAsync()
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                p.Id,
                p.ProjectCode,
                p.Name,
                p.Year,
                p.School,
                p.StartDate,
                p.EndDate,
                p.ClosedAt,
                (SELECT TOP 1 ph.StartDate FROM dbo.MT_ProjectPhases ph WHERE ph.ProjectId = p.Id AND ph.PhaseCode = 2) AS CompositionStartDate,
                ISNULL(u.DisplayName, N'系統') AS CreatorName,
                (SELECT COUNT(*) FROM dbo.MT_ProjectMembers pm WHERE pm.ProjectId = p.Id) AS MemberCount,
                ISNULL(p.ProjectType, 0) AS ProjectType,
                p.ExamLevel
            FROM dbo.MT_Projects p
            LEFT JOIN dbo.MT_Users u ON u.Id = p.CreatedBy
            WHERE p.IsDeleted = 0
            ORDER BY p.Year DESC, p.Id DESC;
            """;

        var result = await conn.QueryAsync<ProjectListItem>(sql);
        return result.ToList();
    }

    /// <summary>
    /// 取得指定專案的可編輯完整資料，供專案編輯表單回填。
    /// </summary>
    public async Task<ProjectEditDto?> GetProjectEditAsync(int projectId)
    {
        using var conn = _db.CreateConnection();
        return await GetProjectEditAsync(conn, null, projectId);
    }

    /// <summary>
    /// 依使用者角色與參與關聯，取得上方專案切換器可見的專案清單。
    /// </summary>
    public async Task<List<ProjectSwitcherItem>> GetVisibleProjectsAsync(int userId)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            WITH UserContext AS (
                SELECT
                    u.Id AS UserId,
                    r.Category AS RoleCategory
                FROM dbo.MT_Users u
                INNER JOIN dbo.MT_Roles r ON r.Id = u.RoleId
                WHERE u.Id = @UserId
                  AND u.Status = 1
            ),
            VisibleProjectIds AS (
                SELECT p.Id AS ProjectId
                FROM dbo.MT_Projects p
                CROSS JOIN UserContext uc
                WHERE uc.RoleCategory = 0
                  AND p.IsDeleted = 0

                UNION

                SELECT pm.ProjectId
                FROM dbo.MT_ProjectMembers pm
                INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
                INNER JOIN UserContext uc ON uc.UserId = pm.UserId
                WHERE uc.RoleCategory = 1

                UNION

                SELECT q.ProjectId
                FROM dbo.MT_Questions q
                INNER JOIN UserContext uc ON uc.UserId = q.CreatorId
                WHERE uc.RoleCategory = 1
                  AND q.IsDeleted = 0

                UNION

                SELECT ra.ProjectId
                FROM dbo.MT_ReviewAssignments ra
                INNER JOIN UserContext uc ON uc.UserId = ra.ReviewerId
                WHERE uc.RoleCategory = 1
            )
            SELECT
                p.Id,
                p.ProjectCode,
                p.Name,
                p.Year,
                p.StartDate,
                p.EndDate,
                p.ClosedAt,
                (SELECT TOP 1 ph.StartDate FROM dbo.MT_ProjectPhases ph WHERE ph.ProjectId = p.Id AND ph.PhaseCode = 2) AS CompositionStartDate,
                ISNULL(p.ProjectType, 0) AS ProjectType,
                p.ExamLevel
            FROM dbo.MT_Projects p
            INNER JOIN (
                SELECT DISTINCT ProjectId
                FROM VisibleProjectIds
            ) vp ON vp.ProjectId = p.Id
            WHERE p.IsDeleted = 0
            ORDER BY
                CASE WHEN p.ClosedAt IS NULL THEN 0 ELSE 1 END,
                p.Year DESC,
                p.Id DESC;
            """;

        var result = await conn.QueryAsync<ProjectSwitcherItem>(sql, new { UserId = userId });
        return result.ToList();
    }

    /// <summary>
    /// 取得單一專案的完整詳細資料，供專案管理頁右側詳情區顯示。
    /// </summary>
    public async Task<ProjectDetailDto?> GetProjectDetailAsync(int projectId)
    {
        using var conn = _db.CreateConnection();
        var detailParams = new { ProjectId = projectId };

        const string multipleSql = """
            -- 1. Project Detail
            SELECT
                p.Id, p.ProjectCode, p.Name, p.Year, p.School,
                p.StartDate, p.EndDate, p.ClosedAt,
                (SELECT TOP 1 ph.StartDate FROM dbo.MT_ProjectPhases ph WHERE ph.ProjectId = p.Id AND ph.PhaseCode = 2) AS CompositionStartDate,
                ISNULL(u.DisplayName, N'系統') AS CreatorName,
                ISNULL(p.ProjectType, 0) AS ProjectType,
                p.ExamLevel
            FROM dbo.MT_Projects p
            LEFT JOIN dbo.MT_Users u ON u.Id = p.CreatedBy
            WHERE p.Id = @ProjectId AND p.IsDeleted = 0;

            -- 2. Phases
            SELECT PhaseCode, PhaseName, StartDate, EndDate
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId
            ORDER BY SortOrder;

            -- 3. Targets（TypeName 由 IQuestionTypeCatalog 在 C# 端補；帶 Granularity/Level 供雙模式渲染）
            SELECT pt.QuestionTypeId, ISNULL(pt.Granularity, 0) AS Granularity, pt.Level, pt.TargetCount
            FROM dbo.MT_ProjectTargets pt
            WHERE pt.ProjectId = @ProjectId
            ORDER BY pt.QuestionTypeId, pt.Granularity, pt.Level;

            -- 4. Members and Roles（帶 Category 供 UI 配色）
            SELECT
                pm.UserId,
                ISNULL(u.DisplayName, N'未知') AS DisplayName,
                t.TeacherCode,
                r.Name AS RoleName,
                r.Category AS RoleCategory
            FROM dbo.MT_ProjectMembers pm
            LEFT JOIN dbo.MT_Users u ON u.Id = pm.UserId
            LEFT JOIN dbo.MT_Teachers t ON t.UserId = u.Id
            LEFT JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
            LEFT JOIN dbo.MT_Roles r ON r.Id = pmr.RoleId
            WHERE pm.ProjectId = @ProjectId
            ORDER BY pm.Id, r.Id;
            """;

        try
        {
            using var multi = await conn.QueryMultipleAsync(multipleSql, detailParams);
            
            var detail = await multi.ReadFirstOrDefaultAsync<ProjectDetailDto>();
            if (detail is null)
                return null;
                
            detail.Phases = (await multi.ReadAsync<PhaseDetailDto>()).ToList();
            // 詳情頁固定顯示完整題型結構（含 0 的項目）— 對 DB 列 + 標準模板做 left-join 式擴展，
            // 避免老專案缺欄、或使用者刻意填 0 時詳情卡片空缺造成「資料不一致」的視覺錯覺
            detail.Targets = ExpandToFullTargetList(
                (await multi.ReadAsync<TargetDetailDto>()).ToList(),
                detail.ProjectType);
            foreach (var t in detail.Targets)
            {
                t.TypeName = _typeCatalog.GetName(t.QuestionTypeId);
                t.DisplayLabel = detail.ProjectType == ProjectType.Lct
                    ? (t.QuestionTypeId == 7
                        ? "聽力題組"
                        : $"難度{ChineseOrdinal(t.Level ?? 0)}")
                    : BuildCwtTargetLabel(t.QuestionTypeId, t.Granularity, _typeCatalog.GetName(t.QuestionTypeId));
            }
            
            var memberRows = await multi.ReadAsync<ProjectMemberRow>();
            detail.Members = memberRows
                .GroupBy(m => new { m.UserId, m.DisplayName, m.TeacherCode })
                .Select(g => new MemberDetailDto
                {
                    UserId = g.Key.UserId,
                    DisplayName = g.Key.DisplayName,
                    TeacherCode = g.Key.TeacherCode,
                    Roles = g.Where(x => !string.IsNullOrWhiteSpace(x.RoleName))
                        .Select(x => new RoleTag(x.RoleName!, x.RoleCategory ?? 0))
                        .GroupBy(r => r.Name)
                        .Select(grp => grp.First())
                        .ToList()
                }).ToList();

            // 批次查「有可下載聘書」的 UserId 集合（給成員下載按鈕條件渲染用）
            if (detail.Members.Count > 0)
            {
                var downloadableUserIds = await _appointmentSvc.GetDownloadableUserIdsInProjectAsync(projectId);
                foreach (var m in detail.Members)
                {
                    m.HasDownloadableCerts = downloadableUserIds.Contains(m.UserId);
                }
            }

            return detail;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入專案詳情失敗 (Id={ProjectId})", projectId);
            throw;
        }
    }

    /// <summary>
    /// 取得目前可分派進專案的人才池教師清單。
    /// </summary>
    public async Task<List<ProjectTalentPoolItem>> GetTalentPoolAsync()
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT
                u.Id AS UserId,
                u.DisplayName AS Name,
                t.TeacherCode AS Identifier
            FROM dbo.MT_Teachers t
            INNER JOIN dbo.MT_Users u ON u.Id = t.UserId
            WHERE u.Status = 1
            ORDER BY t.TeacherCode, u.DisplayName;
            """;

        var result = await conn.QueryAsync<ProjectTalentPoolItem>(sql);
        return result.ToList();
    }

    /// <summary>
    /// 取得專案指派人員可選用的外部角色清單。
    /// </summary>
    public async Task<List<RoleOption>> GetProjectRoleOptionsAsync()
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            SELECT Id, Name, Category, IsDefault
            FROM dbo.MT_Roles
            WHERE Category = 1
            ORDER BY IsDefault DESC, Id;
            """;

        var result = await conn.QueryAsync<RoleOption>(sql);
        return result.ToList();
    }

    /// <summary>
    /// 將指定專案標記為軟刪除，並記錄稽核資料與推播即時更新。
    /// </summary>
    public async Task SoftDeleteProjectAsync(int projectId, int deletedBy)
    {
        using var conn = _db.CreateConnection();

        const string sql = """
            UPDATE dbo.MT_Projects
            SET IsDeleted = 1,
                DeletedAt = GETDATE()
            WHERE Id = @ProjectId;
            """;

        await conn.ExecuteAsync(sql, new { ProjectId = projectId });

        // 全站活動：MT_AuditLogs.ProjectId 留 NULL（SystemLogs「專案變動」Tab 才撈得到）
        const string auditSql = """
            INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, OldValue, IpAddress)
            VALUES (@UserId, @Action, @TargetType, @TargetId, @OldValue, @IpAddress);
            """;

        await conn.ExecuteAsync(auditSql, new
        {
            UserId = deletedBy,
            Action = (byte)AuditAction.Delete,
            TargetType = (byte)AuditTargetType.Projects,
            TargetId = projectId,
            OldValue = AuditLogJsonHelper.Serialize(new { targetDisplayName = $"專案 #{projectId}" }),
            IpAddress = ClientIpResolver.Resolve(_httpContextAccessor)
        });

        await BroadcastProjectChangedAsync(ProjectRealtimeChangeType.Deleted, projectId);
    }

    /// <summary>
    /// 結案入庫：標記梯次為結案，並批次更新該梯次內所有未刪除題目的狀態。
    /// 邏輯（同一 Serializable transaction 內原子提交）：
    ///   1. MT_Projects.ClosedAt 設為現在時間（已結案者直接拋例外）
    ///   2. 母題 Status = 9 (採用)        → 12 (結案入庫 / Archived)
    ///   3. 母題 Status ∉ {9, 11, 12} 且 IsDeleted = 0 → 11 (結案未採用 / ClosedNotAdopted)
    ///   ── Stage B-4-3：子題各自獨立結案 ──
    ///   4. 子題（母題已升 12=Archived）：子題 Status = 9 → 12（採用子題保留入庫）
    ///   5. 子題其餘非結案者一律 → 11（含母題已結案未採用的全部子題，及母題已結案但子題未採用者）
    ///   6. 寫一筆 MT_AuditLogs（Action = Update / Target = Projects）
    /// 任何一步失敗整批 rollback；成功後 SignalR 廣播 ProjectChanged。
    ///
    /// 落實 Plan_022 Q2 規則：母題採用 → 只入採用子題；母題不採用 → 整題組（含子題）全丟棄。
    /// </summary>
    public async Task CloseProjectAsync(int projectId, int closedBy)
    {
        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        using var trans = await conn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            // 1) 梯次主檔：標記結案（已結案者保持原 ClosedAt 不變）
            const string closeSql = """
                UPDATE dbo.MT_Projects
                SET ClosedAt = GETDATE(),
                    UpdatedAt = SYSDATETIME()
                WHERE Id = @ProjectId
                  AND IsDeleted = 0
                  AND ClosedAt IS NULL;
                """;

            var affected = await conn.ExecuteAsync(closeSql, new { ProjectId = projectId }, trans);
            if (affected == 0)
            {
                throw new InvalidOperationException("專案已結案或不存在，無法重複結案。");
            }

            // 2a) 採用母題升為「結案入庫」(Archived = 12)
            const string archiveSql = """
                UPDATE dbo.MT_Questions
                SET Status = 12,
                    UpdatedAt = SYSDATETIME()
                WHERE ProjectId = @ProjectId
                  AND IsDeleted = 0
                  AND Status = 9;
                """;
            await conn.ExecuteAsync(archiveSql, new { ProjectId = projectId }, trans);

            // 2b) 其餘非採用、非已結案母題改為「結案未採用」(ClosedNotAdopted = 11)
            const string updateQuestionsSql = """
                UPDATE dbo.MT_Questions
                SET Status = 11,
                    UpdatedAt = SYSDATETIME()
                WHERE ProjectId = @ProjectId
                  AND IsDeleted = 0
                  AND Status NOT IN (9, 11, 12);
                """;
            await conn.ExecuteAsync(updateQuestionsSql, new { ProjectId = projectId }, trans);

            // ── Stage B-4-3：子題各自獨立結案 ──
            // 規則彙整：
            //   母題 Archived(12) + 子題 Adopted(9)         → 子題 Archived(12)（採用入庫）
            //   母題 Archived(12) + 子題 != Adopted         → 子題 ClosedNotAdopted(11)
            //   母題 ClosedNotAdopted(11)                    → 所有子題 ClosedNotAdopted(11)（整題組丟棄）
            // 兩個 UPDATE 完成上述邏輯（先取採用，後一律降 11）

            // 3a) 採用子題且母題也採用 → 升 12，並寫 DecidedAt
            const string archiveSubSql = """
                UPDATE sq
                SET sq.Status    = 12,
                    sq.DecidedAt = CASE WHEN sq.DecidedAt IS NULL THEN SYSDATETIME() ELSE sq.DecidedAt END
                FROM dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                WHERE q.ProjectId = @ProjectId
                  AND q.IsDeleted = 0
                  AND q.Status    = 12   -- 母題剛升 Archived
                  AND sq.Status   = 9;   -- 該子題自身為 Adopted
                """;
            await conn.ExecuteAsync(archiveSubSql, new { ProjectId = projectId }, trans);

            // 3b) 其餘子題（含「母題不採用」的全部子題、及「母題採用但子題未採用」的子題）
            //     一律 → 11（ClosedNotAdopted），並寫 DecidedAt
            const string closedNotAdoptedSubSql = """
                UPDATE sq
                SET sq.Status    = 11,
                    sq.DecidedAt = CASE WHEN sq.DecidedAt IS NULL THEN SYSDATETIME() ELSE sq.DecidedAt END
                FROM dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                WHERE q.ProjectId = @ProjectId
                  AND q.IsDeleted = 0
                  AND sq.Status NOT IN (11, 12);
                """;
            await conn.ExecuteAsync(closedNotAdoptedSubSql, new { ProjectId = projectId }, trans);

            // 4) 稽核紀錄（全站活動：ProjectId 留 NULL）
            const string auditSql = """
                INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue, IpAddress)
                VALUES (@UserId, @Action, @TargetType, @TargetId, @NewValue, @IpAddress);
                """;

            await conn.ExecuteAsync(auditSql, new
            {
                UserId = closedBy,
                Action = (byte)AuditAction.Update,
                TargetType = (byte)AuditTargetType.Projects,
                TargetId = projectId,
                NewValue = AuditLogJsonHelper.Serialize(new { action = "提前結案入庫" }),
                IpAddress = ClientIpResolver.Resolve(_httpContextAccessor)
            }, trans);

            await trans.CommitAsync();
        }
        catch
        {
            await trans.RollbackAsync();
            throw;
        }

        await BroadcastProjectChangedAsync(ProjectRealtimeChangeType.Updated, projectId);
    }

    /// <summary>
    /// 建立專案主檔與其相關設定資料，完成後回傳新專案識別碼。
    /// </summary>
    public async Task<int> CreateProjectAsync(CreateProjectRequest req)
    {
        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        using var trans = await conn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            const string latestCodeSql = """
                SELECT TOP 1 ProjectCode
                FROM dbo.MT_Projects WITH (UPDLOCK, HOLDLOCK)
                WHERE Year = @Year
                ORDER BY Id DESC;
                """;

            var year = int.Parse(req.Year);
            var latestCode = await conn.QueryFirstOrDefaultAsync<string>(
                latestCodeSql,
                new { Year = year },
                transaction: trans);

            var projectCode = BuildNextProjectCode(req.Year, latestCode);

            const string projectSql = """
                INSERT INTO dbo.MT_Projects (ProjectCode, Name, Year, School, StartDate, EndDate, CreatedBy, ProjectType, ExamLevel)
                OUTPUT INSERTED.Id
                VALUES (@ProjectCode, @Name, @Year, @School, @StartDate, @EndDate, @CreatedBy, @ProjectType, @ExamLevel);
                """;

            var projectStartDate = req.Phases.FirstOrDefault(p => p.PhaseCode == 1)?.StartDate ?? DateTime.Today;
            var projectEndDate = req.Phases.OrderByDescending(p => p.PhaseCode).FirstOrDefault()?.EndDate ?? DateTime.Today.AddMonths(2);

            var projectId = await conn.QuerySingleAsync<int>(
                projectSql,
                new
                {
                    ProjectCode = projectCode,
                    Name = req.Name,
                    Year = year,
                    School = req.School,
                    StartDate = projectStartDate,
                    EndDate = projectEndDate,
                    CreatedBy = req.CreatedBy,
                    ProjectType = (byte)req.ProjectType,
                    // LCT 模式統一寫 NULL；CWT 模式應填 0~4。若 UI 漏帶值也以 NULL 入庫，避免污染
                    ExamLevel = req.ProjectType == ProjectType.Cwt ? req.ExamLevel : (byte?)null
                },
                transaction: trans);

            await ReplaceProjectChildRecordsAsync(
                conn,
                trans,
                projectId,
                req.Phases,
                req.Targets,
                req.MemberAllocations,
                shouldClearExisting: false);

            // 同步聘書 metadata（新成員 → INSERT 占位；FileName 由 client 繪製後上傳補上）
            await _appointmentSvc.SyncCertificatesAsync(projectId, conn, trans);

            // 全站活動：ProjectId 留 NULL；改用 AuditLogJsonHelper（camelCase + targetDisplayName）
            var jsonValue = AuditLogJsonHelper.Serialize(new
            {
                projectId,
                name = req.Name,
                projectCode,
                targetDisplayName = req.Name   // SystemLogs / Dashboard 反查失敗時用此 key fallback
            });

            const string auditSql = """
                INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, NewValue, IpAddress)
                VALUES (@UserId, @Action, @TargetType, @TargetId, @NewValue, @IpAddress);
                """;

            await conn.ExecuteAsync(
                auditSql,
                new
                {
                    UserId = req.CreatedBy,
                    Action = (byte)AuditAction.Create,
                    TargetType = (byte)AuditTargetType.Projects,
                    TargetId = projectId,
                    NewValue = jsonValue,
                    IpAddress = ClientIpResolver.Resolve(_httpContextAccessor)
                },
                transaction: trans);

            trans.Commit();

            await BroadcastProjectChangedAsync(ProjectRealtimeChangeType.Created, projectId);

            return projectId;
        }
        catch (Exception ex)
        {
            trans.Rollback();
            _logger.LogError(ex, "建立專案失敗");
            throw;
        }
    }

    /// <summary>
    /// 更新既有專案主檔與相關設定資料，完成後寫入稽核並推播即時同步。
    /// </summary>
    public async Task UpdateProjectAsync(UpdateProjectRequest req)
    {
        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();

        using var trans = await conn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var oldSnapshot = await GetProjectEditAsync(conn, trans, req.ProjectId);
            if (oldSnapshot is null)
            {
                throw new InvalidOperationException("找不到要編輯的專案，或該專案已被移除。");
            }

            // 命題階段（PhaseCode=2）結束後鎖定：題數需求 / 配額 / 命題教師新增
            var compositionPhaseEnd = oldSnapshot.Phases
                .FirstOrDefault(p => p.PhaseCode == 2)?.EndDate;
            if (compositionPhaseEnd is DateTime compEnd && compEnd < DateTime.Today)
            {
                EnsureCompositionPhaseLockRespected(oldSnapshot, req);
                await EnsureNoNewPropositionTeacherAsync(conn, trans, oldSnapshot, req);
            }

            var year = int.Parse(req.Year);
            var projectStartDate = req.Phases.FirstOrDefault(p => p.PhaseCode == 1)?.StartDate ?? DateTime.Today;
            var projectEndDate = req.Phases.OrderByDescending(p => p.PhaseCode).FirstOrDefault()?.EndDate ?? DateTime.Today.AddMonths(2);

            const string projectSql = """
                UPDATE dbo.MT_Projects
                SET Name = @Name,
                    Year = @Year,
                    School = @School,
                    StartDate = @StartDate,
                    EndDate = @EndDate,
                    UpdatedAt = SYSDATETIME()
                WHERE Id = @ProjectId
                  AND IsDeleted = 0;
                """;

            var affectedRows = await conn.ExecuteAsync(
                projectSql,
                new
                {
                    ProjectId = req.ProjectId,
                    Name = req.Name,
                    Year = year,
                    School = req.School,
                    StartDate = projectStartDate,
                    EndDate = projectEndDate
                },
                transaction: trans);

            if (affectedRows == 0)
            {
                throw new InvalidOperationException("找不到要更新的專案，或該專案已被移除。");
            }

            await ReplaceProjectChildRecordsAsync(
                conn,
                trans,
                req.ProjectId,
                req.Phases,
                req.Targets,
                req.MemberAllocations,
                shouldClearExisting: true);

            // 同步聘書 metadata（新增/撤銷/恢復；新增者佔位等 client 繪製上傳）
            await _appointmentSvc.SyncCertificatesAsync(req.ProjectId, conn, trans);

            var newSnapshot = await GetProjectEditAsync(conn, trans, req.ProjectId)
                ?? throw new InvalidOperationException("專案更新後無法重新讀取資料。");

            // 全站活動：ProjectId 留 NULL；改用 AuditLogJsonHelper 統一序列化
            const string auditSql = """
                INSERT INTO dbo.MT_AuditLogs (UserId, Action, TargetType, TargetId, OldValue, NewValue, IpAddress)
                VALUES (@UserId, @Action, @TargetType, @TargetId, @OldValue, @NewValue, @IpAddress);
                """;

            await conn.ExecuteAsync(
                auditSql,
                new
                {
                    UserId = req.UpdatedBy,
                    Action = (byte)AuditAction.Update,
                    TargetType = (byte)AuditTargetType.Projects,
                    TargetId = req.ProjectId,
                    OldValue = AuditLogJsonHelper.Serialize(oldSnapshot),
                    NewValue = AuditLogJsonHelper.Serialize(newSnapshot),
                    IpAddress = ClientIpResolver.Resolve(_httpContextAccessor)
                },
                transaction: trans);

            trans.Commit();

            await BroadcastProjectChangedAsync(ProjectRealtimeChangeType.Updated, req.ProjectId);
        }
        catch (Exception ex)
        {
            trans.Rollback();
            _logger.LogError(ex, "更新專案失敗 (Id={ProjectId})", req.ProjectId);
            throw;
        }
    }

    /// <summary>
    /// CWT 模式題型目標的顯示標籤（閱讀/短文題組需區分母/子題）。
    /// </summary>
    private static string BuildCwtTargetLabel(int typeId, byte granularity, string typeName)
    {
        // typeId=3:閱讀題組, typeId=5:短文題組 才需要區分母/子題
        if (typeId is 3 or 5)
        {
            return granularity == 1 ? $"{typeName}（子題）" : $"{typeName}（母題）";
        }
        return typeName;
    }

    /// <summary>
    /// 詳情頁固定模板（QuestionTypeId, Granularity, Level）：
    /// CWT — 6 項（一般 / 閱讀母+子 / 長文 / 短文母+子）；
    /// LCT — 6 項（聽力題目難度一~五 + 聽力題組整體）。
    /// 與 <see cref="ExpandToFullTargetList"/> 搭配使用，缺項補 0。
    /// </summary>
    private static readonly (int TypeId, byte Granularity, byte? Level)[] CwtTargetTemplate =
    [
        (1, 0, null),  // 一般單選題
        (3, 0, null),  // 閱讀題組母題
        (3, 1, null),  // 閱讀題組子題
        (4, 0, null),  // 長文題目
        (5, 0, null),  // 短文題組母題
        (5, 1, null),  // 短文題組子題
    ];

    private static readonly (int TypeId, byte Granularity, byte? Level)[] LctTargetTemplate =
    [
        (6, 0, 1),     // 聽力題目 難度一
        (6, 0, 2),     // 聽力題目 難度二
        (6, 0, 3),     // 聽力題目 難度三
        (6, 0, 4),     // 聽力題目 難度四
        (6, 0, 5),     // 聽力題目 難度五
        (7, 0, null),  // 聽力題組（整組，固定母題 + 難三/難四 2 子題）
    ];

    /// <summary>
    /// 以固定模板對 DB 載入的 Targets 做 left-join 擴展，模板存在但 DB 沒有的項目補 TargetCount=0。
    /// 保證詳情頁卡片數量永遠一致（避免老資料缺欄或 0 值被 INSERT 過濾後消失造成的視覺錯亂）。
    /// </summary>
    private static List<TargetDetailDto> ExpandToFullTargetList(
        List<TargetDetailDto> dbRows,
        ProjectType projectType)
    {
        var template = projectType == ProjectType.Lct ? LctTargetTemplate : CwtTargetTemplate;
        var result = new List<TargetDetailDto>(template.Length);
        foreach (var (typeId, granularity, level) in template)
        {
            var existing = dbRows.FirstOrDefault(r =>
                r.QuestionTypeId == typeId &&
                r.Granularity == granularity &&
                r.Level == level);
            result.Add(existing ?? new TargetDetailDto
            {
                QuestionTypeId = typeId,
                Granularity = granularity,
                Level = level,
                TargetCount = 0
            });
        }
        return result;
    }

    /// <summary>
    /// 數字轉中文序數（1→一, 2→二, …, 5→五）。LCT 難度顯示用。
    /// </summary>
    private static string ChineseOrdinal(int n) => n switch
    {
        1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五", _ => n.ToString()
    };

    /// <summary>
    /// 依年度與目前最新流水號，產生下一個專案代碼。
    /// </summary>
    private static string BuildNextProjectCode(string rocYear, string? latestCode)
    {
        var normalizedYear = rocYear.Trim();
        var seq = 1;

        var expectedPrefix = $"P{normalizedYear}";
        if (!string.IsNullOrWhiteSpace(latestCode) &&
            latestCode.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) &&
            latestCode.Length >= expectedPrefix.Length + 3 &&
            int.TryParse(latestCode[^3..], out var lastSeq))
        {
            seq = lastSeq + 1;
        }

        return $"{expectedPrefix}{seq:D3}";
    }

    /// <summary>
    /// 透過 SignalR 廣播專案異動事件，通知前端同步更新畫面。
    /// </summary>
    private Task BroadcastProjectChangedAsync(ProjectRealtimeChangeType changeType, int projectId)
    {
        return _projectsHubContext.Clients.All.SendAsync(
            "ReceiveProjectChanged",
            new ProjectRealtimeSyncMessage(changeType, projectId));
    }

    private async Task<ProjectEditDto?> GetProjectEditAsync(IDbConnection conn, IDbTransaction? transaction, int projectId)
    {
        const string sql = """
            SELECT
                p.Id,
                p.ProjectCode,
                p.Year,
                p.Name,
                p.School,
                p.ClosedAt,
                ISNULL(p.ProjectType, 0) AS ProjectType,
                p.ExamLevel
            FROM dbo.MT_Projects p
            WHERE p.Id = @ProjectId
              AND p.IsDeleted = 0;

            SELECT
                PhaseCode,
                PhaseName AS Name,
                StartDate,
                EndDate
            FROM dbo.MT_ProjectPhases
            WHERE ProjectId = @ProjectId
            ORDER BY SortOrder;

            SELECT
                QuestionTypeId,
                ISNULL(Granularity, 0) AS Granularity,
                Level,
                TargetCount
            FROM dbo.MT_ProjectTargets
            WHERE ProjectId = @ProjectId
            ORDER BY QuestionTypeId, Granularity, Level;

            SELECT
                pm.Id AS ProjectMemberId,
                pm.UserId
            FROM dbo.MT_ProjectMembers pm
            WHERE pm.ProjectId = @ProjectId
            ORDER BY pm.Id;

            SELECT
                pmr.ProjectMemberId,
                pmr.RoleId
            FROM dbo.MT_ProjectMemberRoles pmr
            INNER JOIN dbo.MT_ProjectMembers pm ON pm.Id = pmr.ProjectMemberId
            WHERE pm.ProjectId = @ProjectId
            ORDER BY pmr.ProjectMemberId, pmr.RoleId;

            SELECT
                mq.ProjectMemberId,
                mq.QuestionTypeId,
                ISNULL(mq.Granularity, 0) AS Granularity,
                mq.Level,
                mq.QuotaCount
            FROM dbo.MT_MemberQuotas mq
            INNER JOIN dbo.MT_ProjectMembers pm ON pm.Id = mq.ProjectMemberId
            WHERE pm.ProjectId = @ProjectId
            ORDER BY mq.ProjectMemberId, mq.QuestionTypeId, mq.Granularity, mq.Level;

            SELECT
                q.CreatorId AS UserId,
                COUNT(*) AS QuestionCount
            FROM dbo.MT_Questions q
            WHERE q.ProjectId = @ProjectId
              AND q.IsDeleted = 0
            GROUP BY q.CreatorId;
            """;

        using var multi = await conn.QueryMultipleAsync(sql, new { ProjectId = projectId }, transaction);

        var project = await multi.ReadFirstOrDefaultAsync<ProjectEditDto>();
        if (project is null)
        {
            return null;
        }

        project.Phases = (await multi.ReadAsync<ProjectPhaseDto>()).ToList();
        project.Targets = (await multi.ReadAsync<ProjectTargetDto>()).ToList();

        var memberRows = (await multi.ReadAsync<ProjectEditMemberRow>()).ToList();
        var roleRows = (await multi.ReadAsync<ProjectEditMemberRoleRow>()).ToList();
        var quotaRows = (await multi.ReadAsync<ProjectEditMemberQuotaRow>()).ToList();
        var questionCountRows = (await multi.ReadAsync<ProjectEditMemberQuestionCountRow>()).ToList();
        var questionCountByUser = questionCountRows.ToDictionary(r => r.UserId, r => r.QuestionCount);

        project.MemberAllocations = memberRows
            .Select(member => new ProjectMemberAllocationDto
            {
                UserId = member.UserId,
                RoleIds = roleRows
                    .Where(role => role.ProjectMemberId == member.ProjectMemberId)
                    .Select(role => role.RoleId)
                    .ToList(),
                Quotas = quotaRows
                    .Where(quota => quota.ProjectMemberId == member.ProjectMemberId)
                    .Select(quota => new ProjectMemberQuotaDto
                    {
                        QuestionTypeId = quota.QuestionTypeId,
                        Granularity = quota.Granularity,
                        Level = quota.Level,
                        QuotaCount = quota.QuotaCount
                    })
                    .ToList(),
                CreatedQuestionCount = questionCountByUser.TryGetValue(member.UserId, out var cnt) ? cnt : 0
            })
            .ToList();

        return project;
    }

    private async Task ReplaceProjectChildRecordsAsync(
        IDbConnection conn,
        IDbTransaction transaction,
        int projectId,
        IEnumerable<ProjectPhaseDto> phases,
        IEnumerable<ProjectTargetDto> targets,
        IEnumerable<ProjectMemberAllocationDto> memberAllocations,
        bool shouldClearExisting)
    {
        if (shouldClearExisting)
        {
            const string deleteSql = """
                DELETE mq
                FROM dbo.MT_MemberQuotas mq
                INNER JOIN dbo.MT_ProjectMembers pm ON pm.Id = mq.ProjectMemberId
                WHERE pm.ProjectId = @ProjectId;

                DELETE pmr
                FROM dbo.MT_ProjectMemberRoles pmr
                INNER JOIN dbo.MT_ProjectMembers pm ON pm.Id = pmr.ProjectMemberId
                WHERE pm.ProjectId = @ProjectId;

                DELETE FROM dbo.MT_ProjectMembers
                WHERE ProjectId = @ProjectId;

                DELETE FROM dbo.MT_ProjectTargets
                WHERE ProjectId = @ProjectId;

                DELETE FROM dbo.MT_ProjectPhases
                WHERE ProjectId = @ProjectId;
                """;

            await conn.ExecuteAsync(deleteSql, new { ProjectId = projectId }, transaction: transaction);
        }

        // 批次 INSERT Phases（典型 8 列：產學區間 + 7 階段）— 1 round-trip
        var phaseList = phases.ToList();
        if (phaseList.Count > 0)
        {
            var sb = new System.Text.StringBuilder(
                "INSERT INTO dbo.MT_ProjectPhases (ProjectId, PhaseCode, PhaseName, StartDate, EndDate, SortOrder) VALUES ");
            var args = new DynamicParameters();
            args.Add("ProjectId", projectId);
            for (var i = 0; i < phaseList.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"(@ProjectId, @C{i}, @N{i}, @S{i}, @E{i}, @C{i})");
                args.Add($"C{i}", phaseList[i].PhaseCode);
                args.Add($"N{i}", phaseList[i].Name);
                args.Add($"S{i}", phaseList[i].StartDate);
                args.Add($"E{i}", phaseList[i].EndDate);
            }
            await conn.ExecuteAsync(sb.ToString(), args, transaction: transaction);
        }

        // 批次 INSERT Targets（CWT ~7 列 / LCT ~6 列，過濾掉 TargetCount=0）— 1 round-trip
        var targetList = targets.Where(t => t.TargetCount > 0).ToList();
        if (targetList.Count > 0)
        {
            var sb = new System.Text.StringBuilder(
                "INSERT INTO dbo.MT_ProjectTargets (ProjectId, QuestionTypeId, Granularity, Level, TargetCount) VALUES ");
            var args = new DynamicParameters();
            args.Add("ProjectId", projectId);
            for (var i = 0; i < targetList.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"(@ProjectId, @T{i}, @G{i}, @L{i}, @C{i})");
                args.Add($"T{i}", targetList[i].QuestionTypeId);
                args.Add($"G{i}", targetList[i].Granularity);
                args.Add($"L{i}", targetList[i].Level);
                args.Add($"C{i}", targetList[i].TargetCount);
            }
            await conn.ExecuteAsync(sb.ToString(), args, transaction: transaction);
        }

        var allocList = memberAllocations.Where(a => a.UserId > 0).ToList();
        if (allocList.Count == 0) return;

        // 批次 INSERT Members + OUTPUT 取回 (Id, UserId) 映射 — 1 round-trip
        // 註：SQL Server 單一指令參數上限 2100；典型梯次 ~30 教師遠低於此
        var memberIdByUserId = await BulkInsertMembersAsync(conn, transaction, projectId, allocList);

        // 批次 INSERT 所有 Roles（跨成員合併）— 1 round-trip
        await BulkInsertMemberRolesAsync(conn, transaction, allocList, memberIdByUserId);

        // 批次 INSERT 所有 Quotas（跨成員合併）— 1 round-trip
        await BulkInsertMemberQuotasAsync(conn, transaction, allocList, memberIdByUserId);
    }

    /// <summary>
    /// 批次 INSERT MT_ProjectMembers，回傳 UserId → MemberId 字典。
    /// 用 OUTPUT INSERTED.Id, INSERTED.UserId 一次拿全部映射，後續 Roles / Quotas 用此字典配對。
    /// </summary>
    private static async Task<Dictionary<int, int>> BulkInsertMembersAsync(
        IDbConnection conn,
        IDbTransaction transaction,
        int projectId,
        IReadOnlyList<ProjectMemberAllocationDto> allocList)
    {
        var sb = new System.Text.StringBuilder(
            "INSERT INTO dbo.MT_ProjectMembers (ProjectId, UserId) OUTPUT INSERTED.Id, INSERTED.UserId VALUES ");
        var args = new DynamicParameters();
        args.Add("ProjectId", projectId);
        for (var i = 0; i < allocList.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"(@ProjectId, @U{i})");
            args.Add($"U{i}", allocList[i].UserId);
        }
        var rows = await conn.QueryAsync<MemberInsertRow>(sb.ToString(), args, transaction: transaction);
        return rows.ToDictionary(r => r.UserId, r => r.Id);
    }

    /// <summary>
    /// 批次 INSERT MT_ProjectMemberRoles，合併所有成員的角色為單一 INSERT。
    /// </summary>
    private static async Task BulkInsertMemberRolesAsync(
        IDbConnection conn,
        IDbTransaction transaction,
        IReadOnlyList<ProjectMemberAllocationDto> allocList,
        IReadOnlyDictionary<int, int> memberIdByUserId)
    {
        var sb = new System.Text.StringBuilder();
        var args = new DynamicParameters();
        var idx = 0;
        foreach (var alloc in allocList)
        {
            var memberId = memberIdByUserId[alloc.UserId];
            foreach (var roleId in alloc.RoleIds.Where(id => id > 0).Distinct())
            {
                if (idx > 0) sb.Append(", ");
                sb.Append($"(@M{idx}, @R{idx})");
                args.Add($"M{idx}", memberId);
                args.Add($"R{idx}", roleId);
                idx++;
            }
        }
        if (idx == 0) return;
        await conn.ExecuteAsync(
            "INSERT INTO dbo.MT_ProjectMemberRoles (ProjectMemberId, RoleId) VALUES " + sb,
            args, transaction: transaction);
    }

    /// <summary>
    /// 批次 INSERT MT_MemberQuotas，合併所有成員的配額為單一 INSERT。
    /// </summary>
    private static async Task BulkInsertMemberQuotasAsync(
        IDbConnection conn,
        IDbTransaction transaction,
        IReadOnlyList<ProjectMemberAllocationDto> allocList,
        IReadOnlyDictionary<int, int> memberIdByUserId)
    {
        var sb = new System.Text.StringBuilder();
        var args = new DynamicParameters();
        var idx = 0;
        foreach (var alloc in allocList)
        {
            var memberId = memberIdByUserId[alloc.UserId];
            foreach (var quota in alloc.Quotas.Where(q => q.QuotaCount > 0))
            {
                if (idx > 0) sb.Append(", ");
                sb.Append($"(@M{idx}, @T{idx}, @G{idx}, @L{idx}, @C{idx})");
                args.Add($"M{idx}", memberId);
                args.Add($"T{idx}", quota.QuestionTypeId);
                args.Add($"G{idx}", quota.Granularity);
                args.Add($"L{idx}", quota.Level);
                args.Add($"C{idx}", quota.QuotaCount);
                idx++;
            }
        }
        if (idx == 0) return;
        await conn.ExecuteAsync(
            "INSERT INTO dbo.MT_MemberQuotas (ProjectMemberId, QuestionTypeId, Granularity, Level, QuotaCount) VALUES " + sb,
            args, transaction: transaction);
    }

    private sealed record MemberInsertRow(int Id, int UserId);

    /// <summary>
    /// 取得指定專案的 7 個實作階段（PhaseCode 2~8：命題 / 互審 / 互修 / 專審 / 專修 / 總審 / 總修）。
    /// 排除 PhaseCode = 1 的「產學計畫區間」框架。
    /// </summary>
    public async Task<List<ProjectPhaseInfo>> GetPhasesAsync(int projectId)
    {
        const string sql = """
            SELECT
                ph.PhaseCode,
                ph.PhaseName,
                ph.StartDate,
                ph.EndDate,
                DATEDIFF(DAY, CAST(GETDATE() AS DATE), ph.EndDate) AS DaysLeft,
                p.ClosedAt
            FROM dbo.MT_ProjectPhases ph
            INNER JOIN dbo.MT_Projects p ON p.Id = ph.ProjectId
            WHERE ph.ProjectId = @ProjectId
              AND ph.PhaseCode > 1
            ORDER BY ph.SortOrder;
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<ProjectPhaseInfo>(sql, new { ProjectId = projectId });
        return rows.AsList();
    }

    /// <summary>
    /// 取得指定專案「目前所在階段」：
    /// - 未結案：今日落入的階段（若不在任何階段區間，回傳 null）
    /// - 已結案：ClosedAt 落入的階段（讓審題頁仍能呈現結案位置）
    /// </summary>
    public async Task<ProjectPhaseInfo?> GetCurrentPhaseAsync(int projectId)
    {
        const string sql = """
            SELECT TOP 1
                ph.PhaseCode,
                ph.PhaseName,
                ph.StartDate,
                ph.EndDate,
                DATEDIFF(DAY, CAST(GETDATE() AS DATE), ph.EndDate) AS DaysLeft,
                p.ClosedAt
            FROM dbo.MT_ProjectPhases ph
            INNER JOIN dbo.MT_Projects p ON p.Id = ph.ProjectId
            WHERE ph.ProjectId = @ProjectId
              AND ph.PhaseCode > 1
              AND (
                    -- 未結案：以今日為基準
                    (p.ClosedAt IS NULL AND CAST(GETDATE() AS DATE) BETWEEN ph.StartDate AND ph.EndDate)
                    -- 已結案：以 ClosedAt 為基準
                 OR (p.ClosedAt IS NOT NULL AND CAST(p.ClosedAt AS DATE) BETWEEN ph.StartDate AND ph.EndDate)
              )
            ORDER BY ph.SortOrder;
            """;

        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<ProjectPhaseInfo>(sql, new { ProjectId = projectId });
    }

    /// <summary>
    /// 取得「下載結案資料」EXCEL 所需資料；僅已結案梯次返回實際資料列。
    /// SQL 結構：QueryMultiple 兩段——
    ///   1) 梯次資訊 (Name, ExamLevel)
    ///   2) 母題 + 子題各一份結果集（UNION ALL，並各自 OUTER APPLY 3 個 stage 取最後一筆 ReviewAssignment）
    /// </summary>
    public async Task<ClosedProjectExportData?> GetClosedProjectExportDataAsync(int projectId)
    {
        // 兩段在同一 QueryMultiple 內，第 2 段同時涵蓋母題 + 子題並一起排序。
        // 排序鍵：QuestionTypeId 升序 (同題型相鄰) → MasterQuestionId 升序 (題目順序) → SubOrder 升序 (子題接在母題後)。
        const string sql = """
            -- Sect 1: 梯次資訊（含 ProjectType 給 mapping/UI 切版用）
            SELECT TOP 1
                p.Name AS ProjectName,
                p.ExamLevel,
                ISNULL(p.ProjectType, 0) AS ProjectType
            FROM dbo.MT_Projects p
            WHERE p.Id = @ProjectId AND p.ClosedAt IS NOT NULL;

            -- Sect 2: 母題 + 子題（已決策題目，Status IN 9,10,11,12）
            -- Difficulty 來源：
            --   CWT 全部走 q.Difficulty（0/1/2 → 易/中/難）
            --   LCT 聽力測驗單題 (TypeId=6) 走 q.Level（1-5 → 難度一~五）
            --   LCT 聽力題組母題 (TypeId=7) 為 NULL → C# 端會顯示「－」
            WITH RowSet AS (
                -- 母題層
                SELECT
                    q.QuestionCode,
                    NULL            AS SubSortOrder,
                    q.QuestionTypeId,
                    CAST(0 AS BIT) AS IsSubQuestion,
                    CASE
                        WHEN ISNULL(pj.ProjectType, 0) = 1 AND q.QuestionTypeId = 6 THEN q.Level
                        WHEN ISNULL(pj.ProjectType, 0) = 1 AND q.QuestionTypeId = 7 THEN NULL
                        ELSE q.Difficulty
                    END             AS Difficulty,
                    q.Stem          AS Stem,
                    q.ArticleContent AS ArticleContent,
                    q.UpdatedAt     AS UpdatedAt,
                    creator.DisplayName AS CreatorName,
                    peer.Reviewer   AS PeerReviewerName,
                    peer.CreatedAt  AS PeerReviewedAt,
                    expert.Reviewer AS ExpertReviewerName,
                    expert.CreatedAt AS ExpertReviewedAt,
                    expert.Decision AS ExpertDecision,
                    finalReview.Reviewer AS FinalReviewerName,
                    finalReview.DecidedAt AS FinalDecidedAt,
                    finalReview.Decision  AS FinalDecision,
                    q.Id            AS SortMasterId,
                    0               AS SortSubOrder
                FROM dbo.MT_Questions q
                INNER JOIN dbo.MT_Projects pj ON pj.Id = q.ProjectId
                INNER JOIN dbo.MT_Users creator ON creator.Id = q.CreatorId
                OUTER APPLY (
                    SELECT TOP 1 u.DisplayName AS Reviewer, ra.CreatedAt
                    FROM dbo.MT_ReviewAssignments ra
                    INNER JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
                    WHERE ra.QuestionId = q.Id AND ra.SubQuestionId IS NULL AND ra.ReviewStage = 1
                    ORDER BY ra.CreatedAt DESC
                ) peer
                OUTER APPLY (
                    SELECT TOP 1 u.DisplayName AS Reviewer, ra.CreatedAt, ra.Decision
                    FROM dbo.MT_ReviewAssignments ra
                    INNER JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
                    WHERE ra.QuestionId = q.Id AND ra.SubQuestionId IS NULL AND ra.ReviewStage = 2
                    ORDER BY ra.CreatedAt DESC
                ) expert
                OUTER APPLY (
                    SELECT TOP 1 u.DisplayName AS Reviewer, ra.DecidedAt, ra.Decision
                    FROM dbo.MT_ReviewAssignments ra
                    INNER JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
                    WHERE ra.QuestionId = q.Id AND ra.SubQuestionId IS NULL AND ra.ReviewStage = 3
                      AND ra.Decision IS NOT NULL
                    ORDER BY ra.DecidedAt DESC
                ) finalReview
                WHERE q.ProjectId = @ProjectId
                  AND q.IsDeleted = 0
                  AND q.Status IN (9, 10, 11, 12)

                UNION ALL

                -- 子題層（母題對應同一位命師；難度用 sq.FixedDifficulty；
                -- 子題無獨立 UpdatedAt 欄位，沿用母題 q.UpdatedAt 當「最後修題」）
                SELECT
                    q.QuestionCode,
                    sq.SortOrder,
                    q.QuestionTypeId,
                    CAST(1 AS BIT),
                    sq.FixedDifficulty,
                    sq.Stem,
                    NULL,
                    q.UpdatedAt,
                    creator.DisplayName,
                    peer.Reviewer, peer.CreatedAt,
                    expert.Reviewer, expert.CreatedAt, expert.Decision,
                    finalReview.Reviewer, finalReview.DecidedAt, finalReview.Decision,
                    q.Id,
                    sq.SortOrder
                FROM dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                INNER JOIN dbo.MT_Users creator ON creator.Id = q.CreatorId
                OUTER APPLY (
                    SELECT TOP 1 u.DisplayName AS Reviewer, ra.CreatedAt
                    FROM dbo.MT_ReviewAssignments ra
                    INNER JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
                    WHERE ra.SubQuestionId = sq.Id AND ra.ReviewStage = 1
                    ORDER BY ra.CreatedAt DESC
                ) peer
                OUTER APPLY (
                    SELECT TOP 1 u.DisplayName AS Reviewer, ra.CreatedAt, ra.Decision
                    FROM dbo.MT_ReviewAssignments ra
                    INNER JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
                    WHERE ra.SubQuestionId = sq.Id AND ra.ReviewStage = 2
                    ORDER BY ra.CreatedAt DESC
                ) expert
                OUTER APPLY (
                    SELECT TOP 1 u.DisplayName AS Reviewer, ra.DecidedAt, ra.Decision
                    FROM dbo.MT_ReviewAssignments ra
                    INNER JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
                    WHERE ra.SubQuestionId = sq.Id AND ra.ReviewStage = 3
                      AND ra.Decision IS NOT NULL
                    ORDER BY ra.DecidedAt DESC
                ) finalReview
                WHERE q.ProjectId = @ProjectId
                  AND q.IsDeleted = 0
                  AND sq.Status IN (9, 10, 11, 12)
            )
            SELECT
                QuestionCode, SubSortOrder,
                QuestionTypeId, IsSubQuestion, Difficulty, Stem, ArticleContent, UpdatedAt,
                CreatorName,
                PeerReviewerName, PeerReviewedAt,
                ExpertReviewerName, ExpertReviewedAt, ExpertDecision,
                FinalReviewerName, FinalDecidedAt, FinalDecision
            FROM RowSet
            ORDER BY QuestionTypeId, SortMasterId, SortSubOrder;

            -- Sect 3: 命題進度（每位命題教師 × 題型 × 粒度 × Level），給第 2 sheet「職務任務統計」用
            -- Status IN (9,10,11,12) = 已送到三審有結局的題（throughput 語意，含採用/不採用/結案）
            -- SUM(QuotaCount) 防 MT_MemberQuotas 萬一有重複列（無 UNIQUE 索引）
            -- Level 維度：CWT 模式恆為 NULL；LCT TypeId=6 為 1~5（難度一~五）、TypeId=7 為 NULL
            -- UNION ALL 末段：LCT 聽力題組子題虛擬列（MT_MemberQuotas 無 Granularity=1 列，
            --   從 TypeId=7 母題 QuotaCount × 2 推算子題 Y，每組固定 2 子題）
            ;WITH MasterAdopt AS (
                SELECT q.CreatorId, q.QuestionTypeId, q.Level, COUNT(*) AS Cnt
                FROM dbo.MT_Questions q
                WHERE q.ProjectId = @ProjectId
                  AND q.IsDeleted = 0
                  AND q.Status IN (9, 10, 11, 12)
                GROUP BY q.CreatorId, q.QuestionTypeId, q.Level
            ),
            SubAdopt AS (
                SELECT qp.CreatorId, qp.QuestionTypeId, COUNT(*) AS Cnt
                FROM dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions qp ON qp.Id = sq.ParentQuestionId
                WHERE qp.ProjectId = @ProjectId
                  AND qp.IsDeleted = 0
                  AND sq.IsDeleted = 0
                  AND sq.Status IN (9, 10, 11, 12)
                GROUP BY qp.CreatorId, qp.QuestionTypeId
            )
            SELECT
                pm.UserId,
                ISNULL(u.DisplayName, N'未知') AS DisplayName,
                r.Name AS RoleName,
                mq.QuestionTypeId,
                mq.Granularity,
                mq.Level,
                SUM(mq.QuotaCount) AS QuotaY,
                CASE WHEN mq.Granularity = 0
                     THEN ISNULL(MAX(ma.Cnt), 0)
                     ELSE ISNULL(MAX(sa.Cnt), 0) END AS DoneX
            FROM dbo.MT_ProjectMembers pm
            INNER JOIN dbo.MT_Users u                ON u.Id = pm.UserId
            INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
            INNER JOIN dbo.MT_Roles r                ON r.Id = pmr.RoleId
            INNER JOIN dbo.MT_MemberQuotas mq        ON mq.ProjectMemberId = pm.Id
            LEFT JOIN  MasterAdopt ma                ON ma.CreatorId = pm.UserId
                                                    AND ma.QuestionTypeId = mq.QuestionTypeId
                                                    AND ISNULL(ma.Level, 0) = ISNULL(mq.Level, 0)
            LEFT JOIN  SubAdopt sa                   ON sa.CreatorId = pm.UserId
                                                    AND sa.QuestionTypeId = mq.QuestionTypeId
            WHERE pm.ProjectId = @ProjectId
              AND r.Name = N'命題教師'
            GROUP BY pm.UserId, u.DisplayName, r.Name,
                     mq.QuestionTypeId, mq.Granularity, mq.Level

            UNION ALL

            -- LCT 聽力題組子題虛擬列：QuotaY = TypeId=7 母題 Quota × 2、DoneX 取 SubAdopt
            -- CWT 模式無 TypeId=7 quota，此 UNION 自然不會產生 row
            SELECT
                pm.UserId,
                ISNULL(u.DisplayName, N'未知') AS DisplayName,
                r.Name AS RoleName,
                CAST(7 AS INT) AS QuestionTypeId,
                CAST(1 AS TINYINT) AS Granularity,
                CAST(NULL AS TINYINT) AS Level,
                SUM(mq.QuotaCount * 2) AS QuotaY,
                ISNULL(MAX(sa.Cnt), 0) AS DoneX
            FROM dbo.MT_ProjectMembers pm
            INNER JOIN dbo.MT_Users u                ON u.Id = pm.UserId
            INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
            INNER JOIN dbo.MT_Roles r                ON r.Id = pmr.RoleId
            INNER JOIN dbo.MT_MemberQuotas mq        ON mq.ProjectMemberId = pm.Id
            LEFT JOIN  SubAdopt sa                   ON sa.CreatorId = pm.UserId AND sa.QuestionTypeId = 7
            WHERE pm.ProjectId = @ProjectId
              AND r.Name = N'命題教師'
              AND mq.QuestionTypeId = 7
              AND mq.Granularity = 0
            GROUP BY pm.UserId, u.DisplayName, r.Name;
            -- ORDER BY 不需要：C# 端 BuildJobStats 會做最終排序

            -- Sect 4: 審題進度（每位審題類人員 × Stage，不分題型）
            -- 審題委員只算 Stage=2（專審）；總召集人只算 Stage=3（總審）
            -- 互審 Stage=1 是命題教師之間的工作，本表不列（已於計畫拍板）
            -- 母題 + 子題層 assignment 一起算（題組類 1 母 + N 子 = N+1 個審題單位）
            -- 用 (QuestionId, ISNULL(SubQuestionId, 0)) 複合鍵 DISTINCT 去重：
            --   總召退回重新分配同單元會留多筆紀錄，避免雙計
            SELECT
                ra.ReviewerId,
                ISNULL(u.DisplayName, N'未知') AS DisplayName,
                r.Name AS RoleName,
                ra.ReviewStage,
                COUNT(DISTINCT CONCAT(ra.QuestionId, N'-', ISNULL(ra.SubQuestionId, 0))) AS AssignedY,
                COUNT(DISTINCT CASE
                    WHEN ra.DecidedAt IS NOT NULL
                    THEN CONCAT(ra.QuestionId, N'-', ISNULL(ra.SubQuestionId, 0))
                    END) AS DoneX
            FROM dbo.MT_ReviewAssignments ra
            INNER JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
            INNER JOIN dbo.MT_ProjectMembers pm
                    ON pm.ProjectId = ra.ProjectId AND pm.UserId = ra.ReviewerId
            INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
            INNER JOIN dbo.MT_Roles r ON r.Id = pmr.RoleId
            WHERE ra.ProjectId = @ProjectId
              AND (
                  (r.Name LIKE N'審題%' AND ra.ReviewStage = 2) OR
                  (r.Name = N'總召集人' AND ra.ReviewStage = 3)
              )
            GROUP BY ra.ReviewerId, u.DisplayName, r.Name, ra.ReviewStage
            ORDER BY r.Name, u.DisplayName;
            """;

        using var conn = _db.CreateConnection();
        using var multi = await conn.QueryMultipleAsync(sql, new { ProjectId = projectId });

        var meta = await multi.ReadFirstOrDefaultAsync<ClosedExportMetaRow>();
        if (meta is null) return null;

        var rawRows = (await multi.ReadAsync<ClosedExportRawRow>()).AsList();

        var rows = new List<ClosedExportRow>(rawRows.Count);
        foreach (var r in rawRows)
        {
            var rawText = !string.IsNullOrWhiteSpace(r.Stem) ? r.Stem : r.ArticleContent;
            var code = r.QuestionCode ?? string.Empty;
            // 子題題碼格式：{母題QuestionCode}-{SortOrder:D2}（與審題/總覽各頁面一致）
            var displayCode = r.IsSubQuestion && r.SubSortOrder.HasValue
                ? $"{code}-{r.SubSortOrder.Value:D2}"
                : code;
            rows.Add(new ClosedExportRow(
                DisplayCode:        displayCode,
                QuestionTypeId:     r.QuestionTypeId,
                IsSubQuestion:      r.IsSubQuestion,
                DifficultyLabel:    BuildDifficultyLabel(meta.ProjectType, r.Difficulty),
                CreatorName:        r.CreatorName ?? string.Empty,
                Summary:            BuildSummary(rawText),
                UpdatedAt:          r.UpdatedAt,
                PeerReviewerName:   r.PeerReviewerName,
                PeerReviewedAt:     r.PeerReviewedAt,
                ExpertReviewerName: r.ExpertReviewerName,
                ExpertReviewedAt:   r.ExpertReviewedAt,
                ExpertDecision:     r.ExpertDecision,
                FinalReviewerName:  r.FinalReviewerName,
                FinalDecidedAt:     r.FinalDecidedAt,
                FinalDecision:      r.FinalDecision
            ));
        }

        // ── 第 2 sheet「職務任務統計」資料 pivot ──
        var composeRaw = (await multi.ReadAsync<ClosedExportComposeStatRow>()).AsList();
        var reviewRaw  = (await multi.ReadAsync<ClosedExportReviewStatRow>()).AsList();
        var jobStats   = BuildJobStats(meta.ProjectType, composeRaw, reviewRaw);

        return new ClosedProjectExportData(
            ProjectType:     meta.ProjectType,
            ProjectName:     meta.ProjectName ?? string.Empty,
            // LCT 無 ExamLevel，回空字串；UI 端依 ProjectType 決定不渲染「專案等級」整列
            ExamLevelLabel:  meta.ProjectType == 1 ? string.Empty : BuildExamLevelLabel(meta.ExamLevel),
            Rows:            rows,
            JobStats:        jobStats);
    }

    /// <summary>
    /// 結案匯出第 2 sheet「職務任務統計」pivot：
    ///   命題進度 raw rows → 每位命題教師一個 MemberJobStatsRow（Cells 按 ProjectType 對應表頭順序填值）
    ///   審題進度 raw rows → 每位審題類人員一個 MemberJobStatsRow（merge cell 顯示「審題進度：X/Y」）
    /// 最後依 RoleSortKey + TeacherName 排序。
    /// </summary>
    private static List<MemberJobStatsRow> BuildJobStats(
        byte projectType,
        IReadOnlyList<ClosedExportComposeStatRow> composeRaw,
        IReadOnlyList<ClosedExportReviewStatRow>  reviewRaw)
    {
        var list = new List<MemberJobStatsRow>(composeRaw.Count + reviewRaw.Count);

        // 命題教師列（按 UserId + RoleName 分組）
        foreach (var grp in composeRaw.GroupBy(r => new { r.UserId, r.DisplayName, r.RoleName }))
        {
            var cells = projectType == 1 ? BuildLctCells(grp) : BuildCwtCells(grp);

            list.Add(new MemberJobStatsRow(
                TeacherName:   grp.Key.DisplayName,
                RoleName:      grp.Key.RoleName,
                RoleSortKey:   1,
                IsReviewerRow: false,
                Cells:         cells,
                ReviewSummary: string.Empty
            ));
        }

        // 審題類列（按 ReviewerId + RoleName 分組；同一人不同身分 = 多列）
        foreach (var grp in reviewRaw.GroupBy(r => new { r.ReviewerId, r.DisplayName, r.RoleName }))
        {
            int totalY = grp.Sum(x => x.AssignedY);
            int totalX = grp.Sum(x => x.DoneX);
            // 總召集人排第 3、審題%排第 2
            int sortKey = grp.Key.RoleName == "總召集人" ? 3 : 2;

            list.Add(new MemberJobStatsRow(
                TeacherName:   grp.Key.DisplayName,
                RoleName:      grp.Key.RoleName,
                RoleSortKey:   sortKey,
                IsReviewerRow: true,
                Cells:         Array.Empty<string>(),    // UI 端 AddMergedRegion 合併不會用到
                ReviewSummary: $"審題進度：{totalX}/{totalY}"
            ));
        }

        return list
            .OrderBy(x => x.RoleSortKey)
            .ThenBy(x => x.TeacherName)
            .ToList();
    }

    /// <summary>CWT 6 欄：一般 / 閱讀母 / 閱讀子 / 長文 / 短文母 / 短文子。</summary>
    private static IReadOnlyList<string> BuildCwtCells(IEnumerable<ClosedExportComposeStatRow> grp)
    {
        string Cell(int typeId, byte granularity)
        {
            var row = grp.FirstOrDefault(x => x.QuestionTypeId == typeId && x.Granularity == granularity);
            return row is null || row.QuotaY == 0 ? "—" : $"{row.DoneX}/{row.QuotaY}";
        }
        return new[]
        {
            Cell(1, 0),  // 一般單選題
            Cell(3, 0),  // 閱讀題組母題
            Cell(3, 1),  // 閱讀題組子題
            Cell(4, 0),  // 長文題目
            Cell(5, 0),  // 短文題組母題
            Cell(5, 1),  // 短文題組子題
        };
    }

    /// <summary>LCT 7 欄：難度一 / 難度二 / 難度三 / 難度四 / 難度五 / 聽力題組母 / 聽力題組子。</summary>
    private static IReadOnlyList<string> BuildLctCells(IEnumerable<ClosedExportComposeStatRow> grp)
    {
        string CellByLevel(byte level)
        {
            // TypeId=6 聽力測驗按 Level 分組
            var row = grp.FirstOrDefault(x => x.QuestionTypeId == 6 && x.Level == level);
            return row is null || row.QuotaY == 0 ? "—" : $"{row.DoneX}/{row.QuotaY}";
        }
        string CellListenGroup(byte granularity)
        {
            // TypeId=7 聽力題組母/子（子題的虛擬列由 Sect 3 SQL UNION 產出）
            var row = grp.FirstOrDefault(x => x.QuestionTypeId == 7 && x.Granularity == granularity);
            return row is null || row.QuotaY == 0 ? "—" : $"{row.DoneX}/{row.QuotaY}";
        }
        return new[]
        {
            CellByLevel(1),       // 難度一
            CellByLevel(2),       // 難度二
            CellByLevel(3),       // 難度三
            CellByLevel(4),       // 難度四
            CellByLevel(5),       // 難度五
            CellListenGroup(0),   // 聽力題組母題
            CellListenGroup(1),   // 聽力題組子題
        };
    }

    /// <summary>結案資料 EXCEL 等級欄；ExamLevel 0-4 對應「初/中/中高/高/優」，NULL（LCT）回空白。</summary>
    private static string BuildExamLevelLabel(byte? examLevel) => examLevel switch
    {
        0 => "初等",
        1 => "中等",
        2 => "中高等",
        3 => "高等",
        4 => "優等",
        _ => string.Empty
    };

    /// <summary>
    /// 結案資料 EXCEL 難度/等級欄。
    /// CWT (projectType=0): 0/1/2 → 易/中/難；其餘空白。
    /// LCT (projectType=1): 1-5 → 難度一~五；NULL → 「－」（聽力題組母題本身無等級）；其餘空白。
    /// </summary>
    private static string BuildDifficultyLabel(byte projectType, byte? difficulty)
    {
        if (projectType == 1)
        {
            return difficulty switch
            {
                null => "－",
                1 => "難度一",
                2 => "難度二",
                3 => "難度三",
                4 => "難度四",
                5 => "難度五",
                _ => string.Empty
            };
        }
        return difficulty switch
        {
            0 => "易",
            1 => "中",
            2 => "難",
            _ => string.Empty
        };
    }

    /// <summary>結案資料 EXCEL 摘要欄：StripHtml 後取前 40 字，超過補「…」。</summary>
    private static string BuildSummary(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var stripped = StripHtmlLite(raw);
        if (stripped.Length <= 40) return stripped;
        return stripped[..40] + "…";
    }

    /// <summary>極簡 HTML strip：拿掉所有 &lt;…&gt; 標籤 + decode 常見實體。Quill 輸出已是良性 HTML，不需 HtmlAgilityPack。</summary>
    private static string StripHtmlLite(string html)
    {
        var sb = new System.Text.StringBuilder(html.Length);
        var inside = false;
        foreach (var c in html)
        {
            if (c == '<') { inside = true; continue; }
            if (c == '>') { inside = false; continue; }
            if (!inside) sb.Append(c);
        }
        return System.Net.WebUtility.HtmlDecode(sb.ToString()).Trim();
    }

    private sealed class ClosedExportMetaRow
    {
        public string? ProjectName { get; set; }
        public byte? ExamLevel { get; set; }
        public byte ProjectType { get; set; }
    }

    private sealed class ClosedExportRawRow
    {
        public string? QuestionCode { get; set; }
        public int? SubSortOrder { get; set; }
        public int QuestionTypeId { get; set; }
        public bool IsSubQuestion { get; set; }
        public byte? Difficulty { get; set; }
        public string? Stem { get; set; }
        public string? ArticleContent { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? CreatorName { get; set; }
        public string? PeerReviewerName { get; set; }
        public DateTime? PeerReviewedAt { get; set; }
        public string? ExpertReviewerName { get; set; }
        public DateTime? ExpertReviewedAt { get; set; }
        public byte? ExpertDecision { get; set; }
        public string? FinalReviewerName { get; set; }
        public DateTime? FinalDecidedAt { get; set; }
        public byte? FinalDecision { get; set; }
    }

    // 結案匯出第 2 sheet「職務任務統計」用 — 命題進度 raw row
    private sealed class ClosedExportComposeStatRow
    {
        public int UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public int QuestionTypeId { get; set; }
        public byte Granularity { get; set; }
        public byte? Level { get; set; }           // LCT TypeId=6 為 1~5；其他為 NULL
        public int QuotaY { get; set; }
        public int DoneX { get; set; }
    }

    // 結案匯出第 2 sheet「職務任務統計」用 — 審題進度 raw row
    private sealed class ClosedExportReviewStatRow
    {
        public int ReviewerId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public byte ReviewStage { get; set; }
        public int AssignedY { get; set; }
        public int DoneX { get; set; }
    }

    /// <summary>
    /// 命題階段結束後比對題數需求與配額，禁止任何修改。
    /// </summary>
    private static void EnsureCompositionPhaseLockRespected(ProjectEditDto oldSnapshot, UpdateProjectRequest req)
    {
        // 1. TargetCount 比對
        // 用複合鍵 (QuestionTypeId, Granularity, Level) — MT_ProjectTargets 表同個 TypeId 可有多列：
        //   CWT 閱讀/短文題組：Granularity 0/1 兩列
        //   LCT 聽力測驗：5 個 Level
        //   聽力題組：單一列（Granularity=0, Level=NULL）
        // 用單一 QuestionTypeId 當 key 會撞 "Key already added" 例外。
        var oldTargets = oldSnapshot.Targets.ToDictionary(
            t => (t.QuestionTypeId, t.Granularity, t.Level), t => t.TargetCount);
        var newTargets = req.Targets.ToDictionary(
            t => (t.QuestionTypeId, t.Granularity, t.Level), t => t.TargetCount);
        var allTargetKeys = oldTargets.Keys.Union(newTargets.Keys);
        foreach (var key in allTargetKeys)
        {
            var oldCount = oldTargets.TryGetValue(key, out var o) ? o : 0;
            var newCount = newTargets.TryGetValue(key, out var n) ? n : 0;
            if (oldCount != newCount)
            {
                throw new InvalidOperationException("命題階段已結束，無法修改題數需求。");
            }
        }

        // 2. 配額比對：以 (UserId, QuestionTypeId, Granularity, Level) 為鍵
        //    CWT 閱讀/短文題組：母題(Granularity=0)和子題(Granularity=1)是不同欄位，不可互蓋。
        var oldQuotas = oldSnapshot.MemberAllocations
            .SelectMany(m => m.Quotas.Select(q => new { m.UserId, q.QuestionTypeId, q.Granularity, q.Level, q.QuotaCount }))
            .ToDictionary(x => (x.UserId, x.QuestionTypeId, x.Granularity, x.Level), x => x.QuotaCount);
        var newQuotas = req.MemberAllocations
            .SelectMany(m => m.Quotas.Select(q => new { m.UserId, q.QuestionTypeId, q.Granularity, q.Level, q.QuotaCount }))
            .ToDictionary(x => (x.UserId, x.QuestionTypeId, x.Granularity, x.Level), x => x.QuotaCount);
        var allQuotaKeys = oldQuotas.Keys.Union(newQuotas.Keys);
        foreach (var key in allQuotaKeys)
        {
            var oldCount = oldQuotas.TryGetValue(key, out var o) ? o : 0;
            var newCount = newQuotas.TryGetValue(key, out var n) ? n : 0;
            if (oldCount != newCount)
            {
                throw new InvalidOperationException("命題階段已結束，無法修改命題配額。");
            }
        }
    }

    /// <summary>
    /// 命題階段結束後禁止新增「命題教師」身份（允許移除既有命題教師）。
    /// </summary>
    private async Task EnsureNoNewPropositionTeacherAsync(
        IDbConnection conn,
        IDbTransaction trans,
        ProjectEditDto oldSnapshot,
        UpdateProjectRequest req)
    {
        const string roleSql = "SELECT TOP 1 Id FROM dbo.MT_Roles WHERE Name = N'命題教師';";
        var propositionRoleId = await conn.ExecuteScalarAsync<int?>(roleSql, transaction: trans);
        if (propositionRoleId is null) return;

        var oldPropositionUserIds = oldSnapshot.MemberAllocations
            .Where(m => m.RoleIds.Contains(propositionRoleId.Value))
            .Select(m => m.UserId)
            .ToHashSet();

        foreach (var alloc in req.MemberAllocations)
        {
            if (alloc.RoleIds.Contains(propositionRoleId.Value) && !oldPropositionUserIds.Contains(alloc.UserId))
            {
                throw new InvalidOperationException("命題階段已結束，無法新增命題教師。");
            }
        }
    }

    private sealed class ProjectMemberRow
    {
        public int UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? TeacherCode { get; set; }
        public string? RoleName { get; set; }
        public int? RoleCategory { get; set; }
    }

    private sealed class ProjectEditMemberRow
    {
        public int ProjectMemberId { get; set; }
        public int UserId { get; set; }
    }

    private sealed class ProjectEditMemberRoleRow
    {
        public int ProjectMemberId { get; set; }
        public int RoleId { get; set; }
    }

    private sealed class ProjectEditMemberQuotaRow
    {
        public int ProjectMemberId { get; set; }
        public int QuestionTypeId { get; set; }
        public byte Granularity { get; set; }
        public byte? Level { get; set; }
        public int QuotaCount { get; set; }
    }

    private sealed class ProjectEditMemberQuestionCountRow
    {
        public int UserId { get; set; }
        public int QuestionCount { get; set; }
    }
}
