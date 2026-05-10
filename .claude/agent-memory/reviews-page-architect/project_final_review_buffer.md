---
name: 總審 Buffer 化 + 分流邏輯（Plan_FinalReviewBuffer v3.0）
description: 總審決策在 PhaseCode=7 期間不改 Status；PhaseCode=8 時 EnsureFinalEditingPhaseAsync 批次分流
type: project
---

總審（ReviewStage.Final）的三個決策（Approve/Revise/Reject）在 PhaseCode=7 期間全部回傳 null，Status 維持 7（FinalReviewing）。

PhaseCode=8 時，由 `EnsureFinalEditingPhaseAsync`（QuestionService 私有方法）執行四段 SQL 分流：
- Decision=Approve → Status=9 (Adopted)
- Decision=Revise/Reject → Status=8 (FinalEditing)
- Decision=NULL（未決策）→ Status=8（D2 補機會）
- 無 Stage=3 Assignment → Status=8（防禦）

**Why:** Buffer 化讓命題老師不會在 PhaseCode=7 期間提早看到修題通知，與業務規格一致。

**How to apply:** `MapDecisionToQuestionStatus` 中 Final 三決策全回 null；`EnsurePhaseTransitionAsync` 中 PhaseCode=8 走獨立方法 `EnsureFinalEditingPhaseAsync`，不走原始 fromStatuses→toStatus 路徑。

退回計數：`BumpReturnCountAsync` 使用 MERGE SQL，每次 +1，當 `ReturnCount + 1 >= 3` 時設 `CanEditByReviewer=1`。因此：
- 第 1 次退回：ReturnCount=1，CanEditByReviewer=0
- 第 2 次退回：ReturnCount=2，CanEditByReviewer=0（UI 顯示「下次解鎖」預警徽章）
- 第 3 次退回：ReturnCount=3，CanEditByReviewer=1（解鎖）

Revise 與 Reject 均觸發計數（`req.Decision == Revise || Reject`，且 `!isResubmit` 防止重複計數）。

PhaseCode=8 的第 3 輪 Reject 偵測：`existingReturnCount >= 2` 表示已退回 2 次，本次 Reject 直接設 Rejected(10)，不再走 BumpReturnCount。
