---
name: Dashboard KPI 與圖表實作模式
description: 4 張 KPI 卡片 + 2 張 ApexCharts 圖表的資料來源、Status 碼對應、Service 查詢結構、JS Interop 模式
type: project
---

Dashboard KPI 卡片（2026-04-28 初版；2026-04-28 重構卡片 UI + 修正 PhaseStatus 邏輯；2026-04-28 新增 ApexCharts 圖表）。

**Why:** US-004 要求儀表板顯示當前梯次的命題進度統計，且資料需隨梯次切換動態更新。

**How to apply:** 未來修改或擴充 KPI 卡片時，直接參照以下對應關係，不需重查 db.md。

## Status 碼對應（MT_Questions.Status）
- 0=命題草稿, 1=命題完成, 2=命題送審
- 3=互審中(鎖), 4=互審修題中, 5=專審中(鎖), 6=專審修題中, 7=總審中(鎖), 8=總審修題中
- 9=採用, 10=不採用, 11=改後再審, 12=結案未採用

## MT_Projects.Status 對應（梯次生命週期）
- 0=準備中（命題尚未開始）
- 1=進行中
- 2=已結案

## 4 張卡片資料邏輯
- 卡片1 總目標題數：SUM(MT_ProjectTargets.TargetCount) WHERE ProjectId=@pid，含 7 種題型明細（即使全 0 也顯示完整列表）
- 卡片2 採用：COUNT WHERE Status=9 AND IsDeleted=0；進度條 = AdoptedCount/TotalTarget*100（0% 也顯示結構）
- 卡片3 審修中：COUNT WHERE Status IN (2..8)；Footer 依 PhaseStatusType 列舉切換（見下方）
- 卡片4 退回修題：Status IN (4,6,8) 合計；3 欄 grid 顯示 PeerEdit/ExpertEdit/FinalEdit，全 0 也顯示

## PhaseStatusType 列舉（Models/DashboardModels.cs）
```
NoProject      → "請選擇梯次"     （灰色，CurrentProject=null 時在 LoadKpiAsync 不呼叫 Service，kpi 維持預設）
Preparing      → "命題尚未開始"   （灰色，MT_Projects.Status=0）
InPhase        → "{PhaseName}"    （amber 動畫點 + 剩餘天數）
BetweenPhases  → "階段銜接中"     （amber 沙漏 icon）
Closed         → "已結案"         （灰色，MT_Projects.Status=2）
```
注意：PhaseStatusType.NoProject 是預設值，但實際上 CurrentProject=null 時不呼叫 GetKpiAsync，
kpi 回到 new DashboardKpiDto()，PhaseStatusText 維持預設 "請選擇梯次"。
梯次已選但未進入任何 Phase 區間（Status=1 + 無符合日期）→ BetweenPhases，修正舊版誤顯示「請選擇梯次」的 bug。

## Service 查詢結構
- 方法：IDashboardService.GetKpiAsync(int projectId) → DashboardKpiDto
- 三段 SQL：targets → statusCounts → projectStatus + currentPhase
- ResolvePhaseStatus(int? projectStatus, PhaseRow? phaseRow) 私有靜態方法，純運算，不查 DB
- 內部型別：StatusCountRow / PhaseRow 為 private sealed class

## CascadingParameter 模式
- [CascadingParameter(Name = "CurrentProject")] private ProjectSwitcherItem? CurrentProject
- OnParametersSetAsync 內用 previousProjectId 比對偵測梯次切換，觸發 LoadKpiAsync()
- CurrentProject 為 null 時直接 return new DashboardKpiDto()，不呼叫 Service

## 卡片 UI 統一結構（2026-04-28 重構）
```
<div class="flex flex-col min-h-[200px] border-l-[3px] border-l-{color}">
    Header: 圓角方形徽章 + 標題（8x8 shrink-0）
    Main:   text-4xl font-black tabular-nums（解決數字寬度抖動）
    Footer: mt-auto pt-3 border-t（永遠有內容，0 值也不留白）
</div>
```

## ApexCharts 圖表實作模式（2026-04-28 新增）

### JS Interop 架構
- 入口：`wwwroot/js/apex-interop.js`（render/update/destroy 三方法）
- App.razor 引入順序：apexcharts.min.js → apex-interop.js（apexcharts 必須先載入）
- Dashboard.razor 新增：`@inject IJSRuntime JS` + `@implements IAsyncDisposable`

### 圖表 1：題型缺口達成率（Horizontal Bar + Goals）
- DOM id: `chart-achievement`；lg:col-span-1；h-[350px]
- 資料來源：DashboardKpiDto.AchievementByType（DashboardAchievementItem 清單，7 筆）
- Produced = MT_Questions.Status BETWEEN 2 AND 9（送審後計入，不含草稿 0,1）
- Target = MT_ProjectTargets.TargetCount
- 條形顏色依達成率：<30% → #D98A6C（terracotta），30-70% → #F59E0B（amber），>=70% → #8EAB94（sage）
- goals 標記：strokeDashArray=2 虛線，strokeColor=#374151，每筆資料點帶 goals[]

### 圖表 2：依題型狀態分佈（Vertical Stacked Bar）
- DOM id: `chart-status`；lg:col-span-2；h-[350px]
- 資料來源：DashboardKpiDto.StatusByType（DashboardStatusByTypeItem 清單，7 筆）
- 5 個系列：命題中(0,1)#9CA3AF / 審修中(2,3,5,7)#6B8EAD / 退回修題(4,6,8)#D98A6C / 已採用(9)#8EAB94 / 不採用(10)#991B1B
- stacked: true，borderRadiusApplication: "end"（只有最頂層有圓角）

### 渲染時序
- chartsNeedRender 旗標：LoadKpiAsync 成功後設 true
- OnAfterRenderAsync 偵測旗標 → 呼叫 RenderChartsAsync()
- 無資料時 DOM 元素不存在（顯示 EmptyState），JS render 安全跳過

### 銷毀時機
- DisposeAsync() 呼叫 apexInterop.destroy 兩次
- 梯次切換 → LoadKpiAsync → chartsNeedRender=true → OnAfterRenderAsync → render() 內部先 destroy 再重建

### SQL 查詢新增
- sqlAchievement：LEFT JOIN MT_Questions + 子查詢 MT_ProjectTargets，確保 7 筆（全 0 時也回傳）
- sqlStatusByType：LEFT JOIN MT_Questions，CASE WHEN 分 5 個狀態桶

## 三檔案清單（含圖表 + 緊急待辦 + 稽核歷程）
- Models/DashboardModels.cs（PhaseStatusType + DashboardKpiDto + Breakdown + AchievementItem + StatusByTypeItem + UrgentSeverity + UrgentSourceType + DashboardUrgentItem + UrgentTeacherDetail + RecentAuditLog）
- Services/DashboardService.cs（IDashboardService + DashboardService + BuildUrgentItemsAsync + GetRecentAuditLogsAsync + ResolvePhaseStatus + 所有 SQL + 內部型別）
- Components/Pages/Dashboard.razor（4 KPI + 2 圖表 + 緊急待辦 Top 5 + LOG 列表）
- wwwroot/js/apex-interop.js（非 Dashboard 專屬，共用 JS Interop 工具）

## 緊急待辦 Top 5 實作模式（v2 重構 2026-04-29）

### enum 改名：TypeShortage → TeacherShortage

### 兩種訊號
- PhaseDeadline：查所有 MT_ProjectPhases，依「當前 PhaseCode」過濾
  - 取得 currentPhaseCode：SELECT TOP 1 PhaseCode WHERE StartDate <= TODAY ORDER BY PhaseCode DESC
  - 規則：PhaseCode < currentPhase → 不警示；== currentPhase → 依 EndDate 計算倒數；== currentPhase+1 且 DaysToStart≤5 → Notice；更遠 → 不警示
  - 即將開始（DaysToStart>0）文案："{PhaseName}預計 {N} 天後開始"
  - 既有抑制邏輯（Phase 100% 完成）保留
- TeacherShortage（取代舊 TypeShortage）：
  - 只在當前/下一階段為命題修題階段（IsCommandingPhase: PhaseCode ∈ {1,3,5,7}）時觸發
  - GROUP BY 教師 SQL，達成率 < 70% 才列入；Critical<30% / Warning30-49% / Notice50-69%
  - DashboardUrgentItem 新增 int? UserId（TeacherShortage 用），移除 QuestionTypeId
  - TargetUrl = $"/overview?creatorId={UserId}"

### 批次查詢教師×題型明細
- 對 Top 5 中所有 TeacherShortage 的 UserIds 批次查
- UrgentTeacherDetail 改為：QuestionTypeId / TypeName / Assigned / Produced / Achievement
  （移除 UserId、TeacherName，教師名稱在 DashboardUrgentItem.Title 中）

### 排序：(int)Severity ASC → DaysRemaining ASC → Take(5)

### Razor 展開區塊（TeacherShortage）
- 展開顯示「該教師各題型進度」（TypeName + Produced/Assigned + Achievement:P0）
- 底部「查看」NavLink → /overview?creatorId={item.UserId}
- 圖示：Critical → fa-user-xmark；其他 → fa-user-clock

### Overview.razor 配合改動
- 新增 [SupplyParameterFromQuery] int? CreatorId
- OnParametersSetAsync 在重抓 creatorOptions 後，若 CreatorId 存在於 creatorOptions（比對 o.Id，非 o.UserId）則自動套用篩選
- 不存在則重置（維持切換梯次重置篩選的原有行為）

### 嚴重度色彩（Tailwind，不變）
- Critical：bg-rose-50 / bg-terracotta dot / text-terracotta
- Warning：bg-amber-50 / bg-amber-500 dot / text-amber-700
- Notice：bg-stone-50 / bg-morandi dot / text-morandi

### 達成率字色（AchClass，不變）
- ≥1.0 → text-sage；≥0.7 → text-slate-500；≥0.3 → text-amber-700；其他 → text-terracotta

## 稽核歷程 LOG 區塊（2026-04-29 實作）

### DTO：RecentAuditLog（Models/DashboardModels.cs）
- 欄位：Id / UserId / UserName / Action(byte) / TargetType(byte) / TargetId / TargetName / CreatedAt
- UserName：ISNULL(u.DisplayName, '系統')；TargetName：批次解析或「已刪除」

### Service：GetRecentAuditLogsAsync（DashboardService.cs，private static）
- Step1：主查詢 MT_AuditLogs LEFT JOIN MT_Users，TOP @top ORDER BY CreatedAt DESC
- Step2：GroupBy(TargetType) 批次 SQL，用 IN @ids 一次查完
- TargetType 對應：0=MT_Users/DisplayName, 1=MT_Roles/Name, 2=MT_Projects/Name, 3=MT_Questions/QuestionCode, 4=MT_Announcements/Title, 5=MT_Teachers JOIN MT_Users/DisplayName, 6=直接設"#{TargetId}"
- Fallback：nameMap 查不到 → "已刪除"

### Razor UI（h-[300px]，overflow-y-auto 捲動）
- 每列：圓角圖示(7x7) + 描述文字(truncate + title tooltip) + 相對時間
- 圖示色：建立=bg-emerald-50 text-sage；修改=bg-blue-50 text-morandi；刪除=bg-rose-50 text-terracotta
- 描述格式：「{UserName}」粗體 + 動詞 + 類型 + 「{TargetName}」
- 相對時間：<60s=剛剛 / <60m=N分鐘前 / <24h=N小時前 / <7d=N天前 / else=MM/dd HH:mm
- EmptyState 文案：「目前沒有近期紀錄」/ 「梯次中的增刪改動作將顯示於此」
- Hover：bg-stone-50 + cursor-pointer（Phase 2 再加 Modal）
