-- ============================================================
-- Migration: MT_MemberQuotas 補加 Granularity
-- 日期：2026-05-21
-- 對應計畫：CWT/LCT 雙模式 v1（補刀）
--
-- 變更：
--   MT_MemberQuotas 新增 Granularity TINYINT NOT NULL DEFAULT 0
--
-- 語意：
--   與 MT_ProjectTargets.Granularity 同源（0=母題或單題、1=子題）
--   CWT 模式：閱讀題組/短文題組的配額需區分母題/子題
--   LCT 模式：一律為 0（教師每位的「難度一/二/三/四/五」配額）
--
-- 修補理由：
--   先前 migrate_project_type_and_granularity.sql 只加 MT_ProjectTargets，遺漏 MT_MemberQuotas。
--   ProjectService.cs 的 ReplaceProjectChildRecordsAsync 寫入 MT_MemberQuotas 時帶 @Granularity，
--   實際 DB 欄位不存在 → 新增專案會炸「無效的資料行名稱 'Granularity'」。
--
-- 部署狀態：已執行（2026-05-21）
-- 冪等：是（IF NOT EXISTS 包住 ALTER + IF EXISTS 判斷 update / add 描述）
-- ============================================================

-- 冪等：欄位不存在才加
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.MT_MemberQuotas')
      AND name = N'Granularity'
)
BEGIN
    ALTER TABLE dbo.MT_MemberQuotas ADD Granularity TINYINT NOT NULL DEFAULT 0;
END
GO

-- 冪等：描述已存在就 update，否則 add
IF EXISTS (
    SELECT 1 FROM sys.extended_properties ep
    JOIN sys.columns c
        ON c.object_id = ep.major_id AND c.column_id = ep.minor_id
    WHERE ep.major_id = OBJECT_ID(N'dbo.MT_MemberQuotas')
      AND c.name = N'Granularity'
      AND ep.name = N'MS_Description'
)
    EXEC sys.sp_updateextendedproperty
        @name = N'MS_Description',
        @value = N'題目顆粒度：0=母題或單題、1=子題（與 MT_ProjectTargets.Granularity 同源）',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_MemberQuotas',
        @level2type = N'COLUMN', @level2name = N'Granularity';
ELSE
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description',
        @value = N'題目顆粒度：0=母題或單題、1=子題（與 MT_ProjectTargets.Granularity 同源）',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'MT_MemberQuotas',
        @level2type = N'COLUMN', @level2name = N'Granularity';
GO

-- ============================================================
-- 驗證
-- ============================================================
SELECT t.name AS TableName, c.name AS ColumnName, ep.value AS Description
FROM sys.extended_properties ep
JOIN sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id
JOIN sys.tables  t ON t.object_id = ep.major_id
WHERE t.name = 'MT_MemberQuotas' AND c.name = 'Granularity';
