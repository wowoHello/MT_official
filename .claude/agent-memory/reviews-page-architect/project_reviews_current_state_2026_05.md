---
name: 審題頁面現況快照（2026-05）
description: Reviews.razor / ReviewService / ReviewModels 於 2026-05-13 的實作現況摘要，含狀態流轉、三審制度、迴避邏輯的落地方式
type: project
---

## 現況摘要（最後更新：2026-05-13，commit 1b815f6）

### 三審制度現況實作（正確落地）
- **互審（ReviewStage.Mutual=1）**：PhaseCode=3；分配排除命題者自身；ReviewDecisionBar 只顯示「儲存意見」/「修改意見」，無採用/退回鈕
- **專審（ReviewStage.Expert=2）**：PhaseCode=5；有 Approve / Revise，無 Reject；迴避邏輯在 QuestionService.AssignExpertReviewers
- **總審（ReviewStage.Final=3）**：PhaseCode=7；完整三選項（採用/改後採用/不採用）；退回上限由 MT_ReviewReturnCounts 追蹤

### QuestionStatus 完整編碼（v3.0 重編後，SentBack 已廢用）
| Status | 常數名 | 說明 |
|--------|-------|------|
| 0 | Draft | 命題草稿 |
| 1 | Completed | 命題完成 |
| 2 | Submitted | 命題送審 |
| 3 | PeerReviewing | 互審中（鎖定）|
| 4 | PeerEditing | 互審修題中 |
| 5 | ExpertReviewing | 專審中（鎖定）|
| 6 | ExpertEditing | 專審修題中 |
| 7 | FinalReviewing | 總審中（鎖定）|
| 8 | FinalEditing | 總審修題中 |
| 9 | Adopted | 採用 |
| 10 | Rejected | 不採用 |
| 11 | ClosedNotAdopted | 結案未採用（原12，前移） |
| 12 | Archived | 結案入庫（原13，前移） |

- `HistoryTabStatuses = [9, 10, 11, 12]` — 審核結果與歷史 Tab 的篩選範圍

### IsHistorical 判斷方式（從 Service 端計算，UI 端直接讀取）
PhaseCode → Stage 對照（在 GetMyAssignmentsAsync 中設值）：
- PhaseCode=3（互審）/ 4（互修）→ Mutual 為 active；其餘 Historical
- PhaseCode=5（專審）/ 6（專修）→ Expert 為 active
- PhaseCode=7（總審）/ 8（總修）→ Final 為 active（PhaseCode=8 時 Final 也是 active，舊版 bug 已修）
- 其他 PhaseCode → 全部保守顯示（false）

### MT_ReviewReturnCounts 退回次數邏輯
- ReturnCount >= 2：Modal 頂部顯示「已退回 2 次」紅色預警徽章
- BumpReturnCount 後 ReturnCount+1 >= 3 → CanEditByReviewer=1 → 解鎖「編輯題目」橘鈕
- 母題與子題各自獨立計次（SubQuestionId NULL-safe 比對）

### ReviewService 關鍵方法
- `GetMyAssignmentsAsync`：ROW_NUMBER CTE 去重同單元多筆，排除 Status IN (9,10,11,12)
- `GetHistoryAsync`：篩 Status IN HistoryTabStatuses，歷史 Tab 專用
- `GetModalDataAsync`：一次撈 題目+Assignment+歷程+相似題+ReturnCount+SiblingUnits 六項資料
- `SubmitDecisionAsync`：僅更新 Assignment；題目狀態流轉由 PhaseTransitionCoordinator 負責
- `FinalReviewerEditAndDecideAsync`（Plan_021）：單 tx 內 UPDATE 題目+Status(9/10)+Assignment+2筆 AuditLog

### SystemAutoAuditReasons 排除清單（12 個）
系統批次寫入的 Reason 字串（UserId=NULL），從 ReviewHistoryTimeline 與「最後編輯時間」計算中排除，避免污染歷程顯示。
例：CompositionPhaseEnded / ExpertReviewAssigned / FinalReviewAssigned 等。

### ReviewDecisionBar 五種配置
1. **互審**：[儲存意見] / [修改意見]（無決策按鈕）
2. **專審**：[儲存草稿] + [改後採用] + [採用]（無 Reject）
3. **總審**：[儲存草稿] + [改後採用] + [不採用] + [採用]
4. **總召第 3 次（CanFinalReviewerEdit）**：[儲存草稿] + [編輯題目] + [不採用] + [採用]
5. **IsHistorical / 已決策**：唯讀提示列 + 關閉按鈕

### 聽力題組等級/難度顯示（commit 1b815f6 新增）
- 審題作業區列表：ListenGroup 子題用 FixedDifficulty 取 ListenLevelLabels（難度三/難度四）；母題顯示「—」
- 歷史 Tab：一律以 QuestionId 一列呈現，ListenGroup 無等級無難度顯示 em dash
- 規則：TypeKey != ListenGroup 才套用 Difficulty pill

### Reviews.razor OnInitializedAsync 優化（commit 1b815f6）
- 載入階段改全並行：phasesTask + assignmentsTask + historyTask + memberTask 同時 await Task.WhenAll
- PhaseCoordinator 背景執行（可能耗時 5~10 秒），完成後 InvokeAsync 刷新列表

### 兩個 Tab 現況
- **審題作業區**：inline 摘要 bar（本區題目/待處理/已審題/歷史紀錄）；PhaseCode=2 隱藏改顯示等待提示
- **審核結果與歷史**：3 張統計卡片（全部/採用/不採用）+ 以 QuestionId 一列顯示（不拆子題）

### 三審迴避邏輯落地位置
- **互審迴避**：QuestionService.EnsureCompositionPhaseClosedAsync（分配時排除命題者）
- **專審迴避**：QuestionService.AssignExpertReviewers（排除自己命的題）
- **總審迴避**：QuestionService.AssignFinalReviewers（排除專審已審過的題，給其他總召）
- ReviewService 本身不做分配，只做列表查詢與決策寫入

### SiblingUnits 兄弟單元機制
- 題組類（TypeId IN 3,5,7）開啟 Modal 時，GetModalDataAsync 撈同題同 Stage 同 Reviewer 的所有子題
- 用於母題列顯示「子題索引卡」狀態（完成/待審）
- 非題組或無 Assignment 時，SiblingUnits 為空 List

**Why:** 2026-05-13 進行全面文件與程式碼對讀，確認實作與規格一致性後更新
**How to apply:** 後續任何涉及 Reviews 頁面的任務，先對照此快照確認狀態編碼、PhaseCode 對應、DecisionBar 配置是否有變更
