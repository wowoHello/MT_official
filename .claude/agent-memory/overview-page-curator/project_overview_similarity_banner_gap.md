---
name: Overview 相似題 Banner 整合缺口
description: Plan_SimilarityAnalysis_2026-05-19 規劃 Overview 詳情 SlideOver 顯示相似題區塊，但實作尚未落地 — 標示為待辦缺口
type: project
---

`Plan_SimilarityAnalysis_2026-05-19.md` 第 5.5 章「Overview.razor 詳情 SlideOver 加相似題區塊（複用 `ReviewSimilarityBanner.razor`）」屬於規劃中項目，**截至 2026-05-20 仍未實作**。

**目前 Overview.razor 詳情 SlideOver 三段結構**：
1. 上半：題目內容（`<RenderPreview>` 走 7 種 PreviewXxx 元件）
2. 中段：劃記評語卡片（`detailAnnotations` foreach）
3. 下半：審題意見歷程（`<ReviewHistoryTimeline>` ShowActorName=true）

`<ReviewSimilarityBanner>` 元件已存在於 `Components/Shared/ReviewForms/`（審題端 ReviewActionPanel 在用），但 Overview.razor 沒有引用、`OverviewService` 沒有讀 MT_SimilarityChecks 的方法、`OverviewModels.cs` 也沒對應 DTO。

**Why**：相似度分析 Plan 第 3 章「不動既有資產複用」清單已標示 ReviewSimilarityBanner.razor 為三方共用（命題端、審題端、管理員端），管理員端即指 Overview，但實作分階段未排到（Phase 4 管理員批次掃描頁排在 Overview 詳情整合之後）。

**How to apply**：
- 未來規劃 Overview 改動若涉及「相似度」「重複題」相關需求，先檢查 Plan_SimilarityAnalysis 的階段進度，再決定要在 Overview 加 Banner 還是另起 SimilarityAnalysis 頁。
- 若要實作，預估改動範圍：`OverviewService` 注入 `ISimilarityService` + 在 `GetDetailAsync` 後並行 `GetSimilarityResultsAsync(questionId)`；Razor 端在「下半審題意見歷程」之前插一段 `<ReviewSimilarityBanner Entries="..." />`。
- 該功能屬「管理員視角」，外部教師不在此頁，不需做匿名處理。
- 數值門檻：≥ 60 顯示橘色警示、≥ 80 顯示紅色重複（與 Plan §2.3 一致）。
