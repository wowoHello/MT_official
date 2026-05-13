---
name: 審題頁面現況快照（2026-05）
description: Reviews.razor / ReviewService / ReviewModels 於 2026-05-13 的實作現況摘要，含狀態流轉、三審制度、迴避邏輯的落地方式
type: project
---

## 現況摘要

### 三審制度現況實作（正確落地）
- **互審（ReviewStage.Mutual=1）**：PhaseCode=3；分配排除命題者自身；ReviewDecisionBar 不顯示採用/退回鈕
- **專審（ReviewStage.Expert=2）**：PhaseCode=5；有 Approve / Revise，無 Reject；迴避邏輯在 QuestionService.AssignExpertReviewers
- **總審（ReviewStage.Final=3）**：PhaseCode=7；完整三選項；退回上限由 MT_ReviewReturnCounts 追蹤

### QuestionStatus 完整編碼（v3.0 重編後，Status=11(SentBack) 已廢用）
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

### IsHistorical 判斷方式（已從 Service 端計算）
PhaseCode → Stage 對照：
- PhaseCode=3（互審）/ 4（互修）→ Mutual 為 active；其餘 Historical
- PhaseCode=5（專審）/ 6（專修）→ Expert 為 active
- PhaseCode=7（總審）/ 8（總修）→ Final 為 active（重要：PhaseCode=8 時 Final 也是 active，舊版 bug 已修）
- 其他 PhaseCode → 全部保守顯示（false）

### MT_ReviewReturnCounts 退回次數邏輯
- ReturnCount >= 2：Modal 頂部顯示「已退回 2 次」紅色預警徽章
- ReturnCount+1 >= 3（BumpReturnCount 後）→ CanEditByReviewer=1 → 解鎖「編輯題目」橘鈕
- 母題與子題各自獨立計次（SubQuestionId NULL-safe 比對）

### ReviewService 關鍵方法
- `GetMyAssignmentsAsync`：ROW_NUMBER CTE 去重同單元多筆，排除 Status IN (9,10,11,12)
- `GetHistoryAsync`：篩 Status IN HistoryTabStatuses，歷史 Tab 專用
- `GetModalDataAsync`：一次撈 題目+Assignment+歷程+相似題+ReturnCount+SiblingUnits 六項資料
- `SubmitDecisionAsync`：僅更新 Assignment；題目狀態流轉由 PhaseTransitionCoordinator 負責
- `FinalReviewerEditAndDecideAsync`（Plan_021）：單 tx 內 UPDATE 題目+Status(9/10)+Assignment+2筆 AuditLog

### SystemAutoAuditReasons 排除清單
12 個系統批次寫入的 Reason 字串，會從 ReviewHistoryTimeline 與「最後編輯時間」計算中排除，避免污染歷程顯示。

### ReviewModal 結構
- FullScreen（z-50），Header（h-16）+ Body（左70% 題目 + 右30% 操作）+ Footer（決策列）
- ReviewSimilarityBanner：有相似題才顯示於最頂部
- ReviewDecisionBar：5 種配置（互審僅意見、專審無 Reject、總審完整、總召第3次解鎖、唯讀）
- ReviewHistoryTimeline：Reviews 頁面為匿名顯示

### 兩個 Tab 現況
- **審題作業區**：inline 摘要 bar（本區題目/待處理/已審題/歷史紀錄）；PhaseCode=2 隱藏改顯示等待提示
- **審核結果與歷史**：3 張統計卡片（全部/採用/不採用）

**Why:** 2026-05-13 進行全面文件與程式碼對讀，確認實作與規格一致性
**How to apply:** 後續任何涉及 Reviews 頁面的任務，先對照此快照確認狀態編碼與 PhaseCode 對應關係是否有變更
