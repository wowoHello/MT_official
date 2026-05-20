-- ============================================================
-- add_users_passwordhash_desc.sql
-- 補上 MT_Users.PasswordHash 欄位描述
-- 建立日期：2026-05-20
-- 冪等可重跑（drop+add 模式）
-- ============================================================

USE [MT];
GO

IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.MT_Users')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MT_Users'), N'PasswordHash', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_dropextendedproperty
        @name = N'MS_Description',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_Users',
        @level2type = N'COLUMN', @level2name = N'PasswordHash';
EXEC sys.sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'密碼雜湊 (PBKDF2.v1$迭代次數$鹽值$雜湊)',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'MT_Users',
    @level2type = N'COLUMN', @level2name = N'PasswordHash';

PRINT N'OK PasswordHash 描述已補上';
GO
