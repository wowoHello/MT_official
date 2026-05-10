---
name: 審修作業區「修題」工作區（RevisionSlideOver）
description: revision tab 開啟的 FullScreen 修題介面結構、資料流、修題說明 UPSERT 規則
type: project
---

**位置與命名陷阱：**
元件叫 `RevisionSlideOver` 但**實際是 FullScreen 滿版**（class `fixed inset-0 bg-gray-50`），不是側滑面板。位於 `Components/Shared/RevisionForms/RevisionSlideOver.razor`（~33KB，相當於小型主頁）。

**Why:** 命名沿用 Plan_010 早期設計，後改 FullScreen 但檔名沒換。動到時別誤以為是 slide-over panel。

**結構（從上到下）：**
1. Header（h-14）：返回列表 + 題碼 + 題型 + 等級/難度 badge + 階段 badge + 總審退回計數警示（≥2 紅色）+ 最後編輯時間 + 關閉鈕
2. Phase Banner（≤5 天剩餘 terracotta、否則 morandi）：階段名 + 期限 + 倒數天數
3. Body 三欄：
   - 左：`QuestionAttributesSidebar`（可收合，w-56/64）
   - 中：依 QuestionType 切換 7 個 QuestionForm 元件（與命題 Modal 共用）
   - 右 (w-30%)：`RevisionReferencePanel.razor` — 跨階段審題意見 + 自己歷次修題說明 + 本次修題說明（必填 textarea）
4. Footer（h-16）：左側狀態提示（必填提醒 / 已填字數 / 唯讀提示）+ 右側按鈕群

**按鈕規則：**
- 非編輯期：只有「關閉」
- 修題期 PhaseCode∈[4,6]：「取消」+「完成修題」
- 修題期 PhaseCode==8（總審修題）：額外多「完成送審」（紅色 bg-red-600，呼叫 `ResubmitAfterFinalEditingAsync` 8→7 並建新一筆 Stage=3 Pending Assignment）

**資料 DTO：**
`RevisionSlideOverData` 一次拉所有資料（Question + Comments 匿名化 + MyReplies + CurrentPhaseCode + PhaseEndDate + CurrentDraftContent + HasReplied + FinalReturnCount + QStatus）。
- `IsEditable` 屬性同時檢查 PhaseCode 與 QStatus 對齊（4↔4 / 6↔6 / 8↔8）。
- 評語匿名化為「審題老師 A/B/C」（同階段 ROW_NUMBER）。

**Plan_013 本輪 reply 隔離：**
SQL 用 `CreatedAt > ISNULL(MAX(DecidedAt) WHERE Stage=3 AND Decision IN (2,3), '1900-01-01')` 過濾，意思是只看「上次總審退回後寫的修題說明」才算當前輪已修題，舊輪 reply 不算。`HasRepliedThisStage` 與 `GetMyRevisionRepliedCountAsync` 都共用此規則。

**SaveRevisionAsync：**
帶 `SaveRevisionRequest { QuestionId, FormData, RevisionNote }`，UPSERT 對應 (QuestionId, UserId, Stage) 的 RevisionReply，題目欄位也一併寫回；Stage 取題目當前 Status（4/6/8）。

**How to apply:**
- 動到評語匿名化或本輪隔離邏輯時，CwtList 的列表計數（`GetMyRevisionRepliedCountAsync` + ListAsync 的 `repliedClause`）兩處要同步。
- 別在 RevisionSlideOver 內呼叫 `OpenComposeModal`，兩條路徑互斥。
