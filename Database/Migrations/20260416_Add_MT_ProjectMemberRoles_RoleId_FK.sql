SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.MT_ProjectMemberRoles', N'U') IS NULL
        THROW 50000, N'找不到資料表 dbo.MT_ProjectMemberRoles。', 1;

    IF OBJECT_ID(N'dbo.MT_Roles', N'U') IS NULL
        THROW 50000, N'找不到資料表 dbo.MT_Roles。', 1;

    IF COL_LENGTH(N'dbo.MT_ProjectMemberRoles', N'RoleId') IS NULL
        THROW 50000, N'找不到欄位 dbo.MT_ProjectMemberRoles.RoleId。', 1;

    IF EXISTS (
        SELECT 1
        FROM sys.columns c
        INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
        WHERE c.object_id = OBJECT_ID(N'dbo.MT_ProjectMemberRoles')
          AND c.name = N'RoleId'
          AND t.name <> N'int'
    )
    BEGIN
        ALTER TABLE dbo.MT_ProjectMemberRoles
        ALTER COLUMN RoleId INT NOT NULL;
    END;

    IF EXISTS (
        SELECT 1
        FROM dbo.MT_ProjectMemberRoles pmr
        LEFT JOIN dbo.MT_Roles r ON r.Id = pmr.RoleId
        WHERE r.Id IS NULL
    )
    BEGIN
        THROW 50000, N'MT_ProjectMemberRoles.RoleId 存在找不到對應 MT_Roles.Id 的資料，已中止新增 foreign key。', 1;
    END;

    DECLARE @constraintName sysname = N'FK_MT_ProjectMemberRoles_RoleId_MT_Roles_Id';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_key_columns fkc
        INNER JOIN sys.foreign_keys fk ON fk.object_id = fkc.constraint_object_id
        WHERE fkc.parent_object_id = OBJECT_ID(N'dbo.MT_ProjectMemberRoles')
          AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = N'RoleId'
          AND fkc.referenced_object_id = OBJECT_ID(N'dbo.MT_Roles')
          AND COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) = N'Id'
    )
    BEGIN
        ALTER TABLE dbo.MT_ProjectMemberRoles WITH CHECK
        ADD CONSTRAINT FK_MT_ProjectMemberRoles_RoleId_MT_Roles_Id
            FOREIGN KEY (RoleId) REFERENCES dbo.MT_Roles(Id);
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.MT_ProjectMemberRoles')
          AND name = @constraintName
    )
    BEGIN
        ALTER TABLE dbo.MT_ProjectMemberRoles
        CHECK CONSTRAINT FK_MT_ProjectMemberRoles_RoleId_MT_Roles_Id;
    END;

    COMMIT TRANSACTION;
    PRINT N'已完成 dbo.MT_ProjectMemberRoles.RoleId -> dbo.MT_Roles(Id) foreign key 檢查與建立。';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
