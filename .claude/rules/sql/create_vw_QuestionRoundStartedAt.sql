-- =====================================================================
-- Plan_DB_PerfReview 第二波 #6：vw_QuestionRoundStartedAt
-- 建立日期：2026-05-15
-- 目的：消除「上次總審退回時間 MAX」correlated subquery 在 13 處的複製
--      （QuestionService 5 + DashboardService 5 + OverviewService 2 + HomeService 1）
-- =====================================================================
-- 改一處全站受惠：未來若業務規則改變（如 Decision 編碼異動），
-- 只需改此 View 一處，13 處呼叫點自動同步。
-- =====================================================================

SET NOCOUNT ON;
PRINT '======================================';
PRINT ' Plan_DB_PerfReview 第二波 #6';
PRINT ' vw_QuestionRoundStartedAt';
PRINT '======================================';
PRINT '';

-- ─────────────────────────────────────────────────────────────────────
-- Part 1：健檢 — MT_ReviewAssignments 既有索引狀況
-- ─────────────────────────────────────────────────────────────────────
PRINT '=== [健檢] MT_ReviewAssignments 現有索引 ===';
SELECT i.name AS IndexName,
       i.type_desc AS IndexType,
       i.is_unique AS IsUnique,
       STUFF((SELECT ', ' + c.name
              FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
              ORDER BY ic.key_ordinal
              FOR XML PATH('')), 1, 2, '') AS KeyColumns
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID('dbo.MT_ReviewAssignments')
  AND i.type > 0
ORDER BY i.index_id;
PRINT '';

PRINT '=== [健檢] Stage 3 的退回紀錄量 ===';
SELECT COUNT(*)                                    AS TotalAssignments,
       SUM(CASE WHEN ReviewStage = 3 THEN 1 ELSE 0 END) AS Stage3Count,
       SUM(CASE WHEN ReviewStage = 3 AND Decision IN (2, 3) THEN 1 ELSE 0 END) AS Stage3RejectCount,
       COUNT(DISTINCT CASE WHEN ReviewStage = 3 AND Decision IN (2, 3) THEN QuestionId END) AS DistinctRejectedQuestions
FROM dbo.MT_ReviewAssignments;
PRINT '';

-- ─────────────────────────────────────────────────────────────────────
-- Part 2：建立 View（CREATE OR ALTER 冪等）
-- ─────────────────────────────────────────────────────────────────────
-- 設計：對每個 QuestionId 取「Stage 3 + Decision IN (2,3)（改後再審 / 不採用）」
--      的最晚 DecidedAt，代表「該題本輪總審退回的時間」。
-- 沒有 Stage 3 退回紀錄的 QuestionId 不會在 View 中出現，
-- 呼叫端用 ISNULL(rs.RoundStartedAt, '1900-01-01') fallback 處理 NULL。
-- ─────────────────────────────────────────────────────────────────────
GO

CREATE OR ALTER VIEW dbo.vw_QuestionRoundStartedAt AS
SELECT QuestionId,
       MAX(DecidedAt) AS RoundStartedAt
FROM dbo.MT_ReviewAssignments
WHERE ReviewStage = 3 AND Decision IN (2, 3)
GROUP BY QuestionId;
GO

PRINT '[#6] ✅ vw_QuestionRoundStartedAt 建立 / 更新完成';
PRINT '';

-- ─────────────────────────────────────────────────────────────────────
-- Part 3：對拍驗證（用前 5 筆比對 View vs. 原 correlated subquery）
-- ─────────────────────────────────────────────────────────────────────
PRINT '=== [對拍] View vs. 原 subquery 前 5 筆 ===';
SELECT TOP 5
    q.Id AS QuestionId,
    -- 原 correlated subquery 寫法
    ISNULL((SELECT MAX(DecidedAt) FROM dbo.MT_ReviewAssignments
            WHERE QuestionId = q.Id AND ReviewStage = 3 AND Decision IN (2, 3)),
           '1900-01-01') AS RoundStarted_Old,
    -- 新 View 寫法
    ISNULL((SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt WHERE QuestionId = q.Id),
           '1900-01-01') AS RoundStarted_New
FROM dbo.MT_Questions q
WHERE EXISTS (SELECT 1 FROM dbo.MT_ReviewAssignments ra
              WHERE ra.QuestionId = q.Id AND ra.ReviewStage = 3 AND ra.Decision IN (2, 3))
ORDER BY q.Id;

PRINT '';
PRINT '======================================';
PRINT ' 完成。確認對拍結果 Old vs New 一致即可';
PRINT '======================================';
