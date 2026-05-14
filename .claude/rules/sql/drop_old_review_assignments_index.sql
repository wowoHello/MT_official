-- ============================================================================
-- Plan_022 後續：DROP 舊版 UQ_MT_ReviewAssignments_Pending
--
-- 舊索引：(QuestionId, SubQuestionId, ReviewerId, ReviewStage) WHERE ReviewStatus = 0
-- 新索引（Plan_022）：拆分為 Master/Sub 兩個明確分流的 filtered unique index
-- 舊的功能與新的兩個重複，DROP 保持索引乾淨度與寫入效能
-- ============================================================================

-- 跑前確認新的兩個 index 都存在
SELECT name
FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.MT_ReviewAssignments')
  AND name IN ('UQ_MT_ReviewAssignments_Pending_Master',
               'UQ_MT_ReviewAssignments_Pending_Sub');
-- ※ 須回 2 列才能繼續，否則 DROP 後會失去保護

-- DROP 舊版
DROP INDEX UQ_MT_ReviewAssignments_Pending ON dbo.MT_ReviewAssignments;
GO

-- 驗證
SELECT name, is_unique, has_filter, filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.MT_ReviewAssignments')
  AND name LIKE 'UQ_%';
-- 預期僅留 UQ_MT_ReviewAssignments_Pending_Master + _Pending_Sub
