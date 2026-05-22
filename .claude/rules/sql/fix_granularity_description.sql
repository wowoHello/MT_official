-- ============================================================
-- 修正 Granularity 欄位描述（程式碼實作只用 0/1 兩個值，原描述寫成 0/1/2 三個值不準確）
-- 日期：2026-05-21
--
-- 對 MT_ProjectTargets.Granularity 用 sp_updateextendedproperty 更新已存在描述。
-- MT_MemberQuotas.Granularity 由 migrate_memberquotas_granularity.sql 寫入新描述，無需 update。
-- ============================================================

EXEC sys.sp_updateextendedproperty
    @name = N'MS_Description',
    @value = N'題目顆粒度：0=母題或單題、1=子題',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'MT_ProjectTargets',
    @level2type = N'COLUMN', @level2name = N'Granularity';
GO

-- ============================================================
-- 驗證
-- ============================================================
SELECT t.name AS TableName, c.name AS ColumnName, ep.value AS Description
FROM sys.extended_properties ep
JOIN sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id
JOIN sys.tables  t ON t.object_id = ep.major_id
WHERE t.name IN ('MT_ProjectTargets', 'MT_MemberQuotas')
  AND c.name = 'Granularity';
