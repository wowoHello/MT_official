---
name: Announcements 頁面未完成缺口
description: 2026-05-13 核對功能介紹文件後確認缺口狀態（P0 已修復，P1/P2 仍未解決）
type: project
---

頁面目前為「部分完成」狀態。最後一次 Code Review 紀錄：`D:\MTrefer\pageFinal_doc\Announcements.md`。

**Why:** 存在規格偏差（自動下架未持久化、手冊假資料、缺狀態下拉、InlineQuillEditor 標點列 UI）。

**How to apply:** 實作任務前先確認這 4 項缺口是否已解決：

| 優先 | 缺口 | 最後確認日 |
|:---:|------|-----------|
| ~~P0~~ | ~~`HasAnnouncementPermission()` 用 ModuleKey 比對，改為 PageUrl + OrdinalIgnoreCase~~ | **已修復**（程式碼已確認使用 PageUrl + OrdinalIgnoreCase） |
| P1 | 自動下架未 UPDATE Status=2，`DisplayStatus` 僅在前端由日期推導（DB 仍只有 0=草稿/1=發佈） | 2026-05-13 仍未解決 |
| P1 | 使用說明手冊顯示硬編碼假資料（3 筆 PDF 名稱），未接 MT_UserGuideFiles | 2026-05-13 仍未解決 |
| P2 | Slide-over 表單無「狀態下拉」欄位（無法手動設為已下架；已下架僅由日期決定） | 2026-05-13 仍未解決 |
| P2 | InlineQuillEditor.razor 缺少 12 個中文標點符號快速插入列 UI | 2026-05-13 仍未解決 |

### 權限比對設計（已修復，防止未來迴歸）
`HasAnnouncementPermission()` 已改為：
```csharp
ModuleCards?.Any(m =>
    string.Equals(m.PageUrl, "announcements", StringComparison.OrdinalIgnoreCase)
    && m.IsEnabled) == true;
```
使用 `PageUrl`（而非 `ModuleKey`），與 `MainLayout.EnforceModuleAccess` 保持一致。

### AutoUnpin 邏輯說明（頁面進入時自動執行）
`AutoUnpinExpiredAsync` 會在 `OnParametersSetAsync` 第一次通過權限後呼叫，
SQL 條件為：`IsPinned=1 AND (Status=0 OR (Status=1 AND SYSDATETIME() > UnpublishDate))`。
**注意**：此方法僅取消置頂（IsPinned→0），不會將 Status 改為 2（因為 DB 設計沒有 Status=2）。
