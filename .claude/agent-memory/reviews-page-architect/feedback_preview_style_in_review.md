---
name: 審題 Modal 考卷預覽風格決策
description: ReviewQuestionDisplay 改用 PreviewXxx 元件後的實作模式與隱藏 PreviewHeader 標題的 CSS 方案
type: feedback
---

審題 Modal 左側（ReviewQuestionDisplay）要顯示考卷風格，做法是直接呼叫 PreviewXxx 元件（同 Overview）。

**Why:** 使用者希望審題者在接近實際試卷排版的環境下審閱題目，與 Overview Slide-over 視覺一致。

**How to apply:**

1. **隱藏「全民中文檢定」大標題**：PreviewHeader 沒有 HideTitle 參數（不可動 PreviewHeader）。改為在外層 div 加 class `review-no-cwt-title`，在 `wwwroot/css/app.css` 加：`.review-no-cwt-title .text-center.font-serif { display: none; }`。PreviewHeader 根節點是 `<div class="text-center mb-8 pb-5 border-b border-gray-300 font-serif">`，選擇器 `.text-center.font-serif` 足夠精確。

2. **BuildTags/LevelLabel**：ReviewQuestionDisplay 是純展示元件，採方案 B（自行封裝，邏輯與 OverviewService 相同但不 DI）。不要注入 IOverviewService 進 ReviewForms 元件。

3. **解析折疊區**：用 `<details open>` 置於 PreviewXxx 下方獨立白卡（border-amber-100），amber 配色表示審題者私有資訊。單題型（一般/精選/聽力）顯示 `Question.Analysis`；長文顯示 `Question.GradingNote`；題組型逐子題各自顯示 `sq.Analysis`。

4. **唯一引用點**：`ReviewModal.razor:119`，修改 ReviewQuestionDisplay 不影響 CwtList/Overview。
