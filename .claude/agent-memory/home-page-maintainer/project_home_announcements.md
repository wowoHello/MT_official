---
name: 首頁公告看板實作方式
description: 首頁公告來源、排序、分類標籤配色、Modal 顯示，以及 NEW 標籤判斷邏輯（2026-06-01 查核）
type: project
---

## 資料來源

`HomeService.GetAnnouncementsAsync(projectId?)` → 委派給 `IAnnouncementService.GetHomeAnnouncementsAsync`，由公告 Service 統一處理（已發佈 + 上架期間 + 全站或當前梯次）。

## 列表顯示規則

- 最多顯示 5 筆，超過 5 筆顯示「查看全部 N 則公告」按鈕
- 「查看全部」按鈕開啟 `showAllAnnouncementsModal`（全部公告 Modal）
- 在全部公告 Modal 內點擊單筆，先關閉列表 Modal 再開詳情 Modal（不兩層疊加）

## 分類標籤配色（GetCategoryBadgeClass）

| Category 值 | 名稱 | Tailwind class |
|-------------|------|----------------|
| 1 | 系統公告 | `bg-blue-100 text-blue-700` |
| 2 | 命題公告 | `bg-emerald-100 text-emerald-700` |
| 3 | 審題公告 | `bg-purple-100 text-purple-700` |
| 其他 | 其它 | `bg-gray-100 text-gray-600` |

## NEW 標籤判斷

`IsNewAnnouncement`：發佈日期在 3 天內（0~3 天，不含未來日期）顯示 NEW 標籤。
樣式：`bg-terracotta/15 text-terracotta border border-terracotta/30`，含 `fa-sparkles` 圖示。

## 相對日期格式化（FormatRelativeDate）

- 今天 → "今天"
- 昨天 → "昨天"
- ≤15 天 → "N 天前"
- 未來或 >15 天 → "yyyy-MM-dd"

## Modal 詳情顯示（多梯次支援）

使用 `CustomModal` 元件（Type=Center），顯示：
- `ProjectIds.Count == 0` → 顯示「全站廣播」chip（bg-morandi/10）
- 否則呼叫 `GetProjectNameByCurrentId(CurrentProject?.Id)` 只顯示當前切換梯次對應的名稱（不顯示其他綁定梯次，避免使用者困惑）
- 分類、置頂（紅色）、標題、發佈時間（絕對+相對格式）、作者、富文本（`MarkupString`）

## 內容摘要

`StripHtml` 用 Regex `<[^>]+>` 去除 HTML 標籤，用於列表摘要（line-clamp-2）。

## 置頂標籤

`IsPinned=true` 時顯示 `bg-red-100 text-red-600` 紅色「置頂」標籤。
