---
name: 首頁三檔案架構現況
description: Home.razor / HomeService.cs / HomeModel.cs 的實際結構與職責分工（2026-05-17 查核）
type: project
---

## 檔案規模（2026-05-20 查核）

- `Components/Pages/Home.razor` — 474 行（UI + @code 合計，無變化）
- `Services/HomeService.cs` — 469 行（IHomeService 介面 + HomeService 實作 + 6 個 private sealed class）
- `Models/HomeModel.cs` — 48 行（2 個 enum + 1 個 class，無變化）

## Home.razor 架構重點

- 注入：`IHomeService`、`IQuestionService`（命題配額與成員判斷）、`NavigationManager`
- CascadingParameter：`AuthenticationState`、`ModuleCards`（`List<UserModuleCard>`，由 MainLayout 傳入）、`CurrentProject`（`ProjectSwitcherItem?`）
- 功能卡片清單（`enabledModules`）由 `ModuleCards` 過濾 `IsEnabled` 產生，**不在 Home 自行查 DB**
- 生命週期：`OnInitialized` 設定日期顯示；`OnParametersSetAsync` 偵測梯次切換後呼叫 `LoadHomeDataAsync`（無 `OnAfterRenderAsync`）
- 卡片動畫：inline style `animation: fadeIn 0.4s ease-out @(delay)ms both`，keyframe 定義在 `wwwroot/css/input.css`
- 無任何 EditForm（無表單需求）

## HomeService.cs 架構重點

- 依賴：`IDatabaseService`（Dapper）、`IAnnouncementService`（公告委派）、`ILogger<HomeService>`
- **不依賴** `IMembershipService` 或 `IQuestionTypeCatalog`（第二波共用基底未整合至此）
- 核心方法：`GetAnnouncementsAsync`（委派給 AnnouncementService）、`GetUrgentAlertsAsync`（主邏輯 — 10 結果集 QueryMultiple）
- 閥值常數：`AlertThresholdDays = 5`、`CriticalThresholdDays = 2`（定義於類別頂部）
- 角色語意常數（IsDefault=1）：`RoleProposer = "命題教師"`、`RoleExpert = "審題委員"`、`RoleConvener = "總召集人"`
- 梯次 Category 常數：`CategoryInternal = 0`（對應 MT_Roles.Category 0=內部人員）
- 內部 private sealed class：`PhaseRow`、`QuotaRow`、`StatusCountRow`、`StageCountRow`、`QuotaGapRow`、`OverdueRow`
- 梯次結案判斷：先執行 `closedCheckSql`（`ClosedAt IS NOT NULL OR EndDate < 今日`），是則直接回傳空陣列，不進入主 SQL

## HomeModel.cs 架構重點

- `AlertSeverity` enum（Warning=0, Critical=1）
- `AlertType` enum（PhaseCountdown=0, PersonalBacklog=1, QuotaGap=2, PhaseOverdue=3, AdminSummary=4）
- `UrgentAlertItem` class（AlertType, Severity, PhaseCode, DaysLeft, Title, Subtitle）
- `AnnouncementListItem` 定義於 `Models/AnnouncementModels.cs`（非 HomeModel.cs）

## 右側今日提醒看板結構

1. 標題列 + 「使用說明手冊」按鈕（目前 Toast 提示「建置中」）
2. **急件 / 到期警示**（`urgentAlerts`）— 紅色背景、含計數 Badge；空時顯示笑臉
3. **公告與通知**（`announcements`）— 可捲動列表；點擊開啟 `CustomModal` 詳情

## 導航防護（NavigateToModuleAsync）

- 命題任務（url 含 "cwt-list"）：無命題配額（`QuestionService.GetMyQuotaProgressAsync` 空集合）→ SweetAlert 攔截
- 審題任務（url 含 "reviews"）：非梯次成員（`QuestionService.IsProjectMemberAsync` false）→ SweetAlert 攔截
- 攔截後不跳頁，以 `Swal.fire` 顯示 Warning，confirmButtonColor 用莫蘭迪藍 `#6B8EAD`

**Why:** 防止無任務的使用者誤入頁面，比起在子頁面處理更能早期攔截。
**How to apply:** 修改導航邏輯時須維持此防護。警示連結若未來改為帶 tab 參數（如 ?tab=compose），需在此處加 query string 而非在 module.PageUrl 硬編。

## 已知技術債（2026-05-21 更新）

1. **結果集 #4 與 #10 邏輯重複**：同一個「修題中本輪未回覆」SQL 在個人視角和管理員視角各寫一次（Plan_DB_PerfReview 已記錄）。
2. **HomeService 未整合 IMembershipService**：結果集 #2（梯次角色）是 HomeService 自己打 DB，而非用第二波 #7 建好的 `IMembershipService` cache，是已知未整合殘餘。
3. **使用說明手冊未串接 DB**：`MT_UserGuideFiles` 資料表存在（欄位：Id, FileName, FilePath, FileSize, UploadedBy, IsActive），`ShowManualComingSoon` 方法目前只顯示 Toast（swalInterop.fireToast），尚未串接真正下載。
4. **急件警示無直接連結**：alert 卡片點擊無跳頁行為（`warning_MODIFY.md` 規劃的 `?tab=compose/revision` 連結尚未實作到 Blazor 版本）。
5. **SQL 評論不一致**：HomeService `const string sql` 標頭評論寫「8 個結果集」，實際有 10 個（管理員視角 9/10 未在標頭反映）。
6. **CWT/LCT 未區分**：`GetUrgentAlertsAsync` 不讀取 `ProjectType`，LCT 梯次的配額缺口計算行為未驗證。詳見 `project_cwt_lct_distinction.md`。
