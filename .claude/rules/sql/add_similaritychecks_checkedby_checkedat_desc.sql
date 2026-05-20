-- ============================================================
-- add_similaritychecks_checkedby_checkedat_desc.sql
-- 補上 MT_SimilarityChecks 兩個欄位描述：CheckedBy / CheckedAt
-- 建立日期：2026-05-20
-- 冪等可重跑（drop+add 模式）
-- ============================================================

USE [MT];
GO

-- (1) CheckedBy
IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.MT_SimilarityChecks')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MT_SimilarityChecks'), N'CheckedBy', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_dropextendedproperty
        @name = N'MS_Description',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
        @level2type = N'COLUMN', @level2name = N'CheckedBy';
EXEC sys.sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'觸發此次比對的操作者 UserId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
    @level2type = N'COLUMN', @level2name = N'CheckedBy';

-- (2) CheckedAt
IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.MT_SimilarityChecks')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MT_SimilarityChecks'), N'CheckedAt', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_dropextendedproperty
        @name = N'MS_Description',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
        @level2type = N'COLUMN', @level2name = N'CheckedAt';
EXEC sys.sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'最近一次計算/重算的時間戳',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
    @level2type = N'COLUMN', @level2name = N'CheckedAt';

PRINT N'OK CheckedBy / CheckedAt 描述已補上';
GO
