---
name: 首頁三檔案架構現況
description: Home.razor / HomeService.cs / HomeModel.cs 的實際結構與職責分工（2026-05-13 查核）
type: project
---

## 檔案結構

- `Components/Pages/Home.razor` — 路由 `/` 與 `/home`
- `Services/HomeService.cs` — 實作 `IHomeService`
- `Models/HomeModel.cs` — `UrgentAlertItem`、`AlertSeverity`、`AlertType` enum

## Home.razor 架構重點

- 注入：`IHomeService`、`IQuestionService`（用於命題配額與成員判斷）、`NavigationManager`
- CascadingParameter：`AuthenticationState`、`ModuleCards`（`List<UserModuleCard>`，由 MainLayout 傳入）、`CurrentProject`（`ProjectSwitcherItem?`）
- 功能卡片清單（`enabledModules`）由 `ModuleCards` 過濾 `IsEnabled` 產生，**不在 Home 自行查 DB**
- 生命週期：`OnInitialized` 設定日期顯示；`OnParametersSetAsync` 偵測梯次切換後呼叫 `LoadHomeDataAsync`；`OnAfterRenderAsync` firstRender 後 100ms 設 `isReady=true` 觸發卡片 fadeIn 動畫
- 無任何 EditForm（無表單需求）

## 右側今日提醒看板

兩個子區塊：
1. **急件 / 到期警示**（`urgentAlerts`）— 紅色背景、含計數 Badge；無資料時顯示笑臉 EmptyState
2. **公告與通知**（`announcements`）— 可捲動列表；點擊開啟 `CustomModal` 顯示詳情

## 公告 Modal 顯示欄位

全站廣播 vs 指定專案（`ProjectId is null` 判斷），顯示分類標籤、標題、發佈時間、作者名、富文本內容（`MarkupString`）。

## 導航防護

`NavigateToModuleAsync`：
- 命題任務（cwt-list）：若無命題配額（`QuestionService.GetMyQuotaProgressAsync` 返回空集合）→ SweetAlert 攔截
- 審題任務（reviews）：若非梯次成員（`QuestionService.IsProjectMemberAsync` 返回 false）→ SweetAlert 攔截

**Why:** 防止無任務的使用者誤入命題/審題頁面。
**How to apply:** 修改首頁導航邏輯時需維持此防護機制。

## 使用說明手冊按鈕

右側今日提醒看板頂部有「使用說明手冊」按鈕，目前顯示 Toast 提示「手冊 PDF 建置中，敬請期待」（`ShowManualComingSoon`）。
資料庫有 `MT_UserGuideFiles` 資料表可供日後實作真正的下載功能，目前尚未串接。

## 規格文件來源（2026-05-13 確認）

`D:\jay_liu\Desktop\命題系統相關\網站功能介紹.md` 為最新業務規格文件，首頁右側三個子區塊定義為：
1. 使用說明手冊下載按鈕（搭配今日提醒看板標題列）
2. 急件 / 到期警示
3. 公告與通知
