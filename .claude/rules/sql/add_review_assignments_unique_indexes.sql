-- ============================================================================
-- Plan_022：補 MT_ReviewAssignments filtered unique index（DB 第二道防線）
-- 對應稽核：規則 2「同一 (Q, Sub, R, Stage) 不可重複 Pending」
--
-- 設計重點：
--   應用層採 append-only 歷史（Decided 多筆累積 + 至多 1 筆 Pending）；
--   全表 UNIQUE 會破壞合法多輪歷史，必須加 WHERE ReviewStatus=0 條件。
--   分母題（SubQuestionId IS NULL）與子題兩個 filtered unique index。
-- ============================================================================

-- 跑前自我檢查：是否有違反約束的既有資料
SELECT QuestionId, ReviewerId, ReviewStage, COUNT(*) AS DupCount
FROM dbo.MT_ReviewAssignments
WHERE SubQuestionId IS NULL AND ReviewStatus = 0
GROUP BY QuestionId, ReviewerId, ReviewStage
HAVING COUNT(*) > 1;

SELECT QuestionId, SubQuestionId, ReviewerId, ReviewStage, COUNT(*) AS DupCount
FROM dbo.MT_ReviewAssignments
WHERE SubQuestionId IS NOT NULL AND ReviewStatus = 0
GROUP BY QuestionId, SubQuestionId, ReviewerId, ReviewStage
HAVING COUNT(*) > 1;

-- 若上方兩段都回 0 列，再執行下面建索引：
CREATE UNIQUE INDEX UQ_MT_ReviewAssignments_Pending_Master
ON dbo.MT_ReviewAssignments(QuestionId, ReviewerId, ReviewStage)
WHERE SubQuestionId IS NULL AND ReviewStatus = 0;
GO

CREATE UNIQUE INDEX UQ_MT_ReviewAssignments_Pending_Sub
ON dbo.MT_ReviewAssignments(QuestionId, SubQuestionId, ReviewerId, ReviewStage)
WHERE SubQuestionId IS NOT NULL AND ReviewStatus = 0;
GO

-- 驗證
SELECT name, is_unique, has_filter, filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.MT_ReviewAssignments')
  AND name LIKE 'UQ_MT_ReviewAssignments_Pending%';
