using Dapper;
using MT.Models;

namespace MT.Services;

/// <summary>
/// 命題總覽（Overview.razor）的商業邏輯。
/// 對外封裝 IQuestionService 的細節，razor 端只需注入 IOverviewService。
/// </summary>
public interface IOverviewService
{
    /// <summary>合併「列表」+「status 分桶計數」一次回傳。</summary>
    Task<OverviewListResult> LoadAsync(int projectId, OverviewFilter filter);

    /// <summary>取此專案有命題紀錄的教師清單（給篩選下拉用）。</summary>
    Task<List<OverviewCreatorOption>> GetCreatorOptionsAsync(int projectId);

    /// <summary>取單筆題目詳情（含子題）。</summary>
    Task<QuestionFormData?> GetDetailAsync(int questionId);

    /// <summary>復原已軟刪除題目（包裝 IQuestionService.RestoreAsync）。</summary>
    Task<bool> RestoreAsync(int questionId, int operatorUserId);

    /// <summary>取此題的審題歷程（管理員監控用，不匿名）。委派 IReviewService。</summary>
    Task<List<ReviewHistoryEntry>> GetReviewHistoryAsync(int questionId);

    /// <summary>
    /// 取此單元（母題=subQuestionId null / 子題=subQuestionId 值）所有審題者的劃記評語。
    /// 管理員視角不匿名，依 (Stage, CreatedAt) 升冪排序。委派 IAnnotationService.GetByQuestionUnitAsync。
    /// </summary>
    Task<List<OverviewAnnotationCard>> GetAnnotationsForUnitAsync(int questionId, int? subQuestionId);

    /// <summary>依題型組成詳情面板用的標籤（主類/次類/文體/核心能力…）。</summary>
    Dictionary<string, string> BuildPreviewTags(QuestionFormData formData);

    /// <summary>依題型挑等級標籤字典（聽力 vs 一般題等級顯示不同）。</summary>
    string LevelLabel(string typeKey, byte? level);
}

public class OverviewService(
    IQuestionService questionService,
    IReviewService reviewService,
    IDatabaseService db,
    IPhaseTransitionCoordinator phaseCoordinator,
    IAnnotationService annotationService) : IOverviewService
{
    private readonly IQuestionService _questionService = questionService;
    private readonly IReviewService   _reviewService   = reviewService;
    private readonly IDatabaseService _db = db;
    private readonly IPhaseTransitionCoordinator _phaseCoordinator = phaseCoordinator;
    private readonly IAnnotationService _annotationService = annotationService;

    public async Task<OverviewListResult> LoadAsync(int projectId, OverviewFilter filter)
    {
        // 命題總覽必須看到已刪除題目（紅色「命題刪除」標籤 + 復原按鈕）
        var listFilter = filter.ToListFilter(projectId);
        listFilter.IncludeDeleted = true;

        // 階段轉換統一委派 PhaseTransitionCoordinator（60 秒去重 + 統一 logging）
        await _phaseCoordinator.EnsureAsync(projectId);

        // 先取 PhaseCode：用來決定狀態識別碼如何翻譯（以及 pending 計算）
        var phase = await _questionService.GetCurrentPhaseAsync(projectId);
        var phaseCode = phase is { PhaseCode: var pc } ? (byte?)pc : null;

        // 翻譯 StatusKey → 後端粗篩條件 + 前端精篩策略
        // 後端粗篩：StatusesOverride / HasReplied / IsDeleted（決定 SQL 範圍）
        // 前端精篩：篩完整批回來後再依 ResolveDisplayStatus 同邏輯做 in-memory 過濾（針對需查 AllReviewersResponded 等 4 維度的識別碼）
        var (overrideStatuses, hasReplied, deletedOnly, postFilter) = TranslateStatusKey(filter.StatusKey, phaseCode);

        // 「命題刪除」走特殊路徑：只看 IsDeleted=1
        if (deletedOnly)
        {
            listFilter.StatusesOverride = null;
            listFilter.HasReplied = null;
        }
        else
        {
            listFilter.StatusesOverride = overrideStatuses;
            listFilter.HasReplied = hasReplied;
        }

        // 需前端精篩時關掉分頁拉大 PageSize：避免後端粗篩切片造成精篩後筆數不足
        // postFilter != null 表示需要 in-memory 過濾，這時改一次抓回專案內符合粗篩條件的全部題目
        var needInMemoryFilter = postFilter is not null || deletedOnly;
        if (needInMemoryFilter)
        {
            listFilter.Page = 1;
            listFilter.PageSize = 10000;   // 單梯次題目上限保護值，足以覆蓋實務題量
        }

        // 修補 D：StatusCounts 與 StatusKeyCounts 來自同一份 UNION ALL（母題+子題），
        //   合併為單一 BuildOverviewCountsAsync 一次 SQL 同時算兩種 dict，省一個全表掃描 + EXISTS 子查詢。
        var countsTask  = BuildOverviewCountsAsync(projectId, phaseCode);
        var listTask    = _questionService.ListAsync(listFilter);
        var pendingTask = GetPendingRevisionCountAsync(projectId, phaseCode);

        await Task.WhenAll(countsTask, listTask, pendingTask);

        var (statusRowCounts, statusKeyCounts) = countsTask.Result;
        var list = listTask.Result;
        var items = list.Items;

        // 「命題刪除」過濾：後端 IncludeDeleted=true 把已刪題目混在一起回來，這裡挑 IsDeleted=1
        if (deletedOnly)
            items = items.Where(i => i.IsDeleted).ToList();

        // 「該題此審題階段所有被指派審題者皆已給意見」—— 只查當前頁列表，避免全表掃描
        var responded = await GetAllReviewersRespondedAsync(
            projectId, phaseCode, items.Select(i => i.Id));

        // 前端精篩：依 razor 端 ResolveDisplayStatus 同邏輯重新過濾與分頁
        int totalCount;
        if (needInMemoryFilter)
        {
            // 精篩 + 重新分頁（取代 server-side 分頁）
            var filtered = postFilter is null
                ? items
                : items.Where(i => postFilter(i, phaseCode, responded)).ToList();
            totalCount = filtered.Count;

            var page     = Math.Max(1, filter.Page);
            var pageSize = Math.Max(1, filter.PageSize);
            items = filtered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // 修補 E：原本這裡會用「縮小後的 ids」第二次查 GetAllReviewersRespondedAsync。
            //   但第一次的 responded dict 已涵蓋所有 raw items 的 Q 維度判定（filtered/paginate 後是 raw 子集），
            //   razor 端用 responded.GetValueOrDefault(item.Id, false) lookup 仍正確。直接 reuse 省一個 SQL。
        }
        else
        {
            totalCount = list.TotalCount;
        }

        return new OverviewListResult
        {
            Items                = items,
            StatusCounts         = statusRowCounts,
            TotalCount           = totalCount,
            Page                 = needInMemoryFilter ? Math.Max(1, filter.Page) : list.Page,
            PageSize             = filter.PageSize,
            PendingRevisionCount = pendingTask.Result,
            // 梯次當前 PhaseCode（null = 不在任何進行中階段）；轉 byte? 讓 UI 端易於比對 QuestionStatus 常數
            CurrentPhaseCode     = phaseCode,
            AllReviewersResponded = responded,
            StatusKeyCounts      = statusKeyCounts
        };
    }

    /// <summary>
    /// 把 StatusKey 識別碼翻譯為後端粗篩條件 + 前端精篩策略。
    ///   overrideStatuses：QuestionListFilter.StatusesOverride（IN 條件）
    ///   hasReplied      ：QuestionListFilter.HasReplied（修題已/未送出）
    ///   deletedOnly     ：是否走「命題刪除」特殊路徑（後端不限 status，前端篩 IsDeleted=1）
    ///   postFilter      ：前端 in-memory 精篩函式；若 null 則直接以後端結果為準
    /// 與 razor 端 ResolveDisplayStatus 的判定條件 100% 對齊。
    /// </summary>
    private static (byte[]? overrideStatuses, bool? hasReplied, bool deletedOnly, Func<QuestionListItem, byte?, Dictionary<int, bool>, bool>? postFilter)
        TranslateStatusKey(string? key, byte? phaseCode)
    {
        if (string.IsNullOrWhiteSpace(key))
            return (null, null, false, null);

        switch (key)
        {
            // ─── 命題 ───
            case OverviewStatusKey.Draft:
                // 命題草稿：Status=0 且 PhaseCode < 3（避免抓到 R1「未完成命題」）
                // PhaseCode ≥ 3 時直接回空：後端 SQL 粗篩 Status=0，前端再用 PhaseCode < 3 守門
                return ([QuestionStatus.Draft], null, false,
                    (i, pc, _) => MatchDraft(i, pc));

            case OverviewStatusKey.Completed:
                return ([QuestionStatus.Completed], null, false, null);

            case OverviewStatusKey.FailedComposition:
                // 未完成命題：Status=0 且 PhaseCode ≥ 3
                return ([QuestionStatus.Draft], null, false,
                    (i, pc, _) => MatchFailedComposition(i, pc));

            // ─── 審題（R2）───
            case OverviewStatusKey.AwaitingReview:
                // 待審：PhaseCode∈{3,5,7} + Status∈{2,3,5,7} + 未全給意見
                return ([QuestionStatus.Submitted,
                         QuestionStatus.PeerReviewing,
                         QuestionStatus.ExpertReviewing,
                         QuestionStatus.FinalReviewing], null, false,
                    MatchAwaitingReview);

            case OverviewStatusKey.Reviewed:
                // 已給意見：PhaseCode∈{3,5,7} + Status∈{2,3,5,7} + 全給意見
                return ([QuestionStatus.Submitted,
                         QuestionStatus.PeerReviewing,
                         QuestionStatus.ExpertReviewing,
                         QuestionStatus.FinalReviewing], null, false,
                    MatchReviewed);

            // ─── 修題 ───
            case OverviewStatusKey.InRevision:
                // 修題中：Status∈{4,6,8} + 未送出 + PhaseCode ≥ Status（避開 A 的「OO 完成」）
                return ([QuestionStatus.PeerEditing,
                         QuestionStatus.ExpertEditing,
                         QuestionStatus.FinalEditing], false, false,
                    (i, pc, _) => MatchInRevision(i, pc));

            case OverviewStatusKey.RevisionSubmitted:
                // 修題已送出：Status∈{4,6,8} + 已送出
                return ([QuestionStatus.PeerEditing,
                         QuestionStatus.ExpertEditing,
                         QuestionStatus.FinalEditing], true, false, null);

            case OverviewStatusKey.AwaitingNext:
                // OO 完成：Status∈{4,6,8} + PhaseCode 落後（PhaseCode < Status）
                return ([QuestionStatus.PeerEditing,
                         QuestionStatus.ExpertEditing,
                         QuestionStatus.FinalEditing], null, false,
                    (i, pc, _) => MatchAwaitingNext(i, pc));

            // ─── 結果 ───
            case OverviewStatusKey.Adopted:
                return ([QuestionStatus.Adopted, QuestionStatus.Archived], null, false, null);

            case OverviewStatusKey.NotAdopted:
                return ([QuestionStatus.Rejected, QuestionStatus.ClosedNotAdopted], null, false, null);

            // ─── 其他 ───
            case OverviewStatusKey.Deleted:
                return (null, null, true, null);   // deletedOnly=true：後端不限 status，前端只留 IsDeleted=1

            default:
                return (null, null, false, null);
        }
    }

    /// <summary>審題鎖定中（Submitted/PeerReviewing/ExpertReviewing/FinalReviewing）。</summary>
    private static bool IsReviewLocked(byte status) =>
        status is QuestionStatus.Submitted
               or QuestionStatus.PeerReviewing
               or QuestionStatus.ExpertReviewing
               or QuestionStatus.FinalReviewing;

    /// <summary>修題狀態（PeerEditing/ExpertEditing/FinalEditing）。</summary>
    private static bool IsEditing(byte status) =>
        status is QuestionStatus.PeerEditing
               or QuestionStatus.ExpertEditing
               or QuestionStatus.FinalEditing;

    // ─── 分桶條件（與 razor 端 ResolveDisplayStatus 同源） ─────────────────
    // 此處集中三方共用的條件函式：TranslateStatusKey 的 postFilter 與
    // BuildStatusKeyCountsAsync 都呼叫這些，避免雙寫造成偏移。
    private static bool MatchDraft(QuestionListItem i, byte? pc) =>
        i.Status == QuestionStatus.Draft && (pc is null || pc < 3) && !i.IsDeleted;

    private static bool MatchFailedComposition(QuestionListItem i, byte? pc) =>
        i.Status == QuestionStatus.Draft && pc is byte p && p >= 3 && !i.IsDeleted;

    private static bool MatchAwaitingReview(QuestionListItem i, byte? pc, Dictionary<int, bool> resp) =>
        pc is byte p && (p == 3 || p == 5 || p == 7)
        && IsReviewLocked(i.Status)
        && !resp.GetValueOrDefault(i.Id, false)
        && !i.IsDeleted;

    private static bool MatchReviewed(QuestionListItem i, byte? pc, Dictionary<int, bool> resp) =>
        pc is byte p && (p == 3 || p == 5 || p == 7)
        && IsReviewLocked(i.Status)
        && resp.GetValueOrDefault(i.Id, false)
        && !i.IsDeleted;

    private static bool MatchInRevision(QuestionListItem i, byte? pc) =>
        IsEditing(i.Status) && !i.IsDeleted && (pc is null || pc >= i.Status);

    private static bool MatchAwaitingNext(QuestionListItem i, byte? pc) =>
        IsEditing(i.Status) && !i.IsDeleted && (pc is null || pc < i.Status);

    /// <summary>
    /// 計算當前頁列表中，每筆題目「在當前審題階段是否全體被指派審題者皆已給意見」。
    /// PhaseCode 對應 ReviewStage：3→1（互審） / 5→2（專審） / 7→3（總審）。
    /// 判定：該題該階段被指派筆數 > 0，且所有筆數的 Comment 皆非空。
    /// 非審題階段（PhaseCode 不在 {3,5,7}）或列表為空 → 直接回空 dict，不打 DB。
    /// </summary>
    private async Task<Dictionary<int, bool>> GetAllReviewersRespondedAsync(
        int projectId, byte? phaseCode, IEnumerable<int> questionIds)
    {
        // 題組類 N+1 列拆列（母題 + N 子題）後，呼叫端傳進來的 ids 可能含同一 QuestionId 多次。
        // 用 HashSet 去重後再查，避免 ToDictionary 撞重複 key 拋 ArgumentException。
        var ids = questionIds.ToHashSet();
        if (ids.Count == 0) return new();

        var stage = phaseCode switch
        {
            3 => (byte)1,   // 互審
            5 => (byte)2,   // 專審
            7 => (byte)3,   // 總審
            _ => (byte)0
        };
        if (stage == 0) return new();

        // GROUP BY QuestionId：當分配筆數 = 有效 Comment 筆數時，回傳該 QuestionId
        const string sql = """
            SELECT QuestionId
            FROM   dbo.MT_ReviewAssignments
            WHERE  ProjectId   = @ProjectId
              AND  ReviewStage = @Stage
              AND  QuestionId IN @Ids
            GROUP BY QuestionId
            HAVING COUNT(*) > 0
               AND COUNT(*) = SUM(CASE WHEN Comment IS NOT NULL
                                         AND LEN(LTRIM(RTRIM(Comment))) > 0
                                       THEN 1 ELSE 0 END);
            """;

        using var conn = _db.CreateConnection();
        var responded = (await conn.QueryAsync<int>(sql, new
        {
            ProjectId = projectId,
            Stage     = stage,
            Ids       = ids
        })).ToHashSet();

        return ids.ToDictionary(id => id, id => responded.Contains(id));
    }

    /// <summary>
    /// 計算「待修編」實際數量（列數視角，含子題）：
    ///   母題列：q.Status = PhaseCode（限 4/6/8）且命題者尚未於本輪同 stage 寫 RevisionReplies（SubQuestionId IS NULL）
    ///   子題列：sq.Status = PhaseCode 且命題者尚未於本輪同 stage 寫 RevisionReplies（SubQuestionId = sq.Id）
    /// 兩者 UNION ALL 後 COUNT，與列表「拆列後」實際呈現的待修編列數一致。
    /// Plan_014：本輪過濾 — 只認「上次總審退回後」寫的 reply 為本輪已修。
    /// 若 phaseCode 為 null 或不在 {4,6,8}，直接回 0（不打 DB）。
    /// </summary>
    private async Task<int> GetPendingRevisionCountAsync(int projectId, byte? phaseCode)
    {
        // 邊界守門：非修題階段直接回 0
        if (phaseCode is not (4 or 6 or 8)) return 0;

        const string sql = """
            WITH pending AS (
                -- 母題單元
                SELECT q.Id AS QId, CAST(NULL AS INT) AS SubId
                FROM dbo.MT_Questions q
                WHERE q.ProjectId = @ProjectId
                  AND q.IsDeleted = 0
                  AND q.Status    = @PhaseCode
                  AND NOT EXISTS (
                        SELECT 1 FROM dbo.MT_RevisionReplies rr
                        WHERE rr.QuestionId = q.Id
                          AND rr.SubQuestionId IS NULL
                          AND rr.UserId       = q.CreatorId
                          AND rr.Stage        = q.Status
                          AND rr.CreatedAt > ISNULL(
                              (SELECT MAX(DecidedAt) FROM dbo.MT_ReviewAssignments
                               WHERE QuestionId = q.Id AND ReviewStage = 3 AND Decision IN (2, 3)),
                              '1900-01-01')
                  )

                UNION ALL

                -- 子題單元（題組類拆列）
                SELECT q.Id AS QId, sq.Id AS SubId
                FROM dbo.MT_SubQuestions sq
                INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
                WHERE q.ProjectId = @ProjectId
                  AND q.IsDeleted = 0
                  AND sq.Status   = @PhaseCode
                  AND NOT EXISTS (
                        SELECT 1 FROM dbo.MT_RevisionReplies rr
                        WHERE rr.QuestionId    = q.Id
                          AND rr.SubQuestionId = sq.Id
                          AND rr.UserId        = q.CreatorId
                          AND rr.Stage         = sq.Status
                          AND rr.CreatedAt > ISNULL(
                              (SELECT MAX(DecidedAt) FROM dbo.MT_ReviewAssignments
                               WHERE QuestionId = q.Id AND ReviewStage = 3 AND Decision IN (2, 3)),
                              '1900-01-01')
                  )
            )
            SELECT COUNT(*) FROM pending;
            """;

        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql, new { ProjectId = projectId, PhaseCode = phaseCode.Value });
    }

    /// <summary>
    /// 修補 D：一次 UNION ALL（母題+子題）撈完 → C# 端同時 bucket 兩種 dict：
    ///   - StatusRowCounts（依 Status 分桶，IsDeleted=1 不計）— 給統計卡片「總題數 / 命題進行中 / 已採用」用
    ///   - StatusKeyCounts（依 OverviewStatusKey 分桶）— 給狀態篩選下拉動態渲染用
    /// 規則表與 razor 端 ResolveDisplayStatus + TranslateStatusKey 同源——三方共用 Match* 條件函式。
    /// 一筆列可能同時落在多個 StatusKey（例：修題中 + 修題已送出 → 正常；修題已送出單獨計於 RevisionSubmitted）。
    /// 若梯次無題目直接回空 dict，下拉只會剩「所有狀態」一個選項。
    /// </summary>
    private async Task<(Dictionary<byte, int> StatusRowCounts, Dictionary<string, int> StatusKeyCounts)>
        BuildOverviewCountsAsync(int projectId, byte? phaseCode)
    {
        // 輕量 SQL：母題 + 子題 UNION ALL，每列各自帶自身 Status / HasRepliedThisStage / IsDeleted
        // HasRepliedThisStage 邏輯與 QuestionService.ListAsync 同步：EXISTS RevisionReplies 同 Stage + UserId=CreatorId
        // Plan_014：與 ListAsync 同步加上「本輪過濾」— PC=8 跨輪退回後舊 reply 不算本輪已修
        // 子題列：RevisionReplies 用 SubQuestionId = sq.Id 比對
        const string sql = """
            -- 母題列
            SELECT
                q.Id AS Id,
                CAST(NULL AS INT) AS SubId,
                q.Status,
                q.IsDeleted,
                CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.MT_RevisionReplies rr
                    WHERE rr.QuestionId = q.Id
                      AND rr.SubQuestionId IS NULL
                      AND rr.UserId     = q.CreatorId
                      AND rr.Stage      = q.Status
                      AND q.Status IN (4, 6, 8)
                      AND rr.CreatedAt > ISNULL(
                          (SELECT MAX(DecidedAt) FROM dbo.MT_ReviewAssignments
                           WHERE QuestionId = q.Id AND ReviewStage = 3 AND Decision IN (2, 3)),
                          '1900-01-01')
                ) THEN 1 ELSE 0 END AS BIT) AS HasRepliedThisStage
            FROM dbo.MT_Questions q
            WHERE q.ProjectId = @ProjectId

            UNION ALL

            -- 子題列（題組類）
            SELECT
                q.Id AS Id,
                sq.Id AS SubId,
                sq.Status,
                q.IsDeleted,
                CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.MT_RevisionReplies rr
                    WHERE rr.QuestionId    = q.Id
                      AND rr.SubQuestionId = sq.Id
                      AND rr.UserId        = q.CreatorId
                      AND rr.Stage         = sq.Status
                      AND sq.Status IN (4, 6, 8)
                      AND rr.CreatedAt > ISNULL(
                          (SELECT MAX(DecidedAt) FROM dbo.MT_ReviewAssignments
                           WHERE QuestionId = q.Id AND ReviewStage = 3 AND Decision IN (2, 3)),
                          '1900-01-01')
                ) THEN 1 ELSE 0 END AS BIT) AS HasRepliedThisStage
            FROM dbo.MT_SubQuestions sq
            INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
            WHERE q.ProjectId = @ProjectId;
            """;

        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<StatusBucketRow>(sql, new { ProjectId = projectId })).AsList();
        if (rows.Count == 0) return (new(), new());

        // 全體審題者已給意見字典：只 query 仍在審題鎖定狀態且未刪的題目（其他狀態查了沒意義）
        var responded = await GetAllReviewersRespondedAsync(
            projectId, phaseCode,
            rows.Where(r => !r.IsDeleted && IsReviewLocked(r.Status)).Select(r => r.Id));

        var rowCounts = new Dictionary<byte, int>();
        var keyCounts = new Dictionary<string, int>();
        void BumpStatus(byte s) => rowCounts[s] = rowCounts.GetValueOrDefault(s, 0) + 1;
        void BumpKey(string k)  => keyCounts[k] = keyCounts.GetValueOrDefault(k, 0) + 1;

        foreach (var r in rows)
        {
            // StatusRowCounts：IsDeleted=1 不計（與舊 BuildStatusRowCountsAsync WHERE q.IsDeleted=0 對齊）
            if (!r.IsDeleted) BumpStatus(r.Status);

            // StatusKeyCounts：命題刪除獨立桶且優先（與其他 key 互斥）
            if (r.IsDeleted) { BumpKey(OverviewStatusKey.Deleted); continue; }

            // 用 QuestionListItem 走 Match* 條件——欄位夠用即可
            var item = new QuestionListItem
            {
                Id        = r.Id,
                Status    = r.Status,
                IsDeleted = false,
                HasRepliedThisStage = r.HasRepliedThisStage
            };

            // 命題
            if (MatchDraft(item, phaseCode))             BumpKey(OverviewStatusKey.Draft);
            if (MatchFailedComposition(item, phaseCode)) BumpKey(OverviewStatusKey.FailedComposition);
            if (r.Status == QuestionStatus.Completed)    BumpKey(OverviewStatusKey.Completed);

            // 審題
            if (MatchAwaitingReview(item, phaseCode, responded)) BumpKey(OverviewStatusKey.AwaitingReview);
            if (MatchReviewed(item, phaseCode, responded))       BumpKey(OverviewStatusKey.Reviewed);

            // 修題
            if (MatchInRevision(item, phaseCode))                  BumpKey(OverviewStatusKey.InRevision);
            if (MatchAwaitingNext(item, phaseCode))                BumpKey(OverviewStatusKey.AwaitingNext);
            if (IsEditing(r.Status) && r.HasRepliedThisStage)      BumpKey(OverviewStatusKey.RevisionSubmitted);

            // 結果
            if (r.Status is QuestionStatus.Adopted or QuestionStatus.Archived)
                BumpKey(OverviewStatusKey.Adopted);
            if (r.Status is QuestionStatus.Rejected or QuestionStatus.ClosedNotAdopted)
                BumpKey(OverviewStatusKey.NotAdopted);
        }

        return (rowCounts, keyCounts);
    }

    /// <summary>BuildOverviewCountsAsync 用的輕量資料列（含子題列 — Id=母題 QId、SubId=子題 Id 或 NULL）。</summary>
    private record StatusBucketRow(int Id, int? SubId, byte Status, bool IsDeleted, bool HasRepliedThisStage);

    public async Task<List<OverviewCreatorOption>> GetCreatorOptionsAsync(int projectId)
    {
        // 只列實際有出題的命題教師（含已軟刪除的題目，避免老師全部草稿被刪後就消失於下拉）
        const string sql = """
            SELECT
                u.Id          AS Id,
                u.DisplayName AS DisplayName,
                COUNT(q.Id)   AS QuestionCount
            FROM dbo.MT_Questions q
            INNER JOIN dbo.MT_Users u ON u.Id = q.CreatorId
            WHERE q.ProjectId = @ProjectId
            GROUP BY u.Id, u.DisplayName
            ORDER BY u.DisplayName;
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<OverviewCreatorOption>(sql, new { ProjectId = projectId });
        return rows.AsList();
    }

    public Task<QuestionFormData?> GetDetailAsync(int questionId)
        => _questionService.GetByIdAsync(questionId);

    public Task<bool> RestoreAsync(int questionId, int operatorUserId)
        => _questionService.RestoreAsync(questionId, operatorUserId);

    public Task<List<ReviewHistoryEntry>> GetReviewHistoryAsync(int questionId)
        => _reviewService.GetHistoryByQuestionIdAsync(questionId);

    public async Task<List<OverviewAnnotationCard>> GetAnnotationsForUnitAsync(int questionId, int? subQuestionId)
    {
        var rows = await _annotationService.GetByQuestionUnitAsync(questionId, subQuestionId);
        return rows.Select(a => new OverviewAnnotationCard
        {
            AnnotationId   = a.Id,
            SubQuestionId  = a.SubQuestionId,
            FieldKey       = a.FieldKey,
            FieldLabel     = AnnotationFieldLabel.Describe(a.FieldKey),
            Stage          = (byte)a.Stage,
            // 管理員視角：直接顯示真實階段名稱，不走 AnnotationActorLabel 匿名
            StageLabel     = a.Stage switch
            {
                ReviewStage.Mutual => "互審",
                ReviewStage.Expert => "專審",
                ReviewStage.Final  => "總審",
                _                  => ""
            },
            AnchorStart    = a.AnchorStart,
            AnchorEnd      = a.AnchorEnd,
            SelectedText   = a.SelectedText,
            Comment        = a.Comment,
            ResponseState  = a.ResponseState is null ? (byte?)null : (byte)a.ResponseState.Value,
            NoChangeReason = a.NoChangeReason,
            ResponseByName = a.ResponseByName,
            CreatorName    = a.CreatorName,
            CreatedAt      = a.CreatedAt
        }).ToList();
    }

    public Dictionary<string, string> BuildPreviewTags(QuestionFormData f)
    {
        var tags = new Dictionary<string, string>();
        const string fallback = "未設定";
        string Lab(byte? v, IReadOnlyDictionary<byte, string> map)
            => v is null ? fallback : map.GetValueOrDefault(v.Value, fallback);

        switch (f.QuestionType)
        {
            case QuestionTypeCodes.Single:
            case QuestionTypeCodes.Select:
                tags["主類"] = Lab(f.Topic, QuestionConstants.TopicLabels);
                tags["次類"] = Lab(f.Subtopic, QuestionConstants.SubtopicLabels);
                break;
            case QuestionTypeCodes.LongText:
                tags["寫作模式"] = Lab(f.WritingMode, QuestionConstants.WritingModeLabels);
                break;
            case QuestionTypeCodes.ReadGroup:
                tags["文體"] = Lab(f.Genre, QuestionConstants.GenreLabels);
                break;
            case QuestionTypeCodes.ShortGroup:
                tags["主類"] = "文意判讀";
                tags["次類"] = "篇章辨析";
                tags["文體"] = Lab(f.Genre, QuestionConstants.GenreLabels);
                break;
            case QuestionTypeCodes.Listen:
                tags["核心能力"] = Lab(f.CoreAbility, QuestionConstants.CoreAbilityLabels);
                tags["細目指標"] = Lab(f.DetailIndicator, QuestionConstants.DetailIndicatorLabels);
                tags["語音類型"] = Lab(f.AudioType, QuestionConstants.AudioTypeLabels);
                tags["素材分類"] = Lab(f.Material, QuestionConstants.MaterialLabels);
                break;
            case QuestionTypeCodes.ListenGroup:
                tags["語音類型"] = Lab(f.AudioType, QuestionConstants.AudioTypeLabels);
                tags["素材分類"] = Lab(f.Material, QuestionConstants.MaterialLabels);
                break;
        }
        return tags;
    }

    public string LevelLabel(string typeKey, byte? level)
    {
        if (level is null) return "";
        return typeKey is QuestionTypeCodes.Listen or QuestionTypeCodes.ListenGroup
            ? QuestionConstants.ListenLevelLabels.GetValueOrDefault(level.Value, "")
            : QuestionConstants.GeneralLevelLabels.GetValueOrDefault(level.Value, "");
    }
}
