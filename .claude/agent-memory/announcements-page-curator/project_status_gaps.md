---
name: Announcements 頁面未完成缺口
description: 2026-05-04 Re-review 確認 5 項缺口；P0 已於 2026-05-04 修復（PageUrl 比對問題）
type: project
---

頁面目前為「部分完成」狀態。評估報告放在 `D:\MTrefer\PageNotEnd_doc\Announcements.md`，Code Review 紀錄更新至 `D:\MTrefer\pageFinal_doc\Announcements.md`（v1.1，2026-05-04）。

**Why:** 存在安全風險（AnnouncementPerm 誤判）與規格偏差（Status=2 未持久化、手冊假資料、標點插入列 UI 缺失、缺狀態下拉）。

**How to apply:** 實作任務前先確認這 5 項缺口是否已解決：

| 優先 | 缺口 | 最後確認日 |
|:---:|------|-----------|
| P0 | `HasAnnouncementPermission()` 用 ModuleKey 比對，改為 PageUrl + OrdinalIgnoreCase（已修復） | 2026-05-04 已修復 |
| P1 | 自動下架未 UPDATE Status=2，僅前端 DisplayStatus 推導 | 2026-05-04 仍未解決 |
| P1 | 使用說明手冊顯示硬編碼假資料，未接 MT_UserGuideFiles | 2026-05-04 仍未解決 |
| P2 | Slide-over 表單缺少「狀態下拉」（無法手動設為已下架） | 2026-05-04 仍未解決 |
| P2 | InlineQuillEditor.razor 缺少 12 個中文標點符號快速插入列 UI | 2026-05-04 仍未解決 |

### 權限比對陷阱（已修復，防止未來迴歸）
`HasAnnouncementPermission()` 原本比對 `m.ModuleKey == "announcements"`（精確字串），
`MainLayout.EnforceModuleAccess` 則比對 `m.PageUrl`（OrdinalIgnoreCase）。
若 DB 中 `MT_Modules.ModuleKey` 值大小寫不符或拼字有差，兩者判斷結果不一致，
造成 EnforceModuleAccess 放行但頁面自己踢出使用者。
修復後統一改用 `m.PageUrl` + `OrdinalIgnoreCase` 比對。
