-- =====================================================================
-- Plan_DB_PerfReview 第二波 #9 對拍腳本
-- 驗證 ListAsync 改 CTE LEFT JOIN 後結果與現行 inline EXISTS 完全一致
-- 建立日期：2026-05-15
-- =====================================================================
-- 用法：
--   1. 把下方 @ProjectId 改成你環境內有修題狀態（Status IN 4/6/8）題目的梯次 Id
--   2. SSMS F5 跑全份
--   3. 看「訊息」分頁：4 段都顯示 ✅ 即可動工改 C#
--   4. 任一段 ❌ 表示 CTE 邏輯有差，回報細節給我
-- =====================================================================

SET NOCOUNT ON;

DECLARE @ProjectId INT = 1;       -- ★ 改成你的測試梯次 Id
DECLARE @CreatorId INT = NULL;    -- 不限 creator 留 NULL（建議第一輪先用）
DECLARE @HasReplied BIT = 1;      -- 第二段 WHERE 過濾測試用

PRINT '======================================';
PRINT ' #9 ListAsync CTE 重寫對拍';
PRINT ' ProjectId = ' + CAST(@ProjectId AS NVARCHAR);
PRINT '======================================';
PRINT '';

-- ─────────────────────────────────────────────────────────────────────
-- 對拍 1：母題 HasRepliedThisStage（SELECT 欄位）
-- ─────────────────────────────────────────────────────────────────────
PRINT '=== 對拍 1：母題 HasRepliedThisStage（SELECT 欄位）===';

-- 舊寫法：per-row inline EXISTS
WITH OldMaster AS (
    SELECT
        q.Id,
        CAST(CASE WHEN EXISTS (
            SELECT 1 FROM dbo.MT_RevisionReplies rr
            WHERE rr.QuestionId = q.Id
              AND rr.SubQuestionId IS NULL
              AND rr.UserId     = q.CreatorId
              AND rr.Stage      = q.Status
              AND q.Status IN (4, 6, 8)
              AND rr.CreatedAt > ISNULL(
                  (SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt WHERE QuestionId = q.Id),
                  '1900-01-01')
        ) THEN 1 ELSE 0 END AS BIT) AS HasReplied_Old,
        (SELECT COUNT(*) FROM dbo.MT_SubQuestions sq WHERE sq.ParentQuestionId = q.Id) AS SubCount_Old
    FROM dbo.MT_Questions q
    WHERE q.ProjectId = @ProjectId
      AND q.IsDeleted = 0
      AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
),
-- 新寫法：CTE 預聚合 + LEFT JOIN
MasterReplied AS (
    SELECT q.Id AS QId, 1 AS HasReplied
    FROM dbo.MT_Questions q
    INNER JOIN dbo.MT_RevisionReplies rr
        ON  rr.QuestionId    = q.Id
        AND rr.SubQuestionId IS NULL
        AND rr.UserId        = q.CreatorId
        AND rr.Stage         = q.Status
    LEFT JOIN dbo.vw_QuestionRoundStartedAt rs ON rs.QuestionId = q.Id
    WHERE q.ProjectId = @ProjectId
      AND q.Status IN (4, 6, 8)
      AND rr.CreatedAt > ISNULL(rs.RoundStartedAt, '1900-01-01')
    GROUP BY q.Id
),
SubCounts AS (
    SELECT sq.ParentQuestionId AS QId, COUNT(*) AS Cnt
    FROM dbo.MT_SubQuestions sq
    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
    WHERE q.ProjectId = @ProjectId
    GROUP BY sq.ParentQuestionId
),
NewMaster AS (
    SELECT
        q.Id,
        CAST(ISNULL(mr.HasReplied, 0) AS BIT) AS HasReplied_New,
        ISNULL(sc.Cnt, 0) AS SubCount_New
    FROM dbo.MT_Questions q
    LEFT JOIN MasterReplied mr ON mr.QId = q.Id
    LEFT JOIN SubCounts     sc ON sc.QId = q.Id
    WHERE q.ProjectId = @ProjectId
      AND q.IsDeleted = 0
      AND (@CreatorId IS NULL OR q.CreatorId = @CreatorId)
),
-- 比對：找出兩邊不一致的列
Diff1 AS (
    SELECT o.Id,
           o.HasReplied_Old, n.HasReplied_New,
           o.SubCount_Old,   n.SubCount_New
    FROM OldMaster o
    INNER JOIN NewMaster n ON n.Id = o.Id
    WHERE o.HasReplied_Old <> n.HasReplied_New
       OR o.SubCount_Old   <> n.SubCount_New
)
SELECT
    CASE WHEN COUNT(*) = 0
         THEN '✅ 對拍 1 PASS：母題 HasRepliedThisStage + SubQuestionCount 完全一致'
         ELSE '❌ 對拍 1 FAIL：發現 ' + CAST(COUNT(*) AS NVARCHAR) + ' 列不一致（見下方 Diff）'
    END AS Result
FROM Diff1;

-- 若有差異，把不一致的列印出來方便除錯
WITH OldMaster AS (
    SELECT
        q.Id,
        CAST(CASE WHEN EXISTS (
            SELECT 1 FROM dbo.MT_RevisionReplies rr
            WHERE rr.QuestionId = q.Id
              AND rr.SubQuestionId IS NULL
              AND rr.UserId     = q.CreatorId
              AND rr.Stage      = q.Status
              AND q.Status IN (4, 6, 8)
              AND rr.CreatedAt > ISNULL(
                  (SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt WHERE QuestionId = q.Id),
                  '1900-01-01')
        ) THEN 1 ELSE 0 END AS BIT) AS HasReplied_Old,
        (SELECT COUNT(*) FROM dbo.MT_SubQuestions sq WHERE sq.ParentQuestionId = q.Id) AS SubCount_Old
    FROM dbo.MT_Questions q
    WHERE q.ProjectId = @ProjectId AND q.IsDeleted = 0
),
MasterReplied AS (
    SELECT q.Id AS QId, 1 AS HasReplied
    FROM dbo.MT_Questions q
    INNER JOIN dbo.MT_RevisionReplies rr
        ON  rr.QuestionId    = q.Id
        AND rr.SubQuestionId IS NULL
        AND rr.UserId        = q.CreatorId
        AND rr.Stage         = q.Status
    LEFT JOIN dbo.vw_QuestionRoundStartedAt rs ON rs.QuestionId = q.Id
    WHERE q.ProjectId = @ProjectId
      AND q.Status IN (4, 6, 8)
      AND rr.CreatedAt > ISNULL(rs.RoundStartedAt, '1900-01-01')
    GROUP BY q.Id
),
SubCounts AS (
    SELECT sq.ParentQuestionId AS QId, COUNT(*) AS Cnt
    FROM dbo.MT_SubQuestions sq
    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
    WHERE q.ProjectId = @ProjectId
    GROUP BY sq.ParentQuestionId
),
NewMaster AS (
    SELECT
        q.Id,
        CAST(ISNULL(mr.HasReplied, 0) AS BIT) AS HasReplied_New,
        ISNULL(sc.Cnt, 0) AS SubCount_New
    FROM dbo.MT_Questions q
    LEFT JOIN MasterReplied mr ON mr.QId = q.Id
    LEFT JOIN SubCounts     sc ON sc.QId = q.Id
    WHERE q.ProjectId = @ProjectId AND q.IsDeleted = 0
)
SELECT TOP 10 o.Id, o.HasReplied_Old, n.HasReplied_New, o.SubCount_Old, n.SubCount_New
FROM OldMaster o
INNER JOIN NewMaster n ON n.Id = o.Id
WHERE o.HasReplied_Old <> n.HasReplied_New
   OR o.SubCount_Old   <> n.SubCount_New
ORDER BY o.Id;

-- ─────────────────────────────────────────────────────────────────────
-- 對拍 2：子題 HasRepliedThisStage
-- ─────────────────────────────────────────────────────────────────────
PRINT '';
PRINT '=== 對拍 2：子題 HasRepliedThisStage ===';

WITH OldSub AS (
    SELECT
        sq.Id AS SubId,
        q.Id  AS QId,
        CAST(CASE WHEN EXISTS (
            SELECT 1 FROM dbo.MT_RevisionReplies rr
            WHERE rr.QuestionId    = q.Id
              AND rr.SubQuestionId = sq.Id
              AND rr.UserId        = q.CreatorId
              AND rr.Stage         = sq.Status
              AND sq.Status IN (4, 6, 8)
              AND rr.CreatedAt > ISNULL(
                  (SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt WHERE QuestionId = q.Id),
                  '1900-01-01')
        ) THEN 1 ELSE 0 END AS BIT) AS HasReplied_Old
    FROM dbo.MT_SubQuestions sq
    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
    WHERE q.ProjectId = @ProjectId AND q.IsDeleted = 0
),
SubReplied AS (
    SELECT sq.Id AS SubId, 1 AS HasReplied
    FROM dbo.MT_SubQuestions sq
    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
    INNER JOIN dbo.MT_RevisionReplies rr
        ON  rr.QuestionId    = q.Id
        AND rr.SubQuestionId = sq.Id
        AND rr.UserId        = q.CreatorId
        AND rr.Stage         = sq.Status
    LEFT JOIN dbo.vw_QuestionRoundStartedAt rs ON rs.QuestionId = q.Id
    WHERE q.ProjectId = @ProjectId
      AND sq.Status IN (4, 6, 8)
      AND rr.CreatedAt > ISNULL(rs.RoundStartedAt, '1900-01-01')
    GROUP BY sq.Id
),
NewSub AS (
    SELECT sq.Id AS SubId,
           CAST(ISNULL(sr.HasReplied, 0) AS BIT) AS HasReplied_New
    FROM dbo.MT_SubQuestions sq
    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
    LEFT JOIN SubReplied sr ON sr.SubId = sq.Id
    WHERE q.ProjectId = @ProjectId AND q.IsDeleted = 0
)
SELECT
    CASE WHEN COUNT(*) = 0
         THEN '✅ 對拍 2 PASS：子題 HasRepliedThisStage 完全一致'
         ELSE '❌ 對拍 2 FAIL：發現 ' + CAST(COUNT(*) AS NVARCHAR) + ' 列不一致'
    END AS Result
FROM OldSub o
INNER JOIN NewSub n ON n.SubId = o.SubId
WHERE o.HasReplied_Old <> n.HasReplied_New;

-- ─────────────────────────────────────────────────────────────────────
-- 對拍 3：母題 WHERE filter（@HasReplied = 1，只取「本輪已回覆」的題目）
-- ─────────────────────────────────────────────────────────────────────
PRINT '';
PRINT '=== 對拍 3：母題 WHERE filter（@HasReplied = 1） ===';

-- 舊寫法：MakeRepliedClause 內聯 EXISTS
WITH OldFiltered AS (
    SELECT q.Id
    FROM dbo.MT_Questions q
    WHERE q.ProjectId = @ProjectId
      AND q.IsDeleted = 0
      AND (@HasReplied IS NULL OR
           (q.Status IN (4, 6, 8) AND
            @HasReplied = CAST(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.MT_RevisionReplies rr
                WHERE rr.QuestionId = q.Id
                  AND rr.SubQuestionId IS NULL
                  AND rr.UserId     = q.CreatorId
                  AND rr.Stage      = q.Status
                  AND rr.CreatedAt > ISNULL(
                      (SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt WHERE QuestionId = q.Id),
                      '1900-01-01')
            ) THEN 1 ELSE 0 END AS BIT)))
),
-- 新寫法：用 CTE LEFT JOIN 結果做 WHERE 過濾
MasterReplied AS (
    SELECT q.Id AS QId, 1 AS HasReplied
    FROM dbo.MT_Questions q
    INNER JOIN dbo.MT_RevisionReplies rr
        ON  rr.QuestionId    = q.Id
        AND rr.SubQuestionId IS NULL
        AND rr.UserId        = q.CreatorId
        AND rr.Stage         = q.Status
    LEFT JOIN dbo.vw_QuestionRoundStartedAt rs ON rs.QuestionId = q.Id
    WHERE q.ProjectId = @ProjectId
      AND q.Status IN (4, 6, 8)
      AND rr.CreatedAt > ISNULL(rs.RoundStartedAt, '1900-01-01')
    GROUP BY q.Id
),
NewFiltered AS (
    SELECT q.Id
    FROM dbo.MT_Questions q
    LEFT JOIN MasterReplied mr ON mr.QId = q.Id
    WHERE q.ProjectId = @ProjectId
      AND q.IsDeleted = 0
      AND (@HasReplied IS NULL OR
           (q.Status IN (4, 6, 8) AND @HasReplied = CAST(ISNULL(mr.HasReplied, 0) AS BIT)))
)
SELECT
    CASE WHEN (SELECT COUNT(*) FROM (
                 SELECT Id FROM OldFiltered EXCEPT SELECT Id FROM NewFiltered
             ) X) = 0
          AND (SELECT COUNT(*) FROM (
                 SELECT Id FROM NewFiltered EXCEPT SELECT Id FROM OldFiltered
             ) Y) = 0
         THEN '✅ 對拍 3 PASS：母題 WHERE filter 取出列數完全一致'
         ELSE '❌ 對拍 3 FAIL：兩邊取出列數不同'
    END AS Result;

-- ─────────────────────────────────────────────────────────────────────
-- 對拍 4：子題 WHERE filter（@HasReplied = 1）
-- ─────────────────────────────────────────────────────────────────────
PRINT '';
PRINT '=== 對拍 4：子題 WHERE filter（@HasReplied = 1） ===';

WITH OldSubFiltered AS (
    SELECT sq.Id AS SubId
    FROM dbo.MT_SubQuestions sq
    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
    WHERE q.ProjectId = @ProjectId
      AND q.IsDeleted = 0
      AND (@HasReplied IS NULL OR
           (sq.Status IN (4, 6, 8) AND
            @HasReplied = CAST(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.MT_RevisionReplies rr
                WHERE rr.QuestionId = q.Id
                  AND ISNULL(rr.SubQuestionId, -1) = ISNULL(sq.Id, -1)
                  AND rr.UserId     = q.CreatorId
                  AND rr.Stage      = sq.Status
                  AND rr.CreatedAt > ISNULL(
                      (SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt WHERE QuestionId = q.Id),
                      '1900-01-01')
            ) THEN 1 ELSE 0 END AS BIT)))
),
SubReplied AS (
    SELECT sq.Id AS SubId, 1 AS HasReplied
    FROM dbo.MT_SubQuestions sq
    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
    INNER JOIN dbo.MT_RevisionReplies rr
        ON  rr.QuestionId    = q.Id
        AND rr.SubQuestionId = sq.Id
        AND rr.UserId        = q.CreatorId
        AND rr.Stage         = sq.Status
    LEFT JOIN dbo.vw_QuestionRoundStartedAt rs ON rs.QuestionId = q.Id
    WHERE q.ProjectId = @ProjectId
      AND sq.Status IN (4, 6, 8)
      AND rr.CreatedAt > ISNULL(rs.RoundStartedAt, '1900-01-01')
    GROUP BY sq.Id
),
NewSubFiltered AS (
    SELECT sq.Id AS SubId
    FROM dbo.MT_SubQuestions sq
    INNER JOIN dbo.MT_Questions q ON q.Id = sq.ParentQuestionId
    LEFT JOIN SubReplied sr ON sr.SubId = sq.Id
    WHERE q.ProjectId = @ProjectId
      AND q.IsDeleted = 0
      AND (@HasReplied IS NULL OR
           (sq.Status IN (4, 6, 8) AND @HasReplied = CAST(ISNULL(sr.HasReplied, 0) AS BIT)))
)
SELECT
    CASE WHEN (SELECT COUNT(*) FROM (
                 SELECT SubId FROM OldSubFiltered EXCEPT SELECT SubId FROM NewSubFiltered
             ) X) = 0
          AND (SELECT COUNT(*) FROM (
                 SELECT SubId FROM NewSubFiltered EXCEPT SELECT SubId FROM OldSubFiltered
             ) Y) = 0
         THEN '✅ 對拍 4 PASS：子題 WHERE filter 取出列數完全一致'
         ELSE '❌ 對拍 4 FAIL：兩邊取出列數不同'
    END AS Result;

PRINT '';
PRINT '======================================';
PRINT ' 4 段對拍完成。看 Results 欄四個 ✅ 即可動工';
PRINT '======================================';
