-- =====================================================================
-- Commit 1 對拍腳本：ReplaceProjectChildRecordsAsync 改批次 INSERT
-- 範圍：Phases / Targets / Roles 三段
-- 預期：對拍三段都回 0 列（資料完全等效）
--
-- 使用流程：
--   1. 修改下方 @ProjectId 為要測試的梯次 Id（建議挑一個成員 ≥ 5 的）
--   2. SSMS 開新查詢，跑【Step A 快照】這段
--   3. 切到網站 Projects 頁，開該梯次「編輯」→ 不做任何變更 → 按「儲存」
--   4. 回 SSMS，跑【Step B 對拍】這段
--   5. 三段差異全部回 0 列 = 行為等效，Commit 1 通過
-- =====================================================================

DECLARE @ProjectId int = 1;  -- ← 改這裡

-- =====================================================================
-- Step A：編輯前快照（先跑這段）
-- =====================================================================
IF OBJECT_ID('tempdb..#PhasesBefore') IS NOT NULL DROP TABLE #PhasesBefore;
IF OBJECT_ID('tempdb..#TargetsBefore') IS NOT NULL DROP TABLE #TargetsBefore;
IF OBJECT_ID('tempdb..#RolesBefore') IS NOT NULL DROP TABLE #RolesBefore;

SELECT PhaseCode, PhaseName, StartDate, EndDate, SortOrder
INTO #PhasesBefore
FROM dbo.MT_ProjectPhases WHERE ProjectId = @ProjectId;

SELECT QuestionTypeId, Granularity, [Level], TargetCount
INTO #TargetsBefore
FROM dbo.MT_ProjectTargets WHERE ProjectId = @ProjectId;

SELECT pm.UserId, pmr.RoleId
INTO #RolesBefore
FROM dbo.MT_ProjectMembers pm
INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
WHERE pm.ProjectId = @ProjectId;

DECLARE @PhaseCnt int, @TargetCnt int, @RoleCnt int;
SELECT @PhaseCnt = COUNT(*) FROM #PhasesBefore;
SELECT @TargetCnt = COUNT(*) FROM #TargetsBefore;
SELECT @RoleCnt = COUNT(*) FROM #RolesBefore;

PRINT N'Step A 完成。請去網站編輯該梯次後再跑 Step B。';
PRINT N'快照列數：';
PRINT N'  Phases: ' + CAST(@PhaseCnt AS varchar(10));
PRINT N'  Targets: ' + CAST(@TargetCnt AS varchar(10));
PRINT N'  Roles: ' + CAST(@RoleCnt AS varchar(10));

-- =====================================================================
-- Step B：編輯後對拍（網站儲存後跑這段）
-- 三段查詢應該都回 0 列；若有列代表新舊邏輯有差異。
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

-- Roles 對拍（用 UserId+RoleId 比對，不看自增 MemberId）
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

-- 視覺摘要
PRINT N'';
PRINT N'若上方三個結果集皆 0 列，Commit 1 對拍通過 ✅';
