-- ============================================================
-- 補刀腳本：補上 add_unique_constraints.sql 漏建的 4 個索引
--
-- 既有的 UQ_MT_ProjectMembers_ProjectId_UserId 與 UQ_MT_RolePermissions_RoleId_ModuleId
-- 已涵蓋需求，所以這支腳本只建以下 4 個：
--   1. UQ_MT_Users_Username
--   2. UQ_MT_Users_Email
--   3. UQ_MT_Modules_ModuleKey
--   4. UQ_MT_ProjectMemberRoles_Member_Role
--
-- 全部帶 IF NOT EXISTS 防呆，可重複執行不會錯
-- ============================================================

USE [MT];
GO

-- ----------------------------------------------------------------
-- 1. MT_Users.Username UNIQUE（filtered，排除 null/空）
-- ----------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_MT_Users_Username' AND object_id = OBJECT_ID('dbo.MT_Users')
)
BEGIN
    PRINT N'建立 UQ_MT_Users_Username...';
    CREATE UNIQUE INDEX UQ_MT_Users_Username
        ON dbo.MT_Users(Username)
        WHERE Username IS NOT NULL AND Username <> '';
END
ELSE PRINT N'UQ_MT_Users_Username 已存在，跳過';
GO

-- ----------------------------------------------------------------
-- 2. MT_Users.Email UNIQUE（filtered，排除 null/空）
-- ----------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_MT_Users_Email' AND object_id = OBJECT_ID('dbo.MT_Users')
)
BEGIN
    PRINT N'建立 UQ_MT_Users_Email...';
    CREATE UNIQUE INDEX UQ_MT_Users_Email
        ON dbo.MT_Users(Email)
        WHERE Email IS NOT NULL AND Email <> '';
END
ELSE PRINT N'UQ_MT_Users_Email 已存在，跳過';
GO

-- ----------------------------------------------------------------
-- 3. MT_Modules.ModuleKey UNIQUE
-- ----------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_MT_Modules_ModuleKey' AND object_id = OBJECT_ID('dbo.MT_Modules')
)
BEGIN
    PRINT N'建立 UQ_MT_Modules_ModuleKey...';
    CREATE UNIQUE INDEX UQ_MT_Modules_ModuleKey
        ON dbo.MT_Modules(ModuleKey);
END
ELSE PRINT N'UQ_MT_Modules_ModuleKey 已存在，跳過';
GO

-- ----------------------------------------------------------------
-- 4. MT_ProjectMemberRoles (ProjectMemberId, RoleId) UNIQUE
-- ----------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_MT_ProjectMemberRoles_Member_Role' AND object_id = OBJECT_ID('dbo.MT_ProjectMemberRoles')
)
BEGIN
    PRINT N'建立 UQ_MT_ProjectMemberRoles_Member_Role...';
    CREATE UNIQUE INDEX UQ_MT_ProjectMemberRoles_Member_Role
        ON dbo.MT_ProjectMemberRoles(ProjectMemberId, RoleId);
END
ELSE PRINT N'UQ_MT_ProjectMemberRoles_Member_Role 已存在，跳過';
GO

-- ============================================================
-- 驗收：列出 MT_Users / MT_Modules / MT_ProjectMemberRoles 上的 UNIQUE 索引
-- ============================================================
PRINT N'=== 驗收 ===';
SELECT
    OBJECT_NAME(i.object_id) AS TableName,
    i.name AS IndexName,
    i.is_unique,
    i.filter_definition
FROM sys.indexes i
WHERE i.name LIKE 'UQ_MT_%'
  AND OBJECT_NAME(i.object_id) IN ('MT_Users', 'MT_Modules', 'MT_ProjectMemberRoles')
ORDER BY TableName, IndexName;
-- 預期：4 列
--   MT_Modules            / UQ_MT_Modules_ModuleKey            / 1 / NULL
--   MT_ProjectMemberRoles / UQ_MT_ProjectMemberRoles_Member_Role / 1 / NULL
--   MT_Users              / UQ_MT_Users_Email                  / 1 / 有 filter
--   MT_Users              / UQ_MT_Users_Username               / 1 / 有 filter
