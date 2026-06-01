-- ======================================================================
--  使用說明手冊（以頁面為區分）— MT_UserGuideFiles 加 PageKey
--  日期：2026-06-01
--  說明：PageKey 標記手冊所屬頁面（對應 ModuleCard.PageUrl），下載端依此配對權限
--  注意：PageKey 欄位已由使用者手動新增（nvarchar(50) NULL）；
--        本腳本補建「唯一索引」與「欄位描述」兩段（冪等，可重跑）
-- ======================================================================

-- 1. 欄位（若尚未新增才補；已手動加過會自動略過）
IF COL_LENGTH('dbo.MT_UserGuideFiles', 'PageKey') IS NULL
BEGIN
    ALTER TABLE dbo.MT_UserGuideFiles ADD PageKey NVARCHAR(50) NULL;
    PRINT N'[欄位] PageKey 已新增';
END
ELSE
    PRINT N'[欄位] PageKey 已存在，略過';
GO

-- 2. 唯一索引：IsActive=1 的列 PageKey 唯一（一頁一檔的 DB 安全網）
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_MT_UserGuideFiles_PageKey')
BEGIN
    CREATE UNIQUE INDEX UQ_MT_UserGuideFiles_PageKey
        ON dbo.MT_UserGuideFiles(PageKey) WHERE IsActive = 1;
    PRINT N'[索引] UQ_MT_UserGuideFiles_PageKey 已建立';
END
ELSE
    PRINT N'[索引] UQ_MT_UserGuideFiles_PageKey 已存在，略過';
GO

-- 3. 欄位描述（與既有欄位同風格；已存在則先刪再加避免重複）
IF EXISTS (
    SELECT 1 FROM sys.extended_properties ep
    INNER JOIN sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id
    WHERE ep.name = N'MS_Description'
      AND ep.major_id = OBJECT_ID(N'dbo.MT_UserGuideFiles')
      AND c.name = N'PageKey'
)
    EXEC sys.sp_dropextendedproperty
        @name=N'MS_Description',
        @level0type=N'SCHEMA', @level0name=N'dbo',
        @level1type=N'TABLE',  @level1name=N'MT_UserGuideFiles',
        @level2type=N'COLUMN', @level2name=N'PageKey';
GO

EXEC sys.sp_addextendedproperty
    @name=N'MS_Description',
    @value=N'頁面識別鍵：標記此手冊所屬頁面，對應 ModuleCard.PageUrl（login / home / dashboard / projects / overview / cwt-list / reviews / teachers / roles / announcements / system-logs），下載端依此配對使用者權限',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE',  @level1name=N'MT_UserGuideFiles',
    @level2type=N'COLUMN', @level2name=N'PageKey';
GO

PRINT N'✅ 完成';
