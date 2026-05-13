---
name: Overview 相關資料庫關鍵欄位與 Stage A 子題提升
description: db.md 最新版本中與 Overview 查詢相關的資料表設計重點，含 Stage A 子題審題單元提升、Plan_014 本輪過濾邏輯
type: project
---

**Stage A 起子題提升為「審題單元」**（影響 Overview 跨多表查詢）：
- `MT_SubQuestions` 加上 Status / SubmittedAt / DecidedAt 三欄，沿用母題 0~12 status 編碼，獨立計算流程
- `MT_RevisionReplies`、`MT_ReviewAssignments`、`MT_ReviewReturnCounts` 都新增 `SubQuestionId INT NULL`，NULL = 母題層級、非 NULL = 子題層級
- 退回次數 → 母題與每子題各自累計（達 2 次後解鎖總召自改）
- 索引 `IX_MT_ReviewAssignments_Question_Sub_Stage` 支援以 (QuestionId, SubQuestionId, ReviewStage) 查詢

**Plan_014 本輪修題過濾**（已落實於 OverviewService SQL）：
- `GetPendingRevisionCountAsync` 與 `BuildStatusKeyCountsAsync` 都加上 `rr.CreatedAt > ISNULL((SELECT MAX(DecidedAt)...Decision IN (2,3)), '1900-01-01')` 條件
- PhaseCode 4/6 線性單輪 → MAX 為 NULL → fallback 1900 → 等同未過濾，行為不變
- PhaseCode 8（總召跨多輪退回）→ 真正生效，避免舊輪 reply 被誤判為本輪已修

**MT_Questions 關鍵欄位（Overview 查詢用）**：
- `Status TINYINT` 0~13（13 種流轉狀態，與 QuestionStatus 常數對齊）
- `IsDeleted BIT` 軟刪除（Overview 強制 IncludeDeleted=true 才看得到「命題刪除」標籤）
- `CreatorId / ProjectId / QuestionTypeId` 都是 INT FK
- `Level TINYINT`（0~4）、`Difficulty TINYINT`（1~3）
- 七種題型的屬性欄位 Topic/Subtopic/Genre/Material/WritingMode/AudioType/CoreAbility/DetailIndicator 都是 TINYINT enum，依 QuestionTypeId 解碼

**MT_QuestionImages 圖片獨立表**（新增）：
- 圖片從 Quill HTML 拆出來獨立管理，透過 QuestionId 或 SubQuestionId（互斥 CHECK CONSTRAINT）+ FieldType 對應欄位
- FieldType：1=Stem / 2~5=OptionA~D / 6=ArticleContent（子題禁用 6）
- Overview 詳情面板透過 `detailData.Images` 取得圖片清單，傳給 QuestionPreviews/* 元件

**MT_ProjectPhases.PhaseCode**：1=產學區間, 2=命題, 3=互審, 4=互修, 5=專審, 6=專修, 7=總審, 8=總修

**How to apply**：Overview 列表查詢時必須 IncludeDeleted=true；新增任何審題/修題相關 SQL 都要記得處理 SubQuestionId IS NULL 與否的分支；PhaseCode 8 的查詢必加 Plan_014 本輪過濾條件。
