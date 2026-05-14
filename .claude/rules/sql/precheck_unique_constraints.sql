-- ============================================================
-- 加 UNIQUE 約束前的資料健檢
-- 執行時機：在跑 add_unique_constraints.sql 之前
-- 預期結果：6 個 SELECT 全部回傳「0 列」才能繼續
-- 若有任何 query 回傳 > 0 列，請人工決定保留哪一筆，刪除其他後再跑
-- ============================================================

USE [MT];
GO

-- ----------------------------------------------------------------
-- 健檢 1：MT_Users.Username 重複（理論上已有 filtered UNIQUE，這是雙保險）
-- ----------------------------------------------------------------
PRINT N'=== 健檢 1：Username 重複 ===';
SELECT Username, COUNT(*) AS DuplicateCount
FROM dbo.MT_Users
GROUP BY Username
HAVING COUNT(*) > 1
ORDER BY Username;

-- ----------------------------------------------------------------
-- 健檢 2：MT_Users.Email 重複（排除 null 與空字串）
-- ----------------------------------------------------------------
PRINT N'=== 健檢 2：Email 重複 ===';
SELECT Email, COUNT(*) AS DuplicateCount
FROM dbo.MT_Users
WHERE Email IS NOT NULL AND Email <> ''
GROUP BY Email
HAVING COUNT(*) > 1
ORDER BY Email;

-- ----------------------------------------------------------------
-- 健檢 3：MT_Modules.ModuleKey 重複（大小寫不敏感比對）
-- ----------------------------------------------------------------
PRINT N'=== 健檢 3：ModuleKey 重複 ===';
SELECT LOWER(ModuleKey) AS ModuleKeyLower, COUNT(*) AS DuplicateCount
FROM dbo.MT_Modules
GROUP BY LOWER(ModuleKey)
HAVING COUNT(*) > 1
ORDER BY ModuleKeyLower;

-- ----------------------------------------------------------------
-- 健檢 4：MT_ProjectMembers 同梯次同人重複
-- ----------------------------------------------------------------
PRINT N'=== 健檢 4：同梯次同人重複 ===';
SELECT ProjectId, UserId, COUNT(*) AS DuplicateCount
FROM dbo.MT_ProjectMembers
GROUP BY ProjectId, UserId
HAVING COUNT(*) > 1
ORDER BY ProjectId, UserId;

-- ----------------------------------------------------------------
-- 健檢 5：MT_ProjectMemberRoles 同成員同身份重複
-- ----------------------------------------------------------------
PRINT N'=== 健檢 5：同成員同身份重複 ===';
SELECT ProjectMemberId, RoleId, COUNT(*) AS DuplicateCount
FROM dbo.MT_ProjectMemberRoles
GROUP BY ProjectMemberId, RoleId
HAVING COUNT(*) > 1
ORDER BY ProjectMemberId, RoleId;

-- ----------------------------------------------------------------
-- 健檢 6：MT_RolePermissions 同角色同模組重複
-- ----------------------------------------------------------------
PRINT N'=== 健檢 6：同角色同模組重複 ===';
SELECT RoleId, ModuleId, COUNT(*) AS DuplicateCount
FROM dbo.MT_RolePermissions
GROUP BY RoleId, ModuleId
HAVING COUNT(*) > 1
ORDER BY RoleId, ModuleId;

-- ============================================================
-- 全部 6 個都回 0 列 → 可安全執行 add_unique_constraints.sql
-- ============================================================
