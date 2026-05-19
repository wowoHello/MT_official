-- ============================================================
-- migrate_similarity_checks_v2.sql
-- 為 MT_SimilarityChecks 補上題組子題比對 / 演算法版本 / UNIQUE 索引
-- 建立日期：2026-05-19
-- 對應功能：相似度分析 (Tier 1 寫入層 + Tier 2 讀取層 + Tier 3 批次層)
-- 冪等可重跑（每段都用 IF NOT EXISTS 包住）
-- ============================================================

USE [MT];
GO

PRINT N'== MT_SimilarityChecks Schema Migration v2 開始 ==';
GO

-- ============================================================
-- ① 新增 SourceSubQuestionId (NULL 表示母題層級比對)
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'SourceSubQuestionId'
      AND Object_ID = Object_ID(N'dbo.MT_SimilarityChecks')
)
BEGIN
    ALTER TABLE dbo.MT_SimilarityChecks
    ADD SourceSubQuestionId INT NULL;
    PRINT N'[1] OK 新增欄位 SourceSubQuestionId';
END
ELSE
    PRINT N'[1] SKIP SourceSubQuestionId 已存在';
GO

-- ============================================================
-- ② 新增 ComparedSubQuestionId (NULL 表示母題層級比對)
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'ComparedSubQuestionId'
      AND Object_ID = Object_ID(N'dbo.MT_SimilarityChecks')
)
BEGIN
    ALTER TABLE dbo.MT_SimilarityChecks
    ADD ComparedSubQuestionId INT NULL;
    PRINT N'[2] OK 新增欄位 ComparedSubQuestionId';
END
ELSE
    PRINT N'[2] SKIP ComparedSubQuestionId 已存在';
GO

-- ============================================================
-- ③ 新增 AlgorithmVersion (預設 1 = 4-gram Jaccard v1，未來升級遞增)
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'AlgorithmVersion'
      AND Object_ID = Object_ID(N'dbo.MT_SimilarityChecks')
)
BEGIN
    ALTER TABLE dbo.MT_SimilarityChecks
    ADD AlgorithmVersion TINYINT NOT NULL
        CONSTRAINT DF_MT_SimilarityChecks_AlgVer DEFAULT (1);
    PRINT N'[3] OK 新增欄位 AlgorithmVersion 預設 1';
END
ELSE
    PRINT N'[3] SKIP AlgorithmVersion 已存在';
GO

-- ============================================================
-- ④ FK: SourceSubQuestionId → MT_SubQuestions(Id)
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_MT_SimilarityChecks_SourceSubQuestion'
)
BEGIN
    ALTER TABLE dbo.MT_SimilarityChecks
    ADD CONSTRAINT FK_MT_SimilarityChecks_SourceSubQuestion
    FOREIGN KEY (SourceSubQuestionId) REFERENCES dbo.MT_SubQuestions(Id);
    PRINT N'[4] OK FK SourceSubQuestionId -> MT_SubQuestions';
END
ELSE
    PRINT N'[4] SKIP FK SourceSubQuestion 已存在';
GO

-- ============================================================
-- ⑤ FK: ComparedSubQuestionId → MT_SubQuestions(Id)
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_MT_SimilarityChecks_ComparedSubQuestion'
)
BEGIN
    ALTER TABLE dbo.MT_SimilarityChecks
    ADD CONSTRAINT FK_MT_SimilarityChecks_ComparedSubQuestion
    FOREIGN KEY (ComparedSubQuestionId) REFERENCES dbo.MT_SubQuestions(Id);
    PRINT N'[5] OK FK ComparedSubQuestionId -> MT_SubQuestions';
END
ELSE
    PRINT N'[5] SKIP FK ComparedSubQuestion 已存在';
GO

-- ============================================================
-- ⑥ CHECK: 不自比 (來源與比對目標的母題 Id 必須不同)
--   注：同一母題下兩個子題互比沒業務意義 (同篇文章必然高相似)，一律禁止
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_MT_SimilarityChecks_NotSelfCompare'
)
BEGIN
    ALTER TABLE dbo.MT_SimilarityChecks
    ADD CONSTRAINT CK_MT_SimilarityChecks_NotSelfCompare
    CHECK (SourceQuestionId <> ComparedQuestionId);
    PRINT N'[6] OK CHECK 不自比 (SourceQuestionId != ComparedQuestionId)';
END
ELSE
    PRINT N'[6] SKIP CHECK NotSelfCompare 已存在';
GO

-- ============================================================
-- ⑦ CHECK: 子題對稱 (避免母題 vs 子題的混合比對)
--   合法情境：
--     - 母題 vs 母題：兩個 SubQuestionId 都 NULL
--     - 子題 vs 子題：兩個 SubQuestionId 都 NOT NULL
--   非法情境：
--     - 母題 vs 子題：一個 NULL 一個 NOT NULL → 拒絕
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_MT_SimilarityChecks_SubPairSymmetric'
)
BEGIN
    ALTER TABLE dbo.MT_SimilarityChecks
    ADD CONSTRAINT CK_MT_SimilarityChecks_SubPairSymmetric
    CHECK (
        (SourceSubQuestionId IS NULL AND ComparedSubQuestionId IS NULL)
        OR (SourceSubQuestionId IS NOT NULL AND ComparedSubQuestionId IS NOT NULL)
    );
    PRINT N'[7] OK CHECK 子題對稱 (避免母 vs 子混比)';
END
ELSE
    PRINT N'[7] SKIP CHECK SubPairSymmetric 已存在';
GO

-- ============================================================
-- ⑧ UNIQUE 索引 (防止同對題目重複寫入)
--   注：SQL Server 的 UNIQUE 索引把 NULL 視為相同值，所以
--   (10, 20, NULL, NULL, 1) 同 (10, 20, NULL, NULL, 1) → 第二筆會被拒絕
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UQ_MT_SimilarityChecks_Pair'
      AND object_id = OBJECT_ID(N'dbo.MT_SimilarityChecks')
)
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UQ_MT_SimilarityChecks_Pair
    ON dbo.MT_SimilarityChecks (
        SourceQuestionId,
        ComparedQuestionId,
        SourceSubQuestionId,
        ComparedSubQuestionId,
        AlgorithmVersion
    );
    PRINT N'[8] OK UNIQUE 索引 UQ_MT_SimilarityChecks_Pair';
END
ELSE
    PRINT N'[8] SKIP UNIQUE 索引已存在';
GO

-- ============================================================
-- ⑨ Extended Properties (統一改寫 5 個欄位描述，明確區分母題/子題)
--   - 原本 SourceQuestionId / ComparedQuestionId 寫「題目」太籠統，
--     跟 SourceSubQuestionId / ComparedSubQuestionId 並列時容易誤判重複
--   - 改成「母題 ID」/「子題 ID」明確區分
--   - 用 drop+add 模式，再執行時可覆寫舊描述（idempotent）
-- ============================================================

-- Helper Pattern: 先 drop 舊描述（若存在），再 add 新描述
-- (1) SourceQuestionId
IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.MT_SimilarityChecks')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MT_SimilarityChecks'), N'SourceQuestionId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_dropextendedproperty
        @name = N'MS_Description',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
        @level2type = N'COLUMN', @level2name = N'SourceQuestionId';
EXEC sys.sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'來源母題 ID (不論母題/子題層級比對，都需指定所屬母題)',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
    @level2type = N'COLUMN', @level2name = N'SourceQuestionId';

-- (2) ComparedQuestionId
IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.MT_SimilarityChecks')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MT_SimilarityChecks'), N'ComparedQuestionId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_dropextendedproperty
        @name = N'MS_Description',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
        @level2type = N'COLUMN', @level2name = N'ComparedQuestionId';
EXEC sys.sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'比對目標母題 ID (不論母題/子題層級比對，都需指定所屬母題)',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
    @level2type = N'COLUMN', @level2name = N'ComparedQuestionId';

-- (3) SourceSubQuestionId
IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.MT_SimilarityChecks')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MT_SimilarityChecks'), N'SourceSubQuestionId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_dropextendedproperty
        @name = N'MS_Description',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
        @level2type = N'COLUMN', @level2name = N'SourceSubQuestionId';
EXEC sys.sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'來源子題 ID (NULL=母題層級比對，NOT NULL=子題層級比對；與 ComparedSubQuestionId 須同為 NULL 或同為 NOT NULL)',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
    @level2type = N'COLUMN', @level2name = N'SourceSubQuestionId';

-- (4) ComparedSubQuestionId
IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.MT_SimilarityChecks')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MT_SimilarityChecks'), N'ComparedSubQuestionId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_dropextendedproperty
        @name = N'MS_Description',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
        @level2type = N'COLUMN', @level2name = N'ComparedSubQuestionId';
EXEC sys.sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'比對目標子題 ID (NULL=母題層級比對，NOT NULL=子題層級比對；與 SourceSubQuestionId 須同為 NULL 或同為 NOT NULL)',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
    @level2type = N'COLUMN', @level2name = N'ComparedSubQuestionId';

-- (5) AlgorithmVersion
IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.MT_SimilarityChecks')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MT_SimilarityChecks'), N'AlgorithmVersion', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_dropextendedproperty
        @name = N'MS_Description',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
        @level2type = N'COLUMN', @level2name = N'AlgorithmVersion';
EXEC sys.sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'比對演算法版本 (1=4-gram Jaccard v1)，未來升級時遞增',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'MT_SimilarityChecks',
    @level2type = N'COLUMN', @level2name = N'AlgorithmVersion';

PRINT N'[9] OK Extended Properties 已統一改寫 (5 欄位區分母題/子題)';
GO

-- ============================================================
-- 驗收：列出當前 schema 與索引
-- ============================================================
PRINT N'';
PRINT N'== Verification: 當前欄位 ==';

SELECT
    c.column_id              AS Ord,
    c.name                   AS ColumnName,
    t.name                   AS DataType,
    c.max_length             AS MaxLength,
    c.is_nullable            AS Nullable,
    OBJECT_DEFINITION(c.default_object_id) AS DefaultDefinition
FROM sys.columns c
INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.MT_SimilarityChecks')
ORDER BY c.column_id;

PRINT N'';
PRINT N'== Verification: 索引 ==';
SELECT name, type_desc, is_unique
FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.MT_SimilarityChecks');

PRINT N'';
PRINT N'== Verification: CHECK / FK 約束 ==';
SELECT name, type_desc, OBJECT_DEFINITION(object_id) AS Definition
FROM sys.objects
WHERE parent_object_id = OBJECT_ID(N'dbo.MT_SimilarityChecks')
  AND type IN ('C', 'F', 'D')
ORDER BY type_desc, name;

PRINT N'';
PRINT N'== Migration v2 完成 ==';
GO
