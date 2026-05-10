---
name: ReviewModal 內部元件 UI 細節（ActionPanel / DecisionBar / Timeline / Similarity）
description: 罐頭訊息 8 則、MinCommentLength=10、5 種 DecisionBar 配置、Timeline 匿名邏輯、相似題雙模式的實際實作狀態
type: project
---

## ReviewActionPanel（右側 30% 操作區）

罐頭訊息共 8 則（按鈕插入 textarea）：題意清晰、答案有爭議、選項長度差異、用詞不當、計算錯誤、鑑別度偏低、文意冗長、圖片不清。
顏色語意：sage=正向 / terracotta=警示 / morandi=中性建議。
使用 `TextareaHelper.insertAtCursor` JS interop 插入游標位置，回傳新值後 Blazor 明確同步（避免 IME 打架）。
意見最小字數 `MinCommentLength = 10`（const，與 ReviewDecisionBar 同步）。

使用 `InputTextArea`（Blazor 內建，正確處理 IME）而非 `<textarea>`，加 `lang="zxx" spellcheck="false"` 等屬性防止瀏覽器輔助工具干擾。

## ReviewDecisionBar 5 種按鈕配置

| 情境 | 條件 | 按鈕 |
|------|------|------|
| 未分配 | Assignment is null | 關閉 |
| 歷史唯讀 | IsHistorical=true | 唯讀文字 + 關閉 |
| 已完成未解鎖 | IsCompleted && !CanFinalReviewerEdit | 唯讀決策狀態 + 關閉 |
| 已完成已解鎖 | IsCompleted && CanFinalReviewerEdit | [儲存草稿] + [編輯題目] + [不採用] + [採用] |
| 待審（Pending） | !IsCompleted | 依 Stage 顯示（互審/專審/總審/總審第3次） |

互審 Pending：僅 [儲存意見]（字數不足則 disabled），提示「互審階段僅提供意見回饋」。
專審 Pending：[儲存意見草稿] + [改後採用] + [採用]（後兩者需 >=10 字）。
總審 Pending（未解鎖）：[儲存意見草稿] + [改後採用] + [不採用] + [採用]（後三者需 >=10 字）。
總審 Pending（已解鎖 CanFinalReviewerEdit）：[儲存意見草稿] + [編輯題目]（不需字數） + [不採用] + [採用]（後兩者需 >=10 字）。

## ReviewHistoryTimeline 匿名邏輯

ShowActorName=false（預設，Reviews 用）時，依 Kind/Label 顯示角色稱謂：
- QuestionEvent / RevisionReply → 命題老師
- ReviewComment 含「互審」 → 命題老師
- ReviewComment 含「總審」 → 總召集人
- ReviewComment 其他 → 審題委員

ShowActorName=true 時顯示真名（管理員 Overview 視角）。

「不採用」意見卡片特殊樣式：紅色警示標題 + bg-red-50 content 框。

## ReviewSimilarityBanner 雙模式

BannerMode.Banner：頂部紅色警示橫幅，常駐於 Modal 最頂部（若 SimilarQuestions.Count > 0）。點「展開比對」→ 呼叫 ReviewActionPanel.ExpandSimilarityAsync() 滾動到 Section。
BannerMode.Section：右側操作區下半部可摺疊清單（預設收合 isOpen=false）。ExpandAsync() 可被父層程式觸發展開。

相似度配色：>=80% 紅 / >=60% 橘 / 其他黃。並排檢視按鈕已佔位但 disabled（Phase 5 才實作）。

## FilteredAssignments 預設「所有狀態」過濾規則

queryStatus="all" 時（Plan_013 §3.4）：只顯示當前階段（非歷史），避免多階段分配同時顯示讓使用者誤以為分配量過多。
例外：若 CurrentReviewStage is null（命題/修題期間）則顯示全部，不過濾。

**Why:** 使用者進審題頁時看到歷史互審分配與當前專審分配混在一起，感知「題目太多」。
**How to apply:** 新增篩選選項「歷史紀錄」讓使用者可手動選擇查看，預設隱藏。
