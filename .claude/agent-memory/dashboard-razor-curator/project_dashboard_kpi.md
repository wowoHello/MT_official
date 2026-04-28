---
name: Dashboard KPI 實作模式
description: 4 張 KPI 卡片的資料來源、QuestionStatus 碼對應、DashboardService 查詢結構（含 PhaseStatusType 列舉）
type: project
---

Dashboard KPI 卡片（2026-04-28 初版；2026-04-28 重構卡片 UI + 修正 PhaseStatus 邏輯）。

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

## 三檔案清單
- Models/DashboardModels.cs（PhaseStatusType enum + DashboardKpiDto + DashboardTargetBreakdown）
- Services/DashboardService.cs（IDashboardService + DashboardService + ResolvePhaseStatus）
- Components/Pages/Dashboard.razor（UI + @code，圖表區 / LOG 區不動）
