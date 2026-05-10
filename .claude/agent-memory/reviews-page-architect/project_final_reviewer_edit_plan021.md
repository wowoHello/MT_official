---
name: 總召代修題最終裁決（Plan_021）
description: ReturnCount>=3 解鎖後總召可修題並一鍵採用/不採用的完整實作架構
type: project
---

Plan_021 已完成，架構如下：

**新增 Service method：** `ReviewService.FinalReviewerEditAndDecideAsync(FinalReviewerEditRequest, int)`
- 單一 transaction：UPDATE 題目欄位 + Status(9/10) + Assignment(Completed/Decision) + 2 筆 AuditLog
- Reason="FinalReviewerEdit"（Questions） + Reason="FinalDecision"（Reviews）
- 子題 UPSERT 由 `UpsertFinalSubQuestionsAsync` 精簡版處理（不新增/刪除，只更新現有子題）
- ShortGroup 子題無 CorrectAnswer（開放式問答），不能 UPDATE CorrectAnswer 欄位

**新增 DTO：** `FinalReviewerEditRequest`（在 ReviewModels.cs）

**Reviews.razor 加入 FinalEdit Modal：**
- `@using MT.Components.Shared.QuestionForms` 加在頁頭（不能放在 @if 內）
- `showFinalEditModal` / `finalEditFormData` / `finalEditAssignmentId` 狀態變數
- `OpenFinalEditModalAsync(int questionId)`：從 assignments 緩存找 AssignmentId，避免額外 DB 查詢
- `FinalEditDecideAsync(ReviewDecision)`：ValidateForCompletion → SwalResult 確認 → FinalReviewerEditAndDecideAsync
- 右下角只有「不採用」「採用」兩個按鈕，無「儲存草稿」

**ReviewModal.razor 改動：**
- 新增 `[Parameter] EventCallback<int> OnRequestFinalEdit`
- `OnRequestEditAsync()` 改為先 CloseAsync() 再 OnRequestFinalEdit.InvokeAsync(questionId)（舊 Toast 移除）
- 新增 `[Parameter] bool IsHistoricalFromService`：由 Reviews.razor 傳入 Service 端已算好的 IsHistorical，取代舊的在 Modal 內做 CurrentProjectStage 比對。PhaseCode=8 時 currentStage=null，舊邏輯會把所有 Final Stage 誤標為歷史。
- `IsHistoricalAssignment` 屬性定義：`data?.MyAssignment is null || IsHistoricalFromService`（未分配給此人也算唯讀）。

**選型決策：**
- 元件方案 A（Reviews.razor 直接嵌入，不新增共用元件）
- 關閉 FinalEdit Modal 後不重開 ReviewModal，列表 reload，題目出現在「審核結果與歷史」Tab
- SwalResult record 定義在 Reviews.razor @code 末尾（與 CwtList.razor 相同 pattern）

**Why:** 使用者拍板方案 A 最快、不牽動其他頁面；Q4 確認關閉後直接回列表不重開 ReviewModal。

**How to apply:** 未來若需在其他頁面複用此功能，考慮方案 B（QuestionEditSlideOver.razor 共用元件）。
目前 Reviews.razor 中 `QuestionAttributesSidebar` 的 `QuestionTypeChanged` 用 lambda 直接賦值，不能用 `with`（class 非 record）。
