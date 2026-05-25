---
name: Overview 三檔案行數與結構速查
description: Overview.razor / OverviewService.cs / OverviewModels.cs 行數與職責分布（2026-05-25 盤點）
type: project
---

三檔行數（2026-05-25 盤點）：
- `Components/Pages/Overview.razor` — **1216 行**（UI + code-behind 邏輯）
- `Services/OverviewService.cs` — **633 行**（IOverviewService + OverviewService 實作）
- `Models/OverviewModels.cs` — **167 行**（OverviewFilter / OverviewStatusKey / OverviewCreatorOption / OverviewListResult / OverviewAnnotationCard）

**Why**：當前是「優化與除錯」階段，三檔仍守住三檔案原則。Overview.razor 雖 1216 行偏大但無重複片段（含 7 種題型預覽 switch + ResolveDisplayStatus 與 PhaseProgressStepper 共用），不必硬拆。

**How to apply**：新增功能前先評估「該邏輯屬於 Razor / Service / Model 哪一層」；若 Service 自寫 SQL 超過 50 行考慮抽 helper（已存 BuildOverviewCountsAsync / GetPendingRevisionCountAsync / GetAllReviewersRespondedAsync 三個私有 helper）；不要為了拆檔而新增第四檔。
