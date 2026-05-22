---
name: Overview 頁面版面與資料錨點
description: Overview.razor 統計卡、表格、詳情面板等 UI 區塊的真實組成與資料來源；ResolveDisplayStatus 含母題結案 fallback (2026-05-19)
type: project
---

**統計卡（5 張，grid 2 / md 4 / lg 5）**：
1. 總題數 — `result.StatusCounts` 全部加總
2. 命題進行中 — Draft + Completed
3. 已採用 — Adopted + Archived（含結案入庫）
4. 待修編 — `result.PendingRevisionCount`（不是直接 SumCounts；扣除「已送出」者）
5. 各審查環節（互審/專審/總審 三格）— `ReviewBucket(targetPhase)`，**只有當 `result.CurrentPhaseCode == targetPhase` 才計算**，否則回 -1 灰掉顯示 0；範圍與 `EnsurePhaseTransitionAsync` 各 case 的 fromStatuses 對齊（互審只算 2/3；專審 2/3/4/5；總審 2/3/4/5/6/7）。

**Why**：避免「coordinator 60 秒去重期內未跑」「邊界殘留」等情境少算；非當前階段的卡格灰掉提示使用者「現在不在這個審題階段」。

**列表表格欄位**（`grid-cols-12 gap-4 min-w-[1080px]` 水平捲動）：
- col-span-2：題號 + 更新時間 + 子題 badge（子題列縮排 + 樹枝符號 + `題號-NN` 後綴）
- col-span-1：命題教師（含 truncate + title 提示）
- col-span-1：題型 badge
- col-span-1：等級 / 難度（垂直堆疊兩個 badge）
- col-span-6：`<PhaseProgressStepper>` 7 階段球（min-w-[480px]），傳入 Status + CurrentPhaseCode + HasRepliedThisStage + AllReviewersResponded 共 4 參數
- col-span-1：當前狀態 Badge（IsDeleted 走「命題刪除」+ 復原按鈕；否則 `ResolveDisplayStatus` 五條規則 + 母題結案 fallback）

**詳情 SlideOver**（CustomModal Type=SlideOver max-w-3xl）：
- **上半「題目內容」**先呈現（規格 US-006 強制順序）— 以題型 switch 走 `Components/Shared/QuestionPreviews/*` 七種預覽元件，標籤（主類/次類/文體/核心能力…）由 `OverviewService.BuildPreviewTags` 統一組裝。
- **下半「審題意見歷程」**用 `<ReviewHistoryTimeline Entries="detailHistory" ShowActorName="true">`（管理員視角不匿名，明示真實姓名）。
- ESC 鍵透過 razor 端 `HandleKeyDown` 關閉（CustomModal 沒內建鍵盤關閉）。
- 並行載入：`GetDetailAsync` + `GetReviewHistoryAsync` `Task.WhenAll`。

**ResolveDisplayStatus 規則優先順序（命中即回，razor 端與 OverviewService.Match* 同源）**：
- **母題結案 fallback**：父母題 Status=11 ClosedNotAdopted 時，子題列強制顯示「結案不採用」（不改子題實際 Status，只覆寫顯示文字）。透過 `parentStatusForFallback` 參數傳入，razor 端用 `parentStatusById` dictionary 預先建立母題 Status 索引
- **R1**：`Draft` + PhaseCode ≥ 3 → 「未完成命題」（danger 紅）
- **A**：Status ∈ {4,6,8} 但 PhaseCode 落後 → 「OO 完成」莫蘭迪藍 inline badge（IsAwaitingNext=true，與「修題已送出」綠色語意區隔）
- **B**：Status ∈ {4,6,8} + 本人已送出修題說明 → 「修題已送出」（success 綠）
- **R2**：PhaseCode ∈ {3,5,7} + Status ∈ {2,3,5,7} → 依 `AllReviewersResponded`（已傳入 bool）切「已給意見」(success) 或「待審」(warning)
- **C**：fallback 回 `QuestionStatus.Labels`

**allResponded 在 foreach 頂端先算好**：
```razor
var allResponded = result.AllReviewersResponded.GetValueOrDefault((item.Id, item.SubQuestionId));
// stepper 與 ResolveDisplayStatus 共用，避免重複查 dict
```

**PhaseTransitionCoordinator 串接**：僅由 `OverviewService.LoadAsync` 開頭呼叫一次 `IPhaseTransitionCoordinator.EnsureAsync(projectId)`（razor 端原本的呼叫已於「修補 B」刪除，避免雙呼叫贅餘 round-trip；Coordinator 自身有 60 秒去重保護），確保使用者只進 Overview 不進 CwtList/Reviews 時題目仍能從 FinalReviewing(7) 推進。

**Dashboard 教師跳轉**：路由帶 query `?creatorId=X`，Overview 透過 `[SupplyParameterFromQuery] CreatorId` 接收，`OnParametersSetAsync` 比對 `creatorOptions` 後自動套 `filter.CreatorId` 與 `queryCreatorId`。
