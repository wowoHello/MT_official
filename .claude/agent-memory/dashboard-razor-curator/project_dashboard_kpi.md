---
name: Dashboard KPI 與圖表實作模式
description: 4 張 KPI 卡片 + 2 張 ApexCharts 圖表 + 逾期待辦 + LOG 分頁的資料來源、Status 碼對應、Service 查詢結構、JS Interop 模式（2026-05-13 全面校正版）
type: project
---

Dashboard 頁面完整實作現況（以 2026-05-13 程式碼為準，覆蓋舊版描述）。

**Why:** US-004 要求儀表板顯示當前梯次的命題進度統計，且資料需隨梯次切換動態更新。

**How to apply:** 修改或擴充 KPI 卡片、LOG、圖表時，直接參照以下對應關係，不需重查 db.md 或 Service 原碼。

---

## Status 碼對應（MT_Questions.Status）
- 0=命題草稿, 1=命題完成, 2=命題送審
- 3=互審中(鎖), 4=互審修題中, 5=專審中(鎖), 6=專審修題中, 7=總審中(鎖), 8=總審修題中
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
  - ReviewTotalCount：對應 ReviewStage 的分配總筆數（Fallback：Status 2~8 總數）
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

## Service 查詢結構（GetKpiAsync）
1. sqlTargets → DashboardTargetBreakdown（7 筆，卡片 1 明細）
2. sqlStatusCounts → StatusCountRow（僅 AdoptedCount，Status IN (9, 12) 計數）
3. sqlProjectStatus → ProjectStatusRow（動態計算 Status 0/1/2 + ClosedAt）
4. sqlCurrentPhase → PhaseRow（排除 PhaseCode=1，取今日所在工作階段）
5. sqlNeighbor（phaseRow 為 null 時）→ NeighborPhase（下一個/剛結束的鄰近階段）
6. sqlAchievement → DashboardAchievementItem（7 筆，圖表 1，Produced = Status NOT IN (0,10,11)）
7. sqlStatusBased / sqlReviewBased / sqlRevisionBased → DashboardStatusByTypeItem（7 筆，圖表 2，依 currentPhaseForChart 三分支）
8. GetReviewProgressAsync → (ReviewPhaseLabel, ReviewedCount, ReviewTotalCount)
9. GetRevisionProgressAsync → (RevisionPhaseLabel, RevisedCount, RevisionTotalCount)
10. BuildUrgentItemsAsync → List<DashboardUrgentItem>（逾期待辦）

**重要：GetKpiAsync 最前面呼叫 `PhaseCoordinator.EnsureAsync(CurrentProject.Id)`（60 秒去重），確保審題分配已完成後再查資料。**

---

## CascadingParameter 模式
- `[CascadingParameter(Name = "CurrentProject")] private ProjectSwitcherItem? CurrentProject`
- OnParametersSetAsync 用 previousProjectId 比對偵測梯次切換 → LoadKpiAsync()
- CurrentProject 為 null 時直接 return（kpi 重置為 new DashboardKpiDto()，圖表旗標 false，logPage 清除）

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
- chartsNeedRender 旗標：LoadKpiAsync 成功後設 true
- OnAfterRenderAsync 偵測旗標 → 呼叫 RenderChartsAsync()
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
- Footer：關閉按鈕 + 「前往命題總覽」連結（href = /overview?creatorId={UserId}）
- 進度條色（AchBarClass）：>=1.0 → sage；>=0.7 → morandi；>=0.3 → amber-500；其他 → terracotta

### 空清單文案（EmptyShortageSubMessage 動態判斷）
- 審題階段（ReviewPhase Peer/Expert/Final）→「所有審題委員皆已給意見」
- 修題階段（RevisionPhase Peer/Expert/Final）→「所有教師修題進度皆在正常範圍」
- 其他（命題/未啟動）→「所有教師命題進度皆在正常範圍」

---

## 稽核歷程 LOG 區塊（2026-05-13 校正版）

**重要：LOG 區塊「僅顯示梯次內活動（試題/審題 CUD）」。登入/登出、人員/角色/專案/公告 CUD 等跨梯次活動已完全移至 `/system-logs`（SystemLogs.razor）。**

### 狀態變數
- `logPage: AuditLogPage?`（null = 尚未載入）
- `isLoadingLogs: bool`
- `activeLogFilter: LogTypeFilter`（初始 All）
- `LogPageSize = 50`（每頁筆數）

### Filter Chips（LogTypeFilter 3 種，梯次內活動）
- All（全部）/ Question（試題 TargetType=3）/ Review（審題 TargetType=6）
- **舊記憶誤寫為 7 種（含 Login/Members/Project/Announcement），已更正為 3 種**
- **無「含全站事件」Toggle 和 localStorage 持久化功能（舊記憶誤記，實際程式碼無此邏輯）**

### 載入邏輯
- `LoadLogsAsync(reset: true)`：firstRender（OnAfterRenderAsync）/ 切換梯次 / 切換 Filter 時觸發
- `LoadLogsAsync(reset: false)`：「載入更早 N 筆」按鈕追加（合併 Logs List）
- KPI 載完後（LoadKpiAsync 內）呼叫 `LoadLogsAsync(reset: true)` 重新載入

### LOG 列表 UI
- 每列：圓角圖示(w-7 h-7) + 描述 + 相對時間（tabular-nums）
- max-h-[290px] overflow-y-auto（約 5 筆捲動）
- 格式：「{UserName} 動詞了 類型「名稱」」（truncate + title tooltip）
- 相對時間：<60s=剛剛 / <60m=N分鐘前 / <24h=N小時前 / <7d=N天前 / else=MM/dd HH:mm
- 「載入更多」按鈕（HasMore=true 時顯示，顯示剩餘筆數）
- Header 右側有「全站活動 →」連結跳轉 `/system-logs`（提示跨梯次紀錄在此）

### Action 碼（此頁僅 CUD）
- 0=建立(emerald+sage) / 1=修改(blue+morandi) / 2=刪除(rose+terracotta)

### TargetType 碼（此頁僅梯次內）
- 3=試題 / 6=審題

---

## 三檔案清單
- `Models/DashboardModels.cs`：PhaseStatusType / RevisionPhaseLabel / ReviewPhaseLabel / DashboardKpiDto / DashboardTargetBreakdown / DashboardAchievementItem / DashboardStatusByTypeItem / UrgentSeverity / UrgentSourceType / DashboardUrgentItem / UrgentTeacherDetail / LogTypeFilter / AuditLogQuery / AuditLogPage / RecentAuditLog
- `Services/DashboardService.cs`：IDashboardService（GetKpiAsync + GetAuditLogsAsync）/ DashboardService / BuildUrgentItemsAsync / GetReviewProgressAsync / GetRevisionProgressAsync / GetCurrentPhaseCodeAsync / ResolvePhaseStatus / 所有 SQL / 內部私有型別（StatusCountRow / PhaseRow / NeighborPhase / ProjectStatusRow / ReviewProgressRow）
- `Components/Pages/Dashboard.razor`：4 KPI 卡片 + 2 圖表 + 逾期待辦（含教師 Modal） + LOG 分頁（3 種 Filter Chips，無 Toggle/localStorage）+ 所有 Helper 方法
- `wwwroot/js/apex-interop.js`：非 Dashboard 專屬，全站共用圖表 Interop

## 與其他模組的關聯
- 從命題總覽跳轉：`/overview?creatorId=X`（教師詳情 Modal 的「前往命題總覽」按鈕）
- 頁面快速連結：[專案設定] → /projects；[試題總覽] → /overview
- LOG 跨梯次活動：SystemLogs.razor（/system-logs），MT_LoginLogs + MT_AuditLogs WHERE ProjectId IS NULL
- 梯次階段轉換：`IPhaseTransitionCoordinator.EnsureAsync`（注入），60 秒去重
- 梯次切換事件：CascadingParameter "CurrentProject"（ProjectSwitcherItem?），OnParametersSetAsync 偵測 previousProjectId 差異
