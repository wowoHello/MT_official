---
name: 審修作業區「修題」工作區（RevisionSlideOver）
description: revision tab 開啟的 FullScreen 修題介面結構、Stage B-4-2 子題單元、本輪 reply 隔離規則
type: project
---

**位置與命名陷阱：**
元件叫 `RevisionSlideOver` 但**實際是 FullScreen 滿版**（class `fixed inset-0`），不是側滑面板。位於 `Components/Shared/RevisionForms/RevisionSlideOver.razor`。

**Why:** 命名沿用 Plan_010 早期設計，後改 FullScreen 但檔名沒換。動到時別誤以為是 slide-over panel。

**呼叫端傳入參數（CwtList:517）：**
```razor
<RevisionSlideOver @bind-IsVisible="showRevisionSlideOver"
                   QuestionId="@revisionQuestionId"
                   SubQuestionId="@revisionSubQuestionId"   <!-- Stage B-4-2 子題單元 -->
                   CurrentUserId="@(currentUserId ?? 0)"
                   OnSaved="OnRevisionSavedAsync" />
```

子題單元修題：`OpenRowAsync(questionId, status, subQuestionId)` 帶 sub.Id 進來，後續 `GetRevisionDataAsync` / `SaveRevisionAsync` / `ResubmitAfterFinalEditingAsync` 都按單元處理。

**結構（從上到下）：**
1. Header：返回列表 + 題碼（子題列加 `-NN` 後綴）+ 題型 + 等級/難度 + 階段 badge + 總審退回計數警示（≥2 紅色）+ 最後編輯時間 + 關閉鈕
2. Phase Banner（≤5 天剩餘 terracotta、否則 morandi）：階段名 + 期限 + 倒數天數
3. Body 三欄：
   - 左：`QuestionAttributesSidebar`（可收合）— 接 ProjectType / ExamLevel parameter
   - 中：依 QuestionType 切換 7 個 QuestionForm 元件（與命題 Modal 共用）
   - 右：`RevisionReferencePanel.razor` — 跨階段審題意見 + 自己歷次修題說明 + 本次修題說明（必填 textarea）
4. Footer：左側狀態提示 + 右側按鈕群

**按鈕規則：**
- 非編輯期：只有「關閉」
- 修題期 PhaseCode∈[4,6]：「取消」+「完成修題」
- 修題期 PhaseCode==8（總審修題）：額外多「完成送審」（紅色，呼叫 `ResubmitAfterFinalEditingAsync(qid, uid, subQuestionId)`，子題單元獨立 8→7，建新一筆 Stage=3 Pending Assignment）

**資料 DTO（`RevisionSlideOverData`）：**
- `Question`：母題完整 FormData
- `SubQuestionId`：當前修題單元（NULL=母題單元、非 NULL=該子題單元）
- `Comments`：跨階段審題意見匿名化（**按 SubQuestionId NULL-safe 過濾** — 母題單元只看母題意見、子題單元只看自己的意見）
- `MyReplies`：自己歷次修題說明（同上過濾）
- `CurrentPhaseCode` + `PhaseEndDate`
- `CurrentDraftContent`：當前階段最新一筆 reply
- `HasReplied`：本輪內是否已寫過修題說明
- `FinalReturnCount`：該單元總審退回次數（按單元計次）
- `QStatus`：該單元當前 Status（母題 = MT_Questions.Status / 子題 = MT_SubQuestions.Status）
- `IsEditable`：PhaseCode 與 QStatus 對齊（4↔4 / 6↔6 / 8↔8）

**Plan_013 + Plan_014 本輪 reply 隔離（重要）：**
SQL 用 `CreatedAt > ISNULL((SELECT RoundStartedAt FROM vw_QuestionRoundStartedAt WHERE QuestionId = q.Id), '1900-01-01')` 過濾。

**vw_QuestionRoundStartedAt View（2026-05 全梯次部署）：**
集中提供「上次總審退回時間 MAX」聚合，取代原本散落在 13+ 處的 correlated subquery。CwtList 已整合的消費點：
- `ListAsync` MasterReplied / SubReplied CTE
- `GetMyRevisionRepliedCountAsync` / `GetMySubQuestionRevisionRepliedCountAsync`
- 其他頁面（Reviews / Overview / Home / Dashboard）也同樣整合

**SaveRevisionAsync（QuestionService:2396+）：**
帶 `SaveRevisionRequest { QuestionId, SubQuestionId, FormData, RevisionNote }`：
- SubQuestionId 決定 Status 檢查對象（母題用 MT_Questions.Status；子題用 MT_SubQuestions.Status）
- 也決定 MT_RevisionReplies 寫入時 SubQuestionId 欄位值
- 修題說明採追加而非 UPSERT（Plan_014），保留歷次紀錄

**評語匿名化規則：**
- 互審 → `命題教師 A/B/C`
- 專審 → `審題委員 A/B/C`
- 總審 → `總召集人 A/B/C`
- 用 `AnnotationActorLabel.Anonymize((ReviewStage)r.Stage)` 統一處理，避免「審題委員」hardcode 套到互審造成語意錯誤

**How to apply:**
- 動到評語匿名化或本輪隔離邏輯時，CwtList 的列表計數（`GetMyRevisionRepliedCountAsync` + `GetMySubQuestionRevisionRepliedCountAsync` + ListAsync 的 MasterReplied/SubReplied CTE）四處要同步。
- 別在 RevisionSlideOver 內呼叫 `OpenComposeModal`，兩條路徑互斥。
- SubQuestionId 在 CwtList revision tab 透過 OpenRowAsync 第三個參數傳入；命題 Modal 不傳子題（母題層級編輯永遠包整題）。
