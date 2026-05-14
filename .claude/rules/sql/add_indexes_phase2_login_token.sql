-- =====================================================================
-- Plan_DB_PerfReview 第二波 #10 #11：索引建立
-- 建立日期：2026-05-15
-- 對應頁面：登入頁（AuthService.CountConsecutiveFailedAttemptsAsync）
--          忘記密碼信件連結（PasswordResetService Token 驗證）
-- =====================================================================
-- 腳本特性：冪等（重跑安全），同時內含健檢資訊輸出。
-- 在 SSMS 直接 F5 執行，看訊息確認結果。
-- =====================================================================

SET NOCOUNT ON;
PRINT '======================================';
PRINT ' Plan_DB_PerfReview 第二波 #10 #11';
PRINT ' MT_LoginLogs + MT_PasswordResetTokens';
PRINT '======================================';
PRINT '';

-- ─────────────────────────────────────────────────────────────────────
-- Part 1：健檢 — 看現況
-- ─────────────────────────────────────────────────────────────────────

PRINT '=== [健檢] MT_LoginLogs 現有索引 ===';
SELECT i.name AS IndexName,
       i.type_desc AS IndexType,
       i.is_unique AS IsUnique,
       STUFF((SELECT ', ' + c.name
              FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
              ORDER BY ic.key_ordinal
              FOR XML PATH('')), 1, 2, '') AS KeyColumns
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID('dbo.MT_LoginLogs')
  AND i.type > 0
ORDER BY i.index_id;

PRINT '';
PRINT '=== [健檢] MT_PasswordResetTokens 現有索引 ===';
SELECT i.name AS IndexName,
       i.type_desc AS IndexType,
       i.is_unique AS IsUnique,
       STUFF((SELECT ', ' + c.name
              FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
              ORDER BY ic.key_ordinal
              FOR XML PATH('')), 1, 2, '') AS KeyColumns
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID('dbo.MT_PasswordResetTokens')
  AND i.type > 0
ORDER BY i.index_id;

PRINT '';
PRINT '=== [健檢] 資料量與 Token 實際長度 ===';
SELECT
    (SELECT COUNT(*) FROM dbo.MT_LoginLogs)                AS LoginLogs_RowCount,
    (SELECT COUNT(*) FROM dbo.MT_LoginLogs WHERE IsSuccess = 0) AS LoginLogs_FailedCount,
    (SELECT COUNT(*) FROM dbo.MT_PasswordResetTokens)      AS Tokens_RowCount,
    (SELECT ISNULL(MAX(LEN(Token)), 0) FROM dbo.MT_PasswordResetTokens) AS Tokens_MaxLen,
    (SELECT ISNULL(MIN(LEN(Token)), 0) FROM dbo.MT_PasswordResetTokens) AS Tokens_MinLen;
PRINT '';

-- ─────────────────────────────────────────────────────────────────────
-- Part 2：#10 — MT_LoginLogs 失敗計數複合索引
-- ─────────────────────────────────────────────────────────────────────
-- 服務 query：AuthService.CountConsecutiveFailedAttemptsAsync
--   外層：WHERE UserId = @U AND IsSuccess = 0 AND FailReason = @F AND CreatedAt >= ...
--   內層：SELECT MAX(CreatedAt) WHERE UserId = @U AND IsSuccess = 1
-- 設計：(UserId, IsSuccess, CreatedAt) 複合索引同時涵蓋兩段
--       前綴 (UserId, IsSuccess) 用於 equality seek，CreatedAt 用於 range scan + MAX
-- 不 INCLUDE FailReason（NVARCHAR(200) 太胖；對小資料集 lookup 成本可忽略）
-- ─────────────────────────────────────────────────────────────────────

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_MT_LoginLogs_UserId_IsSuccess_CreatedAt'
      AND object_id = OBJECT_ID('dbo.MT_LoginLogs')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_MT_LoginLogs_UserId_IsSuccess_CreatedAt
        ON dbo.MT_LoginLogs (UserId, IsSuccess, CreatedAt);
    PRINT '[#10] ✅ IX_MT_LoginLogs_UserId_IsSuccess_CreatedAt 建立完成';
END
ELSE
    PRINT '[#10] ⏭️  IX_MT_LoginLogs_UserId_IsSuccess_CreatedAt 已存在，跳過';

-- ─────────────────────────────────────────────────────────────────────
-- Part 3：#11 — MT_PasswordResetTokens.Token 索引
-- ─────────────────────────────────────────────────────────────────────
-- 服務 query：PasswordResetService 兩處 WHERE t.Token = @Token
-- schema 上已宣告 Token NVARCHAR(500) NOT NULL UNIQUE，SQL Server 通常自動建
-- UNIQUE INDEX（名稱類似 UQ__MT_Pass...）。實際 token 為 32 char GUID（64 bytes），
-- 遠低於 900 byte key 限制，UNIQUE INDEX 應可正常 seek。
--
-- 此處用「以 Token 為首位 key column」存在性判斷：
--   若已有任一索引以 Token 為首位 key（含 UNIQUE constraint 自動建立的）→ 跳過
--   若沒有 → 建一個 nonclustered（不指定 UNIQUE，避免欄位型別過長導致建立失敗）
-- ─────────────────────────────────────────────────────────────────────

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes i
    INNER JOIN sys.index_columns ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    INNER JOIN sys.columns c
            ON c.object_id = i.object_id AND c.column_id = ic.column_id
    WHERE i.object_id = OBJECT_ID('dbo.MT_PasswordResetTokens')
      AND c.name = 'Token'
      AND ic.is_included_column = 0
      AND ic.key_ordinal = 1
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_MT_PasswordResetTokens_Token
        ON dbo.MT_PasswordResetTokens (Token);
    PRINT '[#11] ✅ IX_MT_PasswordResetTokens_Token 建立完成';
END
ELSE
    PRINT '[#11] ⏭️  Token 首位 key 索引已存在（UNIQUE 約束自動建立），無需重建';

PRINT '';
PRINT '======================================';
PRINT ' 完成。請看上方 [#10] / [#11] 狀態行';
PRINT '======================================';
