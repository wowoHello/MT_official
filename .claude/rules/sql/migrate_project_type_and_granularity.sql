-- ============================================================
-- Migration: CWT/LCT 雙模式專案類型 + 題目顆粒度
-- 日期：2026-05-21
-- 對應計畫：CWT/LCT 雙模式 v1（階段 A）
--
-- 變更：
--   1. MT_Projects 新增 ProjectType (0=CWT、1=LCT)
--   2. MT_ProjectTargets 新增 Granularity (0=母題或單題、1=子題)
--
-- 部署狀態：已執行（2026-05-21 使用者手動部署 + 資料表重置）
-- 冪等：CREATE 端不冪等；重跑前需手動 DROP 欄位
-- ============================================================

-- MT_Projects.ProjectType
ALTER TABLE dbo.MT_Projects ADD ProjectType TINYINT NOT NULL DEFAULT 0;
GO

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'專案類型：0=CWT、1=LCT',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'MT_Projects',
    @level2type = N'COLUMN', @level2name = N'ProjectType';
GO

-- MT_ProjectTargets.Granularity
ALTER TABLE dbo.MT_ProjectTargets ADD Granularity TINYINT NOT NULL DEFAULT 0;
GO

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'題目顆粒度：0=母題或單題、1=子題',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'MT_ProjectTargets',
    @level2type = N'COLUMN', @level2name = N'Granularity';
GO

-- ============================================================
-- 驗證
-- ============================================================
SELECT t.name AS TableName, c.name AS ColumnName, ep.value AS Description
FROM sys.extended_properties ep
JOIN sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id
JOIN sys.tables  t ON t.object_id = ep.major_id
WHERE t.name IN ('MT_Projects','MT_ProjectTargets')
  AND c.name IN ('ProjectType','Granularity');
