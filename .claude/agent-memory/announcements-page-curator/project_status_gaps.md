---
name: Announcements 頁面未完成缺口
description: 2026-04-27 評估後確認的 5 項待補缺口，判定為未完成頁面
type: project
---

頁面目前為「未完成」狀態，評估報告放在 `D:\MTrefer\PageNotEnd_doc\Announcements.md`。

**Why:** 存在安全風險（AnnouncementPerm 未實作）與規格偏差（Status=2 未持久化、標點插入列 UI 缺失）。

**How to apply:** 實作任務前先確認這 5 項缺口是否已解決：

| 優先 | 缺口 |
|:---:|------|
| P0 | AnnouncementPerm 權限未實作 — 任何登入者皆可 CRUD |
| P1 | 自動下架未 UPDATE Status=2，僅前端 DisplayStatus 推導 |
| P1 | 使用說明手冊顯示硬編碼假資料，未接 MT_UserGuideFiles |
| P2 | Slide-over 表單缺少「狀態下拉」（無法手動設為已下架） |
| P2 | InlineQuillEditor.razor 缺少 12 個中文標點符號快速插入列 UI |
