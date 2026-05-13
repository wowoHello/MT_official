---
name: 網站功能介紹總文件位置
description: 全站 10 個頁面的功能總覽文件，含 Overview 完整規格與「說明語言」白話補述
type: reference
---

`D:\jay_liu\Desktop\命題系統相關\網站功能介紹.md` 是全站功能介紹文件，每頁含正式條列 + 「說明語言」白話補述（給非工程師使用）。

**Overview 段（第 113~155 行）權威重點**：
- 統計卡片明確 5 張：總題數 / 命題進行中（草稿+命題完成）/ 已採用（含結案入庫）/ 待修編 / 各審查環節（互審/專審/總審 三聯格，非當前階段灰掉）
- 篩選只有 4 個：關鍵字（題號/題幹/命題教師）、題型下拉、狀態下拉（動態 optgroup）、命題教師下拉（顯示題數）
- **沒有**等級、難度、目前階段這幾個獨立篩選（與 CwtList 區隔）
- 列表 12 欄 grid，水平捲動 min-w-[1080px]；PhaseProgressStepper 最小寬度 480px
- 詳情 SlideOver max-w-3xl，上題目內容下審題歷程（ReviewHistoryTimeline ShowActorName=true 不匿名）
- ESC 鍵可關閉
- 唯讀，唯一寫入動作 = 復原已刪除題目
- Dashboard 教師卡跳轉帶 `?creatorId=X`

**How to apply**：未來規劃 Overview 改動先讀此文件 113~155 行對齊「使用者期待」，再讀 `D:\MTrefer\pageFinal_doc\Overview.md`（如存在）取得更細的整合資訊。
