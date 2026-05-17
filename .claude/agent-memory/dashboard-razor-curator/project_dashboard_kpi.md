---
name: Dashboard KPI 與圖表實作模式
description: 4 張 KPI 卡片 + 2 張 ApexCharts 圖表 + 逾期待辦 + LOG 分頁的資料來源、Status 碼對應、Service 查詢結構、JS Interop 模式（2026-05-17 全面校正版）
type: project
---

Dashboard 頁面完整實作現況（以 2026-05-17 程式碼為準，覆蓋舊版描述）。

**Why:** US-004 要求儀表板顯示當前梯次的命題進度統計，且資料需隨梯次切換動態更新。

**How to apply:** 修改或擴充 KPI 卡片、LOG、圖表時，直接參照以下對應關係，不需重查 db.md 或 Service 原碼。

---

## 三檔案規模（2026-05-17）
- `Dashboard.razor`：~1234 行（UI + @code）
- `DashboardService.cs`：~970 行（含多個私有方法與內部私有型別）
- `DashboardModels.cs`：~316 行（全部 DTO + Enum）

---

## Status 碼對應（MT_Questions.Status）
- 0=命題草稿, 1=命題完成, 2=命題送審
- 3=互審中(鎖), 4=互審修題中, 5=專審中(鎖), 6=專審修題中, 7=總審中(鎖), 8=總召修題中
- 9=採用, 10=不採用, 11=改後再審, 12=結案入庫（算採用）

## MT_Projects 狀態判定（無 Status 欄位，動態計算）
- ClosedAt 非 NULL 或 EndDate < 今日 → 2=已結案
- StartDate > 今日 → 0=準備中
- 其餘 → 1=進行中

---

## 4 張 KPI 卡片（正確版本）

### 卡片 1：總目標題數（border-l 莫蘭迪灰藍）
- 主數字：SUM(MT_ProjectTargets.TargetCount) WHERE ProjectId=@pid
- Footer：7 種題型明細 2 欄 grid（即使全 0 也顯示完整列表，空時顯示「請先設定各題型目標數量」）

### 卡片 2：已納入題庫（採用）（border-l 鼠尾草綠）
- 主數字：COUNT WHERE Status IN (9, 12) AND IsDeleted=0（注意：12=結案入庫也計入）
- Footer：採用率進度條 = AdoptedCount/TotalTarget（0% 也顯示結構）

### 卡片 3：各階段審題（border-l amber-500）
- 主數字：審題階段（PhaseCode 3/5/7）顯示 XX/OO 題（ReviewedCount/ReviewTotalCount）
  - ReviewedCount：MT_ReviewAssignments 中 Comment 不為空的筆數
  - ReviewTotalCount：對應 ReviewStage 的分配總筆數
- 非審題階段：「本階段審題未啟動」虛線卡（月亮圖示）
- 已結案：「本梯次已結案」印章卡（stone 色，顯示結案日）
- 右上角 Pill Badge：互審階段/專審階段/總召審題/非審題階段/已結案
- Footer：PhaseStatusType switch → InPhase 顯示動畫點+剩餘天數（逾期時 terracotta 色）；BetweenPhases 顯示沙漏 icon

### 卡片 4：各修題階段（border-l terracotta 赤陶色）
- 主數字：修題階段（PhaseCode 4/6/8）顯示 XX/OO 題（RevisedCount/RevisionTotalCount）
  - RevisedCount：MT_RevisionReplies 中 Content 不為空的 distinct QuestionId 數
  - RevisionTotalCount：對應 ReviewStage 有 Comment 的 distinct QuestionId 數
- 非修題階段：「本階段修題未啟動」虛線卡（月亮圖示）
- 已結案：與卡片 3 相同 stone 印章卡
- 右上角 Pill Badge：互審修題/專審修題/總審修題/非修題階段/已結案

**注意：卡片 3 的 Footer（PhaseStatusText/PhaseStatusType）與卡片 4 共用同一組值，都來自 kpi.PhaseStatusType 和 kpi.PhaseStatusText。**

---

## PhaseStatusType 列舉（Models/DashboardModels.cs）
```
NoProject      → "請選擇梯次"     （預設，CurrentProject=null 時 kpi = new，不呼叫 Service）
Preparing      → "命題尚未開始"   （MT_Projects.StartDate > 今日）
InPhase        → "{PhaseName}"    （amber 動畫點 + 剩餘天數，負數代表逾期）
BetweenPhases  → 最近鄰近階段文字（amber 沙漏 icon，查 NeighborPhase）
Closed         → "已結案"         （ClosedAt 有值或 EndDate 已過）
```

## 階段對應表（PhaseCode → 業務意義）
- 1=產學計畫區間（框架，排除在 Footer 顯示外）
- 2=命題階段, 3=交互審題, 4=互審修題
- 5=專家審題, 6=專審修題, 7=總召審題, 8=總召修題

---

## Service 查詢結構（GetKpiAsync）— 修補 G 後雙波並行版本

### 修補 B（重要）：一次撈 sqlAllPhases，in-memory 推導
舊版用兩條獨立 SQL（sqlCurrentPhase + sqlNeighbor），現改為一次 `sqlAllPhases`（SELECT 全梯次所有 phase 含 DaysRemaining/DaysToStart），後續全在 C# 端推導：
- `currentPhaseRecord`：PhaseCode > 1 且 today 在 StartDate~EndDate 區間內，取 SortOrder ASC 首筆
- `neighborPhase`：phaseRow 為 null 時，找距離最近的 upcoming/past phase
- `currentPhaseForChart`：StartDate <= today 的最大 PhaseCode（給圖表 2 分桶用）

### Stage 1：5個無相依 SQL 並行（各自開獨立 conn，WithOwnConnAsync）
1. `sqlTargets` → DashboardTargetBreakdown（7 筆，卡片 1 明細；形態 B 保留 JOIN MT_QuestionTypes 作主表）
2. `sqlStatusCounts` → StatusCountRow（僅 AdoptedCount，Status IN (9, 12) 計數）
3. `sqlProjectStatus` → ProjectStatusRow（動態計算 Status 0/1/2 + ClosedAt）
4. `sqlAllPhases` → List<UrgentPhaseRow>（全部階段，含 DaysRemaining/DaysToStart/SortOrder）
5. `sqlAchievement` → DashboardAchievementItem（7 筆，圖表 1；CTE 聚合後 LEFT JOIN MT_QuestionTypes）

### Stage 3：4個依賴 currentPhaseForChart 的 SQL 並行
6. `LoadStatusByTypeRowsAsync` → DashboardStatusByTypeItem（7 筆，圖表 2，依 phaseCode 走 4 分支）
7. `GetReviewProgressAsync` → (ReviewPhaseLabel, ReviewedCount, ReviewTotalCount)
8. `GetRevisionProgressAsync` → (RevisionPhaseLabel, RevisedCount, RevisionTotalCount)
9. `BuildUrgentItemsAsync` → List<DashboardUrgentItem>

### 重要：GetKpiAsync 最前面（在 Dashboard.razor 端）呼叫 `PhaseCoordinator.EnsureAsync(CurrentProject.Id)`（60 秒去重），確保審題分配已完成後再查資料。

---

## vw_QuestionRoundStartedAt 在 Dashboard 的消費點（第二波 #6）

修題落後判定 SQL（sqlRevisionShortage）直接內嵌 View：
```sql
AND rr.CreatedAt > ISNULL(
    (SELECT RoundStartedAt FROM dbo.vw_QuestionRoundStartedAt
     WHERE QuestionId = ra.QuestionId),
    '1900-01-01')
```

Modal 明細查詢（sqlTypeDetails 修題分支）也用同樣 View：相同結構。

另外在 `DashboardService.cs` 早期版本有 5 處分散的 MAX subquery，已全部改用 View。

---

## QuestionTypeCatalog 接入點（第二波 #8）

形態 B（主表展開）**保留** `FROM dbo.MT_QuestionTypes qt` 的 JOIN：
- `sqlTargets`（行 59）：形態 B，題型目標明細展開 7 筆，含無配額也顯示 0
- `sqlAchievement`（行 114）：形態 B，CTE + LEFT JOIN，含無題目也顯示 0
- `BuildUrgentItemsAsync` 內配額查詢相關 SQL：形態 B（依題型展開）

**不使用** _typeCatalog.GetName() 補名的原因：Dashboard 的題型 SQL 都以 MT_QuestionTypes 為主表展開（形態 B），確保即使某題型無資料也回傳 0，不能省略 JOIN。

---

## BuildUrgentItemsAsync 三分支（最新版）

### 4-a：命題落後（PhaseCode=2 倒數 ≤ 5 天）
- 來源：MT_ProjectMembers + MT_MemberQuotas（配額制）
- 對比：已產出（Status NOT IN (0,10,11)）vs 配額
- Title 格式：「{TeacherName} 命題進度落後」
- TargetUrl：`/overview?creatorId={UserId}`

### 4-b：修題落後（PhaseCode 4/6/8 倒數 ≤ 5 天）
- 來源：MT_ReviewAssignments（有 Comment 的題目）+ MT_RevisionReplies（本輪已修）
- 本輪過濾：rr.CreatedAt > vw_QuestionRoundStartedAt.RoundStartedAt（Plan_014）
- Title 格式：「{TeacherName} 修題進度落後」
- TargetUrl：`/overview?creatorId={UserId}`

### 4-c：審題落後（PhaseCode 3/5/7 倒數 ≤ 5 天）
- 來源：MT_ReviewAssignments（Comment 空白 = 尚未給意見）
- Title 格式：「{TeacherName} 審題進度落後」
- TargetUrl：`/reviews`（注意：審題進度跳審題任務頁，不是 overview）

### 排序：Severity ASC（Critical 優先）→ DaysRemaining ASC
不截斷清單，全部顯示，UI 靠 max-h + scroll 控制視覺空間。

### 批次 TeacherShortage Modal 明細（步驟 6，避免 N+1）
依當前進行中階段選用對應 SQL（修題/審題/命題三種 sqlTypeDetails），
批次查詢所有落後教師的 QuestionTypeId x Assigned/Produced 明細，
TypeName 由 `_typeCatalog.GetName(r.QuestionTypeId)` 補（此處屬形態 A，可用 Catalog）。

---

## CascadingParameter 模式
- `[CascadingParameter(Name = "CurrentProject")] private ProjectSwitcherItem? CurrentProject`
- OnParametersSetAsync 用 previousProjectId 比對偵測梯次切換 → LoadKpiAsync()
- CurrentProject 為 null 時直接 return（kpi 重置為 new DashboardKpiDto()，圖表旗標 false，logPage 清除）
- isLoading 在 OnParametersSetAsync 中立刻拉高並 StateHasChanged()，避免 UI 閃現有資料狀態（修補 A）

## LoadKpiAsync 並行架構（修補 D）
```
LoadKpiAsync()
├─ LoadKpiCoreAsync() → 完成後 isLoading=false + StateHasChanged（卡片/圖表先顯示）
└─ LoadLogsAsync(reset: true) → 完成後 isLoadingLogs=false + StateHasChanged
兩者 Task.WhenAll 並行，總時間取最大值
```
注意：firstRender 的 OnAfterRenderAsync 不再呼叫 LoadLogsAsync（修補 C），由 OnParametersSetAsync 統一觸發。

---

## 卡片 UI 統一結構
```
<div class="flex flex-col min-h-[200px] border-l-[3px] border-l-{color}">
    Header: 圓角方形徽章(w-8 h-8 shrink-0) + 標題 + 右上角 Pill Badge（卡片 3/4 有）
    Main:   text-4xl font-black tabular-nums（解決數字寬度抖動）；非啟動/已結案顯示替代區塊
    Footer: mt-auto pt-3 border-t（永遠有內容，0 值也不留白）
</div>
```

---

## ApexCharts 圖表實作模式

### JS Interop 架構
- 入口：`wwwroot/js/apex-interop.js`（render/update/destroy 三方法）
- Dashboard.razor：`@inject IJSRuntime JS` + `@implements IAsyncDisposable`

### 圖表 1：題型缺口達成率（Horizontal Bar）
- DOM id: `chart-achievement`；lg:col-span-1；h-[350px]
- 呼叫：`apexInterop.renderAchievement("chart-achievement", achievementItems[])`
- 資料：`{ typeName, produced, target, fillColor }` 每筆
- 條形顏色（ResolveBarColor）：target<=0 或 produced=0 → #E5E7EB；<30% → #D98A6C；<70% → #F59E0B；<100% → #8EAB94；>=100% → #5C8A6A

### 圖表 2：依題型狀態分佈（Vertical Stacked Bar）
- DOM id: `chart-status`；lg:col-span-2；h-[350px]
- 呼叫：`apexInterop.render("chart-status", chart2Options)`（完整 ApexCharts options JSON）
- **5 個系列名稱（正確版）**：「命題草稿」#9CA3AF / 「階段進行中」#F59E0B / 「階段完成」#6B8EAD / 「已採用」#8EAB94 / 「不採用」#991B1B

### 渲染時序
- chartsNeedRender 旗標：LoadKpiCoreAsync 成功後設 true
- OnAfterRenderAsync 偵測旗標 → 呼叫 RenderChartsAsync()
- 兩張圖表 JS interop 並行（修補 E，Task.WhenAll）
- 無資料時顯示 EmptyState（DOM 元素不存在），JS 安全跳過

### 銷毀
- DisposeAsync() 呼叫 `apexInterop.destroy` 兩次（chart-achievement / chart-status）

---

## 逾期與緊急待辦區塊

### 兩種訊號（UrgentSourceType）
- PhaseDeadline：顯示在 Header 右側（純文字，不可點）
  - 依 severity：Critical=terracotta 色，Warning=amber 色，Notice=slate 色
- TeacherShortage：清單顯示（可點擊開啟教師詳情 Modal）
  - 圖示：Critical → fa-user-xmark；其他 → fa-user-clock
  - Header 右側顯示 TeacherShortage 數量 badge

### 教師詳情 Modal（TeacherShortage 點擊觸發）
- 觸發條件：item.TeacherDetails?.Count > 0
- 結構：總配額進度卡（石色背景）+ 各題型進度明細（achievement 進度條）
- Footer：關閉按鈕 + 「前往命題總覽」連結（href = selectedDetailItem.TargetUrl）
- 進度條色（AchBarClass）：>=1.0 → sage；>=0.7 → morandi；>=0.3 → amber-500；其他 → terracotta

### 空清單文案（EmptyShortageSubMessage 動態判斷）
- 審題階段（ReviewPhase Peer/Expert/Final）→「所有審題委員皆已給意見」
- 修題階段（RevisionPhase Peer/Expert/Final）→「所有教師修題進度皆在正常範圍」
- 其他（命題/未啟動）→「所有教師命題進度皆在正常範圍」

---

## 稽核歷程 LOG 區塊（梯次內活動分流）

**重要：LOG 區塊「僅顯示梯次內活動（試題/審題 CUD）」。登入/登出、人員/角色/專案/公告 CUD 等跨梯次活動已完全移至 `/system-logs`（SystemLogs.razor）。**

資料分流依據（GetAuditLogsAsync SQL）：
```
WHERE ProjectId = @pid AND TargetType IN (3, 6)  -- 3=試題, 6=審題
```
Filter Chip 選 Question → TargetType = 3；選 Review → TargetType = 6；選 All → 兩者都含。

### 狀態變數
- `logPage: AuditLogPage?`（null = 尚未載入）
- `isLoadingLogs: bool`
- `activeLogFilter: LogTypeFilter`（初始 All）
- `LogPageSize = 50`（每頁筆數）

### Filter Chips（LogTypeFilter 3 種，梯次內活動）
- All / Question（試題 TargetType=3）/ Review（審題 TargetType=6）
- **無「含全站事件」Toggle、無 localStorage 持久化**

### 載入邏輯
- `LoadLogsAsync(reset: true)`：切換梯次 / 切換 Filter 時觸發（由 LoadKpiAsync 的 Task.WhenAll 並行呼叫，非 firstRender 觸發）
- `LoadLogsAsync(reset: false)`：「載入更早 N 筆」按鈕追加（合併 Logs List）

### TargetType=6（審題）細分 LOG 標籤
ResolveLogTypeLabel 方法解析 NewValue/OldValue JSON 的 kind 欄位：
- "annotation" → 「審題劃記」
- "annotationResponse" → 「劃記回應」
- 其他 → 「審題」

### LOG 列表 UI
- max-h-[290px] overflow-y-auto（約 5 筆捲動）
- 每列：圓角圖示(w-7 h-7) + 描述 + 相對時間（tabular-nums）
- 「載入更多」按鈕（HasMore=true 顯示剩餘筆數）
- Header 右側「全站活動 →」連結跳轉 `/system-logs`

---

## 三檔案清單
- `Models/DashboardModels.cs`：PhaseStatusType / RevisionPhaseLabel / ReviewPhaseLabel / DashboardKpiDto / DashboardTargetBreakdown / DashboardAchievementItem / DashboardStatusByTypeItem / UrgentSeverity / UrgentSourceType / DashboardUrgentItem / UrgentTeacherDetail / LogTypeFilter / AuditLogQuery / AuditLogPage / RecentAuditLog
- `Services/DashboardService.cs`：IDashboardService（GetKpiAsync + GetAuditLogsAsync）/ DashboardService（WithOwnConnAsync 並行輔助）/ BuildUrgentItemsAsync（三分支 4-a/4-b/4-c）/ LoadStatusByTypeRowsAsync / GetReviewProgressAsync / GetRevisionProgressAsync / ResolvePhaseStatus / 所有 SQL / 內部私有型別（StatusCountRow / UrgentPhaseRow / PhaseRow / NeighborPhase / ProjectStatusRow / TeacherShortageRow / TeacherTypeDetailRow）
- `Components/Pages/Dashboard.razor`：4 KPI 卡片 + 2 圖表 + 逾期待辦（含教師 Modal）+ LOG 分頁（3 種 Filter Chips）+ 所有 Helper 方法（~30 個輔助 method）
- `wwwroot/js/apex-interop.js`：非 Dashboard 專屬，全站共用圖表 Interop

## 與其他模組的關聯
- 從命題總覽跳轉：`/overview?creatorId=X`（命題落後/修題落後的「前往命題總覽」按鈕）
- 審題落後跳轉：`/reviews`（而非 overview，此細節重要）
- 頁面快速連結：[專案設定] → /projects；[試題總覽] → /overview
- LOG 跨梯次活動：SystemLogs.razor（/system-logs），MT_LoginLogs + MT_AuditLogs WHERE ProjectId IS NULL
- 梯次階段轉換：`IPhaseTransitionCoordinator.EnsureAsync`（注入 Dashboard.razor），60 秒去重
- 梯次切換事件：CascadingParameter "CurrentProject"（ProjectSwitcherItem?），OnParametersSetAsync 偵測 previousProjectId 差異

## 已知技術債
- UrgentItems 不截斷顯示（無 Top 5 限制），全部落後教師皆顯示，資料量大時清單很長
- BuildUrgentItemsAsync 在命題/修題/審題三分支內有個批次明細 SQL，與落後教師清單 SQL 存在結構重複
- 第三波 #13 OverviewService CTE 合併尚未完成，間接影響 Dashboard 的 vw_QuestionRoundStartedAt 維護
