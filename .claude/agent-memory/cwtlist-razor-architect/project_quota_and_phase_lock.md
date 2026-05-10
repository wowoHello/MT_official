---
name: 配額進度卡片與命題階段鎖死（Plan_008）
description: 配額卡片資料來源、達標標示、命題階段結束後的全頁鎖死行為
type: project
---

**配額卡片資料來源（`GetMyQuotaProgressAsync`）：**
SQL JOIN 三張表：`MT_MemberQuotas` mq INNER `MT_ProjectMembers` pm + `MT_QuestionTypes` qt LEFT `MT_Questions` q（`Status >= 1 AND IsDeleted = 0`）→ GROUP BY 題型 → 回傳 `QuotaProgressItem { QuestionTypeId, TypeName, Target, Completed }`。

**重要：「Completed」計算包含 Status≥1 全部**，亦即命題完成 + 已送審 + 各審題階段中 + 修題中 + 採用 + 不採用 + 結案。草稿 (Status=0) 不計入。

**Why:** 一旦離開草稿階段，題目就「貢獻給配額」，避免命題教師退回再修還要重複算配額。

**配額卡片 UI：**
- 7 張橫向 grid（grid-cols-2 sm:4 lg:7）
- 每張 = 題型名 + 「current / target」+ 進度條（gray→morandi→sage 依 0%/<100%/≥100%）
- 達標時右上 `已達標` badge（sage 系），仍可繼續超量命題
- 命題階段結束 (`IsCompositionPhaseClosed`) 時整張卡片變灰且 disabled
- 點卡片直接 `OpenComposeModal(typeKey)` 帶該題型開新題

**Plan_008 命題階段結束鎖死（核心邏輯 in CwtList.razor）：**
`compositionPhaseEndDate = MT_ProjectPhases.EndDate WHERE PhaseCode = 2`。
`IsCompositionPhaseClosed = endDate.HasValue && endDate.Value.Date < DateTime.Today`。

當 `IsCompositionPhaseClosed = true`：
1. 「新增試題」按鈕 disabled
2. 配額卡片所有「點擊新增」disabled
3. 配額進度區顯示紅色 lock 提示「命題階段已於 yyyy-MM-dd 結束，配額不再變動」
4. `IsEditableStatus(Draft|Completed)` 強制 false（草稿 / 命題完成都變唯讀）
5. 列表中草稿的「刪除」按鈕隱藏

**Server-side 同樣防呆：**
`UpdateAsync` / `SoftDeleteAsync` 在 transaction 內呼叫 `IsCompositionPhaseClosedInTxAsync` 守門。

**配額為 0 的攔截：**
`LoadPageDataAsync` 回傳空 `quotaProgress` 時設 `needNoPermissionRedirect=true`，等 `OnAfterRenderAsync(firstRender)` 後彈 SweetAlert 警告「此身分在該專案沒有命題任務」並導回首頁，避免 prerender 階段呼叫 JS。

**How to apply:**
- 動到階段轉換邏輯前先看 `EnsureCompositionPhaseClosedAsync` 與 `EnsurePhaseTransitionAsync`（Idempotent，可重複呼叫）。
- `loadedProjectId` 守門避免父層 ProjectSwitcher 重渲染時誤重載配額（會閃骨架）。
