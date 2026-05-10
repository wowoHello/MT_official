---
name: CwtList 三 Tab 佈局與統計卡片組成
description: 命題作業區/審修作業區/審核結果與歷史 三大 Tab 的卡片、篩選、列表行為實際內容
type: project
---

**頁面佈局（從上到下）：**
1. Header（麵包屑 + h1「命題任務」+ 階段指示燈 + 「新增試題」按鈕；按鈕在 `IsCompositionPhaseClosed` 時 disabled）
2. 配額進度卡片區（最多 7 張橫向 grid，依 `quotaProgress` 動態渲染）
3. 三大 Tab Bar（compose=sage / revision=terracotta / history=morandi 配色）
4. revision Tab 階段橫幅（依 PhaseCode 顯示鎖定/修題中/尚未開始/已結束 4 種狀態）
5. 該 Tab 統計卡片（compose 4 張 / revision 4 張 / history 3 張）
6. 篩選列（DebouncedSearchInput + 題型 select + 等級 select）
7. 題目列表（含 Pagination 共用元件）

**統計卡片實際組成：**
- compose: 命題總計 / 命題草稿 / 命題完成 / 已送審
- revision: 審題總計 / 審題鎖定 / 修題中 / 已修題（4 張，非舊版規格的 3 張）
- history: 全部題目 / 已採用 / 不採用

**Why:** revision tab 由 3 張擴成 4 張：Plan_012 把「修題中」拆成「未送出修題」(editing) + 「已送出修題」(replied)，使用 `HasRepliedThisStage` 區分。
**How to apply:**
- 點卡片會切換 `activeFilter` (`all/draft/done/submitted/locked/editing/replied/adopted/rejected`)，由 `ResolveStatusFilter` / `ResolveStatusesOverride` / `ResolveHasReplied` 三組 switch 解析成 SQL filter。
- 列表 ORDER BY 寫死：Status 0→1→其餘→Id ASC，**不再以 UpdatedAt 排序**。

**列表「狀態」欄文字映射（`RowStatusLabel`）：**
- compose: 0/1 顯示原文，2-8 一律顯示「已送審」
- revision: [2,3,5,7]→「審題鎖定」、[4,6,8] 依 `HasRepliedThisStage` 切「已修題」/「修題中」
- history: 直接顯示 `QuestionStatus.Labels` 對應文字

**列表「操作」欄按鈕（`RowActionLabel`/`RowActionIcon`）：**
- revision + Status∈[4,6,8]：未送出 → 「修題」(fa-pen)、已送出 → 「編輯」(fa-pen-to-square)
- 其他：依 `IsEditableStatus` 切「編輯」/「檢視」
- 刪除按鈕僅 `Status==Draft && !IsCompositionPhaseClosed` 時顯示（軟刪除確認框）

**OpenRow 分流：**
- revision tab → 開 `RevisionSlideOver`（FullScreen）
- compose / history tab → 開 `CustomModal` FullScreen 命題表單（含唯讀模式）
