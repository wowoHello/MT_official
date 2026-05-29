/*****************************************************************
 * create_announcement_projects.sql
 *
 * 用途：建立 MT_AnnouncementProjects 中介表（公告 ↔ 梯次 N:N）
 *      支援單筆公告綁定多個梯次的需求
 *
 * 日期：2026-05-29
 * 對應計畫書：Plan_Announcement_Multiselect_2026-05-29.md
 *
 * 部署步驟：
 *   1. 在 SSMS 連到目標 DB
 *   2. 整份腳本貼上、F5 執行
 *   3. 確認訊息「✅ MT_AnnouncementProjects 建立完成」
 *   4. 確認既有資料 migration 結果（搬遷筆數應 = MT_Announcements.ProjectId 非 NULL 列數）
 *
 * 冪等性：全段以 IF NOT EXISTS / IF NOT EXISTS 包住，可重複執行不會出錯
 *****************************************************************/

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ──────────────────────────────────────────────────────────────
-- 1. 建立中介表
-- ──────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'MT_AnnouncementProjects' AND schema_id = SCHEMA_ID(N'dbo'))
BEGIN
    CREATE TABLE [dbo].[MT_AnnouncementProjects](
        [Id]              [int] IDENTITY(1,1) NOT NULL,
        [AnnouncementId]  [int] NOT NULL,
        [ProjectId]       [int] NOT NULL,
        [CreatedAt]       [datetime2](7) NOT NULL,
        PRIMARY KEY CLUSTERED ([Id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY],
        CONSTRAINT [UQ_MT_AnnouncementProjects_Pair]
            UNIQUE NONCLUSTERED ([AnnouncementId], [ProjectId])
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY]
    ) ON [PRIMARY]

    PRINT N'✅ MT_AnnouncementProjects 表建立完成'
END
ELSE
    PRINT N'⏭️  MT_AnnouncementProjects 表已存在，跳過 CREATE TABLE'
GO

-- ──────────────────────────────────────────────────────────────
-- 2. CreatedAt 預設值（與 MT_Announcements / MT_ProjectMembers 等表風格一致）
-- ──────────────────────────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.MT_AnnouncementProjects')
      AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MT_AnnouncementProjects'), N'CreatedAt', 'ColumnId')
)
BEGIN
    ALTER TABLE [dbo].[MT_AnnouncementProjects]
        ADD DEFAULT (SYSDATETIME()) FOR [CreatedAt]
    PRINT N'✅ CreatedAt 預設值 SYSDATETIME() 已加'
END
GO

-- ──────────────────────────────────────────────────────────────
-- 3. 加速查詢用的 nonclustered 索引
--    IX_AnnouncementId：給「該公告綁了哪些梯次」用（編輯載入、列表 STRING_AGG 聚合）
--    IX_ProjectId：給「該梯次有哪些公告」用（首頁公告過濾 GetHomeAnnouncementsAsync）
-- ──────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MT_AnnouncementProjects_AnnouncementId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_MT_AnnouncementProjects_AnnouncementId]
        ON [dbo].[MT_AnnouncementProjects] ([AnnouncementId] ASC)
        ON [PRIMARY]
    PRINT N'✅ IX_MT_AnnouncementProjects_AnnouncementId 索引建立完成'
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MT_AnnouncementProjects_ProjectId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_MT_AnnouncementProjects_ProjectId]
        ON [dbo].[MT_AnnouncementProjects] ([ProjectId] ASC)
        ON [PRIMARY]
    PRINT N'✅ IX_MT_AnnouncementProjects_ProjectId 索引建立完成'
END
GO

-- ──────────────────────────────────────────────────────────────
-- 4. 欄位描述（MS_Description，與既有 MT_Announcements 等表風格一致）
--    全段冪等：若描述已存在則更新、不存在則新增
-- ──────────────────────────────────────────────────────────────

DECLARE @sch    sysname = N'dbo';
DECLARE @tbl    sysname = N'MT_AnnouncementProjects';

-- Id
IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(@sch + N'.' + @tbl)
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(@sch + N'.' + @tbl), N'Id', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'唯一識別碼',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_AnnouncementProjects',
        @level2type = N'COLUMN', @level2name = N'Id'
GO

-- AnnouncementId
IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.MT_AnnouncementProjects')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MT_AnnouncementProjects'), N'AnnouncementId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'公告 ID (對應 MT_Announcements.Id)',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_AnnouncementProjects',
        @level2type = N'COLUMN', @level2name = N'AnnouncementId'
GO

-- ProjectId
IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.MT_AnnouncementProjects')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MT_AnnouncementProjects'), N'ProjectId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'梯次 ID (對應 MT_Projects.Id)',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_AnnouncementProjects',
        @level2type = N'COLUMN', @level2name = N'ProjectId'
GO

-- CreatedAt
IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.MT_AnnouncementProjects')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MT_AnnouncementProjects'), N'CreatedAt', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'建立時間',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_AnnouncementProjects',
        @level2type = N'COLUMN', @level2name = N'CreatedAt'
GO

PRINT N'✅ 欄位描述全部寫入完成'
GO

-- ──────────────────────────────────────────────────────────────
-- 5. 既有資料 Migration
--    把 MT_Announcements.ProjectId 非 NULL 的列搬到 junction table
--    冪等：用 NOT EXISTS 防重複插入
-- ──────────────────────────────────────────────────────────────
DECLARE @migrated INT = 0;

INSERT INTO dbo.MT_AnnouncementProjects (AnnouncementId, ProjectId)
SELECT a.Id, a.ProjectId
FROM dbo.MT_Announcements a
WHERE a.ProjectId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.MT_AnnouncementProjects ap
      WHERE ap.AnnouncementId = a.Id AND ap.ProjectId = a.ProjectId
  );

SET @migrated = @@ROWCOUNT;
PRINT N'✅ 既有資料 migration 完成，搬遷 ' + CAST(@migrated AS NVARCHAR(10)) + N' 列';
GO

-- ──────────────────────────────────────────────────────────────
-- 6. 健檢輸出
-- ──────────────────────────────────────────────────────────────
PRINT N''
PRINT N'──── 健檢結果 ────'

SELECT
    (SELECT COUNT(*) FROM dbo.MT_Announcements WHERE ProjectId IS NULL)         AS [全站廣播公告數 (junction 0 列)],
    (SELECT COUNT(*) FROM dbo.MT_Announcements WHERE ProjectId IS NOT NULL)     AS [既有指定梯次公告數 (應已搬到 junction)],
    (SELECT COUNT(*) FROM dbo.MT_AnnouncementProjects)                          AS [junction 表總列數],
    (SELECT COUNT(DISTINCT AnnouncementId) FROM dbo.MT_AnnouncementProjects)    AS [junction 涵蓋的公告數];

SELECT name AS [索引名稱], type_desc AS [類型]
FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.MT_AnnouncementProjects')
ORDER BY index_id;

PRINT N''
PRINT N'部署完成。請接著到 Service / Razor 層套用對應改動。'
