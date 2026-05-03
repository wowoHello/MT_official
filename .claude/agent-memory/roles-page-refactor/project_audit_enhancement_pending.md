---
name: 稽核強化未提交狀態
description: RoleService.cs 有未提交的稽核品質強化修改，依賴未追蹤的 AuditLogJsonHelper.cs，兩者必須一起提交，且提交前需確認 DB 欄位
type: project
---

RoleService.cs 有大量未提交的稽核強化改動，同時新增了 Services/AuditLogJsonHelper.cs（untracked）。

**Why:** 原本稽核寫入只有 3 欄且用 JsonSerializer 直接序列化，缺少 OldValue 對照；此次強化補齊了所有 CRUD 操作的 before/after 快照，並統一序列化格式（camelCase + 中文不 escape）。

**How to apply:**
- 提交前必須確認 `MT_AuditLogs` 資料表有 `ProjectId INT NULL` 與 `OldValue NVARCHAR(MAX) NULL` 欄位
- `git add Services/AuditLogJsonHelper.cs Services/RoleService.cs` 必須一起提交
- 若先提交 RoleService.cs 而遺漏 AuditLogJsonHelper.cs，其他人 clone 後 `dotnet build` 會失敗
