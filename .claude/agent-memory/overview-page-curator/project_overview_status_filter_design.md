---
name: Overview 狀態篩選下拉設計（與 Badge 對齊）
description: Overview 篩選下拉不直接用 QuestionStatus.Labels 而改用 OverviewStatusKey 識別碼，因為列表 Badge 經 ResolveDisplayStatus 重寫
type: project
---

Overview 頁面的「狀態篩選」下拉選單**不能直接拿 `QuestionStatus.Labels`**（13 個原始 byte 狀態），改用 `Models/OverviewModels.cs::OverviewStatusKey` 定義的 11 個語意識別碼（草稿/完成/未完成命題/待審/已給意見/修題中/修題已送出/OO 完成/採用/不採用/命題刪除）。

**Why**：列表「當前狀態」Badge 經過 `Overview.razor::ResolveDisplayStatus` 4 維度（Status × PhaseCode × HasRepliedThisStage × AllReviewersResponded）重寫——直接放原始 13 個 byte label 會讓使用者選了「已送審」「互審中」「專審中」「總審中」後，列表卻沒有任何 Badge 對應（被 R2 改成「待審/已給意見」）。

**How to apply**：篩選下拉選項翻譯為 SQL 條件的工作落在 `OverviewService.TranslateStatusKey`：返回 `(StatusesOverride, HasReplied, deletedOnly, postFilter)`。簡單識別碼（completed/adopted/not-adopted/revision-submitted）走純後端 IN 條件；需要 PhaseCode/AllReviewersResponded 4 維度判斷的識別碼（draft/failed-composition/awaiting-review/reviewed/in-revision/awaiting-next/deleted）採「後端粗篩 + 前端 in-memory 精篩」混合策略——關 server-side 分頁、PageSize 拉到 10000、抓回後用與 ResolveDisplayStatus 同邏輯的 lambda 過濾再重新分頁。

**StatusCounts 不受影響**：四張統計卡片仍由 `GetStatusCountsAsync` 算原始 byte 統計，篩選只影響列表本身。

**Plan 020（2026-05-07）動態下拉**：下拉只渲染梯次內實際存在的 key，由 `OverviewListResult.StatusKeyCounts` 提供（忽略所有 filter，以梯次全題為基底）。為避免分桶規則三邊雙寫，把 razor `ResolveDisplayStatus` 各條件抽成 `OverviewService.Match*` 靜態函式，razor + `TranslateStatusKey.postFilter` + `BuildStatusKeyCountsAsync` 三方共用。`Overview.razor::LoadAsync` 結尾加守門：當前 `queryStatus` 不在 `StatusKeyCounts` 時自動清空為「所有狀態」並重載。
