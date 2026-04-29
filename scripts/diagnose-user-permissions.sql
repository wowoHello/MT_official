/* =====================================================================
   診斷腳本：使用者「系統角色 ∪ 梯次角色」對所有模組的最終權限
   用法：把 @TargetUserId / @TargetProjectId 換成你登入的 UserId
         與目前選中的 ProjectId 後執行
   ===================================================================== */
DECLARE @TargetUserId   INT = 1;   -- ← 換成你登入的 UserId（劉明杰 = jay）
DECLARE @TargetProjectId INT = 1;  -- ← 換成首頁當前選中的 ProjectId

PRINT '=========== A. 使用者基本資料 + 系統角色 ===========';
SELECT u.Id AS UserId, u.Username, u.DisplayName,
       u.RoleId AS SystemRoleId, r.Name AS SystemRoleName, r.Category, r.IsDefault
FROM   dbo.MT_Users u
INNER JOIN dbo.MT_Roles r ON r.Id = u.RoleId
WHERE  u.Id = @TargetUserId;

PRINT '=========== B. 此使用者在指定梯次的所有「梯次角色」===========';
SELECT pm.Id AS ProjectMemberId, pm.UserId, pm.ProjectId,
       pmr.RoleId AS ProjectRoleId, r.Name AS ProjectRoleName, r.Category, r.IsDefault
FROM   dbo.MT_ProjectMembers pm
INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
INNER JOIN dbo.MT_Roles r ON r.Id = pmr.RoleId
WHERE  pm.UserId = @TargetUserId AND pm.ProjectId = @TargetProjectId;

PRINT '=========== C. 上面所有角色 對 8 個模組的 IsEnabled 矩陣 ===========';
WITH AllRoles AS (
    SELECT u.RoleId FROM dbo.MT_Users u WHERE u.Id = @TargetUserId
    UNION
    SELECT pmr.RoleId
    FROM   dbo.MT_ProjectMembers pm
    INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
    WHERE  pm.UserId = @TargetUserId AND pm.ProjectId = @TargetProjectId
)
SELECT r.Id AS RoleId, r.Name AS RoleName,
       m.Id AS ModuleId, m.ModuleKey, m.Name AS ModuleName,
       ISNULL(rp.IsEnabled, 0) AS IsEnabled
FROM   AllRoles ar
CROSS JOIN dbo.MT_Modules m
INNER JOIN dbo.MT_Roles r ON r.Id = ar.RoleId
LEFT JOIN  dbo.MT_RolePermissions rp ON rp.RoleId = ar.RoleId AND rp.ModuleId = m.Id
WHERE  m.IsActive = 1
ORDER BY r.Id, m.SortOrder;

PRINT '=========== D. OR-union 後 → 首頁應該顯示的模組（IsEnabled=1 才會出現）===========';
SELECT m.Id AS ModuleId, m.ModuleKey, m.Name AS ModuleName,
       CASE WHEN EXISTS (
           SELECT 1
           FROM   dbo.MT_RolePermissions rp
           WHERE  rp.ModuleId = m.Id AND rp.IsEnabled = 1
             AND  rp.RoleId IN (
                 SELECT u.RoleId FROM dbo.MT_Users u WHERE u.Id = @TargetUserId
                 UNION
                 SELECT pmr.RoleId
                 FROM   dbo.MT_ProjectMembers pm
                 INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
                 WHERE  pm.UserId = @TargetUserId AND pm.ProjectId = @TargetProjectId
             )
       ) THEN 1 ELSE 0 END AS IsEnabled_OR_Union
FROM   dbo.MT_Modules m
WHERE  m.IsActive = 1
ORDER BY m.SortOrder;
