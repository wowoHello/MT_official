using System.Data;
using System.IO.Compression;
using Dapper;
using MT.Models;

namespace MT.Services;

/// <summary>
/// 聘書 metadata 管理 + Canvas 繪製待辦清單管理。
///
/// 業務 key = (UserId, ProjectId, RoleId)；不掛 MT_ProjectMembers.Id 因為 ProjectService 的
/// ReplaceProjectChildRecordsAsync 會整批 DELETE/INSERT 造成 FK 失效。
///
/// 字號 Year 取自 MT_Projects.StartDate 民國年；CertNumber 流水號用「SELECT MAX(CertNumber)+1
/// WITH (UPDLOCK, HOLDLOCK) WHERE Year=@Year」在現有 transaction 內取得，配合 UNIQUE(Year, CertNumber)
/// 雙保險避免 race condition。
///
/// 同步邏輯（SyncCertificatesAsync）：
///   - 現存 ProjectMemberRoles - 已簽發 ⇒ 新建（INSERT FileName=null）
///   - 已簽發 IsRevoked=0 - 現存 ⇒ 撤銷（UPDATE IsRevoked=1，保留檔案與字號）
///   - 已簽發 IsRevoked=1 ∩ 現存 ⇒ 恢復（UPDATE IsRevoked=0，沿用原字號與原檔案）
///
/// 繪製失敗 retry：FileName IS NULL 視為「未完成」；user 下次進相關頁面時主動撈未完成清單重畫。
/// </summary>
public interface IAppointmentService
{
    /// <summary>
    /// 在現有 transaction 內同步該專案的聘書 metadata（新增/撤銷/恢復）。
    /// 由 ProjectService.Create/UpdateProjectAsync、TeacherService.AssignToProjectAsync 呼叫。
    /// 不負責繪製，繪製由前端 JS 完成。
    /// </summary>
    Task SyncCertificatesAsync(int projectId, IDbConnection conn, IDbTransaction transaction);

    /// <summary>
    /// JS 上傳 jpg/png blob 後，server 寫檔到 wwwroot/files/ 並更新 FileName。
    /// </summary>
    Task<bool> SaveDrawnFileAsync(
        int certId,
        byte[] imageBytes,
        string webRootPath,
        int requesterUserId,
        bool canManageCertificates,
        string fileExtension);

    /// <summary>
    /// 撈該使用者所有「未完成繪製」（FileName IS NULL、IsRevoked=0）的聘書，
    /// 組成 DTO 給 JS 端補畫。供使用者下次登入時補畫（MainLayout 觸發）。
    /// </summary>
    Task<List<AppointmentDraftDto>> GetPendingDraftsForUserAsync(int userId);

    /// <summary>
    /// 撈該專案所有「未完成繪製」的聘書（FileName IS NULL、IsRevoked=0），
    /// 供 Projects / Teachers 頁面操作後 admin 端 browser 即時繪製整批新加聘書。
    /// </summary>
    Task<List<AppointmentDraftDto>> GetPendingDraftsByProjectAsync(int projectId);

    /// <summary>
    /// 教師詳細頁「參與專案」用：列出該教師在指定專案的所有有效聘書（FileName IS NOT NULL、IsRevoked=0）。
    /// projectId = null 時撈該教師所有專案的聘書。
    /// </summary>
    Task<List<AppointmentDownloadItem>> ListByUserAsync(int userId, int? projectId = null);

    /// <summary>
    /// 「下載本梯次聘書」用 — 智慧切換：
    ///   - 1 份聘書 → 直接回該圖片檔案（單檔直下）
    ///   - 2 份以上 → 動態打包成 zip
    /// 無聘書或檔案不存在時回 null。
    /// </summary>
    Task<AppointmentDownloadFile?> BuildDownloadForUserProjectAsync(int userId, int projectId, string webRootPath);

    /// <summary>該使用者擁有「可下載聘書」(FileName IS NOT NULL AND IsRevoked=0) 的 ProjectId 集合。給 Teachers 詳細頁批次標記。</summary>
    Task<HashSet<int>> GetDownloadableProjectIdsForUserAsync(int userId);

    /// <summary>該專案內擁有「可下載聘書」的 UserId 集合。給 Projects 詳情成員列表批次標記。</summary>
    Task<HashSet<int>> GetDownloadableUserIdsInProjectAsync(int projectId);

    /// <summary>單筆檢查：該使用者在指定專案是否有「可下載聘書」。給 MainLayout「下載聘書」按鈕用。</summary>
    Task<bool> HasDownloadableCertsAsync(int userId, int projectId);

    /// <summary>
    /// 取得某 (UserId, ProjectId) 的聘期編輯資料 — 含預設聘期、所有身份對應的 cert 與其 Custom 設定。
    /// 給 EditAppointmentPeriodModal 開啟時載入用。
    /// </summary>
    Task<CertEditPanelData?> GetCertEditPanelAsync(int userId, int projectId);

    /// <summary>
    /// 批次更新多張 cert 的自訂聘期，並把這些 cert 的 FileName 設 NULL 觸發 client 端重畫。
    /// 成對原則：StartDate 與 EndDate 必須同時為 NULL 或同時有值（service 端會 normalize）。
    /// </summary>
    Task UpdateCustomPeriodsAsync(IEnumerable<CertPeriodUpdate> updates, string webRootPath);
}

/// <summary>
/// 聘書下載回傳封包：含 byte 內容、Content-Type、Content-Disposition 用的檔名（含副檔名）。
/// </summary>
public sealed record AppointmentDownloadFile(byte[] Bytes, string ContentType, string FileName);

public class AppointmentService : IAppointmentService
{
    private readonly IDatabaseService _db;

    public AppointmentService(IDatabaseService db)
    {
        _db = db;
    }

    // ====================================================================
    //  同步聘書 metadata（在現有 transaction 內運行）
    // ====================================================================

    public async Task SyncCertificatesAsync(int projectId, IDbConnection conn, IDbTransaction transaction)
    {
        // 1. 撈該專案 StartDate 民國年（西元 - 1911）
        const string projectSql = """
            SELECT StartDate FROM dbo.MT_Projects WHERE Id = @ProjectId;
            """;
        var startDate = await conn.QuerySingleOrDefaultAsync<DateTime?>(
            projectSql, new { ProjectId = projectId }, transaction);
        if (startDate is null) return; // 專案不存在
        var year = startDate.Value.Year - 1911;

        // 2. 撈該專案「現存」ProjectMemberRoles（user × role 組合）
        const string currentSql = """
            SELECT pm.UserId, pmr.RoleId
            FROM dbo.MT_ProjectMembers pm
            INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
            WHERE pm.ProjectId = @ProjectId;
            """;
        var currentPairs = (await conn.QueryAsync<(int UserId, int RoleId)>(
            currentSql, new { ProjectId = projectId }, transaction)).ToHashSet();

        // 3. 撈該專案「既有」聘書紀錄
        const string existingSql = """
            SELECT Id, UserId, RoleId, IsRevoked
            FROM dbo.MT_AppointmentCertificates
            WHERE ProjectId = @ProjectId;
            """;
        var existingRows = (await conn.QueryAsync<(int Id, int UserId, int RoleId, bool IsRevoked)>(
            existingSql, new { ProjectId = projectId }, transaction)).ToList();
        var existingMap = existingRows.ToDictionary(r => (r.UserId, r.RoleId));

        // 4. 比對：分類成「新增」「恢復」「撤銷」三組
        var toInsert = currentPairs.Where(p => !existingMap.ContainsKey(p)).ToList();
        var toRestore = currentPairs.Where(p =>
            existingMap.TryGetValue(p, out var r) && r.IsRevoked).Select(p => existingMap[p].Id).ToList();
        var toRevoke = existingRows.Where(r =>
            !r.IsRevoked && !currentPairs.Contains((r.UserId, r.RoleId))).Select(r => r.Id).ToList();

        // 5a. 新增：用 UPDLOCK + HOLDLOCK 取下一號流水
        if (toInsert.Count > 0)
        {
            // 一次性鎖該年範圍取 MAX，C# 端逐一遞增分配，最後一起 INSERT
            const string maxSql = """
                SELECT ISNULL(MAX(CertNumber), 0)
                FROM dbo.MT_AppointmentCertificates WITH (UPDLOCK, HOLDLOCK)
                WHERE Year = @Year;
                """;
            var nextValue = await conn.ExecuteScalarAsync<int>(
                maxSql, new { Year = year }, transaction);

            const string insertSql = """
                INSERT INTO dbo.MT_AppointmentCertificates
                    (UserId, ProjectId, RoleId, Year, CertNumber, CreatedAt, IsRevoked)
                VALUES (@UserId, @ProjectId, @RoleId, @Year, @CertNumber, SYSDATETIME(), 0);
                """;

            foreach (var (userId, roleId) in toInsert)
            {
                nextValue++;
                await conn.ExecuteAsync(insertSql, new
                {
                    UserId = userId,
                    ProjectId = projectId,
                    RoleId = roleId,
                    Year = year,
                    CertNumber = nextValue
                }, transaction);
            }
        }

        // 5b. 恢復：UPDATE IsRevoked=0
        if (toRestore.Count > 0)
        {
            const string restoreSql = """
                UPDATE dbo.MT_AppointmentCertificates
                SET IsRevoked = 0
                WHERE Id IN @Ids;
                """;
            await conn.ExecuteAsync(restoreSql, new { Ids = toRestore }, transaction);
        }

        // 5c. 撤銷：UPDATE IsRevoked=1（保留 metadata 與檔案）
        if (toRevoke.Count > 0)
        {
            const string revokeSql = """
                UPDATE dbo.MT_AppointmentCertificates
                SET IsRevoked = 1
                WHERE Id IN @Ids;
                """;
            await conn.ExecuteAsync(revokeSql, new { Ids = toRevoke }, transaction);
        }
    }

    // ====================================================================
    //  繪製檔案上傳
    // ====================================================================

    public async Task<bool> SaveDrawnFileAsync(
        int certId,
        byte[] imageBytes,
        string webRootPath,
        int requesterUserId,
        bool canManageCertificates,
        string fileExtension)
    {
        var normalizedExt = NormalizeCertificateImageExtension(fileExtension);
        if (normalizedExt is null) return false;

        using var conn = _db.CreateConnection();

        // 1. 撈紀錄取得需要的欄位組檔名
        const string selectSql = """
            SELECT UserId, RoleId, CreatedAt
            FROM dbo.MT_AppointmentCertificates
            WHERE Id = @Id AND IsRevoked = 0;
            """;
        var row = await conn.QuerySingleOrDefaultAsync<(int UserId, int RoleId, DateTime CreatedAt)?>(
            selectSql, new { Id = certId });
        if (row is null) return false;

        if (row.Value.UserId != requesterUserId && !canManageCertificates) return false;

        // CertId 作為檔名前綴：cert 表 IDENTITY PK 全表唯一，防止「同 UserId+RoleId+CreatedAt
        // 跨梯次（不同 ProjectId）」的聘書圖片互相覆蓋（DB UNIQUE 是 UserId+ProjectId+RoleId，
        // 但檔名規則缺 ProjectId 會撞檔，導致下載時檔名字號與圖內字號不一致）
        var fileStem = $"{certId}_{row.Value.UserId}_{row.Value.CreatedAt:yyyyMMdd}_{row.Value.RoleId}";
        var fileName = $"{fileStem}{normalizedExt}";
        var filesDir = Path.Combine(webRootPath, "files");
        Directory.CreateDirectory(filesDir);
        var filePath = Path.Combine(filesDir, fileName);

        DeleteAlternateCertificateImageFiles(filesDir, fileStem, normalizedExt);

        // 2. 寫檔（覆寫既有，避免恢復場景檔案不存在）
        await File.WriteAllBytesAsync(filePath, imageBytes);

        // 3. 更新 FileName 欄位
        const string updateSql = """
            UPDATE dbo.MT_AppointmentCertificates
            SET FileName = @FileName
            WHERE Id = @Id;
            """;
        await conn.ExecuteAsync(updateSql, new { Id = certId, FileName = fileName });
        return true;
    }

    // ====================================================================
    //  待繪製清單（給 JS 補畫用）
    // ====================================================================

    public async Task<List<AppointmentDraftDto>> GetPendingDraftsForUserAsync(int userId)
        => await QueryPendingDraftsAsync("c.UserId = @KeyValue", userId);

    public async Task<List<AppointmentDraftDto>> GetPendingDraftsByProjectAsync(int projectId)
        => await QueryPendingDraftsAsync("c.ProjectId = @KeyValue", projectId);

    /// <summary>
    /// Pending drafts 共用查詢 — 唯一差異是 WHERE 條件（user 或 project 範圍）。
    /// FileName IS NULL 視為「繪製未完成」；IsRevoked=0 才補畫（被撤銷的不畫）。
    /// </summary>
    private async Task<List<AppointmentDraftDto>> QueryPendingDraftsAsync(string whereClause, int keyValue)
    {
        using var conn = _db.CreateConnection();
        var sql = $"""
            SELECT
                c.Id              AS CertId,
                c.UserId,
                c.RoleId,
                c.Year,
                c.CertNumber,
                c.CreatedAt,
                ISNULL(t.School, '')        AS School,
                u.DisplayName,
                ISNULL(t.Title, '')         AS Title,
                r.Name                       AS RoleName,
                COALESCE(c.CustomStartDate, p.StartDate) AS ProjectStart,
                COALESCE(c.CustomEndDate,   p.EndDate)   AS ProjectEnd
            FROM dbo.MT_AppointmentCertificates c
            INNER JOIN dbo.MT_Users u ON u.Id = c.UserId
            LEFT JOIN dbo.MT_Teachers t ON t.UserId = c.UserId
            INNER JOIN dbo.MT_Roles r ON r.Id = c.RoleId
            INNER JOIN dbo.MT_Projects p ON p.Id = c.ProjectId
            WHERE {whereClause}
              AND c.FileName IS NULL
              AND c.IsRevoked = 0;
            """;

        var rows = await conn.QueryAsync<PendingDraftRow>(sql, new { KeyValue = keyValue });
        return rows.Select(BuildDraftDto).ToList();
    }

    // ====================================================================
    //  下載清單（教師詳細頁 / MainLayout）
    // ====================================================================

    public async Task<List<AppointmentDownloadItem>> ListByUserAsync(int userId, int? projectId = null)
    {
        using var conn = _db.CreateConnection();
        var sql = """
            SELECT
                c.Id              AS CertId,
                c.ProjectId,
                p.Name             AS ProjectName,
                c.RoleId,
                r.Name             AS RoleName,
                u.DisplayName,
                c.Year,
                c.CertNumber,
                c.FileName,
                c.CreatedAt
            FROM dbo.MT_AppointmentCertificates c
            INNER JOIN dbo.MT_Projects p ON p.Id = c.ProjectId
            INNER JOIN dbo.MT_Roles r ON r.Id = c.RoleId
            INNER JOIN dbo.MT_Users u ON u.Id = c.UserId
            WHERE c.UserId = @UserId
              AND c.IsRevoked = 0
              AND c.FileName IS NOT NULL
              {0}
            ORDER BY c.CreatedAt DESC;
            """;
        sql = string.Format(sql, projectId.HasValue ? "AND c.ProjectId = @ProjectId" : "");

        var rows = await conn.QueryAsync<DownloadItemRow>(sql, new { UserId = userId, ProjectId = projectId ?? 0 });
        return rows.Select(r => new AppointmentDownloadItem
        {
            CertId         = r.CertId,
            ProjectId      = r.ProjectId,
            ProjectName    = r.ProjectName,
            RoleId         = r.RoleId,
            RoleName       = r.RoleName,
            DisplayName    = r.DisplayName,
            CertNumberText = FormatCertNumber(r.Year, r.CertNumber),
            FileName       = r.FileName ?? "",
            CreatedAt      = r.CreatedAt
        }).ToList();
    }

    // ====================================================================
    //  批次「有無可下載聘書」查詢 — UX：隱藏無聘書的下載按鈕避免 404
    // ====================================================================

    public async Task<HashSet<int>> GetDownloadableProjectIdsForUserAsync(int userId)
    {
        using var conn = _db.CreateConnection();
        const string sql = """
            SELECT DISTINCT ProjectId
            FROM dbo.MT_AppointmentCertificates
            WHERE UserId = @UserId
              AND IsRevoked = 0
              AND FileName IS NOT NULL;
            """;
        var rows = await conn.QueryAsync<int>(sql, new { UserId = userId });
        return rows.ToHashSet();
    }

    public async Task<HashSet<int>> GetDownloadableUserIdsInProjectAsync(int projectId)
    {
        using var conn = _db.CreateConnection();
        const string sql = """
            SELECT DISTINCT UserId
            FROM dbo.MT_AppointmentCertificates
            WHERE ProjectId = @ProjectId
              AND IsRevoked = 0
              AND FileName IS NOT NULL;
            """;
        var rows = await conn.QueryAsync<int>(sql, new { ProjectId = projectId });
        return rows.ToHashSet();
    }

    public async Task<bool> HasDownloadableCertsAsync(int userId, int projectId)
    {
        using var conn = _db.CreateConnection();
        const string sql = """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.MT_AppointmentCertificates
                WHERE UserId = @UserId AND ProjectId = @ProjectId
                  AND IsRevoked = 0 AND FileName IS NOT NULL
            ) THEN 1 ELSE 0 END AS BIT);
            """;
        return await conn.ExecuteScalarAsync<bool>(sql, new { UserId = userId, ProjectId = projectId });
    }

    // ====================================================================
    //  下載（1 份直回圖片、2+ 打包 zip）— MainLayout / Teachers / Projects 下載聘書共用
    // ====================================================================

    public async Task<AppointmentDownloadFile?> BuildDownloadForUserProjectAsync(int userId, int projectId, string webRootPath)
    {
        var items = await ListByUserAsync(userId, projectId);
        if (items.Count == 0) return null;

        var filesDir = Path.Combine(webRootPath, "files");

        // ① 1 份：直接讀圖片檔案內容回傳（Results.File 會帶 Content-Disposition: attachment 強制下載）
        if (items.Count == 1)
        {
            var only = items[0];
            var filePath = Path.Combine(filesDir, only.FileName);
            if (!File.Exists(filePath)) return null;

            var bytes = await File.ReadAllBytesAsync(filePath);
            var displayName = BuildDownloadName(only);
            return new AppointmentDownloadFile(bytes, GetCertificateImageContentType(only.FileName), displayName);
        }

        // ② 2+ 份：動態打包成 zip
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in items)
            {
                var filePath = Path.Combine(filesDir, item.FileName);
                if (!File.Exists(filePath)) continue;

                var entry = zip.CreateEntry(BuildDownloadName(item), CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(filePath);
                await fileStream.CopyToAsync(entryStream);
            }
        }
        return new AppointmentDownloadFile(ms.ToArray(), "application/zip", $"appointments-project-{projectId}.zip");
    }

    /// <summary>
    /// 下載呈現用檔名：{姓名}_{身份名}_{字號}.{原副檔名}（user 解壓 / 看到的檔名更直觀）。
    /// 移除作業系統不允許的字元避免寫檔 / zip 內 entry name 問題。
    /// </summary>
    private static string BuildDownloadName(AppointmentDownloadItem item)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(item.DisplayName.Where(c => !invalid.Contains(c)));
        var safeRole = string.Concat(item.RoleName.Where(c => !invalid.Contains(c)));
        var safeCert = string.Concat(item.CertNumberText.Where(c => !invalid.Contains(c)));
        var ext = NormalizeCertificateImageExtension(Path.GetExtension(item.FileName)) ?? ".jpg";
        return $"{safeName}_{safeRole}_{safeCert}{ext}";
    }

    // ====================================================================
    //  聘期編輯（CustomStartDate / CustomEndDate） — Modal 載入與儲存
    // ====================================================================

    public async Task<CertEditPanelData?> GetCertEditPanelAsync(int userId, int projectId)
    {
        using var conn = _db.CreateConnection();
        const string sql = """
            SELECT
                u.DisplayName,
                p.Name           AS ProjectName,
                p.StartDate      AS DefaultStart,
                p.EndDate        AS DefaultEnd,
                c.Id             AS CertId,
                r.Name           AS RoleName,
                c.Year,
                c.CertNumber,
                c.CustomStartDate,
                c.CustomEndDate
            FROM dbo.MT_AppointmentCertificates c
            INNER JOIN dbo.MT_Users u    ON u.Id = c.UserId
            INNER JOIN dbo.MT_Projects p ON p.Id = c.ProjectId
            INNER JOIN dbo.MT_Roles r    ON r.Id = c.RoleId
            WHERE c.UserId = @UserId
              AND c.ProjectId = @ProjectId
              AND c.IsRevoked = 0
            ORDER BY r.Id;
            """;
        var rows = (await conn.QueryAsync<CertEditPanelRow>(sql, new { UserId = userId, ProjectId = projectId })).ToList();
        if (rows.Count == 0) return null;

        var first = rows[0];
        return new CertEditPanelData
        {
            DisplayName  = first.DisplayName,
            ProjectName  = first.ProjectName,
            DefaultStart = first.DefaultStart,
            DefaultEnd   = first.DefaultEnd,
            Certs = rows.Select(r => new CertEditInfo
            {
                CertId          = r.CertId,
                RoleName        = r.RoleName,
                CertNumberText  = FormatCertNumber(r.Year, r.CertNumber),
                CustomStartDate = r.CustomStartDate,
                CustomEndDate   = r.CustomEndDate
            }).ToList()
        };
    }

    public async Task UpdateCustomPeriodsAsync(IEnumerable<CertPeriodUpdate> updates, string webRootPath)
    {
        // Normalize 成對原則：若只填一邊，視同未客製（兩邊都歸 NULL）
        var normalized = updates
            .Select(u => (u.CertId,
                          Start: u.StartDate.HasValue && u.EndDate.HasValue ? u.StartDate : null,
                          End:   u.StartDate.HasValue && u.EndDate.HasValue ? u.EndDate   : null))
            .ToList();
        if (normalized.Count == 0) return;

        using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync();
        using var trans = await conn.BeginTransactionAsync();

        try
        {
            // 1. 先撈這些 cert 既有 (CustomStart, CustomEnd, FileName) 比對是否真的有變
            //    沒變的 cert 不動 FileName，避免「沒改聘期但被誤觸發重畫」
            var certIds = normalized.Select(n => n.CertId).ToList();
            const string existingSql = """
                SELECT Id, CustomStartDate, CustomEndDate, FileName
                FROM dbo.MT_AppointmentCertificates
                WHERE Id IN @Ids;
                """;
            var existing = (await conn.QueryAsync<(int Id, DateTime? CustomStartDate, DateTime? CustomEndDate, string? FileName)>(
                existingSql, new { Ids = certIds }, transaction: trans)).ToDictionary(r => r.Id);

            const string updateSql = """
                UPDATE dbo.MT_AppointmentCertificates
                SET CustomStartDate = @Start,
                    CustomEndDate   = @End,
                    FileName        = NULL
                WHERE Id = @Id;
                """;

            foreach (var (certId, start, end) in normalized)
            {
                if (!existing.TryGetValue(certId, out var prev)) continue;

                // 真的有變動才 UPDATE + 清 FileName 觸發重畫
                var changed = prev.CustomStartDate != start || prev.CustomEndDate != end;
                if (!changed) continue;

                await conn.ExecuteAsync(updateSql, new { Id = certId, Start = start, End = end }, transaction: trans);

                // 同步刪掉舊 jpg（避免 user 解壓 zip 拿到舊聘期版本檔案）
                if (!string.IsNullOrEmpty(prev.FileName))
                {
                    var oldPath = Path.Combine(webRootPath, "files", prev.FileName);
                    if (File.Exists(oldPath))
                    {
                        try { File.Delete(oldPath); } catch { /* 刪檔失敗不阻擋 UPDATE */ }
                    }
                }
            }

            await trans.CommitAsync();
        }
        catch
        {
            await trans.RollbackAsync();
            throw;
        }
    }

    private sealed class CertEditPanelRow
    {
        public string DisplayName { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public DateTime DefaultStart { get; set; }
        public DateTime DefaultEnd { get; set; }
        public int CertId { get; set; }
        public string RoleName { get; set; } = "";
        public int Year { get; set; }
        public int CertNumber { get; set; }
        public DateTime? CustomStartDate { get; set; }
        public DateTime? CustomEndDate { get; set; }
    }

    // ====================================================================
    //  Helper
    // ====================================================================

    private static string FormatCertNumber(int year, int certNumber)
        => $"({year})中檢(中)聘字第{certNumber:D5}號";

    private static string? NormalizeCertificateImageExtension(string? extension)
        => extension?.Trim().ToLowerInvariant() switch
        {
            ".jpg"  => ".jpg",
            ".jpeg" => ".jpg",
            ".png"  => ".png",
            _       => null
        };

    private static string GetCertificateImageContentType(string fileName)
        => NormalizeCertificateImageExtension(Path.GetExtension(fileName)) switch
        {
            ".png" => "image/png",
            _      => "image/jpeg"
        };

    private static void DeleteAlternateCertificateImageFiles(string filesDir, string fileStem, string currentExtension)
    {
        foreach (var ext in new[] { ".jpg", ".png" })
        {
            if (string.Equals(ext, currentExtension, StringComparison.OrdinalIgnoreCase)) continue;

            var path = Path.Combine(filesDir, $"{fileStem}{ext}");
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { /* 舊副檔名檔案刪除失敗不阻擋新檔寫入 */ }
            }
        }
    }

    private static AppointmentDraftDto BuildDraftDto(PendingDraftRow r)
    {
        var startYearROC = r.ProjectStart.Year - 1911;
        var endYearROC = r.ProjectEnd.Year - 1911;
        var period = $"{startYearROC}年{r.ProjectStart.Month}月{r.ProjectStart.Day}日起至"
                   + $"{endYearROC}年{r.ProjectEnd.Month}月{r.ProjectEnd.Day}日止";
        var createdROC = r.CreatedAt.Year - 1911;
        return new AppointmentDraftDto
        {
            CertId          = r.CertId,
            // CertId 前綴必須與 SaveDrawnFileAsync 同步，避免跨梯次相同 user+role 撞檔
            TargetFileName  = $"{r.CertId}_{r.UserId}_{r.CreatedAt:yyyyMMdd}_{r.RoleId}.jpg",
            CertNumberText  = FormatCertNumber(r.Year, r.CertNumber),
            School          = r.School,
            DisplayName     = r.DisplayName,
            Title           = r.Title,
            RoleName        = r.RoleName,
            PeriodText      = period,
            IssuedYearROC   = createdROC,
            IssuedMonth     = r.CreatedAt.Month,
            IssuedDay       = r.CreatedAt.Day
        };
    }

    private sealed class PendingDraftRow
    {
        public int CertId { get; set; }
        public int UserId { get; set; }
        public int RoleId { get; set; }
        public int Year { get; set; }
        public int CertNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public string School { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Title { get; set; } = "";
        public string RoleName { get; set; } = "";
        public DateTime ProjectStart { get; set; }
        public DateTime ProjectEnd { get; set; }
    }

    private sealed class DownloadItemRow
    {
        public int CertId { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public int RoleId { get; set; }
        public string RoleName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int Year { get; set; }
        public int CertNumber { get; set; }
        public string? FileName { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
