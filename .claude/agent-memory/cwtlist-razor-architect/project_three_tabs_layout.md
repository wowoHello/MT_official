---
name: CwtList 三 Tab 佈局與統計卡片組成
description: 命題作業區/審修作業區/審核結果與歷史 三大 Tab 的卡片、篩選、列表行為實際內容
type: project
---

**頁面佈局（從上到下）：**
1. Header（麵包屑 + h1「命題任務」+ 階段指示燈 + 「新增試題」按鈕；按鈕在 `IsCompositionPhaseClosed` 時 disabled）
2. 配額進度卡片區（最多 7 張橫向 grid，依 `BuildQuotaCardEntries` 動態渲染 — 題組類自動合併母+子為雙段卡）
3. 三大 Tab Bar（compose=sage / revision=terracotta / history=morandi 配色）
   - revision tab 的 badge 計數 = `SumCounts + SubSumCounts`（含母題 + 子題）
4. revision Tab 階段橫幅（依 PhaseCode 顯示鎖定/修題中/尚未開始/已結束 4 種狀態）
5. 該 Tab 統計卡片（compose 4 張 / revision 4 張 / history 3 張）
6. 篩選列（DebouncedSearchInput + 題型 select + 等級 select；下拉「有才顯示」由 `listResult.TypeIdCounts / LevelCounts` 控制）
7. 題目列表（含 Pagination 共用元件）

**統計卡片實際組成：**
- **compose**: 命題總計 / 命題草稿 / 命題完成 / 已送審
- **revision**: 審題總計 / 審題鎖定 / 修題中 / 已修題（4 張，**用 `RevisionStatCard` 雙值版**：母題 X / 子題 Y 並排）
- **history**: 全部題目 / 已採用 / 不採用

**Why:** revision tab 統計卡片改 `RevisionStatCard` 雙值版（左母題 + 中分隔 / + 右子題），因為 ListAsync 在 revision tab 把母題與子題拆成獨立列（N+1 列），統計卡片需同步呈現兩條分支的數量。

**How to apply:**
- 點卡片會切換 `activeFilter` (`all/draft/done/submitted/locked/editing/replied/adopted/rejected`)，由 `ResolveStatusFilter` / `ResolveStatusesOverride` / `ResolveHasReplied` 三組 switch 解析成 SQL filter。
- 列表 ORDER BY 寫死：Status 0→1→其餘→Id ASC（revision tab 加 `ISNULL(SubSortOrder, -1) ASC` 讓子題接在母題後）。

**列表「狀態」欄文字映射（`RowStatusLabel`）：**
- compose: 0/1 顯示原文，2-8 一律顯示「已送審」
- revision: [2,3,5,7]→「審題鎖定」、[4,6,8] 依 `HasRepliedThisStage` 切「已修題」/「修題中」
- history: 直接顯示 `QuestionStatus.Labels` 對應文字

**列表「操作」欄按鈕（`RowActionLabel`/`RowActionIcon`）：**
- revision + Status∈[4,6,8]：未送出 → 「修題」(fa-pen)、已送出 → 「編輯」(fa-pen-to-square)
- 其他：依 `IsEditableStatus` 切「編輯」/「檢視」
- 刪除按鈕僅 `Status==Draft && !IsCompositionPhaseClosed && !isSubRow` 時顯示（子題不能單獨軟刪）

**OpenRow 分流（`OpenRowAsync(id, status, subQuestionId)`）：**
- revision tab → 開 `RevisionSlideOver`（FullScreen，傳 SubQuestionId）
- compose / history tab → 開 `CustomModal` FullScreen 命題表單（含唯讀模式，**永遠只開母題層級**）

**子題列視覺（Stage B-4-2）：**
- revision tab 子題列以 `↳`（fa-turn-up rotate-90）符號開頭 + 完整子題碼 `Q-{Year}-{NNNNN}-{NN}`
- row class 加 `bg-morandi/2` 淡背景區分
- 母題列才顯示「N 子題」徽章，子題列本身不再重複
- 子題刪除按鈕一律隱藏（子題不能單獨軟刪）

**篩選下拉「有才顯示」：**
題型/等級下拉用 `listResult.TypeIdCounts / LevelCounts` 過濾，避免使用者選了卻篩到 0 筆。計算範圍刻意忽略 type/level/keyword/HasReplied filter（與 Overview.StatusKeyCounts 同源語意）。`GetVisibleTypeIdToKeyForProject(ProjectType, ExamLevel)` 取交集，過濾出實際梯次內有的題型。
