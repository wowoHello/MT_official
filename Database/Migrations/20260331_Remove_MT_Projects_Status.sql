SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT N'開始執行 migration: 移除 dbo.MT_Projects.Status 欄位';

IF OBJECT_ID(N'dbo.MT_Projects', N'U') IS NULL
BEGIN
    RAISERROR(N'dbo.MT_Projects 不存在，migration 中止。', 16, 1);
    RETURN;
END;

IF COL_LENGTH(N'dbo.MT_Projects', N'Status') IS NULL
BEGIN
    PRINT N'dbo.MT_Projects.Status 已不存在，無需重複執行。';
    RETURN;
END;

DECLARE @DependencyHits TABLE
(
    RowNo int IDENTITY(1,1) PRIMARY KEY,
    ObjectType nvarchar(60) NOT NULL,
    ObjectName nvarchar(512) NOT NULL
);

INSERT INTO @DependencyHits (ObjectType, ObjectName)
SELECT DISTINCT
    o.type_desc,
    QUOTENAME(SCHEMA_NAME(o.schema_id)) + N'.' + QUOTENAME(o.name)
FROM sys.sql_modules sm
INNER JOIN sys.objects o ON o.object_id = sm.object_id
WHERE sm.definition LIKE N'%MT_Projects%'
  AND (
      sm.definition LIKE N'%[[]Status[]]%'
      OR sm.definition LIKE N'%.Status%'
      OR sm.definition LIKE N'% Status%'
  );

IF EXISTS (SELECT 1 FROM @DependencyHits)
BEGIN
    PRINT N'偵測到可能仍引用 dbo.MT_Projects.Status 的資料庫物件，請先確認後再執行 migration：';

    SELECT ObjectType, ObjectName
    FROM @DependencyHits
    ORDER BY RowNo;

    RAISERROR(N'偵測到資料庫物件依賴 dbo.MT_Projects.Status，migration 已停止。', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @Sql nvarchar(max);

    DECLARE @DefaultConstraintName sysname;
    SELECT @DefaultConstraintName = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
       AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.MT_Projects')
      AND c.name = N'Status';

    IF @DefaultConstraintName IS NOT NULL
    BEGIN
        SET @Sql = N'ALTER TABLE dbo.MT_Projects DROP CONSTRAINT ' + QUOTENAME(@DefaultConstraintName) + N';';
        EXEC sys.sp_executesql @Sql;
        PRINT N'已移除 default constraint: ' + @DefaultConstraintName;
    END;

    DECLARE ConstraintCursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT cc.name
    FROM sys.check_constraints cc
    WHERE cc.parent_object_id = OBJECT_ID(N'dbo.MT_Projects')
      AND EXISTS
      (
          SELECT 1
          FROM sys.sql_expression_dependencies sed
          INNER JOIN sys.columns c
              ON c.object_id = sed.referenced_id
             AND c.column_id = sed.referenced_minor_id
          WHERE sed.referencing_id = cc.object_id
            AND sed.referenced_id = OBJECT_ID(N'dbo.MT_Projects')
            AND c.name = N'Status'
      );

    OPEN ConstraintCursor;

    DECLARE @ConstraintName sysname;
    FETCH NEXT FROM ConstraintCursor INTO @ConstraintName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Sql = N'ALTER TABLE dbo.MT_Projects DROP CONSTRAINT ' + QUOTENAME(@ConstraintName) + N';';
        EXEC sys.sp_executesql @Sql;
        PRINT N'已移除 check constraint: ' + @ConstraintName;

        FETCH NEXT FROM ConstraintCursor INTO @ConstraintName;
    END;

    CLOSE ConstraintCursor;
    DEALLOCATE ConstraintCursor;

    DECLARE IndexCursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT DISTINCT i.name
    FROM sys.indexes i
    INNER JOIN sys.index_columns ic
        ON ic.object_id = i.object_id
       AND ic.index_id = i.index_id
    INNER JOIN sys.columns c
        ON c.object_id = ic.object_id
       AND c.column_id = ic.column_id
    WHERE i.object_id = OBJECT_ID(N'dbo.MT_Projects')
      AND i.is_primary_key = 0
      AND i.is_unique_constraint = 0
      AND c.name = N'Status';

    OPEN IndexCursor;

    DECLARE @IndexName sysname;
    FETCH NEXT FROM IndexCursor INTO @IndexName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Sql = N'DROP INDEX ' + QUOTENAME(@IndexName) + N' ON dbo.MT_Projects;';
        EXEC sys.sp_executesql @Sql;
        PRINT N'已移除 index: ' + @IndexName;

        FETCH NEXT FROM IndexCursor INTO @IndexName;
    END;

    CLOSE IndexCursor;
    DEALLOCATE IndexCursor;

    IF EXISTS
    (
        SELECT 1
        FROM sys.extended_properties ep
        WHERE ep.major_id = OBJECT_ID(N'dbo.MT_Projects')
          AND ep.minor_id =
          (
              SELECT column_id
              FROM sys.columns
              WHERE object_id = OBJECT_ID(N'dbo.MT_Projects')
                AND name = N'Status'
          )
          AND ep.name = N'MS_Description'
    )
    BEGIN
        EXEC sys.sp_dropextendedproperty
            @name = N'MS_Description',
            @level0type = N'SCHEMA', @level0name = N'dbo',
            @level1type = N'TABLE',  @level1name = N'MT_Projects',
            @level2type = N'COLUMN', @level2name = N'Status';

        PRINT N'已移除 Status 欄位的 extended property。';
    END;

    ALTER TABLE dbo.MT_Projects DROP COLUMN Status;

    COMMIT TRANSACTION;

    PRINT N'完成：dbo.MT_Projects.Status 已安全移除。';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    DECLARE @ErrorMessage nvarchar(4000) = ERROR_MESSAGE();
    DECLARE @ErrorNumber int = ERROR_NUMBER();
    DECLARE @ErrorLine int = ERROR_LINE();

    RAISERROR(
        N'migration 失敗。Error %d, Line %d: %s',
        16,
        1,
        @ErrorNumber,
        @ErrorLine,
        @ErrorMessage
    );
END CATCH;

