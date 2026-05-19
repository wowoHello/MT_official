---
name: OverviewService 依賴注入與跨 Service 委派
description: OverviewService 主建構子注入的 5 個依賴與委派職責拆分；GetAllReviewersRespondedAsync 改 per-row DecidedAt (2026-05-19)
type: project
---

`OverviewService` 主建構子注入五個依賴（primary constructor 語法）：
1. **IQuestionService** — 題目查詢主力（`ListAsync` / `GetStatusCountsAsync` / `GetByIdAsync` / `RestoreAsync` / `GetCurrentPhaseAsync`）
2. **IReviewService** — 委派 `GetHistoryByQuestionIdAsync` 取審題歷程（管理員視角不匿名）
3. **IDatabaseService** — 自寫 SQL 用（`GetAllReviewersRespondedAsync` / `GetPendingRevisionCountAsync` / `BuildOverviewCountsAsync` / `GetCreatorOptionsAsync`）
4. **IPhaseTransitionCoordinator** — 階段轉換統一委派（60 秒去重 + 統一 logging），`LoadAsync` 進入點呼叫 `EnsureAsync(projectId)`
5. **IAnnotationService** — 委派 `GetByQuestionUnitAsync` 取得審題者劃記，再用 `AnnotationFieldLabel.Describe` 翻譯欄位名稱

**Why**：不直接在 Razor 端 inject `IQuestionService`、`IReviewService`、`IAnnotationService`，目的是讓 Overview.razor 只認 `IOverviewService` 一個面，三檔案原則的紀律維持。`PhaseTransitionCoordinator` 在 Razor 端 `OnParametersSetAsync` 與 Service 端 `LoadAsync` 都呼叫一次（雙保險），確保使用者只進 Overview 不進 CwtList/Reviews 時題目不會卡在 FinalReviewing(7)。

**自寫 SQL 的方法**：
- `GetAllReviewersRespondedAsync(projectId, phaseCode, questionIds)` —
  **2026-05-19 重構**：回傳型別改為 `Dictionary<(int QuestionId, int? SubQuestionId), bool>`
  SQL: `SELECT DISTINCT ra.QuestionId, ra.SubQuestionId FROM MT_ReviewAssignments WHERE ProjectId=@P AND ReviewStage=@S AND QuestionId IN @Ids AND DecidedAt IS NOT NULL`
  改為 **row-level DecidedAt 檢查**（不再 GROUP BY 推導「被指派筆數 = 有效 Comment 筆數」）；母題單元 key=(Id, null) 與子題單元 key=(Id, subId) 各自獨立判定 done
- `GetPendingRevisionCountAsync(projectId, phaseCode)` — 待修編真實數量（扣除已送出），只對 PhaseCode ∈ {4,6,8} 有效；UNION ALL（母題列 + 子題列）後 COUNT
- `BuildOverviewCountsAsync(projectId, phaseCode)` — 計算梯次內每個 OverviewStatusKey 對應的題數（含子題拆列），忽略所有 filter，給狀態下拉動態渲染
- `GetCreatorOptionsAsync(projectId)` — 列此專案有命題紀錄的老師（含已刪題目）

**並行載入優化**：`LoadAsync` 內 `Task.WhenAll(countsTask, listTask, pendingTask)` 同步發 3 個查詢；Razor 端 `OpenDetailAsync` 也用 `Task.WhenAll(dataTask, historyTask)` 並行。

**How to apply**：要在 Overview 加新功能時，若是純 UI 狀態 → 改 razor；若需 DB 查詢 → 優先 IQuestionService 既有方法；只有跨表 join 不易塞回 QuestionService 才在 OverviewService 自寫 SQL（並寫進 IOverviewService 介面）。
