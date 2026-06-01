# Stored XSS 修補計畫書 — Quill / 公告 HTML 內容消毒

> **計畫日期**：2026-06-01
> **作者**：Jay 與 Claude 共同設計
> **狀態**：✅ 已實作（2026-06-01）— Q1 Ganss.Xss / Q2 架構 A / Q3 意見消毒 / Q4 移除外部圖片 全數拍板。
> Stage 1（HtmlSanitizationService + DI）+ Stage 2（4 服務寫入端接線）完成，dotnet build 0 警告 0 錯誤。
> **Stage 3（既有資料清洗）取消**：user 確認現有為假資料、上正式會先清空一次，新內容一律經消毒寫入路徑，無需遷移。
> **嚴重度**：🔴 CRITICAL（全站 22 個 `MarkupString` sink 渲染未消毒的使用者 HTML）
> **影響範圍**：新增 1 個共用 Service（消毒引擎）+ 4 個寫入端 Service 接線 + 既有資料一次性清洗 +（選配）讀取端縱深防禦
> **關聯審查**：`D:\jay_liu\Desktop\MT_codereview\MT_ClaudeReview.md`（CRITICAL-1）

---

## 一、問題本質（先講清楚為什麼非修不可）

Quill 編輯器存的是 `quill.root.innerHTML` 原始 HTML（`wwwroot/js/quill-interop.js:73`），**完全由使用者控制**。全站 22 個地方用 `@((MarkupString)...)` 把這段 HTML 原樣噴回瀏覽器，且**整個專案沒有任何 HTML sanitizer**。

**攻擊鏈**：
```
命題教師（最低權限）在題幹/選項/解析/文章/公告塞
  <img src=x onerror="fetch('/auth/logout');竊取cookie/冒用操作">
        ↓ 存檔入庫（原樣存）
審題委員 / 總召 / 系統管理員（高權限）打開審題、總覽、預覽、公告
        ↓ MarkupString 原樣渲染
惡意 JS 在「高權限者」的瀏覽器執行 → 權限提升 / 冒用身分 / 竊取資料
```

Quill 工具列只限制「編輯器 UI 能產出什麼」，擋不住：
- 手刻 HTTP POST 直接打 `/api/upload` 圖片 + 直接寫 DB 的內容欄位
- 透過 `/api/upload` 上傳 `.html`（見審查 CRITICAL-3，另案處理）後用 `<img>`/`<iframe>` 引用
- DB 被其他管道竄改

**結論**：必須在「伺服器端」用允許清單（allowlist）消毒，不能信任前端 Quill。

---

## 二、22 個 sink 清單（修補後須全部安全）

| 群組 | 檔案:行 | 渲染資料 |
|---|---|---|
| Quill 共用元件 | `Components/Shared/QuillField.razor:13` | 欄位內容 |
| | `Components/Shared/QuillEditor.razor:18` | 欄位預覽 |
| 公告 | `Components/Pages/Announcements.razor:584` | 公告 body |
| | `Components/Pages/Home.razor:293` | 公告 body（**所有登入者可見**）|
| 總覽 | `Components/Pages/Overview.razor:1229,1257` | 題幹/文章、子題/解析 |
| 審題顯示 | `Components/Shared/ReviewForms/ReviewQuestionDisplay.razor:197,387,406,432,522,616` | 解析、analysisHtml、stem、ArticleContent ×3 |
| 後台修訂 | `Components/Shared/RevisionForms/AdminReviseModal.razor:225` | ArticleContent |
| 題型預覽 | `Components/Shared/QuestionPreviews/PreviewChoiceOptions.razor:45` | 選項 |
| | `Components/Shared/QuestionPreviews/PreviewPassage.razor:21` | 文章 |
| | `Components/Shared/QuestionPreviews/PreviewSingle.razor:12` | Stem |
| | `Components/Shared/QuestionPreviews/PreviewListen.razor:15` | Stem |
| | `Components/Shared/QuestionPreviews/PreviewLongText.razor:12` | Stem |
| | `Components/Shared/QuestionPreviews/PreviewReadGroup.razor:34` | subStem |
| | `Components/Shared/QuestionPreviews/PreviewShortGroup.razor:31` | sq.Stem |
| | `Components/Shared/QuestionPreviews/PreviewListenGroup.razor:20,47` | Passage、sq.Stem |

> 共 22 sink。`Projects.razor:1619` 的 `GetValidationMessage()` 僅串硬編碼字面字串，**安全**，不在範圍。

---

## 三、技術選型 — 為什麼破例引入第三方套件

> **CLAUDE.md 核心原則第 1 條「零依賴優先」例外條款：「除非我明確要求使用特定套件」。本計畫主張這正是該破例的情境，請 user 拍板（Q1）。**

| 方案 | 評估 |
|---|---|
| **A. 自己手刻 sanitizer（regex / 字串處理）** | ❌ **強烈不建議**。HTML 消毒是公認的「不要自己寫」領域：mutation XSS（mXSS）、畸形標籤、編碼繞過、CSS 注入、`javascript:`/`data:` scheme、SVG 事件處理器…要全部擋住極難，regex 尤其脆弱。自寫等於把資安賭在自己沒想到的繞過手法上。 |
| **B. 引入 `Ganss.Xss`（HtmlSanitizer）** | ✅ **推薦**。.NET 生態事實標準、活躍維護、預設 deny-all + allowlist、內建處理 mXSS / scheme / CSS、可精準設定允許的 tag/attr/class/CSS 屬性。MIT 授權。單一相依、無傳遞依賴地獄。 |

**推薦 B**。資安防護用成熟函式庫是業界共識；這條原則的「明確要求」例外就是為這種情境存在。

```
dotnet add package Ganss.Xss
```

---

## 四、Allowlist 設計（必須精準對齊 Quill 實際產出，否則會誤刪老師的格式）

依 `quill-interop.js` 三套工具列（標準 / simple / full+image）盤點 Quill 2.x 實際產出的 HTML：

### 4.1 允許的標籤
`p`, `br`, `strong`, `b`, `u`, `s`, `em`, `span`, `ol`, `ul`, `li`, `img`

### 4.2 允許的屬性
| 標籤 | 屬性 | 限制 |
|---|---|---|
| `span` / `p` / `li` | `class` | 僅允許 `ql-` 前綴白名單值（見 4.3）|
| `span` | `style` | 僅允許 CSS 屬性 `color`（色彩選擇器產出 inline color）|
| `li` | `data-list` | Quill 2.x 的 ordered/bullet 清單會在 `<li>` 帶 `data-list`（**動工時以實際產出為準確認**）|
| `img` | `src`, `alt`, `width`, `height` | `src` 僅允許相對路徑 `uploads/...` 或同源（禁 `javascript:`/`data:`/外部網域）|

### 4.3 允許的 class（`AllowedClasses` 白名單）
- 字體：`ql-font-dfkai-sb`、`ql-font-times-new-roman`
- 字級：`ql-size-small`、`ql-size-large`
- 對齊：`ql-align-center`、`ql-align-right`、`ql-align-justify`
- 縮排：`ql-indent-1` ~ `ql-indent-8`
- 自訂雙底線：`ql-double-underline`

### 4.4 允許的 CSS 屬性
`color`（且值須通過 Ganss 內建顏色驗證；`expression()` / `url()` 一律擋）

### 4.5 允許的 URI scheme
保留相對路徑與 `https`/`http`（同源圖片）；**明確禁用** `javascript:`、`data:`、`vbscript:`。

> ⚠️ **關鍵風險**：allowlist 太嚴 → 寫入時誤刪老師既有合法格式（且若採寫入時消毒，這是**不可逆**的內容破壞）。因此 4.1-4.5 必須在動工時用「實際產出 HTML 樣本」逐項對拍，**先 dry-run（只 diff 不寫入）確認只移除惡意/雜訊、不動合法格式，再正式套用**。

---

## 五、架構決策（請拍板 Q2）— 寫入時 vs 讀取時 vs 縱深防禦

| 方案 | 做法 | 優點 | 缺點 |
|---|---|---|---|
| **A. 寫入時消毒**（推薦主軸）| 在 4 個寫入端 Service 存檔前消毒 | DB 一律乾淨；渲染端 0 改動 → **無顯示破版風險**；每次存檔才跑一次（效能最佳）| ① 須清洗既有資料（一次性 migration）② 漏接任一寫入路徑就破功 ③ allowlist 太嚴會**不可逆**破壞內容 |
| **B. 讀取時消毒** | 改 22 個 sink 走共用 `<SafeHtml>` 元件 | 原始內容保留（可逆）；防 DB 竄改、防漏接寫入路徑 | 22 處改動；每次渲染都跑（效能成本，須用內容 hash 快取）；可能顯示被過濾後樣貌 |
| **C. A + B 縱深防禦** | 寫入清乾淨 + 讀取端再當安全網 | 最穩 | 工作量最大 |

**推薦：A 為主 + 既有資料一次性清洗**，理由：渲染端零改動 = 零破版風險、效能最佳、與「極致效能」原則一致。
**若 user 重視縱深防禦** → 升級為 C：保留寫入時消毒，另加一個共用 `<SafeHtml Value="@x"/>` 元件取代 22 處 `(MarkupString)`，渲染端再過濾一次（內容 hash 快取避免重算）。

> 我的傾向：先做 A（含 dry-run 驗證 allowlist 不誤刪），上線穩定後若要再補 B 的 `<SafeHtml>` 安全網成本很低。但這是 user 的資安胃納決策。

---

## 六、實作清單

### 6.1 新增（1 檔，嚴格三檔精神之共用 Service）

| 檔案 | 內容 | LOC |
|---|---|---|
| `Services/HtmlSanitizationService.cs` | `IHtmlSanitizationService.Sanitize(string? html)`；建構時依 4.1-4.5 設定 `Ganss.Xss.HtmlSanitizer` 一次（Singleton 重用，HtmlSanitizer 執行緒安全）；null/空字串原樣回傳 | ~70 |

### 6.2 修改 — DI 註冊（1 行）

| 檔案 | 改動 |
|---|---|
| `Program.cs` | `builder.Services.AddSingleton<IHtmlSanitizationService, HtmlSanitizationService>();`（無狀態、設定一次、可 Singleton）|

### 6.3 修改 — 寫入端接線（方案 A 主軸）

| Service | 方法 | 須消毒的欄位 |
|---|---|---|
| `QuestionService.cs` | `CreateAsync`(251)、`UpdateAsync`(325)、`SaveRevisionAsync`(2396) | `Stem`、`Analysis`、`OptionA~D`、`ArticleTitle`*、`ArticleContent`、`AudioTranscript`（*Title 若為純文字可只 strip） |
| | `InsertSubQuestionsAsync`、`UpsertSubQuestionsAsync`（子題寫入 helper）| 子題 `Stem`、`OptionA~D`、`Analysis` |
| `AnnouncementService.cs` | `CreateAsync`(158)、`UpdateAsync`(227) | `Content`（`Title` 視需求 strip）|
| `ReviewService.cs` | `FinalReviewerEditAndDecideAsync`(964)、`UpsertFinalSubQuestionsAsync` | 後台改題的內容欄位（同 QuestionService 欄位集）|
| `RevisionService.cs` | `SaveAsync`(99) → `UpdateMasterContentAsync`、子題內容 | 同上 |

> 接線方式：注入 `IHtmlSanitizationService`，在組 Dapper 參數**前**對每個富文本欄位呼叫 `Sanitize(...)`。集中在「即將入庫」這一點，避免散落。
> **審題意見（Quill simple 工具列）**：`SaveCommentDraftAsync`(580)、`SubmitDecisionAsync`(688) 的意見欄位即使目前多以文字呈現，仍建議一併消毒（simple 工具列只產 bold/underline/strike/list，allowlist 子集即可）— 廉價保險，避免日後意見改用 MarkupString 渲染時破功。（請拍板 Q3 是否納入）

### 6.4 既有資料一次性清洗（方案 A 必做）

| 項目 | 做法 |
|---|---|
| 範圍 | `MT_Questions`（Stem/Analysis/OptionA~D/ArticleContent/AudioTranscript）、`MT_SubQuestions`（同欄位集）、`MT_Announcements`（Content）|
| 形式 | 一次性 C# 清洗程序（建議：受權限保護的一次性 admin 動作或啟動旗標一次性執行，跑完即移除），逐列讀 → `Sanitize` → 僅在「消毒後 ≠ 原值」時 UPDATE |
| **安全閥** | **先 dry-run**：只計算並輸出「有幾列會被改、改了什麼（diff 摘要）」，由 user 確認移除的都是惡意/雜訊、無合法格式被誤刪，**再正式寫入** |
| 稽核 | 清洗屬系統維運，不寫使用者稽核；保留執行 log（影響列數）|

### 6.5 （選配）方案 C 讀取端安全網

| 檔案 | 改動 |
|---|---|
| `Components/Shared/SafeHtml.razor`（新）| `[Parameter] string? Value`；內部 `Sanitize` 後輸出 `(MarkupString)`；以內容 hash 做 per-render 快取避免重算 |
| 22 個 sink | `@((MarkupString)x)` → `<SafeHtml Value="@x" />` |

---

## 七、邊界條件 / Gotcha

| 案例 | 處理 |
|---|---|
| Quill 2.x list 產出 `<li data-list="bullet/ordered">` | allowlist 須允許 `<li>` 的 `data-list`；**動工時以實際 innerHTML 為準**，避免清單被消毒成無序 |
| 色彩 inline style `color: rgb(...)` | `AllowedCssProperties = {color}`；Ganss 會驗證顏色值、擋 `expression()` |
| 圖片相對路徑 `uploads/{guid}.png` | sanitizer 預設保留相對 URL；明確擋 `data:`/`javascript:`；`src` 為外部網域時依 Q4 決定保留或移除 |
| 自訂 `ql-double-underline` span | 必須列入 `AllowedClasses`，否則雙底線消失 |
| 既有資料含「合法但 allowlist 未涵蓋」的格式 | dry-run diff 階段就會浮現 → 補進 allowlist 再正式清洗（避免不可逆破壞）|
| 空字串 / `<p><br></p>` | `Sanitize` 對 null/空白原樣回傳，不產生差異；與 `getNormalizedHtml` 既有空值約定一致 |
| 字數統計 / 相似度比對 | 皆吃純文字（既有 StripHtml），消毒只動 HTML 標籤不影響文字內容 → 統計與相似度分數不變 |
| 效能 | 方案 A 只在存檔時跑一次，對年長使用者的編輯體驗無感；HtmlSanitizer 單次 < 1ms |

---

## 八、Verification Plan

| # | 案例 | 步驟 | 預期 |
|---|---|---|---|
| 1 | 惡意 payload 被中和 | 題幹存 `<img src=x onerror=alert(1)>`，存檔後開審題/預覽 | `onerror` 被移除，不執行；`<img>`（若 src 合法）保留或整個移除，無 JS |
| 2 | `<script>` / `<iframe>` | 內容塞 `<script>`、`<iframe>` | 完全移除 |
| 3 | `javascript:` 連結 | `<a href="javascript:...">` | 移除 scheme 或整個 href |
| 4 | 合法格式不受損 | 粗體/底線/雙底線/字體/字級/顏色/對齊/縮排/有序無序清單/圖片 | 全部保留，顯示與消毒前一致 |
| 5 | 既有資料 dry-run | 跑清洗 dry-run | 輸出受影響列數 + diff，確認只移惡意/雜訊 |
| 6 | 既有資料正式清洗 | 確認後執行 | 受影響列更新、合法內容 0 損失 |
| 7 | 公告渲染 | 公告 body 塞 payload，於 Home/Announcements 檢視 | 中和 |
| 8 | 子題 | 短文/閱讀/聽力題組子題塞 payload | 母題與子題皆中和 |
| 9 | 後台改題 | 總召 FinalReviewerEdit / RevisionService 改題塞 payload | 中和 |
| 10 | 圖片正常 | 正常透過工具列上傳圖片 | `<img src="uploads/...">` 保留、正常顯示 |
| 11 | Build | `dotnet build` | 0 警告 0 錯誤 |
| 12 | （方案 C）DB 直接竄改 | 手動在 DB 寫入 payload，開頁面 | 讀取端 `<SafeHtml>` 仍中和 |

---

## 九、實作階段（建議分段 commit）

| 階段 | 內容 | 預估 | 可獨立 |
|---|---|---|---|
| 1 | 引入 Ganss.Xss + `HtmlSanitizationService` + Allowlist + DI + 單元驗證（用樣本 HTML 跑案例 1-4）| 0.5 天 | ✅（功能尚未接線）|
| 2 | 寫入端接線（QuestionService / AnnouncementService / ReviewService / RevisionService）| 0.5 天 | ✅（新內容開始乾淨）|
| 3 | 既有資料 dry-run → user 確認 → 正式清洗 | 0.5 天 | ✅ |
| 4 |（選配 C）`SafeHtml` 元件 + 22 sink 改接 | 0.5 天 | ✅ |
| 5 | 驗證 + 修正 | 0.5 天 | — |
| **合計** | 方案 A：~2 天；方案 C：~2.5 天 | | |

---

## 十、待 user 拍板

| # | 議題 | 選項 | 推薦 |
|---|---|---|---|
| **Q1** | 是否破例引入 `Ganss.Xss`？ | A 引入（推薦）/ B 堅持自寫 | **A** — 資安消毒不該自寫 |
| **Q2** | 架構 | A 寫入時消毒 + 既有資料清洗（推薦）/ B 讀取時消毒 / C 縱深防禦（A+B）| **A**（重縱深防禦則選 C）|
| **Q3** | 審題意見欄位是否一併消毒？ | A 納入（廉價保險）/ B 暫不 | **A** |
| **Q4** | 內容內的外部網域圖片 `<img src="https://其他站">` | A 移除（只准同源 uploads）/ B 保留 https 外部圖 | **A** — 防 SSRF/追蹤像素，且本站圖片本就走 `/api/upload` |

---

**本計畫僅評估與規劃，未修改任何程式碼。** 請 user 拍板 Q1-Q4 後，我依階段分段 commit，每階段 `dotnet build` + 自驗 + 跟您確認後才進下一階段。動工前先用實際 Quill 產出 HTML 對拍 allowlist（4.1-4.5），確保不誤刪合法格式。
