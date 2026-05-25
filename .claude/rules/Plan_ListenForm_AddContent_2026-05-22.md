# 聽力測驗表單擴充計畫書（實作版）

> **計畫日期**：2026-05-22
> **狀態**：✅ 設計層拍板完成 — 等待最後權重 A/B/C 確認後動工
> **影響範圍**：聽力測驗（TypeId=6）相關 CRUD、預覽、審題、Overview 詳情、相似題比對、共用 Quill 編輯器

---

## 一、拍板決議

| # | 議題 | 結論 |
|---|---|---|
| Q1 | 「題幹改題目」適用範圍 | **A**：只改聽力測驗（TypeId=6）一個題型 |
| Q2 | 「內容」欄位影響範圍 | **完整集**：表單 + 預覽 + Reviews + Overview + **相似題比對重算** |
| Q3 | Quill 編輯器調整方向 | 從**縮小**改為**放大**（25vh → 40vh）|
| Q4 | 哪個 Quill 區塊 | **底部滑入面板**：`Components/Shared/QuillEditor.razor:29` `h-[25vh]` → `h-[40vh]` |
| Q5 | 相似題權重分配 | ⏳ **待最後拍板** A/B/C（見第四節）|

---

## 二、關鍵發現（不需 DB migration）

`MT_Questions.ArticleContent` 欄位的 DB 註解為「**文章/語音內容 (HTML)**」，設計上就支援聽力測驗。`QuestionFormData.ArticleContent` 屬性也已存在。

聽力題組（TypeId=7）的 `QuestionFormListenGroup.razor` 早就用 ArticleContent 存「語音逐字稿」。聽力測驗（TypeId=6）只是當初沒接這欄位 — Phase 7 補上即可。

**不需 migration、不需新 Model 屬性**。

---

## 三、相似題比對權重現況與重算

### 現況（`SimilarityService.cs:472-488`）

```
聽力題目 (TypeId=6)：Stem 70% + 四選項 30%
聽力題組 (TypeId=7)：ArticleContent 70% + 子題 Stem 30%
```

### 重算方案（待拍板 A/B/C）

| 方案 | ArticleContent | Stem | 四選項 | 說明 |
|---|---|---|---|---|
| **A（推薦）** | 70% | 20% | 10% | 與聽力題組權重結構一致（語音逐字稿為核心比對）|
| B | 60% | 25% | 15% | 內容與題幹較均衡 |
| C | 50% | 30% | 20% | 加大選項權重，較分散 |

---

## 四、實作清單（依拍板逐項列出）

### 4.1 `Components/Shared/QuestionForms/QuestionFormListen.razor`

| 改動 | 行 |
|---|---|
| `FormSectionCard Title="題幹"` → `Title="題目"` | 行 16 |
| `QuillField` 的 `FieldLabel="題幹"` → `FieldLabel="題目"` | 行 18 |
| Placeholder「點擊此處開始編輯題幹內容...」→「點擊此處開始編輯題目內容...」| 行 18 |
| 在「題目」與「選項與答案」之間插入「內容」FormSectionCard + QuillField（用 ArticleContent）| 行 20 前 |
| `@code` 加 `[Parameter] string ArticleContent` + `[Parameter] EventCallback<string> ArticleContentChanged` | 行 43 附近 |

### 4.2 `Components/Pages/CwtList.razor`

| 改動 | 內容 |
|---|---|
| 找到 `<QuestionFormListen` 元件呼叫處，補 `ArticleContent="@formData.ArticleContent"` + `ArticleContentChanged="v => formData.ArticleContent = v"` | 1 處 |

### 4.3 `Services/QuestionService.cs`

| 改動 | 確認項 |
|---|---|
| `CreateAsync` / `UpdateAsync` SQL 內 INSERT/UPDATE 對 TypeId=6 是否包含 ArticleContent 欄位 | 既有對題組類已寫，需確認對聽力測驗也走同樣寫入 |
| `GetByIdAsync` 對 TypeId=6 是否回讀 ArticleContent | 同上 |

> 預期既有 SQL 已對全題型統一處理 ArticleContent，可能無需動 Service。實作時 verify。

### 4.4 `Components/Shared/QuestionPreviews/QuestionPreviewListen.razor`（預覽 Modal）

| 改動 | 內容 |
|---|---|
| 模擬考卷格式加入「內容」段顯示（位於題目之後、選項之前）| 1 處 |

### 4.5 `Components/Shared/ReviewForms/ReviewQuestionDisplay.razor`（審題端 + Overview 共用）

| 改動 | 內容 |
|---|---|
| 聽力測驗顯示分支加「內容」區塊（同預覽 Modal 位置）| 1 處 |

### 4.6 `Components/Pages/Overview.razor`（詳情面板）

| 改動 | 內容 |
|---|---|
| 確認 Overview 詳情用 ReviewQuestionDisplay（共用），若是則 4.5 已涵蓋；若獨立渲染則需另補 | 確認後決定 |

### 4.7 `Components/Shared/QuillEditor.razor`

| 改動 | 行 |
|---|---|
| 底部滑入面板 `h-[25vh]` → `h-[40vh]` | 行 29 |

### 4.8 `Services/SimilarityService.cs`

| 改動 | 行 |
|---|---|
| `ExtractListening` 加入 ArticleContent，權重重分配（依 A/B/C 拍板）| 行 477-488 |
| 註解更新：移除「DB 端聽力題無獨立逐字稿欄位」說法 | 行 472-476 |

---

## 五、影響側寫

### 5.1 既存聽力測驗題目相容性

- 既有題目 `ArticleContent` 為 NULL/空字串 → 新表單顯示空「內容」欄位
- 老師若不填仍可保存（Quill 空字串）
- 預覽/審題介面對空 ArticleContent 不渲染（或顯示「（無）」）

### 5.2 相似題比對副作用

舊版 Stem 70% → 新版 Stem 20%（A 案），權重重分配後**舊題目的相似度分數會變動**。但因為演算法 algorithmVersion 機制（`SimilarityChecks.AlgorithmVersion`），新計算的相似度寫入新版本，舊資料保留供歷史參考。

### 5.3 統計與配額不受影響

ArticleContent 只是純內容欄位，不影響配額計算、採用率、Dashboard KPI。

---

## 六、實作順序

| 階段 | 內容 | 預估 |
|---|---|---|
| 1 | QuestionFormListen.razor 改題目 + 加 ArticleContent 欄位 + 新增參數 | 0.2 天 |
| 2 | CwtList 雙向綁定 | 0.1 天 |
| 3 | Service CRUD verify（必要時補）| 0.2 天 |
| 4 | 預覽 Modal + Reviews 顯示 + Overview 詳情 | 0.3 天 |
| 5 | QuillEditor h-[25vh] → h-[40vh] | 0.05 天 |
| 6 | SimilarityService 聽力測驗權重重算 | 0.1 天 |
| **合計** | | **約 1 天** |

---

## 七、驗證清單

| 項目 | 預期 |
|---|---|
| 命題教師開新聽力測驗 | 看到「題目」+「內容」+ 選項 + 解析（4 段 Quill 區）|
| 既有聽力測驗開編輯 | 「內容」欄位為空，可填可不填 |
| 點任一 Quill 欄位後底部滑入面板 | 高度撐到 40vh（比現在大）|
| 點命題完成存檔 | DB ArticleContent 寫入 |
| 預覽 Modal | 「內容」段在題目與選項之間 |
| 審題 Modal | 同上位置顯示 |
| Overview 詳情 | 同上位置顯示 |
| 相似題比對 | 新版本 algorithmVersion 寫入，分數使用新權重 |
| `dotnet build` | 0 警告 0 錯誤 |

---

**請拍板 Q5（相似題權重 A/B/C），我立即動工**。
