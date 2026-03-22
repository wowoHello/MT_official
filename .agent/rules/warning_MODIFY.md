---
trigger: always_on
---

# 首頁代辦事項連結功能實作計畫

將首頁「今日提醒看板」內的急件警示連結至對應的頁面與頁籤，提升導覽效率。

## Proposed Changes

### 首頁模組

#### [MODIFY] [firstpage.js]
- 更新 `remindersDb` 中的 `link` 欄位：
  - 命題階段提醒 -> `cwt-list.html?tab=compose`
  - 互審修題提醒 -> `cwt-list.html?tab=revision`

---

### 命題任務模組

#### [MODIFY] [cwt-list.js]
- 在 `DOMContentLoaded` 事件中加入 URL 參數解析邏輯。
- 若偵測到 `tab` 參數，自動呼叫 [switchTab(tab)]。

---

### 審題任務模組

#### [MODIFY] [cwt-review.js]
- 比照命題任務，在 `DOMContentLoaded` 事件中加入 URL 參數解析邏輯。
- 若偵測到 `tab` 參數，自動呼叫 [switchTab(tab)]。

### 字體大小控制器
#### [MODIFY]［shared.js］
- 字體縮放控制器重構與拖拽優化 (Speed Dial UI)
- UI 優化：將原本佔位較大的面板，改為 Speed Dial（懸浮操作按鈕） 設計。主按鈕固定不動，滑鼠移入時子按鈕會向上垂直彈出，徹底解決原本水平展開導致「滑鼠必須追著按鈕跑」的糟糕 UX 問題。
- 功能擴充準備：已在展開選單中加入未來「留言通知」功能的按鈕佔位符，待日後實作。
- 拖拽功能：實作可自由拖拽的機制，解決工具球遮擋列表按鈕的問題。使用者可將其移至螢幕任何角落。
- 持久化位置：系統會記憶拖拽後的 X, Y 座標（LocalStorage），換頁或重新整理後依然維持在指定位置。
- 集中管理：將所有 HTML 結構與 JavaScript 邏輯整合至 shared.js，移除全站 9 個頁面的冗餘代碼。

## Verification Plan

### Automated Tests (Manual Steps)
1. 開啟首頁 [firstpage.html]。
2. 點擊「命題階段」急件提醒，確認跳轉至 `cwt-list.html` 且預設選中「命題作業區」。
3. 點擊「互審修題」急件提醒，確認跳轉至 `cwt-list.html` 且預設選中「審修作業區」。
4. 點擊「審題」相關提醒（若有），確認跳轉至 [reviews.html]。

