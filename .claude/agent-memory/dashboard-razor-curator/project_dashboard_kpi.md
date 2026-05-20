---
name: Dashboard KPI 與圖表實作模式
description: 4 張 KPI 卡片 + 2 張 ApexCharts 圖表 + 逾期待辦 + LOG 分頁的資料來源、Status 碼對應、Service 查詢結構、JS Interop 模式（2026-05-20 校正版，含子題修題粒度、PhaseDeadline 抑制邏輯細節、LOG UserId 過濾）
type: project
---

Dashboard 頁面完整實作現況（以 2026-05-20 程式碼為準，覆蓋舊版描述）。

**Why:** US-004 要求儀表板顯示當前梯次的命題進度統計，且資料需隨梯次切換動態更新。

**How to apply:** 修改或擴充 KPI 卡片、LOG、圖表時，直接參照以下對應關係，不需重查 db.md 或 Service 原碼。

---

## 三檔案規模（2026-05-20 實測）
- `Dashboard.razor`：~1030 行（UI + @code，大型頁面）
- `DashboardService.cs`：~1355 行（含多個私有方法與內部私有型別）
- `DashboardModels.cs`：~328 行（全部 DTO + Enum）

---

## Status 碼對應（MT_Questions.Status）
- 0=命題草稿, 1=命題完成, 2=命題送審
- 3=互審中(鎖), 4=互審修題中, 5=專審中(鎖), 6=專審修題中, 7=總審中(鎖), 8=總召修題中
- 9=採用, 10=不採用（三審判決）, 11=ClosedNotAdopted（結案清盤未採用）, 12=結案入庫（算採用）
- **注意：Status 10/11 都是「不採用」終態，差異為觸發來源，圖表合併入 Rejected 桶**

## MT_Projects 狀態判定（無 Status 欄位，動態計算）
- ClosedAt 非 NULL 或 EndDate < 今日 → 2=已結案（sqlProjectStatus 用 COALESCE 取 ClosedAt 或 EndDate）
- StartDate > 今日 → 0=準備中
- 其餘 → 1=進行中

---

## 4 張 KPI 卡片

### 卡片 1：總目標題數（border-l 莫蘭迪灰藍）
- 主數字：SUM(MT_ProjectTargets.TargetCount) WHERE ProjectId=@pid
- Footer：7 種題型明細 2 欄 grid（即使全 0 也顯示完整列表，空時顯示「請先設定各題型目標數量」）

### 卡片 2：已納入題庫（採用）（border-l 鼠尾草綠）
- 主數字：COUNT WHERE Status IN (9, 12) AND IsDeleted=0（注意：12=結案入庫也計入）
- Footer：採用率進度條 = AdoptedCount/TotalTarget（0% 也顯示結構）

### 卡片 3：各階段審題（border-l amber-500）
- 主數字：審題階段（PhaseCode 3/5/7）**母/子題拆分顯示**：「母題 X/Y · 子題 X/Y」
  - ReviewedCount / ReviewTotalCount：母題（SubQuestionId IS NULL）已審/應審計數
  - ReviewedSubCount / ReviewTotalSubCount：子題（SubQuestionId IS NOT NULL）已審/應審計數
  - **完成判定：DecidedAt IS NOT NULL**（草稿只 UPDATE Comment 不寫 DecidedAt）
  - 兩組計數獨立互不影響，均源自 MT_ReviewAssignments
  - **Fallback**：ReviewAssignments 尚無資料（尚未跑 EnsurePhaseTransition）時，改用 MT_Questions/MT_SubQuestions 估算待審池總數（Status BETWEEN 2 AND 8），MasterReviewed=0, SubReviewed=0
- 非審題階段：「本階段審題未啟動」虛線卡（月亮圖示）
- 已結案：「本梯次已結案」印章卡（stone 色，顯示結案日）
- 右上角 Pill Badge：互審階段/專審階段/總召審題/非審題階段/已結案
- Footer：PhaseStatusType switch → InPhase 顯示動畫點+剩餘天數（逾期時 terracotta 色）；BetweenPhases 顯示沙漏 icon

### 卡片 4：各修題階段（border-l terracotta 赤陶色）
- 主數字：修題階段（PhaseCode 4/6/8）顯示 XX/OO 題（RevisedCount/RevisionTotalCount）
  - **AssignedUnits CTE**：`DISTINCT (QuestionId, SubQuestionId)` 兩維，母題與每子題各為獨立修題單元
  - **RevisionTotalCount**：ReviewAssignments 中 DecidedAt IS NOT NULL 且 Status NOT IN (9,10,11,12) 的 distinct (QuestionId, SubQuestionId) 數
  - **RevisedCount**：上述題目中，有對應 MT_RevisionReplies（Stage=PhaseCode, Content 非空, CreatedAt > vw_QuestionRoundStartedAt）的數量
  - **ISNULL(SubQuestionId, -1) NULL-safe 比對**：避免母題 reply 誤認子題或反之
- 非修題階段：「本階段修題未啟動」虛線卡（月亮圖示）
- 修題階段但 RevisionTotalCount=0：「本階段已無待修題目」虛線卡
- 已結案：與卡片 3 相同 stone 印章卡
- 右上角 Pill Badge：互審修題/專審修題/總審修題/非修題階段/已結案

**注意：卡片 3 的 Footer（PhaseStatusText/PhaseStatusType）與卡片 4 共用同一組值，都來自 kpi.PhaseStatusType 和 kpi.PhaseStatusText。**

**兩個 Stage 欄位數值定義不同！**
- MT_ReviewAssignments.ReviewStage = 1(互審)/2(專審)/3(總召)
- MT_RevisionReplies.Stage = PhaseCode 4/6/8（由 QuestionService.SaveRevisionAsync 存入）

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
- 1=產學計畫區間（框架，不顯示在 Footer；BuildUrgentItemsAsync 中 continue 跳過）
- 2=命題階段, 3=交互審題, 4=互審修題
- 5=專家審題, 6=專審修題, 7=總召審題, 8=總召修題

---

## Service 查詢結構（GetKpiAsync）— 修補 G 後雙波並行版本

### 修補 B（重要）：一次撈 sqlAllPhases，in-memory 推導
舊版用兩條獨立 SQL（sqlCurrentPhase + sqlNeighbor），現改為一次 `sqlAllPhases`（SELECT 全梯次所有 phase 含 DaysRemaining/DaysToStart/SortOrder），後續全在 C# 端推導：
- `currentPhaseRecord`：PhaseCode > 1 且 today 在 StartDate~EndDate 區間內，取 SortOrder ASC 首筆
- `neighborPhase`：phaseRow 為 null 時，找距離最近的 upcoming/past phase（優先 IsUpcoming 降序→距離升序）
- `currentPhaseForChart`：StartDate <= today 的最大 PhaseCode（給圖表 2 分桶用）

### Stage 1：5個無相依 SQL 並行（各自開獨立 conn，WithOwnConnAsync）
1. `sqlTargets` → DashboardTargetBreakdown（7 筆，卡片 1 明細；形態 B 保留 JOIN MT_QuestionTypes 作主表）
2. `sqlStatusCounts` → StatusCountRow（僅 AdoptedCount，Status IN (9, 12) 計數）
3. `sqlProjectStatus` → ProjectStatusRow（動態計算 Status 0/1/2 + ClosedAt）
4. `sqlAllPhases` → List<UrgentPhaseRow>（全部階段，含 DaysRemaining/DaysToStart/SortOrder）
5. `sqlAchievement` → DashboardAchievementItem（7 筆，圖表 1；CTE 聚合後 LEFT JOIN MT_QuestionTypes）

### Stage 3（依賴 currentPhaseForChart）：4個 SQL 並行（WithOwnConnAsync）
6. `LoadStatusByTypeRowsAsync` → DashboardStatusByTypeItem（7 筆，圖表 2，依 phaseCode 走 4 分支）
7. `GetReviewProgressAsync` → **(ReviewPhaseLabel, masterReviewed, masterTotal, subReviewed, subTotal)** — 5-tuple，母/子題各自計數，完成判定為 `DecidedAt IS NOT NULL`；含 fallback 機制
8. `GetRevisionProgressAsync` → (RevisionPhaseLabel, RevisedCount, RevisionTotalCount)，子題粒度 AssignedUnits CTE
9. `BuildUrgentItemsAsync` → List<DashboardUrgentItem>

**重要：GetKpiAsync 在 Dashboard.razor 端先呼叫 `PhaseCoordinator.EnsureAsync(CurrentProject.Id)`（60 秒去重），確保審題分配已完成後再查資料。**

---

## BuildUrgentItemsAsync 詳細邏輯（最新版）

### PhaseDeadline 過濾規則（重要，舊版記憶缺失此細節）
對每個 phaseRow 依以下規則決定是否列入警示：
1. PhaseCode == 1 → 跳過（產學區間框架）
2. phaseCode < currentPhaseCode → 跳過（已過階段）
3. phaseCode == currentPhaseCode → 僅 DaysRemaining ≤ 5 才警示（含負數逾期）
4. phaseCode == currentPhaseCode + 1 → 僅 DaysToStart ≤ 5 才警示（下一個即將開始）
5. phaseCode > currentPhaseCode + 1 → 跳過（更遠未來）
6. currentPhaseCode 未取得 → 依舊邏輯 DaysRemaining ≤ 5

**階段抑制條件**（suppress = true 則跳過）：
- PhaseCode=2 命題：所有題型 produced >= target（achievement 全達成）
- PhaseCode=3~8：對應 Status (3~8) 計數 = 0（無題目卡在此狀態）

即將到來的下一階段（DaysToStart > 0）顯示 Notice 並附「預計 N 天後開始」文案。

### 4-a：命題落後（PhaseCode=2 倒數 ≤ 5 天，isProposingPhase）
- 來源：MT_ProjectMembers + MT_MemberQuotas + OUTER APPLY 已產出（Status NOT IN (0,10,11)）
- 觸發：TotalProduced < TotalAssigned，HAVING 過濾
- Severity：< 30% Critical / < 70% Warning / 70~99% Notice
- TargetUrl：`/overview?creatorId={UserId}`

### 4-b：修題落後（PhaseCode 4/6/8 倒數 ≤ 5 天）
- 來源：MT_ReviewAssignments（DecidedAt IS NOT NULL + Status NOT IN 9/10/11/12）+ MT_RevisionReplies
- 本輪過濾：vw_QuestionRoundStartedAt（Plan_014）
- TargetUrl：`/overview?creatorId={UserId}`

### 4-c：審題落後（PhaseCode 3/5/7 倒數 ≤ 5 天）
- **判定：`DecidedAt IS NULL` = 尚未完成審題**（舊版記憶誤寫 Comment IS NULL）
- HAVING：COUNT(*) > SUM(CASE WHEN DecidedAt IS NOT NULL THEN 1 ELSE 0 END)
- TargetUrl：`/reviews`（注意：跳審題任務頁，不是 overview）

### 排序與截斷
- Severity ASC（Critical=0 最高優先）→ DaysRemaining ASC（null 排最後）
- **不截斷**，全部警示皆顯示，UI 靠 max-height + scroll 控制視覺

### 批次 TeacherShortage Modal 明細（步驟 6，避免 N+1）
依當前進行中階段選用對應 sqlTypeDetails（修題/審題/命題三種），
TypeName 由 `_typeCatalog.GetName(r.QuestionTypeId)` 補（形態 A，可用 Catalog）。
**審題分支的 Produced 欄位也用 DecidedAt IS NOT NULL 計數（與 4-c 主清單同口徑）。**

---

## vw_QuestionRoundStartedAt 在 Dashboard 的消費點（第二波 #6）

DashboardService.cs 內共 5 處消費（全部已改用 View）：
1. `LoadStatusByTypeRowsAsync` 修題分支 sqlRevisionBased（行 ~291）
2. `GetRevisionProgressAsync` AssignedUnits 內 EXISTS 子句（行 ~526）
3. `BuildUrgentItemsAsync` sqlRevisionShortage 修題落後（行 ~763）
4. `BuildUrgentItemsAsync` sqlTypeDetails 修題明細分支（行 ~915）

---

## QuestionTypeCatalog 接入點（第二波 #8）

形態 B（主表展開）**保留** `FROM dbo.MT_QuestionTypes qt` 的 JOIN（6 處）：
- `sqlTargets`：題型目標明細展開 7 筆，含無配額也顯示 0
- `sqlAchievement`：CTE + LEFT JOIN，含無題目也顯示 0
- `LoadStatusByTypeRowsAsync` 4 個 SQL 分支（均以 MT_QuestionTypes 為主表展開）

形態 A（補名稱）：`BuildUrgentItemsAsync` 步驟 6 批次明細 → `_typeCatalog.GetName(typeId)` 補 TypeName。

**TeacherTypeDetailRow 無 TypeName 欄位**，消費端才補。

---

## CascadingParameter 模式
- `[CascadingParameter(Name = "CurrentProject")] private ProjectSwitcherItem? CurrentProject`
- OnParametersSetAsync 用 previousProjectId 比對偵測梯次切換 → LoadKpiAsync()
- CurrentProject 為 null 時：kpi 重置 new, chartsNeedRender=false, selectedDetailItem=null, logPage=null
- isLoading 在 OnParametersSetAsync 中立刻拉高並 StateHasChanged()，避免 UI 閃現（修補 A）

## LoadKpiAsync 並行架構（修補 D）
```
LoadKpiAsync()
├─ LoadKpiCoreAsync()    → PhaseCoordinator.EnsureAsync → GetKpiAsync → isLoading=false + StateHasChanged
└─ LoadLogsAsync(reset: true) → isLoadingLogs=false + StateHasChanged
兩者 Task.WhenAll 並行，總時間取最大值
```
- firstRender 的 OnAfterRenderAsync 不呼叫 LoadLogsAsync（修補 C），由 OnParametersSetAsync 統一觸發
- chartsNeedRender 旗標：LoadKpiCoreAsync 成功後設 true → OnAfterRenderAsync 偵測 → RenderChartsAsync()

---

## 卡片 UI 統一結構
```
<div class="flex flex-col min-h-[200px] border-l-[3px] border-l-{color}">
    Header: 圓角方形徽章(w-8 h-8 shrink-0) + 標題 + ⓘ 計算口徑按鈕（卡片 1/3/4 有）+ 右上角 Pill Badge（卡片 3/4 有）
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
- DOM id: `chart-achievement`
- 呼叫：`apexInterop.renderAchievement("chart-achievement", achievementItems[])`
- 資料：`{ typeName, produced, target, fillColor }` 每筆
- 條形顏色（ResolveBarColor）：target<=0 或 produced=0 → #E5E7EB；<30% → #D98A6C；<70% → #F59E0B；<100% → #8EAB94；>=100% → #5C8A6A

### 圖表 2：依題型狀態分佈（Vertical Stacked Bar）
- DOM id: `chart-status`
- 呼叫：`apexInterop.render("chart-status", chart2Options)`（完整 ApexCharts options JSON）
- 5 個系列：「命題草稿」#9CA3AF / 「階段進行中」#F59E0B / 「階段完成」#6B8EAD / 「已採用」#8EAB94 / 「不採用」#991B1B
- 有資料判定：`StatusByType.Any(x => x.Drafts>0 || x.InProgress>0 || x.DoneStage>0 || x.Adopted>0 || x.Rejected>0)`

### 銷毀
- DisposeAsync() 呼叫 `apexInterop.destroy` 兩次（chart-achievement / chart-status）

---

## 稽核歷程 LOG 區塊（梯次內活動分流）

**重要：LOG 區塊「僅顯示梯次內活動（試題/審題 CUD）」，且只顯示 UserId IS NOT NULL 的人工操作（系統批次如審題分配 UserId=NULL 的記錄 DB 仍保留，UI 過濾掉）。**

資料分流依據（GetAuditLogsAsync SQL）：
```sql
WHERE al.ProjectId = @pid
  AND al.Action IN (0, 1, 2)
  AND al.TargetType IN @typeCodes   -- 3=試題, 6=審題
  AND al.UserId IS NOT NULL         -- 過濾系統批次（審題分配等）
```

### Filter Chips（LogTypeFilter 3 種）
- All / Question（TargetType=3）/ Review（TargetType=6）

### TargetName 批次解析（ResolveLogTargetNamesAsync）
- 依 TargetType 分組批次 JOIN 各資料表取 TargetName
- Fallback：查無時解析 OldValue/NewValue JSON 的 `targetDisplayName` / `questionCode` / `title` 等欄位
- ExtractNameFromJson 同時嘗試 camelCase 與 PascalCase 兩種 key

### TargetType=6（審題）細分 LOG 標籤
ResolveLogTypeLabel 方法解析 NewValue/OldValue JSON 的 kind 欄位：
- "annotation" → 「審題劃記」；"annotationResponse" → 「劃記回應」；其他 → 「審題」

---

## 三檔案清單
- `Models/DashboardModels.cs`：PhaseStatusType / RevisionPhaseLabel / ReviewPhaseLabel / DashboardKpiDto（含 ReviewedSubCount + ReviewTotalSubCount）/ 其他 DTO + Enum 共 15 個公開型別
- `Services/DashboardService.cs`：IDashboardService + DashboardService（WithOwnConnAsync）/ GetKpiAsync / LoadStatusByTypeRowsAsync（4 分支）/ GetReviewProgressAsync（含 fallback）/ GetRevisionProgressAsync（AssignedUnits 子題粒度 CTE）/ BuildUrgentItemsAsync（PhaseDeadline 過濾+三教師分支）/ GetAuditLogsAsync（ResolveLogTargetNamesAsync + ExtractNameFromJson）/ ResolvePhaseStatus / ResolvePhaseUrl + 內部私有型別 10 個
- `Components/Pages/Dashboard.razor`：4 KPI 卡片 + 2 圖表 + 逾期待辦（教師 Modal）+ LOG 分頁
- `wwwroot/js/apex-interop.js`：非 Dashboard 專屬，全站共用圖表 Interop

## 與其他模組的關聯
- 命題落後/修題落後 TargetUrl：`/overview?creatorId={UserId}`
- 審題落後 TargetUrl：`/reviews`（此細節重要，不是 overview）
- PhaseDeadline TargetUrl：`/cwt-list?tab=compose`（PhaseCode=2）/ `/cwt-list?tab=revision`（4/6/8）/ `/reviews?tab=review`（3/5/7）
- 梯次階段轉換：`IPhaseTransitionCoordinator.EnsureAsync`（DI 注入 Dashboard.razor），60 秒去重
- 梯次切換：CascadingParameter "CurrentProject"（ProjectSwitcherItem?）

## 已知技術債
- UrgentItems 不截斷（全部警示皆顯示），資料量大時清單很長
- BuildUrgentItemsAsync 修題/審題分支的落後清單 SQL 與批次明細 SQL 結構部分重複
- 第三波 #13 OverviewService CTE 合併尚未完成，間接影響 vw_QuestionRoundStartedAt 維護
