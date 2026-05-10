---
name: 七種題型表單實作位置與差異點
description: 7 個 QuestionForm 元件的對應、共用結構、各題型獨有的屬性與子題規則
type: project
---

**七種題型 → 元件對應（皆位於 `Components/Shared/QuestionForms/`）：**
- Single (1) / Select (2) → `QuestionFormSingle.razor`（共用同一個元件）
- ReadGroup (3) → `QuestionFormReadGroup.razor`
- LongText (4) → `QuestionFormLongText.razor`
- ShortGroup (5) → `QuestionFormShortGroup.razor`
- Listen (6) → `QuestionFormListen.razor`
- ListenGroup (7) → `QuestionFormListenGroup.razor`

CwtList.razor 用 `switch (formData.QuestionType)` 切換，**沒有用 DynamicComponent**。

**共用結構（避免重複的 4 個元件）：**
- `FormSectionCard.razor` — 表單區塊外框（Title + Icon + Required + DataField anchor + ContainerClass for InvalidRing）
- `OptionGroup.razor` — ABCD 選項共用（Vertical / Grid 兩種 layout，含 radio 標示正確答案）
- `QuestionAttributesSidebar.razor`（454 行）— 左側屬性側欄，依 `QuestionType` 動態渲染對應 enum 下拉
- `QuillField.razor`（位於 Shared/，非 QuestionForms/）— 點擊滑出底部 Quill 編輯器

**各題型差異（屬性側欄欄位）：**
- Single/Select：Topic（主類）+ Subtopic（次類，級聯）+ Difficulty
- LongText：WritingMode（引導寫作 / 資訊整合）+ Difficulty；可選等級 [0,1,2,4]
- ReadGroup：Genre（文體）+ Difficulty
- ShortGroup：Genre + Difficulty；Topic/Subtopic **寫死** Topic=6 文意判讀 / Subtopic=17 篇章辨析（不放入級聯）
- Listen：CoreAbility（核心能力）+ DetailIndicator（細目指標）+ AudioType + Material；等級 [1..5] 用 ListenLevelLabels「難度一～五」
- ListenGroup：母題僅 AudioType + Material（**無等級**，TypeLevels 為空陣列），兩個固定子題分別 FixedDifficulty=3/4

**子題規則：**
- ReadGroup：可動態新增/刪除子題；**僅 Status==Draft 才允許新增/刪除**（Add/Remove 按鈕條件）
- ShortGroup：同 ReadGroup（可變子題數）
- ListenGroup：固定 2 子題（第 1 題難度三、第 2 題難度四），**不可新增也不可刪除**
- 子題 CoreAbility/Indicator 解碼透過 `QuestionConstants.DecodeSubCoreAbility` / `DecodeSubIndicator` 兩個 helper（會依母題 typeKey 切換 ShortGroup* 或全表 CoreAbility/DetailIndicator）

**Why:**
- Topic=6/Subtopic=17 不放在 `TopicSubtopicMap` 級聯，是因為 ShortGroup 在 UI 上根本不顯示主類/次類選擇。
- ListenGroup 母題沒有等級概念，子題自帶固定難度。

**How to apply:**
- 動到子題增刪邏輯前先確認 Status 守門（避免送審後動到結構）。
- 新增題型差異請對齊 `QuestionConstants` 的 dictionary（TypeLabels / TypeLevels / TopicSubtopicMap 等），razor 不寫死中文。
