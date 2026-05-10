---
name: 稽核強化已實作（AuditLogJsonHelper 已追蹤）
description: RoleService.cs 使用 AuditLogJsonHelper.cs 進行稽核序列化，兩個檔案均已存在於專案中
type: project
---

`Services/AuditLogJsonHelper.cs` 已確認存在（2026-05-08 驗證）。RoleService.cs 的所有 CRUD 操作（CreateAccount、UpdateAccount、ToggleStatus、ResetPassword、CreateRole、UpdateRole、DeleteRole）均已整合 `AuditLogJsonHelper.Serialize()` 寫入 before/after 快照至 `MT_AuditLogs`。

**Why:** 稽核品質強化需要 OldValue 對照，原本只有 NewValue；AuditLogJsonHelper 統一了 camelCase + 中文不 escape 的序列化格式。

**How to apply:**
- 修改 RoleService.cs 的稽核相關程式碼時，需同步確認 AuditLogJsonHelper.cs 中的 Serialize 方法簽名沒有破壞性變更
- 若未來需擴充稽核欄位，確認 `MT_AuditLogs` 資料表有 `ProjectId INT NULL` 與 `OldValue NVARCHAR(MAX) NULL` 欄位
