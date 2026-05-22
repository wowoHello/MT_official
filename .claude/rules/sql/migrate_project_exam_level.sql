-- ============================================================
-- Migration: MT_Projects 加 ExamLevel（CWT 統一命題等級）
-- 日期：2026-05-21
-- 對應計畫：CWT/LCT 雙模式 v1（CWT 統一等級功能）
--
-- 變更：
--   MT_Projects 新增 ExamLevel TINYINT NULL
--
-- 語意：
--   CWT 模式：0=初等、1=中等、2=中高等、3=高等、4=優等
--   LCT 模式：NULL（不使用統一等級，由各題自選聽力難度一~五）
--
-- 部署狀態：待執行
-- 冪等：CREATE 端不冪等；重跑前需手動 DROP 欄位
-- ============================================================

ALTER TABLE dbo.MT_Projects ADD ExamLevel TINYINT NULL;
GO

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'CWT 統一命題等級：0=初等、1=中等、2=中高等、3=高等、4=優等；LCT 模式 NULL',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'MT_Projects',
    @level2type = N'COLUMN', @level2name = N'ExamLevel';
GO

-- ============================================================
-- 驗證
-- ============================================================
SELECT t.name AS TableName, c.name AS ColumnName, ep.value AS Description
FROM sys.extended_properties ep
JOIN sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id
JOIN sys.tables  t ON t.object_id = ep.major_id
WHERE t.name = 'MT_Projects' AND c.name = 'ExamLevel';
