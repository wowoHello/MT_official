-- ============================================================
-- MT_Users.PasswordHash 從 binary(32) 遷移到 nvarchar(150)
-- ============================================================
-- 背景：原本用裸 SHA256 雜湊（32 bytes），改為 PBKDF2-SHA256 含 salt + iteration
-- 新格式：PBKDF2.v1$<iter>$<salt-base64>$<hash-base64>
-- 舊格式（遷移後）：純 Base64 編碼的 32 byte SHA256（44 字元）
--
-- 程式碼端：偵測 "PBKDF2." 前綴決定走哪條驗證路徑
-- 使用者下次登入成功後，程式會自動把舊 hash 重 hash 為 PBKDF2 寫回 DB
--
-- ⚠️ 執行前請先備份 MT_Users 表
-- ⚠️ 此遷移無法回滾，建議在維護視窗執行
-- ============================================================

USE [MT];
GO

-- Step 1: 新增暫存欄位
ALTER TABLE dbo.MT_Users
    ADD PasswordHashNew nvarchar(150) NULL;
GO

-- Step 2: 將既有 binary(32) 轉成 Base64 字串
-- SQL Server 沒有原生 binary-to-base64，使用 XML xs:base64Binary 轉換
UPDATE dbo.MT_Users
SET PasswordHashNew = CAST(
    CAST('' AS xml).value(
        'xs:base64Binary(sql:column("PasswordHash"))',
        'varchar(150)'
    ) AS nvarchar(150))
WHERE PasswordHash IS NOT NULL;
GO

-- Step 3: 驗證每筆都成功轉換（應為 0）
DECLARE @unmigrated int;
SELECT @unmigrated = COUNT(*)
FROM dbo.MT_Users
WHERE PasswordHash IS NOT NULL AND PasswordHashNew IS NULL;

IF @unmigrated > 0
BEGIN
    RAISERROR(N'有 %d 筆 PasswordHash 未成功轉換，請檢查資料後再繼續', 16, 1, @unmigrated);
    RETURN;
END;
GO

-- Step 4: 移除舊欄位、改名新欄位
ALTER TABLE dbo.MT_Users DROP COLUMN PasswordHash;
GO

EXEC sp_rename N'dbo.MT_Users.PasswordHashNew', N'PasswordHash', N'COLUMN';
GO

-- Step 5: 設為 NOT NULL（與原本 binary(32) NOT NULL 一致）
ALTER TABLE dbo.MT_Users
    ALTER COLUMN PasswordHash nvarchar(150) NOT NULL;
GO

-- Step 6: 驗收
SELECT TOP 5 Id, Username, LEN(PasswordHash) AS HashLen, LEFT(PasswordHash, 50) AS HashPreview
FROM dbo.MT_Users
ORDER BY Id;
-- 預期：所有 HashLen = 44（Base64 of 32 bytes，含 = padding），開頭非 "PBKDF2."
-- 程式碼上線後，使用者登入成功會自動重 hash 為 PBKDF2 格式
