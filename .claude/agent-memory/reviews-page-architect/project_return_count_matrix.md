---
name: ReturnCount 決策矩陣（Plan_019 修正）
description: PhaseCode=8 下 List 按鈕、Modal 意見唯讀、決策按鈕顯示的完整規則
type: project
---

Plan_019 修正三個交織問題，落實以下決策矩陣（PhaseCode=8 / Stage=Final）：

| 情境 | Status | ReturnCount | List 按鈕 | Modal 意見 | Modal 決策按鈕 |
|---|---|---|---|---|---|
| 待審（Pending） | Pending | n/a | **審題**（綠） | 可輸入 | **亮** |
| 已退回 1 次 | Completed | 1 | **檢視**（灰） | 唯讀 | **不亮** |
| 已退回 2 次（警示） | Completed | 2 | **檢視**（灰） | 唯讀 | **不亮** |
| 已退回 3 次（解鎖） | Completed | 3（CanEditByReviewer=true） | **編輯題目**（橘） | 唯讀 | **不亮**（但另顯採用/不採用） |

**注意（2026-05-08 驗證）：** List 按鈕的「編輯題目」判斷使用 `item.CanFinalReviewerEdit && item.Stage == ReviewStage.Final && item.IsCompleted` 三個條件全滿足才顯示。並非純 ReturnCount 數字判斷，而是信任 Service 端已設定好的 `CanFinalReviewerEdit` 欄位（MERGE SQL 中 `ReturnCount + 1 >= 3` 時設為 1）。

Modal 的「已退回 2 次，解鎖編輯與總決策」警示徽章：`FinalReturnCount >= 2 && stage == ReviewStage.Final` 顯示（提前一輪預警「下次解鎖」）。

**修改位置：**
- `ReviewService.cs BumpReturnCountAsync`：`>= 2` → `>= 3`（閾值修正）
- `ReviewModels.cs ReviewListItem`：新增 `FinalReturnCount`、`CanFinalReviewerEdit` 欄位
- `ReviewService.cs GetMyAssignmentsAsync SQL`：LEFT JOIN `MT_ReviewReturnCounts`，SELECT ReturnCount/CanEditByReviewer
- `Reviews.razor` List 按鈕：三路決策（historical=檢視 / Pending=審題 / Completed+解鎖=編輯題目 / Completed+未解鎖=檢視）
- `ReviewModal.razor`：`isActionReadOnly` 加入 `IsCompleted` 判斷
- `ReviewDecisionBar.razor`：`else` 拆為三路（IsCompleted+未解鎖=唯讀提示 / IsCompleted+解鎖=編輯+採用/不採用 / Pending=正常決策按鈕）

**Why:** 問題 A 閾值錯（>= 2 = 第 2 次就解鎖，規格是第 3 次）；問題 B List 顯示「編輯」不符預期；問題 C IsCompleted 的 row 仍可輸入意見。

**How to apply:** 任何涉及「已退回」badge 或按鈕的 UI，都應先查 `IsCompleted`，再查 `CanFinalReviewerEdit`。警示徽章仍維持 `FinalReturnCount >= 2`（提前一輪預警「下次解鎖」）。
