---
name: CwtList 14 種狀態碼與三 Tab 範圍
description: MT_Questions.Status 0-12 的對應、SentBack(11) 已廢用、三 Tab 各自的狀態集合
type: project
---

**Status 列表（`QuestionStatus` 常數 in `Models/QuestionModels.cs`）：**
- 0 Draft / 1 Completed / 2 Submitted
- 3 PeerReviewing / 4 PeerEditing
- 5 ExpertReviewing / 6 ExpertEditing
- 7 FinalReviewing / 8 FinalEditing
- 9 Adopted / 10 Rejected
- 11 ClosedNotAdopted（原值，2026-05-05 後從 12 前移一位）
- 12 Archived（結案入庫，原 13 前移一位）

**Why:** 原本 Status=11 是 SentBack（總審退回），於 2026-05-05 D1 決策正式廢用，因此 ClosedNotAdopted/Archived 各自前移一位。動到 SQL 寫死值時要小心對齊新編號。

**三 Tab 狀態範圍（驅動列表預設過濾與 EmptyState）：**
- `ComposeTabStatuses = [0..8]` — 命題作業區（包含 2-8 的「已送審快照」唯讀）
- `RevisionTabStatuses = [2..8]` — 審修作業區
- `HistoryTabStatuses = [9, 10, 11, 12]` — 審核結果與歷史
- `SubmittedSnapshotStatuses = [2..8]` — compose tab 的「已送審」統計卡片所涵蓋

**How to apply:**
- 命題端 (compose) 看 Status 2-8 都顯示「已送審」彩標（一律唯讀），實際審題進度在 revision tab 才細分。
- history tab 的「已採用」卡片要包 [9, 12]（首輪通過 + 結案入庫）。
- revision tab 的「審題鎖定」要包 [2,3,5,7]（送審剛入庫 + 三審鎖定階段），「修題中」與「已修題」共享 [4,6,8] 但用 `HasRepliedThisStage` 切分。
