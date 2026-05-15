-- ======================================================================
-- 子題 Status 與母題對齊一次性補正
-- 背景：EnsureCompositionPhaseClosedAsync 過去只升級 MT_Questions 從 1→2，
--      MT_SubQuestions 沒同步，導致進入互審後子題 sq.Status 仍卡在 Completed(1)。
--      CwtList 審修作業區、Reviews 子題列因 sq.Status IN (2,3,5,7) 篩不到而消失。
--      程式端已修正（QuestionService.cs:1131 後新增同步 UPDATE）。
--      本腳本補正資料表中現有不一致的列。
--
-- 對齊規則：
--   • 母題在「審題鎖定 / 修題期間 / 結案」（Status 2-12）但子題卡在 0 或 1 → 拉齊到母題狀態
--   • 子題已自然分歧（>=Adopted/9 或處於修題中等狀態）不動
--   • 軟刪除題目不動
--
-- 健檢先（不改動）：執行第 1 段確認影響筆數，再決定是否執行第 2 段
-- ======================================================================

-- ① 健檢：列出將被同步的子題（含母題、子題目前狀態）
SELECT
    q.Id           AS MasterId,
    q.QuestionCode,
    q.Status       AS MasterStatus,
    sq.Id          AS SubId,
    sq.SortOrder,
    sq.Status      AS SubStatus_Before,
    q.Status       AS SubStatus_After
FROM dbo.MT_SubQuestions sq
INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
WHERE q.IsDeleted = 0
  AND sq.IsDeleted = 0
  AND q.Status BETWEEN 2 AND 12        -- 母題已過命題階段
  AND sq.Status IN (0, 1)              -- 但子題卡在 Draft / Completed
ORDER BY q.ProjectId, q.Id, sq.SortOrder;

-- ② 同步：把子題 Status 拉齊到母題 Status
-- BEGIN TRAN
UPDATE sq
SET sq.Status = q.Status
FROM dbo.MT_SubQuestions sq
INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
WHERE q.IsDeleted = 0
  AND sq.IsDeleted = 0
  AND q.Status BETWEEN 2 AND 12
  AND sq.Status IN (0, 1);

-- ROLLBACK    -- 如果想取消結果可改 ROLLBACK
-- COMMIT      -- 確認無誤再 COMMIT
