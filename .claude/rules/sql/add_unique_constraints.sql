-- ============================================================
-- 加 6 個 UNIQUE 約束 + 統一 ModuleKey 大小寫
-- 執行時機：跑完 precheck_unique_constraints.sql 確認 0 重複後
-- 涵蓋：
--   1. MT_Users.Username（filtered UNIQUE，雙保險）
--   2. MT_Users.Email（filtered UNIQUE）
--   3. MT_Modules.ModuleKey（normalize lowercase + UNIQUE）
--   4. MT_ProjectMembers(ProjectId, UserId)
--   5. MT_ProjectMemberRoles(ProjectMemberId, RoleId)
--   6. MT_RolePermissions(RoleId, ModuleId)
--
-- 每個 ALTER 都帶 IF NOT EXISTS 防呆，可重複執行不會錯
-- ============================================================

USE [MT];
GO

-- ----------------------------------------------------------------
-- Step 0：統一 MT_Modules.ModuleKey 為 lowercase
-- ----------------------------------------------------------------
PRINT N'Step 0: 統一 ModuleKey 為 lowercase...';
UPDATE dbo.MT_Modules
SET ModuleKey = LOWER(ModuleKey)
WHERE ModuleKey <> LOWER(ModuleKey);

PRINT N'  變更筆數: ' + CAST(@@ROWCOUNT AS nvarchar(10));
GO

-- ----------------------------------------------------------------
-- Step 1：MT_Users.Username UNIQUE（filtered，排除 null/空）
-- ----------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_MT_Users_Username' AND object_id = OBJECT_ID('dbo.MT_Users')
)
BEGIN
    PRINT N'Step 1: 建立 UQ_MT_Users_Username...';
    CREATE UNIQUE INDEX UQ_MT_Users_Username
        ON dbo.MT_Users(Username)
        WHERE Username IS NOT NULL AND Username <> '';
END
ELSE PRINT N'Step 1: UQ_MT_Users_Username 已存在，跳過';
GO

-- ----------------------------------------------------------------
-- Step 2：MT_Users.Email UNIQUE（filtered，排除 null/空）
-- ----------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_MT_Users_Email' AND object_id = OBJECT_ID('dbo.MT_Users')
)
BEGIN
    PRINT N'Step 2: 建立 UQ_MT_Users_Email...';
    CREATE UNIQUE INDEX UQ_MT_Users_Email
        ON dbo.MT_Users(Email)
        WHERE Email IS NOT NULL AND Email <> '';
END
ELSE PRINT N'Step 2: UQ_MT_Users_Email 已存在，跳過';
GO

-- ----------------------------------------------------------------
-- Step 3：MT_Modules.ModuleKey UNIQUE
-- ----------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_MT_Modules_ModuleKey' AND object_id = OBJECT_ID('dbo.MT_Modules')
)
BEGIN
    PRINT N'Step 3: 建立 UQ_MT_Modules_ModuleKey...';
    CREATE UNIQUE INDEX UQ_MT_Modules_ModuleKey
        ON dbo.MT_Modules(ModuleKey);
END
ELSE PRINT N'Step 3: UQ_MT_Modules_ModuleKey 已存在，跳過';
GO

-- ----------------------------------------------------------------
-- Step 4：MT_ProjectMembers (ProjectId, UserId) UNIQUE
-- ----------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_MT_ProjectMembers_Project_User' AND object_id = OBJECT_ID('dbo.MT_ProjectMembers')
)
BEGIN
    PRINT N'Step 4: 建立 UQ_MT_ProjectMembers_Project_User...';
    CREATE UNIQUE INDEX UQ_MT_ProjectMembers_Project_User
        ON dbo.MT_ProjectMembers(ProjectId, UserId);
END
ELSE PRINT N'Step 4: UQ_MT_ProjectMembers_Project_User 已存在，跳過';
GO

-- ----------------------------------------------------------------
-- Step 5：MT_ProjectMemberRoles (ProjectMemberId, RoleId) UNIQUE
-- ----------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_MT_ProjectMemberRoles_Member_Role' AND object_id = OBJECT_ID('dbo.MT_ProjectMemberRoles')
)
BEGIN
    PRINT N'Step 5: 建立 UQ_MT_ProjectMemberRoles_Member_Role...';
    CREATE UNIQUE INDEX UQ_MT_ProjectMemberRoles_Member_Role
        ON dbo.MT_ProjectMemberRoles(ProjectMemberId, RoleId);
END
ELSE PRINT N'Step 5: UQ_MT_ProjectMemberRoles_Member_Role 已存在，跳過';
GO

-- ----------------------------------------------------------------
-- Step 6：MT_RolePermissions (RoleId, ModuleId) UNIQUE
-- ----------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_MT_RolePermissions_Role_Module' AND object_id = OBJECT_ID('dbo.MT_RolePermissions')
)
BEGIN
    PRINT N'Step 6: 建立 UQ_MT_RolePermissions_Role_Module...';
    CREATE UNIQUE INDEX UQ_MT_RolePermissions_Role_Module
        ON dbo.MT_RolePermissions(RoleId, ModuleId);
END
ELSE PRINT N'Step 6: UQ_MT_RolePermissions_Role_Module 已存在，跳過';
GO

-- ============================================================
-- 驗收：列出所有新建的 UNIQUE 索引
-- ============================================================
PRINT N'=== 驗收：UNIQUE 索引清單 ===';
SELECT
    OBJECT_NAME(i.object_id) AS TableName,
    i.name AS IndexName,
    i.is_unique,
    i.has_filter,
    i.filter_definition
FROM sys.indexes i
WHERE i.name IN (
    'UQ_MT_Users_Username',
    'UQ_MT_Users_Email',
    'UQ_MT_Modules_ModuleKey',
    'UQ_MT_ProjectMembers_Project_User',
    'UQ_MT_ProjectMemberRoles_Member_Role',
    'UQ_MT_RolePermissions_Role_Module'
)
ORDER BY TableName, IndexName;
-- 預期：6 列，全部 is_unique = 1
