# 相似度分析功能計畫書

> **計畫日期**：2026-05-19
> **作者**：Jay 與 Claude 共同設計
> **狀態**：✅ DB Schema v2 已部署 — 等待 Service / UI 實作授權
> **對應規格**：cwt-prop-rules.md（命題）、cwt-ex-rules.md（審題）、prd-cwt-proposition-platform.md US-007/US-008/US-009
> **DB Migration**：`.claude/rules/sql/migrate_similarity_checks_v2.sql`（已執行）

---

## 一、功能定位

「相似度分析」是命題品質管控功能，目的是在試題進入題庫前**自動偵測並標記**疑似重複的題目，避免：
- 同梯次內重複出題（最常見）
- 抄錄歷史已採用題（次常見）
- 自抄（教師命過類似題卻忘記）

**核心觸發模型：三軌混合**

| 軌道 | 觸發時機 | 是否寫 DB | 服務對象 |
|---|---|---|---|
| ① 自動寫入 | CwtList「命題送審」/「完成修題」鈕後 | ✅ | 審題端 Banner / 管理員報表 |
| ② 命題教師主動 | CwtList Modal 內【🔍 比對相似題】鈕 | ❌（記憶體即算即拋）| 命題教師自查 |
| ③ 管理員批次 | SimilarityAnalysis 頁【🔄 重新掃描】鈕 | ✅（覆寫）| 結案前最後把關 |

---

## 二、演算法規格

### 2.1 核心：4-gram + Jaccard 加權

**步驟**：
1. `StripHtmlFull`（已存於 ReviewService）→ 文字正規化（去空白、繁簡同化、全形半形統一）
2. 4-gram 滑動窗切分 → `HashSet<string>`
3. Jaccard = `|A ∩ B| / |A ∪ B|`
4. 各區塊加權平均 → 0~1 浮點 → ×100 寫入 `decimal(5,2)`

### 2.2 七題型加權表

| TypeId | 題型 | 比對欄位 + 權重 |
|---|---|---|
| 1 | 一般題目 | Stem 70% + (OptionA+B+C+D 串接) 30% |
| 2 | 精選題目 | 同一般 |
| 3 | 短文題組（母題層）| ArticleContent 60% + 全子題 Stem 串接 40% |
| 4 | 長文題目 | ArticleContent 90% + Title 10% |
| 5 | 閱讀題組（母題層）| ArticleContent 50% + 全子題 Stem 串接 30% + 全子題選項串接 20% |
| 6 | 聽力題目 | AudioTranscript 70% + Stem 20% + 四選項 10% |
| 7 | 聽力題組（母題層）| AudioTranscript 70% + 2 固定子題 Stem 串接 30% |

**子題層比對**（僅 TypeId 3/5/7 觸發）：
- 短文/閱讀子題：Stem 70% + 四選項 30%
- 聽力題組子題：Stem 100%（無選項變動空間）

### 2.3 判定門檻

| Jaccard ×100 | Determination | 顏色 | 寫 DB？ |
|---|---|---|---|
| ≥ 80 | 3 確認重複 | 🔴 紅 | ✅ |
| ≥ 60 | 2 相似度高 | 🟠 橘 | ✅ |
| ≥ 40 | 1 安全 | 🟡 黃 | ✅（供參考）|
| < 40 | — | — | ❌（節省空間）|

### 2.4 比對範圍

**必比**：同 ProjectId + 同 QuestionTypeId + 同 ExamLevel + IsDeleted=0 + Id ≠ Source + Status NOT IN (0=草稿, 1=命題完成)

**選比**（暫不做，留 Phase 6）：跨梯次同題型 + Status=10 採用

**迴避**：不跟自己比；不比草稿/命題完成（題目尚未定型）

---

## 三、檔案規劃（嚴格三檔規則）

### 新增（5 個）

| 檔案 | 用途 | 行數預估 |
|---|---|---|
| `Services/SimilarityService.cs` | 計算引擎 + DB 寫入 + 批次掃描 | ~450 |
| `Models/SimilarityModels.cs` | DTO、批次結果、Top-N 結果 | ~120 |
| `Components/Pages/SimilarityAnalysis.razor` | 管理員批次掃描頁（路由 `/similarity-analysis`） | ~400 |
| `Components/Shared/SimilarityCompareModal.razor` | 並排檢視 Modal（兩題雙欄並列） | ~200 |
| `Components/Shared/SimilarityResultPanel.razor` | Top-N 結果面板（命題教師按鈕觸發顯示）| ~150 |

### 修改（5 個）

| 檔案 | 修改點 |
|---|---|
| `Services/QuestionService.cs` | `SubmitForReviewAsync` / `CompleteRevisionAsync` 加 fire-and-forget 呼叫 `SimilarityService.ComputeAndPersistAsync` |
| `Components/Pages/CwtList.razor` | 編輯 Modal 右上加【🔍 比對相似題】鈕；命題已送審題目 Modal 內補 Banner |
| `Components/Pages/Overview.razor` | 詳情 SlideOver 加相似題區塊（複用 `ReviewSimilarityBanner.razor`）|
| `Services/DashboardService.cs` | KPI 區塊加「本梯次重複風險題對數」卡片 |
| `Program.cs` | 註冊 `ISimilarityService` Scoped |

### 不動（既有資產複用）

- ✅ `Components/Shared/ReviewForms/ReviewSimilarityBanner.razor`：三方共用（命題端、審題端、管理員）
- ✅ `Components/Shared/ReviewForms/ReviewActionPanel.razor`：審題 Modal Banner 已接好
- ✅ `Models/ReviewModels.cs:ReviewSimilarityEntry`：審題端 DTO 不變
- ✅ `Services/ReviewService.cs:LoadSimilaritiesAsync`：審題端讀取邏輯不變

---

## 四、Service 介面設計

### `ISimilarityService`

```csharp
public interface ISimilarityService
{
    // Tier 1: 命題送審/完成修題後背景寫入
    // 完整流程：抽取文字 → 取比對範圍 → 平行計算 → UPSERT MT_SimilarityChecks
    // fire-and-forget 模式呼叫，失敗只 LogWarning，不擋使用者操作
    Task ComputeAndPersistAsync(int questionId, int operatorId, CancellationToken ct = default);

    // Tier 2-A: 命題教師編輯時即時計算 (Modal 內【比對相似題】鈕)
    // 不寫 DB，只回 Top-N 結果給命題教師預覽
    // 用 QuestionDraftSnapshot（含未存檔內容），避免必須先存草稿才能比對
    Task<IReadOnlyList<SimilarityCompareResult>> ComputeOnDemandAsync(
        QuestionDraftSnapshot draft, int topN = 5);

    // Tier 2-B: 讀取已寫入的相似度結果 (命題端「已送審」題目 Modal 顯示用)
    Task<IReadOnlyList<SimilarityCompareResult>> GetSimilarityResultsAsync(int questionId);

    // Tier 3-A: 管理員批次掃描全梯次
    // 進度回報透過 IProgress<BatchScanProgress>，UI 端可顯示進度條
    Task<BatchScanResult> RunBatchScanAsync(
        int projectId, int operatorId,
        IProgress<BatchScanProgress>? progress = null,
        CancellationToken ct = default);

    // Tier 3-B: Dashboard KPI 卡片計數
    Task<int> CountRiskyPairsAsync(int projectId, decimal minScore = 60m);

    // Tier 3-C: 列表（批次頁主表）
    Task<IReadOnlyList<SimilarityPairItem>> ListPairsAsync(
        int projectId, decimal minScore = 40m,
        int? questionTypeId = null);
}
```

### Models（`SimilarityModels.cs`）

```csharp
// 比對結果單筆（給命題端 Modal 顯示）
public class SimilarityCompareResult
{
    public int ComparedQuestionId { get; set; }
    public string ComparedQuestionCode { get; set; } = "";
    public decimal Score { get; set; }              // 0-100
    public byte Determination { get; set; }         // 1/2/3
    public string SummaryText { get; set; } = "";   // StripHtml 後的題幹摘要
    public int? SubQuestionId { get; set; }         // NULL = 母題層
    public string CreatorName { get; set; } = "";   // 命題者
}

// 批次掃描進度回報
public class BatchScanProgress
{
    public int Total { get; set; }
    public int Processed { get; set; }
    public int RiskyFound { get; set; }
}

// 批次掃描結果摘要
public class BatchScanResult
{
    public int TotalQuestions { get; set; }
    public int TotalPairs { get; set; }       // 實際算的配對數
    public int WrittenCount { get; set; }     // 寫入 DB 的（≥ 40）
    public int RiskyCount { get; set; }       // 高風險（≥ 60）
    public TimeSpan Duration { get; set; }
}

// 批次掃描列表項（管理員頁主表）
public class SimilarityPairItem
{
    public int SourceQuestionId { get; set; }
    public string SourceQuestionCode { get; set; } = "";
    public int ComparedQuestionId { get; set; }
    public string ComparedQuestionCode { get; set; } = "";
    public int? SourceSubQuestionId { get; set; }
    public int? ComparedSubQuestionId { get; set; }
    public decimal Score { get; set; }
    public byte Determination { get; set; }
    public DateTime CheckedAt { get; set; }
    public string SourceCreatorName { get; set; } = "";
    public string ComparedCreatorName { get; set; } = "";
}

// 即時計算用 — 未必有 QuestionId（草稿）
public class QuestionDraftSnapshot
{
    public int? QuestionId { get; set; }      // 編輯中題目 Id (排除自己)
    public int ProjectId { get; set; }
    public int QuestionTypeId { get; set; }
    public int ExamLevel { get; set; }
    public string Stem { get; set; } = "";
    public string? ArticleContent { get; set; }
    public string? Title { get; set; }
    public string? OptionA { get; set; }
    public string? OptionB { get; set; }
    public string? OptionC { get; set; }
    public string? OptionD { get; set; }
    public string? AudioTranscript { get; set; }
    public List<SubQuestionSnapshot> SubQuestions { get; set; } = new();
}

public class SubQuestionSnapshot
{
    public int? SubQuestionId { get; set; }
    public string Stem { get; set; } = "";
    public string? OptionA { get; set; }
    public string? OptionB { get; set; }
    public string? OptionC { get; set; }
    public string? OptionD { get; set; }
}
```

---

## 五、UI/UX 設計

### 5.1 軌道 ② — 命題教師主動按鈕

**位置**：CwtList 編輯 Modal 右上，緊鄰「預覽」鈕
**樣式**：莫蘭迪藍 + 放大鏡圖示，文案『🔍 比對相似題』

**互動流程**：
1. 點下後按鈕變 disabled + spinner，文案改為「比對中...」
2. 後端跑 `ComputeOnDemandAsync(draft, 5)` 約 1-2 秒
3. 從畫面**右側滑入** `SimilarityResultPanel`（不是 Modal，因為已在 Modal 內，避免多層彈窗）
4. 面板內容：
   - 標題：「找到 N 道相似題目」（N=0 時顯示鼓勵語「未發現相似題，安心送審 👍」）
   - 每筆顯示：色標 + 題目代號 + 命題者 + 分數（百分比）+ 題幹摘要（2-3 行）
   - 每筆右側「並排檢視」鈕，點下開 `SimilarityCompareModal`
5. 關閉鈕 / 點背景關閉面板，不影響當前編輯狀態

### 5.2 軌道 ① — 自動寫入後的審題端 Banner

**位置**：審題 Modal 頂部（已存在於 `ReviewActionPanel.razor`，不動）
**新增**：高度警示時（≥ 1 筆 Determination=3）Banner 變紅色橫條，附「全部標重複可影響審題判決」一行說明

### 5.3 軌道 ③ — 管理員批次掃描頁

**路由**：`/similarity-analysis`
**權限**：併入既有「命題總覽」模組（不新增權限項）

**版面**：
```
┌─ 頁面頭部 ────────────────────────────────────────────┐
│ [命題總覽] > [相似度分析]                              │
│ 標題：本梯次相似度分析報告                              │
│ 副標：最後掃描時間：2026-05-19 14:32  共 3,185 對     │
│ 右上：【🔄 重新掃描本梯次】（disabled 時顯示倒數）     │
└──────────────────────────────────────────────────────┘

┌─ KPI 列 (4 張卡片) ──────────────────────────────────┐
│  總配對   高風險(紅)   中風險(橘)   低風險(黃)        │
│   3,185      12           28           45             │
└──────────────────────────────────────────────────────┘

┌─ 篩選列 ─────────────────────────────────────────────┐
│ 題型: [全部▼]  風險: [全部▼]  關鍵字: [        ]      │
└──────────────────────────────────────────────────────┘

┌─ 結果列表 (Pagination 10/頁) ──────────────────────┐
│ # | 題1 | 題2 | 分數 | 等級標籤 | 命題者 | 操作      │
│ 1 │ Q-001 │ Q-045 │ 87.3 │ 🔴 重複 │ 王教師 / 李教師 │ [並排檢視]│
│ 2 │ Q-002 │ Q-103 │ 72.1 │ 🟠 高 │ 王教師 / 陳教師   │ [並排檢視]│
│ ...
└──────────────────────────────────────────────────────┘
```

### 5.4 SimilarityCompareModal — 並排檢視

**全螢幕雙欄 Modal**：
```
┌────────────────┬────────────────┐
│ Q-001 王教師   │ Q-045 李教師   │
│ ──────────     │ ──────────     │
│ [題幹]         │ [題幹]         │
│ 紅字 highlight │ 紅字 highlight │
│ 相同片段       │ 相同片段       │
│ ──────────     │ ──────────     │
│ (A) 選項...    │ (A) 選項...    │
│ (B) 選項...    │ (B) 選項...    │
│ ...            │ ...            │
└────────────────┴────────────────┘
          [關閉]
```

**HighLight 規則**：找出兩題的共同 4-gram，將共同片段以 `<mark class="bg-red-100">` 包起來顯示。

### 5.5 Dashboard KPI 卡

新增一張卡：
- 標題：本梯次重複風險題對
- 主數字：12 對（高風險）
- 副資訊：另有 28 對中度相似
- 顏色：當主數字 > 0 紅色，= 0 鼠尾草綠
- 點擊跳轉 `/similarity-analysis`

---

## 六、Algorithm 內部結構

### `internal static class NGramJaccard`

```csharp
// 切 4-gram
public static HashSet<string> ToGrams(string text, int n = 4);
//  範例: "下列何者為臺灣" → ["下列何者", "列何者為", "何者為臺", "者為臺灣"]

// 兩集合 Jaccard
public static decimal Jaccard(HashSet<string> a, HashSet<string> b);
//  return decimal.Divide(|A∩B|, |A∪B|);

// 整合 + 加權平均
public static decimal WeightedJaccard(
    IReadOnlyList<(HashSet<string> Grams, decimal Weight)> a,
    IReadOnlyList<(HashSet<string> Grams, decimal Weight)> b);
```

### `internal static class QuestionTextExtractor`

```csharp
// 7 題型統一抽取介面
public static QuestionTextBundle Extract(QuestionEntity q, IReadOnlyList<SubQuestionEntity> subs);

public class QuestionTextBundle
{
    // 母題層 — 7 題型都會填
    public IReadOnlyList<(HashSet<string> Grams, decimal Weight)> MasterFields { get; set; }

    // 子題層 — 僅 TypeId 3/5/7 填
    public IReadOnlyList<SubQuestionGrams> SubQuestions { get; set; }
}
```

### 文字正規化

```csharp
public static string Normalize(string raw)
{
    // 1. StripHtml (用 ReviewService.StripHtmlFull 或抽到 TextUtil)
    // 2. 全形 → 半形 (英數標點)
    // 3. 繁簡同化 — 暫不處理 (中文檢定都是繁體，影響極小)
    // 4. 去除多空白
    // 5. 標點不變 (4-gram 會自然涵蓋)
}
```

---

## 七、UPSERT 策略

### 寫入規則

對於每對 (Source, Compared)：
1. SQL 嘗試 INSERT
2. 撞 UNIQUE 索引 → `SqlException 2601/2627` → 改 UPDATE Score/Determination/CheckedAt
3. 用 `MERGE` statement 寫成一個 round-trip 比較安全：

```sql
MERGE dbo.MT_SimilarityChecks WITH (HOLDLOCK) AS tgt
USING (SELECT @SourceQ, @ComparedQ, @SourceSub, @ComparedSub, @AlgVer) src(SQ, CQ, SSQ, CSQ, AV)
ON tgt.SourceQuestionId = src.SQ AND tgt.ComparedQuestionId = src.CQ
   AND ISNULL(tgt.SourceSubQuestionId, -1) = ISNULL(src.SSQ, -1)
   AND ISNULL(tgt.ComparedSubQuestionId, -1) = ISNULL(src.CSQ, -1)
   AND tgt.AlgorithmVersion = src.AV
WHEN MATCHED THEN
    UPDATE SET SimilarityScore = @Score, Determination = @Det,
               CheckedBy = @Operator, CheckedAt = SYSDATETIME()
WHEN NOT MATCHED THEN
    INSERT (SourceQuestionId, ComparedQuestionId, SourceSubQuestionId,
            ComparedSubQuestionId, SimilarityScore, Determination,
            CheckedBy, CheckedAt, AlgorithmVersion)
    VALUES (src.SQ, src.CQ, src.SSQ, src.CSQ, @Score, @Det,
            @Operator, SYSDATETIME(), src.AV);
```

### 批次寫入

批次掃描時用 TVP 或多列 `INSERT...VALUES (),(),()` 一次寫入：
- 預估 500 題梯次寫入 ~100-200 筆（過濾掉 < 40 分的）
- 單次 SQL 一次寫完，避免 N round-trip

---

## 八、效能預估

### Tier 1 自動寫入（單題送審）
- 比對範圍：同梯次同題型同等級 ≈ 14 題（平均）
- 文字處理 + 4-gram + Jaccard：~10ms/對
- 14 對 × 10ms = ~140ms
- + 文字抽取 + DB 寫入 ~100ms
- **總計 ~250ms** — fire-and-forget 對使用者完全無感

### Tier 2 主動按鈕（即時計算）
- 同上，但不寫 DB → 約 ~150ms
- 加 UI 動畫總共 ~1.5 秒，可接受

### Tier 3 批次掃描（500 題梯次）
- 共 ~3,185 對配對（過濾後）
- Parallel.ForEach × 8 cores：~5 秒計算
- + 寫入 ~200 筆到 DB：~500ms
- **總計 ~6-8 秒** — 進度條期間使用者可看著跑

---

## 九、實作階段與里程碑

| 階段 | 內容 | 對應檔案 | 工時 | 可獨立部署？ |
|---|---|---|---|---|
| **Phase 1** | DB Migration v2（done）+ Service 骨架 + 演算法核心 | `SimilarityService.cs`、`SimilarityModels.cs`、`Program.cs` 註冊 | 3-4h | ✅（無 UI 變動，但功能不可見）|
| **Phase 2** | Tier 1 自動寫入（QuestionService 整合）| `QuestionService.cs` 兩處 fire-and-forget | 1.5h | ✅（DB 開始有資料，但 UI 還沒讀）|
| **Phase 3** | Tier 2 命題端 Modal Banner 與【比對相似題】鈕 | `CwtList.razor`、`SimilarityResultPanel.razor` | 3-4h | ✅（命題教師可看到自查結果）|
| **Phase 4** | Tier 3 批次掃描頁 + Dashboard KPI 卡 | `SimilarityAnalysis.razor`、`DashboardService.cs` | 4-5h | ✅（管理員可看全梯次）|
| **Phase 5** | 並排檢視 Modal + highlight | `SimilarityCompareModal.razor` | 2-3h | ✅（UX 加強）|
| **Phase 6**（暫不做）| 跨梯次同題型已採用題比對 | Service 加 ScanCrossProject 方法 | 2-3h | ✅（選用功能）|
| **合計 Phase 1-5** | | | **13-17h** | 5 個獨立 commit |

---

## 十、Verification Plan

| 測試案例 | 步驟 | 預期結果 |
|---|---|---|
| 演算法正確性 | 兩道高度雷同題（手寫範例）餵入 NGramJaccard | 分數 ≥ 70 |
| 7 題型加權 | 7 種題型各放 2 道相似題 | 短文/閱讀題組類分數受子題影響 |
| UNIQUE 防重複 | 同對題目連跑 2 次寫入 | DB 只有 1 筆，CheckedAt 更新 |
| 子題層比對 | 短文 Q1 子題 3 vs 短文 Q2 子題 1 | DB 寫入時 SubQuestionId 雙非 NULL |
| 自比擋掉 | Q1 vs Q1 嘗試寫入 | CHECK 約束拒絕 |
| 母 vs 子混比擋掉 | 一筆 SourceSubQuestionId=null, ComparedSubQuestionId=5 | CHECK 約束拒絕 |
| Tier 1 fire-and-forget | 命題教師按「命題送審」 | UI 立刻成功跳轉，背景跑完寫入 |
| Tier 1 計算失敗不擋送審 | 故意製造 DB 錯誤（如斷網） | 送審成功，Log 有 Warning |
| Tier 2 即時比對 | Modal 內按【比對相似題】 | 1-2 秒後右側面板滑入 Top-5 |
| Tier 2 不寫 DB | Tier 2 跑完查 DB | 無新增列（軌道 ② 設計如此）|
| Tier 3 批次掃描 | 點【🔄 重新掃描】 | 進度條跑、結果列表更新、CheckedAt 全部新 |
| Dashboard 卡片 | 跑完掃描後看 Dashboard | 顯示正確「N 對重複風險」|
| 並排檢視 | 列表點「並排檢視」 | Modal 開啟，紅字 highlight 相同 4-gram |
| Build | 每階段完成 | `dotnet build` 0 警告 0 錯誤 |
| 外部教師權限 | 命題教師點 `/similarity-analysis` URL | 403 或導向首頁（無權限）|

---

## 十一、待決議事項（請使用者拍板）

| # | 議題 | 選項 | 推薦 |
|---|---|---|---|
| Q1 | 是否做 Tier 3 批次掃描頁？ | A 做完整版（Phase 4）<br>B 簡化版（只 Dashboard 卡片+列表 Modal）<br>C 完全不做 | A — 結案前最後把關必須 |
| Q2 | Tier 1 觸發時機 | A 只在「命題送審」<br>B 加上「完成修題」<br>C 也在「存為草稿」 | B — 修題後內容差異大需重算 |
| Q3 | 跨梯次同題型已採用題比對（Phase 6）| 現在做 / 之後做 / 永不做 | 之後做（看 Phase 1-5 跑完後是否需要）|
| Q4 | 比對結果頁面權限歸屬 | A 併入「命題總覽」模組<br>B 新增第 9 模組 | A — 角色重疊度高 |
| Q5 | < 40 分要不要寫 DB？ | A 完全不寫（省空間）<br>B 寫但 UI 不顯示<br>C 全寫全顯示 | A — 噪音太多反而干擾 |
| Q6 | 演算法版本升級時的舊資料處理 | A 廢棄留檔（AlgVer=1 永久保留）<br>B 自動刪除 v=1 | A — 留歷史，按 AlgVer 過濾 |

---

## 十二、風險與緩解

| 風險 | 影響 | 緩解 |
|---|---|---|
| 4-gram 對非常短的題目（< 8 字）失準 | 小型聽力題誤判 | 加 fallback：< 8 字題目用編輯距離；目前實作先不處理，觀察 |
| Parallel.ForEach 在大量寫入時鎖表 | 批次掃描中其他寫入卡住 | 寫入改用單一 INSERT 多列、批次 100/次 |
| 教師連續按【比對相似題】鈕 | DB 重複 hit（雖不寫但會 SELECT 比對範圍）| Razor 端 debounce 1 秒 + button disabled 期間擋住 |
| Fire-and-forget 失敗無使用者察覺 | DB 缺資料 | ILogger.LogWarning 寫入 + Dashboard 顯示「上次掃描時間」讓管理員手動補掃 |
| 草稿 Snapshot 沒 QuestionId 時無法排除自己 | 教師對著自己未存的題目自比 | 用 `QuestionId == null` 判斷跳過自比邏輯，反正不會撞到 |

---

## 十三、後續改善（非本次範圍）

- 平行化 Jaccard 計算（CPU-bound，已建議用 Parallel.ForEach 但實作可先序列）
- LSH 索引加速大型題庫（題庫破萬題時才需要）
- 中文斷詞 + TF-IDF（jieba.NET）取代字面 N-gram
- 向量嵌入語意比對（OpenAI Embedding / ML.NET）
- 跨梯次題庫比對（Phase 6）
- 報表匯出 Excel/PDF

---

## 十四、技術債註記

本次完成後將產生以下技術債：
- **SC-01**：教師端 Modal 內【比對相似題】鈕的結果不寫 DB，如果同教師多人同時編輯相似題，無法事先協調 — 後續可考慮在伺服器端短暫快取結果
- **SC-02**：跨梯次比對未做（Phase 6），結案入庫題與舊題庫無法關聯 — 等實際運作後評估需求
- **SC-03**：並排檢視 Modal 的 highlight 用前端 JS 直接掃 4-gram，未抽離為共用 Util — 待第二個 highlight 需求出現時再抽
- **SC-04**：批次掃描沒有「取消」按鈕，跑到一半無法中斷 — Phase 4 可加 CancellationToken 整合 SignalR
