---
name: CwtList 相關資料表的演化重點（Stage A 子題提升為審題單元）
description: 2026-05 更新後 db.md 對命題流程的重大結構變動，影響子題審題與圖片管理
type: project
---

**1. MT_Questions 主表（最新欄位輪廓）**

`MT_Questions` 共 8 個 TINYINT 屬性欄位（依題型擇用）：
- `Topic` / `Subtopic`（single/select/shortGroup 用）
- `Genre`（readGroup/shortGroup 用）
- `Material` / `AudioType`（listen/listenGroup 用）
- `WritingMode`（longText 用）
- `CoreAbility` / `DetailIndicator`（**僅 listen 母題用**；listenGroup 母題無此屬性，子題各自設定）

`QuestionCode` 由 `MT_QuestionCodeSequence` 配發（Year 主鍵 + NextValue），格式 `Q-{民國年}-{NNNNN}`，例：`Q-115-00012`。

**2. MT_QuestionImages（圖片獨立表，已上線）**

- 從 Quill HTML 拆出圖片，避免富文本爆炸成圖片 base64。
- 互斥：`QuestionId` 或 `SubQuestionId` 二擇一（CK_MT_QuestionImages_OneParent）。
- `FieldType` enum：1=Stem / 2=OptionA / 3=OptionB / 4=OptionC / 5=OptionD / 6=ArticleContent（**僅母題**，子題禁止 CK_MT_QuestionImages_SubNoArticle）。
- 解析（Analysis）**不支援圖片**，故沒對應 enum。
- 上傳走 `POST /api/upload` → `wwwroot/uploads/{guid}.{ext}`（PNG/JPEG/GIF/WebP，5MB 上限）。
- C# 對應：`QuestionImage` 類別 + `QuestionFormData.Images` 集合 + `GetImages/SetImages` helper。

**3. Stage A：子題提升為「審題單元」（重大變動）**

`MT_SubQuestions` 新增三欄：
- `Status TINYINT NOT NULL DEFAULT 0`（**沿用 MT_Questions.Status 編碼，0~12 共 13 種**）
- `SubmittedAt DATETIME2 NULL`
- `DecidedAt DATETIME2 NULL`

意義：題組類試題（readGroup/shortGroup/listenGroup）的每個子題**可獨立流轉狀態**，不再強制與母題同進退（Stage B 才會啟用獨立流程）。

連帶以下三張表都加上 `SubQuestionId NULL` 欄位（NULL=母題層級 / 非 NULL=該子題層級）：
- `MT_ReviewAssignments` — 母題+每子題各建一筆 assignment，配給同一位 reviewer
- `MT_RevisionReplies` — NULL=母題修題說明、非 NULL=子題修題說明
- `MT_ReviewReturnCounts` — 母題與每子題各自累計總審退回次數（皆達 2 次才解鎖總召自改）

對應索引：`IX_MT_ReviewAssignments_Question_Sub_Stage`、`IX_MT_RevisionReplies_Question_Sub_Stage`。

C# Model 已預埋三欄：`SubQuestionChoice` / `SubQuestionFreeResponse` / `ListenGroupSubQuestion` 都有 `Status / SubmittedAt / DecidedAt`，但目前 Stage A 預設值跟著母題，**Stage B 才會啟用獨立流程**。

**4. MT_QuestionTypes.Id 鎖定 1-7**

種子資料用 `SET IDENTITY_INSERT` 強制鎖 1-7，跨環境一致。Id 同時當業務代碼用，對應前端 `QuestionTypeCodes` string key。

**How to apply:**
- 寫 SQL 拉子題時記得 `ParentQuestionId` JOIN + `IsDeleted = 0`（如有）+ 視需要新增 `SubQuestionId IS NULL` 或 `IS NOT NULL` 過濾母題/子題層級。
- 動到審題分配/修題說明/退回計次三表時，必須同時考慮母題與子題兩種層級。
- 圖片改用 MT_QuestionImages，**不要再把 base64 寫回 Quill HTML**。
