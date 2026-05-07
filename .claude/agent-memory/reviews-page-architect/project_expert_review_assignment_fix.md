---
name: 專審分配 Bug 修復（Plan_013）
description: 專審/總審分配 throw 改 graceful return + PhaseCode=3 狀態升級 + UI 預設過濾歷史記錄
type: project
---

專審/總審分配器的例外阻擋問題與互審 Status 遺漏已於 2026-05-07 修復（Plan_013）。

**修復內容（QuestionService.cs）**：
1. `AssignExpertReviewersAsync` 池為空時：原本 `throw InvalidOperationException` 改為 `return`（graceful）。避免 PhaseTransitionCoordinator 吞例外後 cache 生效 60 秒，導致分配一直未執行且 Status 升級被 rollback。
2. `AssignFinalReviewersAsync` 池為空 / 池不足時：同樣改為 `return`（兩個 throw 均移除）。
3. `EnsurePhaseTransitionAsync` switch 補上 PhaseCode=3 case：把 `Submitted(2)` 升到 `PeerReviewing(3)`，reasonLabel="PeerReviewingPhaseStart"。之前 PhaseCode=3 落到 `_ =>` 直接 return 0，題目 Status 永遠停在 2。

**修復內容（ReviewService.cs）**：
4. `SystemAutoAuditReasons` 新增 `"PeerReviewingPhaseStart"`，避免交互審題 Status 升級記錄出現在審題 Modal 的歷程 timeline。

**修復內容（Reviews.razor）**：
5. `FilteredAssignments` 的 `_ => true` 改為 `_ => !a.IsHistorical`。預設狀態篩選「所有狀態」現在只顯示當前階段分配，不再混入歷史互審記錄，消除「看到太多題目」的感知問題。

**Why:** 分配 throw 會讓整個 EnsurePhaseTransitionAsync transaction rollback，包括 Status 升級；PhaseTransitionCoordinator catch 吞例外後仍 cache.Set，60 秒內不重試；用戶每次進頁面都看不到分配。

**How to apply:** 未來若發現審題清單為空，先確認 AuditLogs 有無 `ExpertReviewerPoolEmpty` 或 `FinalReviewerPoolEmpty` 的系統記錄，表示角色配置問題，去專案管理頁補人員即可（下次 coordinator 觸發就會重試分配）。
