---
name: 配額進度卡片與命題階段鎖死（Plan_008 + 雙模式擴充）
description: 配額卡片資料來源（已支援母/子拆分 + LCT 難度）、達標標示、命題階段結束後的全頁鎖死行為
type: project
---

**配額卡片資料來源（`GetMyQuotaProgressAsync`，2026-05-22 已重寫）：**

**三段 QueryMultiple SQL**（取代舊版單一 LEFT JOIN GROUP BY）：
1. 配額列：`MT_MemberQuotas` JOIN `MT_ProjectMembers`，SELECT `QuestionTypeId, Level, Granularity, QuotaCount AS Target`
2. Questions 統計：`MT_Questions WHERE CreatorId=@U AND ProjectId=@P AND IsDeleted=0 AND Status>=1` GROUP BY `QuestionTypeId, Level`
3. SubQuestions 統計：`MT_SubQuestions sq INNER JOIN MT_Questions q ON q.Id=sq.ParentQuestionId WHERE ... AND sq.Status>=1` GROUP BY `q.QuestionTypeId`

C# 端 `ComputeQuotaCompleted`（QuestionService L152-173）依 3 分支計算 Completed：
- **LCT 單題**（Level 非 NULL, Granularity=0）：按 TypeId+Level 取 Questions 計數
- **CWT 子題**（Granularity=1）：按 TypeId 取 SubQuestions 計數
- **其他**（CWT 母題/單題、LCT 聽力題組）：按 TypeId 合計 Questions

回傳順序：`SortOrder（catalog）→ Level → Granularity`（母題在子題前）。

**重要：「Completed」計算包含 Status≥1 全部**，亦即命題完成 + 已送審 + 各審題階段中 + 修題中 + 採用 + 不採用 + 結案。草稿 (Status=0) 不計入。

**Why:** 一旦離開草稿階段，題目就「貢獻給配額」，避免命題教師退回再修還要重複算配額。

**配額卡片 UI（CwtList:QuotaCard L1456 / QuotaCardWithSub L1543 / BuildQuotaCardEntries L1518 / GetQuotaSuffix L1501）：**

UI 兩種卡片：
- `QuotaCard(q)`：普通單段卡（一般單選 / 長文 / 聽力 / 聽力題組 + 子題獨立顯示）
  - 標題後綴由 `GetQuotaSuffix(q)` 決定：
    - `q.Level.HasValue` → `- 難度三`（LCT 聽力按難度）
    - `q.Granularity == 1` → `- 子題`
    - `q.QuestionTypeId is 3 or 5 && Granularity == 0` → `- 母題`（罕見單獨出現的兜底）
- `QuotaCardWithSub(master, sub)`：題組類雙段卡（TypeId 3 閱讀 / 5 短文），上段母題進度 + 下段子題進度
  - `BuildQuotaCardEntries()` 自動把母+子兩列合併為一張卡（用 `consumed` HashSet 避免重複出列）

進度條配色（`SegmentBarColor`）：
- `bg-gray-200`（未開始）→ `bg-morandi`（進行中）→ `bg-sage`（≥100%）
- 達標時右上 `已達標` badge（雙段卡需「兩段皆達標」才出）
- 命題階段結束 (`IsCompositionPhaseClosed`) 時整張卡片變灰且 disabled
- 點卡片 `OpenComposeModal(typeKey, q.Level)` — CWT 帶 ExamLevel、LCT 帶配額卡的 Level

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

**LoadPageDataAsync 並行設計（L706-746）：**
8 個 Task 同時跑：quota / phase / counts / phaseEnd / replied / subCounts / subReplied / list。`Task.WhenAll` 之後填欄位。子題計數與「已修題」計數一律永遠載入（不 lazy by tab）—— 因為審修作業區 tab badge 是「母 + 子」合計，切 tab 前就要正確；計數 SQL 都是輕量 COUNT GROUP BY，並行下總時間取決於最慢的 ListAsync。`silent=true` 時不切 isLoadingQuota/isLoadingList 避免背景刷新二次閃爍。

**How to apply:**
- 動到階段轉換邏輯前先看 `EnsureCompositionPhaseClosedAsync` 與 `EnsurePhaseTransitionAsync`（Idempotent，可重複呼叫）。
- PhaseCoordinator 已改背景跑（`RunPhaseCoordinatorBackgroundAsync`）不阻塞首載 — 階段升級完成後會自動觸發 InvokeAsync 重新載入。
- `loadedProjectId` 守門避免父層 ProjectSwitcher 重渲染時誤重載配額（會閃骨架）。
- 動配額相關功能務必同時更新 `QuotaProgressItem` (Model) + `ComputeQuotaCompleted` (Service) + `QuotaCard / QuotaCardWithSub / GetQuotaSuffix / BuildQuotaCardEntries` (CwtList.razor)。
