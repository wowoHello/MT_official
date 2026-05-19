---
name: Overview 狀態篩選下拉設計（與 Badge 對齊）
description: Overview 篩選下拉不直接用 QuestionStatus.Labels 而改用 OverviewStatusKey 識別碼，因為列表 Badge 經 ResolveDisplayStatus 重寫；AllReviewersResponded 改 per-unit dict (2026-05-19)
type: project
---

Overview 頁面的「狀態篩選」下拉選單**不能直接拿 `QuestionStatus.Labels`**（13 個原始 byte 狀態），改用 `Models/OverviewModels.cs::OverviewStatusKey` 定義的 11 個語意識別碼，分 5 組：
- 命題：draft / completed / failed-composition
- 審題：awaiting-review / reviewed
- 修題：in-revision / revision-submitted / awaiting-next
- 結果：adopted / not-adopted
- 其他：deleted

**Why**：列表「當前狀態」Badge 經過 `Overview.razor::ResolveDisplayStatus` 4 維度（Status × PhaseCode × HasRepliedThisStage × AllReviewersResponded）重寫——直接放原始 13 個 byte label 會讓使用者選了「已送審」「互審中」「專審中」「總審中」後，列表卻沒有任何 Badge 對應（被 R2 改成「待審/已給意見」）。

**How to apply**：篩選下拉選項翻譯為 SQL 條件的工作落在 `OverviewService.TranslateStatusKey`：返回 `(StatusesOverride, HasReplied, deletedOnly, postFilter)`。簡單識別碼（completed/adopted/not-adopted/revision-submitted）走純後端 IN 條件；需要 PhaseCode/AllReviewersResponded 4 維度判斷的識別碼（draft/failed-composition/awaiting-review/reviewed/in-revision/awaiting-next/deleted）採「後端粗篩 + 前端 in-memory 精篩」混合策略——關 server-side 分頁、PageSize 拉到 10000、抓回後用 `Match*` 條件函式（MatchDraft / MatchFailedComposition / MatchAwaitingReview / MatchReviewed / MatchInRevision / MatchAwaitingNext）過濾再重新分頁。

**AllReviewersResponded 改用 unit-level dict（2026-05-19）**：
- `Dictionary<(int QuestionId, int? SubQuestionId), bool>` 取代舊版 `Dictionary<int, bool>`
- 母題單元 key = `(Id, null)`；子題單元 key = `(Id, SubQuestionId)`
- 判定改為 per-row `DecidedAt IS NOT NULL`（不再 GROUP BY 算「被指派筆數 = 有效 Comment 筆數」）
- `MatchAwaitingReview` / `MatchReviewed` 函式以 `(i.Id, i.SubQuestionId)` lookup 做 unified 過濾
- razor 端 foreach 在進入 stepper / Badge 之前先算 `var allResponded = result.AllReviewersResponded.GetValueOrDefault((item.Id, item.SubQuestionId));`，避免重複查 dict

**動態下拉**：下拉只渲染梯次內實際存在的 key，由 `OverviewListResult.StatusKeyCounts` 提供（忽略所有 filter，以梯次全題為基底）。razor `ResolveDisplayStatus` 各條件已抽成 `OverviewService.Match*` 靜態函式，razor + `TranslateStatusKey.postFilter` + `BuildStatusKeyCountsAsync` 三方共用。`Overview.razor::LoadAsync` 結尾有守門：當前 `queryStatus` 不在 `StatusKeyCounts` 時自動清空為「所有狀態」並重載。

**StatusCounts 不受影響**：底下 4 張統計卡（總題數/命題進行中/已採用/待修編）由 `IQuestionService.GetStatusCountsAsync` 算原始 byte 統計，篩選只影響列表本身。
