---
name: CwtList 實作現況概覽
description: CwtList 已從 prototype 進入正式實作；本檔記錄當前架構、Plan 編號歷程、超出三檔案規則的輔助服務
type: project
---

CwtList 不再是 prototype 階段，已是正式實作中的核心頁面。

**Why:** 過往 memory 把它當待設計頁，現在應視為「維護 + 增量改善」階段。

**How to apply:**
- 撰寫計畫書時請使用 Plan_NNN 命名延續歷程（已知最新到 Plan_013）。已知計畫包含 Plan_008（命題階段結束鎖死）、Plan_009（修題狀態與階段對齊）、Plan_010（修題 Slide-Over）、Plan_011（互審/專審/總審分配）、Plan_012（已修題卡片）、Plan_013（總審退回後本輪 reply 隔離）。
- razor 內常見 `// Plan_008` `// Plan_009` 註解，動到既有邏輯前先確認該計畫意圖。

**三檔案規則的實際擴張（合理例外）：**
主三檔案（鐵律）：
- `Components/Pages/CwtList.razor`（~1217 行）
- `Services/QuestionService.cs`（~2054 行）
- `Models/QuestionModels.cs`（~559 行）

但已長出兩個輔助 service（其他頁面也共用，因此不算違反「一頁三檔」）：
- `Services/QuestionFormValidator.cs` — 7 種題型欄位驗證（`ValidateForDraft` 寬鬆 / `ValidateForCompletion` 嚴格），回傳 `List<ValidationError>` 含 FieldKey
- `Services/PhaseTransitionCoordinator.cs` — 集中呼叫 `IQuestionService.EnsurePhaseTransitionAsync` 等批次升級，避免多頁面重複觸發

新增輔助服務前先確認是否真的跨頁共用，否則優先把邏輯放回 QuestionService.cs。

**Quill 編輯器架構：**
所有富文本欄位用 `QuillField` 而非 `QuillEditor` 直接呼叫；外層由 `QuillEditorHost` 提供共用底部面板（CascadingValue `IsFormReadOnly` 控制唯讀）。CwtList 的 Modal 與 RevisionSlideOver 各自包一個 QuillEditorHost。

**Status 對齊驗證：**
`IsEditableStatus` 同時檢查三件事：
1. compose tab 的 Status≥2 強制唯讀（避免從命題 Tab 編到審題流程中的題目）
2. `IsCompositionPhaseClosed` 後 Draft/Completed 也唯讀（Plan_008）
3. PeerEditing(4)/ExpertEditing(6)/FinalEditing(8) 必須 `currentPhase?.PhaseCode` 對齊才開放（Plan_009）
