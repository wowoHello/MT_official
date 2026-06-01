---
name: CwtList 實作現況概覽
description: CwtList 正式實作中；本檔記錄當前架構、Plan 編號歷程、超出三檔案規則的輔助服務、檔案行數
type: project
---

CwtList 不再是 prototype 階段，已是正式實作中的核心頁面。

**Why:** 過往 memory 把它當待設計頁，現在應視為「維護 + 增量改善」階段。

**How to apply:**
- 撰寫計畫書時請使用 Plan_NNN 命名延續歷程（已知最新到 Plan_014 + Stage B-4-2）。
  - Plan_008：命題階段結束鎖死
  - Plan_009：修題狀態與階段對齊
  - Plan_010：修題 Slide-Over（A 骨架 / B 內容 / C 進階）
  - Plan_011：互審/專審/總審分配 + Header 跟 Dashboard 統一
  - Plan_012：審修作業區「已修題」卡片拆分
  - Plan_013：總審退回後本輪 reply 隔離（Badge & FormReset）
  - Plan_014：RevisionRepliesAppendOnly（修題說明改為追加而非 UPSERT）
  - Stage B-4-2：子題單元獨立流轉（revision tab 拆 N+1 列、SubQuestionId 傳遞、按單元計次）
- razor / Service 內常見 `// Plan_NNN` 或 `// Stage B-4-2` 註解，動到既有邏輯前先確認該計畫意圖。

**三檔案規則的實際擴張（合理例外）：**
主三檔案（鐵律，2026-06-01 結案版確認）：
- `Components/Pages/CwtList.razor`（**1611 行**，@code 起 L582）
- `Services/QuestionService.cs`（**3187 行**）
- `Models/QuestionModels.cs`（**779 行**）

但已長出輔助 service / model（其他頁面也共用）：
- `Services/QuestionFormValidator.cs` — 7 種題型欄位驗證（`ValidateForDraft` 寬鬆 / `ValidateForCompletion` 嚴格）
- `Services/PhaseTransitionCoordinator.cs` / `IPhaseTransitionCoordinator` — 60 秒 cache 防重觸發 EnsurePhaseTransitionAsync
- `Services/QuestionTypeCatalog.cs` — 啟動載入 7 題型字典，CwtList 透過 _typeCatalog.GetName/SortOrder 補欄位
- `Services/SimilarityService.cs`（**535 行，Plan A 簡化版**）— 只有 ComputeOnDemandAsync 一個方法
- `Models/SimilarityModels.cs`（211 行）— QuestionDraftSnapshot / SimilarityCompareResult 等
- DB View `vw_QuestionRoundStartedAt` — 全梯次「上次總審退回時間」聚合，ListAsync CTE 與多處查詢已整合

**ProjectPhaseInfo 生命週期欄位（QuestionModels.cs L22-67）：**
`ProjectPhaseInfo` 帶 `ClosedAt? / IsClosed / IsUrgent / DisplayState`（computed），搭配 `PhaseDisplayState` enum（Done=0 / Active=1 / Upcoming=2 / Closed=3）。已結案專案依 ClosedAt 落點標 Closed（橘），與 Projects 頁時程列視覺一致。`GetCurrentPhaseAsync` JOIN MT_Projects 帶入 ClosedAt。

**Quill 編輯器架構：**
所有富文本欄位用 `QuillField` 而非 `QuillEditor` 直接呼叫；外層由 `QuillEditorHost` 提供共用底部面板（CascadingValue `IsFormReadOnly` 控制唯讀）。CwtList 的 Modal 與 RevisionSlideOver 各自包一個 QuillEditorHost。

**圖片獨立表（2026-05 上線）：**
`MT_QuestionImages` 將圖片從 Quill HTML 拆出；前端用 `QuestionImagesField.razor` 嵌在題幹/選項/文章內容底下，C# 用 `QuestionFormData.Images` + `GetImages/SetImages` 切片 by FieldType+SubQuestionIndex。`AudioUploadField.razor` 負責聽力音檔。

**Status 對齊驗證：**
`IsEditableStatus` 同時檢查三件事：
1. compose tab 的 Status≥2 強制唯讀（避免從命題 Tab 編到審題流程中的題目）
2. `IsCompositionPhaseClosed` 後 Draft/Completed 也唯讀（Plan_008）
3. PeerEditing(4)/ExpertEditing(6)/FinalEditing(8) 必須 `currentPhase?.PhaseCode` 對齊才開放（Plan_009）

**子題層級三表（Stage A 預埋 + Stage B-4-2 已啟用部分）：**
`MT_ReviewAssignments` / `MT_RevisionReplies` / `MT_ReviewReturnCounts` 都有 `SubQuestionId NULL` 欄位（NULL=母題層級 / 非 NULL=子題層級）。

**Stage B-4-2 已啟用範圍**：
- ListAsync revision tab 拆 N+1 列（filter.IncludeSubRows）
- GetRevisionDataAsync 接受 subQuestionId 參數，按單元載入意見/修題/退回次數
- SaveRevisionAsync 用 SubQuestionId 定位 RevisionReplies 寫入位置
- ResubmitAfterFinalEditingAsync 子題單元獨立 8→7
- 配額卡片母題/子題雙段呈現

**PhaseCoordinator 改背景跑（2026-05 後）：**
`OnParametersSetAsync` 不再 await PhaseCoordinator.EnsureAsync — 改用 `RunPhaseCoordinatorBackgroundAsync` fire-and-forget。LoadPageDataAsync 立即用 DB 當前狀態渲染，避免互審/專審/總審分配跑 5-10 秒卡住首屏。背景跑完後切回 UI thread reload。
