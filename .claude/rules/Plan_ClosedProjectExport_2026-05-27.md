# 命題專案結案資料 Excel 匯出計畫書

> **計畫日期**：2026-05-27
> **作者**：Jay 與 Claude 共同設計
> **狀態**：⏳ 等待 Q9 釐清 → 通過後動工
> **影響範圍**：Projects.razor 既有「下載結案資料」按鈕 + ProjectService 新增匯出 SQL/NPOI 組裝
> **參考 DEMO**：`D:\jay_liu\Desktop\MT_OM\結案資料DEMO.xlsx`

---

## 一、需求拍板（Q1-Q8 已確認）

| # | 議題 | 結論 |
|---|---|---|
| Q1 | 互審/專審/總審多位委員 | **不會有一題多位**，每 stage 一位即可 |
| Q2 | 題組類展開 | **母題 + 各子題各 1 對 row**；B 欄子題用「閱讀子題」「短文子題」「聽力子題」；D 欄(命師) 子題=母題同一人 |
| Q3 | 時間欄對應 DB 欄位 | 最後修題=`MT_Questions.UpdatedAt` / 子題=`MT_SubQuestions.UpdatedAt`；審題時間=`MT_ReviewAssignments.CreatedAt`；決策時間=`MT_ReviewAssignments.DecidedAt` |
| Q4 | 題目摘要規則 | `StripHtml(Stem)` 取前 40 字+「…」；Stem IS NULL 則用 ArticleContent |
| Q5 | 採用/不採用顏色 | Decision: 1=採用(綠粗體) / 2=改後採用(綠粗體) / 3=不採用(紅粗體) |
| Q6 | 入口位置 | Projects.razor:192 既有「下載結案資料」按鈕（綠 sage）改 onclick |
| Q7 | 未結案能否匯出 | **否**，僅 `ClosedAt IS NOT NULL` 的梯次顯示按鈕（既有條件已成立） |
| Q8 | LCT 結案資料 | 先做 CWT，LCT 等使用者出 demo 再開單獨計畫 |

---

## 二、EXCEL 樣式參數（依 DEMO 拆解）

### 整份預設
- **字型**：微軟正黑體（fontName = "微軟正黑體"）、size 14
- **最小列高**：25 pt
- **欄寬**（NPOI 256-units，即 char-width × 256）：
  - A=13.14 / B=16.14 / C=10.14 / D=16.71 / E=75.71 / F=40.71 / G,H,I,J=20.71

### 列高
| Row | 內容 | 高度 |
|---|---|---|
| 1 | 梯次名稱 | 25 |
| 2 | 專案等級 | 25 |
| 3 | 表頭 | 25 |
| 主行 (4,6,8...) | 題目主行 | 37.5 |
| 副行 (5,7,9...) | 時間副行 | 25 |

### 合併規則（每題 2 列成對）
- 表頭 ROW3：G3:H3（專審）、I3:J3（總審）
- 主行 ROW4：A4:A5 / B4:B5 / C4:C5 / D4:D5
- 副行 ROW5：G5:H5（審題時間）、I5:J5（決策時間）
- ROW1 / ROW2：B1:C1 / B2:C2（值跨兩格顯示）

### 樣式
- 表頭：粗體 + 灰底（NPOI `HSSFColor.Grey25Percent.Index`）+ 置中 + 邊框 thin
- 主行：左對齊（題目摘要可換行 wrap=true） + 邊框 thin + 採用/不採用文字粗體紅綠
- 副行：左對齊 + 邊框 thin + 字色灰 (#6B7280) 區分時間註記

### 採用 / 不採用 顏色
- decision 1 或 2 → 字串「採用」、字色 **#16A34A**（綠）、粗體
- decision 3 → 字串「不採用」、字色 **#DC2626**（紅）、粗體
- decision IS NULL 或 stage 沒紀錄 → 空白

---

## 三、Service 端設計

### 三檔規則
- `Services/ProjectService.cs` 加 1 個 public 方法 + 1 個 private helper
- `Models/ProjectModels.cs` 加 1 組 record 型 DTO
- `Components/Pages/Projects.razor` 換 button onclick + 加 JS download

### 新增 Public 方法
```csharp
// IProjectService.cs / ProjectService.cs
Task<ClosedProjectExportData> GetClosedProjectExportDataAsync(int projectId);
```

### DTO（Models/ProjectModels.cs）
```csharp
public sealed record ClosedProjectExportData(
    string ProjectName,    // 「CWT 初等測試」
    string ExamLevelLabel, // 「初等」「中等」…「優等」；LCT 留空（本次只做 CWT）
    IReadOnlyList<ClosedExportRow> Rows
);

public sealed record ClosedExportRow(
    int    SeqNo,           // 行內題號（不是 QuestionCode）
    string TypeLabel,       // 「一般單選題」「閱讀主題」「閱讀子題」「短文主題」「短文子題」「長文題目」
    string DifficultyLabel, // 「易」「中」「難」
    string CreatorName,
    string Summary,         // 截 40 字 + 「…」
    DateTime? UpdatedAt,    // 「最後修題」時間
    string?   PeerReviewerName,    // 互審委員（stage=1）
    DateTime? PeerReviewedAt,
    string?   ExpertReviewerName,  // 專審委員（stage=2）
    DateTime? ExpertReviewedAt,
    byte?     ExpertDecision,
    string?   FinalReviewerName,   // 總審委員（stage=3）
    DateTime? FinalDecidedAt,
    byte?     FinalDecision
);
```

### SQL（兩段 QueryMultipleAsync）

**Sect 1** — 梯次資訊：
```sql
SELECT TOP 1 p.Name AS ProjectName, p.ExamLevel
FROM dbo.MT_Projects p
WHERE p.Id = @ProjectId AND p.ClosedAt IS NOT NULL;
```

**Sect 2** — 題目 + 子題 + 各 stage 最後一筆 ReviewAssignment（UNION ALL 母+子）：
```sql
-- 母題層
SELECT
    1 AS Granularity,            -- 0=子題, 1=母題
    q.Id            AS QId,
    q.QuestionTypeId,
    q.Level         AS Difficulty,
    q.Stem,
    q.ArticleContent,
    q.UpdatedAt     AS UpdatedAt,
    creator.DisplayName AS CreatorName,
    -- 互審
    peer.Reviewer   AS PeerReviewerName, peer.ReviewedAt AS PeerReviewedAt,
    -- 專審
    expert.Reviewer AS ExpertReviewerName, expert.ReviewedAt AS ExpertReviewedAt,
    expert.Decision AS ExpertDecision,
    -- 總審
    final.Reviewer  AS FinalReviewerName,  final.DecidedAt AS FinalDecidedAt,
    final.Decision  AS FinalDecision,
    q.QuestionTypeId AS SortType,
    q.Id            AS SortQId,
    0               AS SortSubIdx
FROM dbo.MT_Questions q
INNER JOIN dbo.MT_Users creator ON creator.Id = q.CreatorId
OUTER APPLY (
    SELECT TOP 1 u.DisplayName AS Reviewer, ra.CreatedAt AS ReviewedAt
    FROM dbo.MT_ReviewAssignments ra
    INNER JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
    WHERE ra.QuestionId = q.Id AND ra.SubQuestionId IS NULL AND ra.ReviewStage = 1
    ORDER BY ra.CreatedAt DESC
) peer
OUTER APPLY (
    SELECT TOP 1 u.DisplayName AS Reviewer, ra.CreatedAt AS ReviewedAt, ra.Decision
    FROM dbo.MT_ReviewAssignments ra
    INNER JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
    WHERE ra.QuestionId = q.Id AND ra.SubQuestionId IS NULL AND ra.ReviewStage = 2
    ORDER BY ra.CreatedAt DESC
) expert
OUTER APPLY (
    SELECT TOP 1 u.DisplayName AS Reviewer, ra.DecidedAt, ra.Decision
    FROM dbo.MT_ReviewAssignments ra
    INNER JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
    WHERE ra.QuestionId = q.Id AND ra.SubQuestionId IS NULL AND ra.ReviewStage = 3
      AND ra.Decision IS NOT NULL
    ORDER BY ra.DecidedAt DESC
) final
WHERE q.ProjectId = @ProjectId AND q.IsDeleted = 0

UNION ALL

-- 子題層
SELECT
    0,                            -- Granularity = 0 子題
    sq.Id, q.QuestionTypeId, sq.Level, sq.Stem, NULL,
    sq.UpdatedAt, creator.DisplayName,
    peer.Reviewer, peer.ReviewedAt,
    expert.Reviewer, expert.ReviewedAt, expert.Decision,
    final.Reviewer, final.DecidedAt, final.Decision,
    q.QuestionTypeId,
    q.Id,
    sq.SortOrder
FROM dbo.MT_SubQuestions sq
INNER JOIN dbo.MT_Questions q     ON q.Id = sq.ParentQuestionId
INNER JOIN dbo.MT_Users creator   ON creator.Id = q.CreatorId
OUTER APPLY ( … ra.SubQuestionId = sq.Id AND ReviewStage = 1 … ) peer
OUTER APPLY ( … ra.SubQuestionId = sq.Id AND ReviewStage = 2 … ) expert
OUTER APPLY ( … ra.SubQuestionId = sq.Id AND ReviewStage = 3 AND Decision IS NOT NULL … ) final
WHERE q.ProjectId = @ProjectId AND q.IsDeleted = 0

ORDER BY SortType, SortQId, SortSubIdx;
```

### TypeLabel 映射（Service 端 switch）
| TypeId | Granularity=1（母） | Granularity=0（子） |
|---|---|---|
| 1 一般單選題 | 一般單選題 | — |
| 2 精選單選題 | 精選單選題 | — |
| 3 閱讀題組 | 閱讀主題 | 閱讀子題 |
| 4 長文題目 | 長文題目 | — |
| 5 短文題組 | 短文主題 | 短文子題 |
| 6 聽力測驗 | 聽力測驗 | — |
| 7 聽力題組 | 聽力主題 | 聽力子題 |

### DifficultyLabel 映射
- Q `Level`：0/1/2 → 易/中/難（CWT 一般類）
- 題組母題 Level 通常 NULL → 顯示空白
- 聽力測驗 Level 1-5 → 「難度一」…「難度五」（LCT 走這套，本次 CWT 不會撞到）

### Summary 截斷
- `Stem ?? ArticleContent` → `StripHtml` → 取 40 字 → 若原文 > 40 字補「…」

---

## 四、UI 端（Projects.razor）

### 改動 1 — Button onclick
**修改前**（行 192）：
```razor
<button type="button" @onclick='() => ShowComingSoonAsync("下載結案資料")'
```
**修改後**：
```razor
<button type="button" @onclick="HandleDownloadClosedDataAsync" disabled="@isDownloading"
```

### 改動 2 — 新增 method（Projects.razor.cs 區段）
```csharp
private bool isDownloading;

private async Task HandleDownloadClosedDataAsync()
{
    if (selectedDetail is null || selectedDetail.ClosedAt is null) return;
    isDownloading = true; StateHasChanged();
    try
    {
        var data = await ProjectService.GetClosedProjectExportDataAsync(selectedDetail.Id);
        var bytes = BuildClosedExportWorkbook(data);
        var safeName = string.Join("_", data.ProjectName.Split(InvalidFileChars));
        var fileName = $"結案資料_{safeName}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        await JS.InvokeVoidAsync("downloadByteArray", Convert.ToBase64String(bytes), fileName,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "下載結案資料失敗 ProjectId={Id}", selectedDetail.Id);
        await JS.InvokeVoidAsync("Swal.fire", new { icon = "error", title = "下載失敗", text = ex.Message, confirmButtonColor = "#6B8EAD" });
    }
    finally { isDownloading = false; StateHasChanged(); }
}
```

### 改動 3 — `BuildClosedExportWorkbook` (NPOI 組裝)
- 仿 `Teachers.razor.BuildExportWorkbook` 結構，但要加：
  - 設 `sheet.DefaultRowHeightInPoints = 25`
  - 設 column widths 8 欄
  - 8 個 CellStyle：headerStyle / titleStyle(ROW1-2) / mainCellStyle / subCellStyle / adoptStyle (綠粗體) / rejectStyle (紅粗體) / mainBoldStyle / numStyle
  - 字型 = 微軟正黑體 + size 14
  - 全部 `AddMergedRegion(new CellRangeAddress(r1, r2, c1, c2))`
  - 處理 `IsDownloading` 狀態與 button disabled

---

## 五、SQL 邊界條件

| 情境 | 處理 |
|---|---|
| 結案前已標 `Status=11`（結案不採用）的題目 | 仍納入匯出（已決策過的也要列） |
| 一題完全沒進審題（命題草稿狀態） | `IsDeleted=0` 但 `Status<4` 應排除？→ **僅匯出 `Status IN (9,10,11,12)` 已決策/結案題目**（待釐清，見 Q9） |
| 子題沒有 ReviewAssignment | OUTER APPLY 回 NULL，所有 Reviewer 欄留空 |
| 題組母題本身沒 Stem | 用 ArticleContent；都 NULL → 空白 |
| 子題 Decision=3 但結案是因母不採子被淘汰 | 仍顯示子題自己的 Decision，不沿用母題 |

---

## 六、實作階段（單一 commit 可交付）

| 階段 | 內容 | 預估 | 檔案 |
|---|---|---|---|
| 1 | DTO + SQL + Service 方法 | 2h | ProjectModels.cs, ProjectService.cs, IProjectService.cs |
| 2 | NPOI workbook 組裝 helper | 2h | Projects.razor（@code 區） |
| 3 | Button onclick + disable + 錯誤處理 | 0.5h | Projects.razor |
| 4 | 驗證 | 1h | dotnet build + 開瀏覽器實測 |
| **合計** | | **~5h** | |

---

## 七、Verification Plan

| 案例 | 步驟 | 預期 |
|---|---|---|
| 進行中梯次 | 開 Projects → 進行中梯次 | 「下載結案資料」按鈕不顯示（既有條件已成立）|
| 已結案 CWT 梯次 | 開 Projects → 已結案梯次 → 點按鈕 | 跳出 `結案資料_{ProjectName}_{yyyyMMdd_HHmm}.xlsx` 下載 |
| Excel 結構 | 用 Excel 打開 | ROW1/ROW2 跨欄、ROW3 表頭灰底粗體、每題 2 列成對合併、字型微軟正黑體 14 |
| 採用/不採用顏色 | 檢視 H/J 欄 | 採用=綠粗體、不採用=紅粗體 |
| 題組展開 | 含閱讀題組的梯次匯出 | 母題 1 列+ 各子題 1 列、B 欄顯示「閱讀主題/閱讀子題」 |
| 多種題型 | 同梯次同時有一般+題組 | SortType 排序確保同題型相鄰、子題接在母題之後 |
| 摘要截斷 | Stem 超過 40 字 | 顯示前 40 字+「…」 |
| Stem NULL | 題組母題（無 Stem） | 顯示 ArticleContent 截斷後內容 |
| 未審題目 | 命題草稿題（Status<4） | **取決於 Q9 結論**（見下） |
| Build | dotnet build | 0 警告 0 錯誤 |
| LCT 結案梯次點按鈕 | 暫時跑 CWT 邏輯 | 確認不會 crash（雖然欄位不對齊，但本次先不修，等 LCT demo） |

---

## 八、剩餘待釐清（Q9）

**Q9：哪些 Status 的題目納入匯出？**

選項：
- **A**：只列 `Status IN (9, 10, 11, 12)` — 即「採用 / 不採用 / 結案不採用 / 結案入庫」這 4 種**已有結局**的題目
- **B**：列所有 `IsDeleted = 0` 的題目（含命題草稿、命題完成、送審中、修題中等中間態）— 但這些題目沒有 ReviewAssignment 紀錄，G/H/I/J 欄都會空白
- **C**：只列「曾被審過」的題目，即 `EXISTS (SELECT 1 FROM MT_ReviewAssignments WHERE QuestionId = q.Id)`

我推薦 **A**：結案資料本應只列「最終定案的題目」，否則匯出檔包含一堆 G/H/I/J 空白的列會混亂。

請拍板 Q9，我立即動工。
