-- =====================================================================
-- Commit 2 對拍腳本：ReplaceProjectChildRecordsAsync 完整批次化
-- 範圍：Phases / Targets / Members / Roles / Quotas 五段全部
-- 預期：對拍五段都回 0 列（資料完全等效）
--
-- 使用流程：
--   1. 修改下方 @ProjectId 為要測試的梯次 Id（建議挑成員 ≥ 5 且配額豐富的）
--   2. SSMS 開新查詢，跑【Step A 快照】這段
--   3. 切到網站 Projects 頁，開該梯次「編輯」→ 不做任何變更 → 按「儲存」
--   4. 回 SSMS，跑【Step B 對拍】這段
--   5. 五段差異全部回 0 列 = 行為等效，Commit 2 通過
-- =====================================================================

DECLARE @ProjectId int = 1;  -- ← 改這裡

-- =====================================================================
-- Step A：編輯前快照（先跑這段）
-- =====================================================================
IF OBJECT_ID('tempdb..#PhasesBefore')  IS NOT NULL DROP TABLE #PhasesBefore;
IF OBJECT_ID('tempdb..#TargetsBefore') IS NOT NULL DROP TABLE #TargetsBefore;
IF OBJECT_ID('tempdb..#MembersBefore') IS NOT NULL DROP TABLE #MembersBefore;
IF OBJECT_ID('tempdb..#RolesBefore')   IS NOT NULL DROP TABLE #RolesBefore;
IF OBJECT_ID('tempdb..#QuotasBefore')  IS NOT NULL DROP TABLE #QuotasBefore;

SELECT PhaseCode, PhaseName, StartDate, EndDate, SortOrder
INTO #PhasesBefore
FROM dbo.MT_ProjectPhases WHERE ProjectId = @ProjectId;

SELECT QuestionTypeId, Granularity, [Level], TargetCount
INTO #TargetsBefore
FROM dbo.MT_ProjectTargets WHERE ProjectId = @ProjectId;

SELECT UserId
INTO #MembersBefore
FROM dbo.MT_ProjectMembers WHERE ProjectId = @ProjectId;

SELECT pm.UserId, pmr.RoleId
INTO #RolesBefore
FROM dbo.MT_ProjectMembers pm
INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
WHERE pm.ProjectId = @ProjectId;

SELECT pm.UserId, mq.QuestionTypeId, mq.Granularity, mq.[Level], mq.QuotaCount
INTO #QuotasBefore
FROM dbo.MT_ProjectMembers pm
INNER JOIN dbo.MT_MemberQuotas mq ON mq.ProjectMemberId = pm.Id
WHERE pm.ProjectId = @ProjectId;

DECLARE @PhaseCnt int, @TargetCnt int, @MemberCnt int, @RoleCnt int, @QuotaCnt int;
SELECT @PhaseCnt  = COUNT(*) FROM #PhasesBefore;
SELECT @TargetCnt = COUNT(*) FROM #TargetsBefore;
SELECT @MemberCnt = COUNT(*) FROM #MembersBefore;
SELECT @RoleCnt   = COUNT(*) FROM #RolesBefore;
SELECT @QuotaCnt  = COUNT(*) FROM #QuotasBefore;

PRINT N'Step A 完成。請去網站編輯該梯次後再跑 Step B。';
PRINT N'快照列數：';
PRINT N'  Phases:  ' + CAST(@PhaseCnt  AS varchar(10));
PRINT N'  Targets: ' + CAST(@TargetCnt AS varchar(10));
PRINT N'  Members: ' + CAST(@MemberCnt AS varchar(10));
PRINT N'  Roles:   ' + CAST(@RoleCnt   AS varchar(10));
PRINT N'  Quotas:  ' + CAST(@QuotaCnt  AS varchar(10));

-- =====================================================================
-- Step B：編輯後對拍（網站儲存後跑這段）
-- 五段查詢應該都回 0 列；若有列代表新舊邏輯有差異。
-- =====================================================================

-- Phases 對拍
SELECT N'Phases 差異' AS Topic, *
FROM (
    SELECT PhaseCode, PhaseName, StartDate, EndDate, SortOrder FROM #PhasesBefore
    EXCEPT
    SELECT PhaseCode, PhaseName, StartDate, EndDate, SortOrder
    FROM dbo.MT_ProjectPhases WHERE ProjectId = @ProjectId
    UNION ALL
    SELECT PhaseCode, PhaseName, StartDate, EndDate, SortOrder
    FROM dbo.MT_ProjectPhases WHERE ProjectId = @ProjectId
    EXCEPT
    SELECT PhaseCode, PhaseName, StartDate, EndDate, SortOrder FROM #PhasesBefore
) x;

-- Targets 對拍
SELECT N'Targets 差異' AS Topic, *
FROM (
    SELECT QuestionTypeId, Granularity, [Level], TargetCount FROM #TargetsBefore
    EXCEPT
    SELECT QuestionTypeId, Granularity, [Level], TargetCount
    FROM dbo.MT_ProjectTargets WHERE ProjectId = @ProjectId
    UNION ALL
    SELECT QuestionTypeId, Granularity, [Level], TargetCount
    FROM dbo.MT_ProjectTargets WHERE ProjectId = @ProjectId
    EXCEPT
    SELECT QuestionTypeId, Granularity, [Level], TargetCount FROM #TargetsBefore
) x;

-- Members 對拍（只比對 UserId，不看自增 Id）
SELECT N'Members 差異' AS Topic, *
FROM (
    SELECT UserId FROM #MembersBefore
    EXCEPT
    SELECT UserId FROM dbo.MT_ProjectMembers WHERE ProjectId = @ProjectId
    UNION ALL
    SELECT UserId FROM dbo.MT_ProjectMembers WHERE ProjectId = @ProjectId
    EXCEPT
    SELECT UserId FROM #MembersBefore
) x;

-- Roles 對拍（用 UserId+RoleId 比對）
SELECT N'Roles 差異' AS Topic, *
FROM (
    SELECT UserId, RoleId FROM #RolesBefore
    EXCEPT
    SELECT pm.UserId, pmr.RoleId
    FROM dbo.MT_ProjectMembers pm
    INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
    WHERE pm.ProjectId = @ProjectId
    UNION ALL
    SELECT pm.UserId, pmr.RoleId
    FROM dbo.MT_ProjectMembers pm
    INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
    WHERE pm.ProjectId = @ProjectId
    EXCEPT
    SELECT UserId, RoleId FROM #RolesBefore
) x;

-- Quotas 對拍（用 UserId + 配額 5 欄比對）
SELECT N'Quotas 差異' AS Topic, *
FROM (
    SELECT UserId, QuestionTypeId, Granularity, [Level], QuotaCount FROM #QuotasBefore
    EXCEPT
    SELECT pm.UserId, mq.QuestionTypeId, mq.Granularity, mq.[Level], mq.QuotaCount
    FROM dbo.MT_ProjectMembers pm
    INNER JOIN dbo.MT_MemberQuotas mq ON mq.ProjectMemberId = pm.Id
    WHERE pm.ProjectId = @ProjectId
    UNION ALL
    SELECT pm.UserId, mq.QuestionTypeId, mq.Granularity, mq.[Level], mq.QuotaCount
    FROM dbo.MT_ProjectMembers pm
    INNER JOIN dbo.MT_MemberQuotas mq ON mq.ProjectMemberId = pm.Id
    WHERE pm.ProjectId = @ProjectId
    EXCEPT
    SELECT UserId, QuestionTypeId, Granularity, [Level], QuotaCount FROM #QuotasBefore
) x;

PRINT N'';
PRINT N'若上方五個結果集皆 0 列，Commit 2 對拍通過 ✅';
