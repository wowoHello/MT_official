---
name: 首頁三檔案架構現況
description: Home.razor / HomeService.cs / HomeModel.cs 的實際結構與職責分工（2026-05-29 查核）
type: project
---

## 檔案規模（2026-05-29 查核）

- `Components/Pages/Home.razor` — 474 行（UI + @code 合計）
- `Services/HomeService.cs` — 512 行（IHomeService 介面 + HomeService 實作 + 6 個 private sealed class）
- `Models/HomeModel.cs` — 49 行（2 個 enum + 1 個 class）

## Home.razor 架構重點

- 注入：`IHomeService`、`IQuestionService`（命題配額與成員判斷）、`NavigationManager`
- CascadingParameter：`AuthenticationState`、`ModuleCards`（`List<UserModuleCard>`，由 MainLayout 傳入，`Name="ModuleCards"`）、`CurrentProject`（`ProjectSwitcherItem?`，`Name="CurrentProject"`）
- 功能卡片清單（`enabledModules`）由 `ModuleCards` 過濾 `IsEnabled==true` 產生，**不在 Home 自行查 DB**
- 生命週期：`OnInitialized` 設定日期顯示（中文星期格式）；`OnParametersSetAsync` 偵測梯次切換（`projectId != previousProjectId` 或 `!hasLoadedHomeData`）後呼叫 `LoadHomeDataAsync`（無 `OnAfterRenderAsync`）
- `LoadHomeDataAsync` 用 `Task.WhenAll` 並行呼叫公告與急件兩支 API，任一失敗時用 `IsCompletedSuccessfully` 判斷降級取空陣列
- `cachedUserId` 避免重複讀取 AuthenticationState Claims
- 卡片動畫：inline style `animation: fadeIn 0.4s ease-out @(delay)ms both`，delay = index * 50ms，keyframe 定義在 `wwwroot/css/input.css`
- 無任何 EditForm（無表單需求）

## HomeService.cs 架構重點

- 依賴：`IDatabaseService`（Dapper）、`IAnnouncementService`（公告委派）、`IQuestionService`（個人配額計算）、`ILogger<HomeService>`
- **不依賴** `IMembershipService` 或 `IQuestionTypeCatalog`（第二波共用基底未整合至此）
- 核心方法：`GetAnnouncementsAsync`（委派給 `_announcementService.GetHomeAnnouncementsAsync`）、`GetUrgentAlertsAsync`（主邏輯 — 10 結果集 QueryMultiple）
- **額外 Service 呼叫**：`GetUrgentAlertsAsync` 在 10 結果集之外，額外呼叫 `_questionService.GetMyQuotaProgressAsync(userId, projectId)` 取得個人配額精確缺口（結果集 #3 的 SUM 算法不含子題且無 cap，因此用 QuestionService per-row 計算補救）
- 閥值常數：`AlertThresholdDays = 5`、`CriticalThresholdDays = 2`
- 角色語意常數（IsDefault=1）：`RoleProposer = "命題教師"`、`RoleExpert = "審題委員"`、`RoleConvener = "總召集人"`
- 梯次 Category 常數：`CategoryInternal = 0`（對應 MT_Roles.Category 0=內部人員）
- 內部 private sealed class：`PhaseRow`、`QuotaRow`、`StatusCountRow`、`StageCountRow`、`QuotaGapRow`、`OverdueRow`
- 梯次結案判斷：先執行獨立 `closedCheckSql`（`ClosedAt IS NOT NULL OR EndDate < CAST(GETDATE() AS DATE)`），結果 = 1 則直接回傳空陣列
- `const string sql` 標頭評論寫「8 個結果集」，實際有 10 個（評論未更新，不影響執行）

## HomeModel.cs 架構重點

- `AlertSeverity` enum（Warning=0, Critical=1）
- `AlertType` enum（PhaseCountdown=0, PersonalBacklog=1, QuotaGap=2, PhaseOverdue=3, AdminSummary=4）
- `UrgentAlertItem` class（AlertType, Severity, PhaseCode, DaysLeft, Title, Subtitle）
- `AnnouncementListItem` 定義於 `Models/AnnouncementModels.cs`（非 HomeModel.cs）

## 右側今日提醒看板結構

1. 標題列 + 「使用說明手冊」按鈕（`ShowManualComingSoon`，呼叫 `swalInterop.fireToast`，Toast 提示「手冊 PDF 建置中，敬請期待。」）
2. **急件 / 到期警示**（`urgentAlerts`）— 紅色背景、含計數 Badge；空時顯示笑臉圖示
3. **公告與通知**（`announcements`）— 可捲動列表；點擊呼叫 `OpenAnnouncementModal` 開啟 `CustomModal` 詳情

## 導航防護（NavigateToModuleAsync）

- 命題任務（url 含 "cwt-list"）：無命題配額（`QuestionService.GetMyQuotaProgressAsync` 空集合）→ SweetAlert 攔截
- 審題任務（url 含 "reviews"）：非梯次成員（`QuestionService.IsProjectMemberAsync` false）→ SweetAlert 攔截
- 攔截後不跳頁，以 `Swal.fire` 顯示 Warning，`confirmButtonColor = "#6B8EAD"`（莫蘭迪藍）
- 僅在 `CurrentProject is not null` 時執行防護（未選梯次時直接跳頁）

**Why:** 防止無任務的使用者誤入頁面。
**How to apply:** 修改導航邏輯時須維持此防護。

## 技術債（2026-05-29 查核）

1. **結果集 #4 與 #10 邏輯重複**：同一個「修題中本輪未回覆」NOT EXISTS 子查詢在個人視角和管理員視角各寫一次。
2. **HomeService 未整合 IMembershipService**：結果集 #2（梯次角色）是 HomeService 自己打 DB，而非用第二波 #7 建好的 `IMembershipService` cache。
3. **使用說明手冊未串接 DB**：`MT_UserGuideFiles` 資料表存在，`ShowManualComingSoon` 方法目前只顯示 Toast，尚未串接真正下載。
4. **急件警示無直接連結**：alert 卡片點擊無跳頁行為（`warning_MODIFY.md` 規劃的 `?tab=compose/revision` 連結尚未實作到 Blazor 版本）。
5. **SQL 評論不一致**：HomeService `const string sql` 標頭評論寫「8 個結果集」，實際有 10 個（管理員視角 9/10 未在標頭反映）。
6. **CWT/LCT 未區分**：`GetUrgentAlertsAsync` 不讀取 `ProjectType`，LCT 梯次的配額缺口計算行為未驗證。詳見 `project_cwt_lct_distinction.md`。
7. **個人配額計算雙重查詢**：`GetUrgentAlertsAsync` 在結果集 #3 外又呼叫 `_questionService.GetMyQuotaProgressAsync`（額外一次 DB 查詢），結果集 #3 的 SUM 算法不含子題且未做 cap，屬補救性 workaround。
