---
name: Dashboard KPI 與圖表實作模式
description: 4 張 KPI 卡片 + 2 張 ApexCharts 圖表 + 逾期待辦 + LOG 分頁的資料來源、Status 碼對應、Service 查詢結構、JS Interop 模式（2026-05-29 全文盤點版，含完整 LCT 雙模式與所有私有方法）
type: project
---

Dashboard 頁面完整實作現況（以 2026-05-29 全文讀取三檔為準，覆蓋舊版描述）。

**Why:** US-004 要求儀表板顯示當前梯次的命題進度統計，且資料需隨梯次切換動態更新。

**How to apply:** 修改或擴充 KPI 卡片、LOG、圖表時，直接參照以下對應關係，不需重查 db.md 或 Service 原碼。

---

## 三檔案規模（2026-05-29 實測）
- `Dashboard.razor`：1409 行（UI + @code）
- `DashboardService.cs`：2290 行（含多個私有方法與內部私有型別）
- `DashboardModels.cs`：415 行（全部 DTO + Enum）

---

## Status 碼對應（MT_Questions.Status）
- 0=命題草稿, 1=命題完成, 2=命題送審
- 3=互審中(鎖), 4=互審修題中, 5=專審中(鎖), 6=專審修題中, 7=總審中(鎖), 8=總召修題中
- 9=採用, 10=不採用（三審判決）, 11=ClosedNotAdopted（結案清盤未採用）, 12=結案入庫（算採用）
- **Status 10/11 都是「不採用」終態，差異為觸發來源，圖表合併入 Rejected 桶**

## MT_Projects 狀態判定（無 Status 欄位，動態計算）
- ClosedAt 非 NULL 或 EndDate < 今日 → 2=已結案
- StartDate > 今日 → 0=準備中
- 其餘 → 1=進行中

---

## 4 張 KPI 卡片

### 卡片 1：總目標題數（border-l 莫蘭迪灰藍）
- 主數字：`kpi.TotalTarget` = SUM(TargetRows.TargetCount)
- Footer：DashboardTargetBreakdown 清單，2-欄 grid，顯示 `DisplayLabel + Produced/TargetCount`
  - 即使全為 0 也顯示完整列表（不空白），空時顯示「請先設定各題型目標數量」
- 計算口徑 ⓘ 按鈕：「題組類試題整題視為 1 題，不另計子題」

### 卡片 2：已納入題庫（採用）（border-l 鼠尾草綠）
- 主數字：`kpi.AdoptedTotal`（AdoptedCount 的 alias）= Status IN (9, 12) 合計
- CWT：母題 + 閱讀/短文子題（Status IN 9,12）；LCT：聽力測驗 master + 聽力題組整組 all-or-nothing
- Footer：採用率進度條 = AdoptedTotal/TotalTarget（0% 也顯示結構）
- `AdoptedPercent` property：Math.Min(100, AdoptedTotal * 100 / TotalTarget)

### 卡片 3：各階段審題（border-l amber-500）
- 審題階段（PhaseCode 3/5/7）：**母/子題拆分並排**
  - Markup：`母題 [3xl數字]/[總數] · 子題 [3xl數字]/[總數]`（兩組 flex items-baseline gap-1）
  - ReviewedCount/ReviewTotalCount：母題（SubQuestionId IS NULL）已審/應審
  - ReviewedSubCount/ReviewTotalSubCount：子題（SubQuestionId IS NOT NULL）已審/應審
  - **完成判定：DecidedAt IS NOT NULL**
  - **Fallback**：ReviewAssignments 無資料時改用 MT_Questions/MT_SubQuestions 估算（masterReviewed=0, subReviewed=0）
- 非審題階段（ReviewPhaseLabel.None）：虛線卡 + 月亮圖示 + 「本階段審題未啟動」
- 已結案（ReviewPhaseLabel.Closed）：stone 色印章卡 + 「本梯次已結案」 + 結案日
- 右上角 Pill Badge：ReviewPhasePillClass + ReviewPhaseLabel（5 種）
- Footer：PhaseStatusType switch（與卡片 4 Footer 共用同一組 kpi.PhaseStatusType/PhaseStatusText/PhaseDaysRemaining）

### 卡片 4：各修題階段（border-l terracotta 赤陶色）
- 修題階段（PhaseCode 4/6/8）且 RevisionTotalCount > 0：**母/子題拆分並排**（與卡片 3 相同 layout）
  - `RevisedMasterCount / RevisionMasterTotal` · `RevisedSubCount / RevisionSubTotal`
- 修題階段但 `RevisionTotalCount == 0`：虛線卡 + check 圖示 + 「本階段已無待修題目」
- 非修題階段（RevisionPhaseLabel.None）：虛線卡 + 月亮圖示 + 「本階段修題未啟動」
- 已結案（RevisionPhaseLabel.Closed）：stone 色印章卡（與卡片 3 相同樣式）
- 右上角 Pill Badge：RevisionPhasePillClass + RevisionPhaseLabel（5 種）
- Footer：同卡片 3 共用 kpi.PhaseStatusType switch

**兩個 Stage 欄位數值定義不同：**
- `MT_ReviewAssignments.ReviewStage` = 1(互審)/2(專審)/3(總召)
- `MT_RevisionReplies.Stage` = PhaseCode 4/6/8（由 QuestionService.SaveRevisionAsync 寫入）

---

## PhaseStatusType 列舉（Models/DashboardModels.cs）
```
NoProject      → "請選擇梯次"
Preparing      → "命題尚未開始"
InPhase        → "{PhaseName}"（amber 動畫點 + 剩餘天數，負數代表逾期用 terracotta 色）
BetweenPhases  → 最近鄰近階段文字（amber 沙漏 icon）
Closed         → "已結案"
```

## 階段對應表（PhaseCode）
- 1=產學計畫區間（框架，BuildUrgentItemsAsync 中 continue 跳過）
- 2=命題階段, 3=交互審題, 4=互審修題
- 5=專家審題, 6=專審修題, 7=總召審題, 8=總召修題

---

## Service 查詢結構（GetKpiAsync）— 雙波並行版本

### CWT / LCT 雙模式入口
`GetKpiAsync(int projectId, ProjectType projectType)` — `projectType` 由 Razor 端傳入（`CurrentProject?.ProjectType ?? ProjectType.Cwt`），不需額外 DB round-trip。

```csharp
bool isLct = projectType == ProjectType.Lct;
int[] cwtTypeIds = [1, 3, 4, 5];   // CWT 有效題型
int[] lctTypeIds = [6, 7];          // LCT 有效題型
int[] activeTypeIds = isLct ? lctTypeIds : cwtTypeIds;
```

### 修補 B：一次撈 sqlAllPhases，in-memory 推導
`sqlAllPhases`（SELECT 全梯次所有 phase 含 DaysRemaining/DaysToStart/SortOrder），後續 C# 端推導：
- `currentPhaseRecord`：PhaseCode > 1 且 today 在 StartDate~EndDate 區間內，取 SortOrder ASC 首筆
- `neighborPhase`：phaseRow 為 null 時，距離最近的 upcoming/past phase
- `currentPhaseForChart`：StartDate <= today 的最大 PhaseCode

### Stage 1：5 個無相依 SQL 並行（WithOwnConnAsync，各自開獨立 conn）
1. `targetsTask`（CWT: `sqlTargetsCwt` / LCT: `sqlTargetsLct`）→ DashboardTargetBreakdown
2. `statusCountsTask`（CWT: `sqlStatusCountsCwt` / LCT: `sqlStatusCountsLct`）→ StatusCountRow.AdoptedCount
3. `projectStatusTask`（`sqlProjectStatus`）→ ProjectStatusRow（動態計算 0/1/2 + ClosedAt）
4. `allPhasesTask`（`sqlAllPhases`）→ List\<UrgentPhaseRow\>（全部階段）
5. `achievementTask`（CWT: `sqlAchievementCwt` / LCT: `sqlAchievementLct`）→ DashboardAchievementItem

### Stage 3（依賴 currentPhaseForChart）：4 個 SQL 並行（WithOwnConnAsync）
6. `LoadStatusByTypeRowsAsync` → DashboardStatusByTypeItem（圖表 2，4 分支）
7. `GetReviewProgressAsync` → 5-tuple（ReviewPhaseLabel, masterReviewed, masterTotal, subReviewed, subTotal）
8. `GetRevisionProgressAsync` → 5-tuple（RevisionPhaseLabel, masterRevised, masterRevTotal, subRevised, subRevTotal）
9. `BuildUrgentItemsAsync` → List\<DashboardUrgentItem\>

**GetKpiAsync 在 Dashboard.razor 端先呼叫 `PhaseCoordinator.EnsureAsync(projectId)`（60 秒去重），確保審題分配已完成。**

---

## LoadStatusByTypeRowsAsync（圖表 2 分支）

CWT 模式（`LoadStatusByTypeRowsAsync`）依 currentPhaseCode 走 4 分支：
- PC=4/6/8 修題階段：`sqlRevisionBased`，依 MT_RevisionReplies.Content 是否存在（本輪過濾透過 vw_QuestionRoundStartedAt）
- PC=3/5/7 審題階段：`sqlReviewBased`，依 MT_ReviewAssignments.DecidedAt（QuestionStageStatus CTE）
- PC=2 命題階段：`sqlCompositionPhase`，TypeAgg + SubAgg，InProgress=目標缺口
- 其他：`sqlStatusBased`，純 Status 推進

LCT 模式（`LoadStatusByTypeRowsLctAsync`）同樣依 currentPhaseCode 走 3 分支：
- PC=3/5/7 審題：`LoadStatusByTypeRowsLctAuditAsync`（獨立私有方法）
- PC=4/6/8 修題：`LoadStatusByTypeRowsLctRevisionAsync`（獨立私有方法）
- 命題/閒置/結案：inline SQL（LevelBuckets CTE + Type6Counts + Type7Counts + Type7SubCounts）

**所有 CWT 分支皆以 `FROM dbo.MT_QuestionTypes qt` 為主表（形態 B，保留 JOIN）確保無配額題型也顯示 0 值。**

---

## vw_QuestionRoundStartedAt 在 Dashboard 的消費點（5 處）

1. `LoadStatusByTypeRowsAsync` 修題分支 `sqlRevisionBased`
2. `LoadStatusByTypeRowsLctRevisionAsync` 修題分支 `QuestionRevisionStatus` CTE
3. `GetRevisionProgressAsync` 的 `RevisedCheck` CTE EXISTS 子句
4. `BuildUrgentItemsAsync` `sqlRevisionShortage` 修題落後查詢
5. `BuildUrgentItemsAsync` `sqlTypeDetails` 修題明細分支

---

## QuestionTypeCatalog 接入點

**形態 B（主表展開）保留 `FROM dbo.MT_QuestionTypes qt` 的 JOIN 共 6 處**（包含 sqlTargetsCwt、sqlAchievementCwt、LoadStatusByTypeRowsAsync 4 分支）。

**形態 A（補名稱）**：`BuildUrgentItemsAsync` 步驟 6 批次明細 → `_typeCatalog.GetName(r.QuestionTypeId)` 補 TypeName，透過 `BuildDisplayLabel()` 組裝 UI 標籤。

`TeacherTypeDetailRow`（sealed record）無 TypeName 欄位，TypeName 在消費端補。

---

## BuildUrgentItemsAsync 詳細邏輯

### PhaseDeadline 過濾規則
1. PhaseCode == 1 → 跳過
2. phaseCode < currentPhaseCode → 跳過（已過階段）
3. phaseCode == currentPhaseCode → DaysRemaining ≤ 5 才警示
4. phaseCode == currentPhaseCode + 1 → DaysToStart ≤ 5 才警示
5. phaseCode > currentPhaseCode + 1 → 跳過

**階段抑制條件**（suppress = true 則跳過）：
- PhaseCode=2：achievement 全達成（所有 target > 0 的 produced >= target）
- PhaseCode=3~8：對應 Status(3~8) 計數 = 0（sqlStatusCounts2 結果）

### 三種教師落後分支
- **4-a 命題落後**（PhaseCode=2 倒數 ≤ 5 天，`isProposingPhase`）：MT_MemberQuotas + OUTER APPLY 已產出，LCT 加 Level 過濾避免膨脹，HAVING 用 clamped SUM 避免超量掩蓋缺口，TargetUrl：`/overview?creatorId={UserId}`
- **4-b 修題落後**（PhaseCode 4/6/8 倒數 ≤ 5 天，`revisionPhase`）：MT_ReviewAssignments + MT_RevisionReplies 本輪過濾（vw_QuestionRoundStartedAt），TargetUrl：`/overview?creatorId={UserId}`
- **4-c 審題落後**（PhaseCode 3/5/7 倒數 ≤ 5 天，`reviewerPhase`）：MT_ReviewAssignments DecidedAt IS NULL 判斷，TargetUrl：`/reviews`（注意：不是 /overview）

**排序：`top5` 變數名但實際不截斷**，全部警示皆顯示（Severity ASC → DaysRemaining ASC），UI 靠 max-height + scroll 控制。

**批次 TeacherShortage Modal 明細**（步驟 6，避免 N+1）：依當前進行中階段選用 3 種 sqlTypeDetails（修題/審題/命題），批次查 UserId IN @userIds 後 GroupBy 塞回 UrgentItem.TeacherDetails。

---

## BuildDisplayLabel（靜態方法）
集中處理 CWT/LCT 的 UI 顯示標籤：
- Level 有值（LCT）：直接對應「難度一~五」
- Level 為 null 且 TypeName.Contains("題組")（CWT 題組類或 LCT 聽力題組）：Granularity=0 → 「XXX（母題）」；Granularity=1 → 「XXX（子題）」
- 其他：直接回傳 TypeName

---

## CascadingParameter 與載入流程

```
OnParametersSetAsync（偵測 CurrentProject.Id 變更）
└─ LoadKpiAsync()
   ├─ LoadKpiCoreAsync()     → PhaseCoordinator.EnsureAsync → GetKpiAsync → isLoading=false + StateHasChanged + chartsNeedRender=true
   └─ LoadLogsAsync(reset:true) → isLoadingLogs=false + StateHasChanged
   Task.WhenAll 並行（修補 D）

OnAfterRenderAsync（每次 render 後偵測 chartsNeedRender）
└─ RenderChartsAsync()       → Task.WhenAll([renderAchievement, render])（修補 E 並行）
```

- `isLoading = true + StateHasChanged()` 在 LoadKpiAsync 起點立刻呼叫（修補 A），避免 UI 閃現
- `firstRender` 不呼叫 LoadLogsAsync（修補 C），由 OnParametersSetAsync 統一觸發
- CurrentProject 為 null：kpi = new(), chartsNeedRender = false, selectedDetailItem = null, logPage = new AuditLogPage()（空物件，不是 null）

---

## ApexCharts 圖表實作

### 圖表 1：題型缺口達成率（Horizontal Bar）
- DOM id: `chart-achievement`；呼叫：`apexInterop.renderAchievement`
- 資料：`{ typeName, produced, target, fillColor }[]`
- fillColor 判定（ResolveBarColor）：target<=0 或 produced=0 → #E5E7EB；<30% → #D98A6C；<70% → #F59E0B；<100% → #8EAB94；>=100% → #5C8A6A
- 空狀態：`!kpi.AchievementByType.Any(x => x.Target > 0 || x.Produced > 0)` → EmptyState

### 圖表 2：依題型狀態分佈（Vertical Stacked Bar）
- DOM id: `chart-status`；呼叫：`apexInterop.render`（完整 chart2Options JSON）
- 5 個 series：草稿 #9CA3AF / 階段進行中 #F59E0B / 階段完成 #6B8EAD / 已採用 #8EAB94 / 不採用 #991B1B
- **空狀態：`!kpi.StatusByType.Any()`（列表非空即渲染，即使全 0 也顯示空棒）**
  - 原因：SQL 端 JOIN 保證 CWT 6 桶 / LCT 5 桶，新建專案設定目標後立即可見棒結構

### 銷毀
`DisposeAsync()` 呼叫 `apexInterop.destroy` 兩次（chart-achievement / chart-status）

---

## 稽核歷程 LOG 區塊

**僅顯示梯次內活動（試題/審題 CUD），且只顯示 UserId IS NOT NULL（過濾系統批次如審題分配）。**

```sql
WHERE al.ProjectId = @pid
  AND al.Action IN (0, 1, 2)
  AND al.TargetType IN @typeCodes   -- 3=試題, 6=審題
  AND al.UserId IS NOT NULL
```

Filter Chips：All / Question（TargetType=3）/ Review（TargetType=6）；`LogTypeFilter` enum（值：All=0, Question=1, Review=5）

const `LogPageSize = 50`；「載入更多」追加分頁（reset=false）。

### ResolveLogVerb（語意化動詞解析）
Razor 端靜態方法，解析 AuditLog NewValue/OldValue JSON 輸出中文動詞。DB 有兩種 JSON 風格：
- QuestionService/ReviewService：預設 JsonSerializer → PascalCase（Status, Decision, Reason, Stage, ReviewStatus, IsDeleted）
- AnnotationService/RoleService：AuditLogJsonHelper.Serialize → camelCase（kind, targetDisplayName）
四個靜態 helper（TryParseJsonObject、GetStringCI、GetByteCI、GetBoolCI）同時嘗試 Pascal/camelCase。

主要識別規則：
- TargetType=3（試題）：Action=0 → 新增；Action=2 → 刪除；Reason=Revision → 完成修題；IsDeleted=true → 刪除子題；Status 舊→新轉移 → 完成命題/送出審題/編輯
- TargetType=6（審題）：kind=annotation → 審題劃記 CUD；Reason=FinalDecision → 代修並採用/不採用；Decision=1/2/3 → 採用/退回修改/不採用；有 ReviewStatus → 互審意見 CUD

### TargetName 批次解析（ResolveLogTargetNamesAsync）
依 TargetType 分組批次查各資料表取 TargetName；查無時解析 OldValue/NewValue JSON（`ExtractNameFromJson`），同時嘗試 camelCase 與 PascalCase。TargetType=6（審題）JOIN MT_ReviewAssignments 取對應 QuestionCode。

---

## CWT / LCT 雙模式對照

| 項目 | CWT | LCT |
|------|-----|-----|
| activeTypeIds | [1,3,4,5] | [6,7] |
| 卡片 1 目標桶 | 7種題型（閱讀/短文拆母+子） | 難度一~五 5 桶（Level） |
| 卡片 2 採用計算 | master+sub Status IN(9,12) | 聽力測驗 master + 聽力題組 all-or-nothing |
| 圖表 2 X 軸 | 6 桶（4 CWT 題型，閱讀/短文各拆母+子） | 難度一~五 5 桶（聽力題組母+子另外） |
| 逾期待辦命題落後 | MT_MemberQuotas typeIds=[1,3,4,5] | MT_MemberQuotas typeIds=[6,7] + Level 過濾 |

---

## 三檔案公開型別清單（DashboardModels.cs）
- `PhaseStatusType`（enum）
- `RevisionPhaseLabel`（enum）
- `ReviewPhaseLabel`（enum）
- `LogTypeFilter`（enum，值：All=0, Question=1, Review=5）
- `UrgentSeverity`（enum）
- `UrgentSourceType`（enum）
- `DashboardKpiDto`（主 KPI DTO，含 AdoptedTotal/AdoptedPercent computed properties）
- `AuditLogQuery`（LOG 查詢條件）
- `AuditLogPage`（LOG 分頁回傳，含 Logs + TotalCount + HasMore）
- `RecentAuditLog`（LOG 單筆，含 OldValue/NewValue 欄位）
- `DashboardUrgentItem`（含 TeacherDetails List）
- `UrgentTeacherDetail`（含 QuestionTypeId/Granularity/Level/TypeName/Assigned/Produced/Achievement）
- `DashboardTargetBreakdown`（含 Granularity/DisplayLabel/Produced）
- `DashboardAchievementItem`（含 Granularity/Level/DisplayLabel）
- `DashboardStatusByTypeItem`（含 Granularity/Level/DisplayLabel/5個計數欄位）

## Service 內部私有型別（DashboardService.cs）
- `PhaseStatusCounts`：各 Status(3~8) 計數
- `UrgentPhaseRow`：含 PhaseCode/PhaseName/StartDate/EndDate/DaysRemaining/DaysToStart/SortOrder
- `TeacherShortageRow`：sealed record(UserId, TeacherName, TotalAssigned, TotalProduced)
- `TeacherTypeDetailRow`：sealed record(UserId, QuestionTypeId, Granularity, Level, Assigned, Produced)
- `StatusCountRow`：AdoptedCount
- `ReviewProgressRow`：MasterReviewed/MasterTotal/SubReviewed/SubTotal
- `RevisionProgressRow`：MasterRevised/MasterTotal/SubRevised/SubTotal
- `ProjectStatusRow`：Status/ClosedAt
- `PhaseRow`：PhaseName/DaysLeft
- `NeighborPhase`：PhaseCode/PhaseName/StartDate/EndDate/IsUpcoming

## 與其他模組的關聯
- 命題/修題落後 TargetUrl：`/overview?creatorId={UserId}`
- 審題落後 TargetUrl：`/reviews`（不是 /overview）
- PhaseDeadline TargetUrl：`/cwt-list?tab=compose`（2）/ `/cwt-list?tab=revision`（4/6/8）/ `/reviews?tab=review`（3/5/7）
- 梯次階段轉換：`IPhaseTransitionCoordinator.EnsureAsync`（DI 注入 Dashboard.razor），60 秒去重
- 梯次切換：CascadingParameter "CurrentProject"（ProjectSwitcherItem?）
