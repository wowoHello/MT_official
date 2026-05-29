---
name: project-projects-closed-export
description: Projects 結案資料 Excel 匯出（2 sheets）的 SQL、DTO、NPOI 組裝現況快照（2026-05-29）
type: project
---

## 觸發點（Projects.razor 行 1741）

`HandleDownloadClosedDataAsync`：只有 `selectedDetail.ClosedAt != null` 才可觸發（已結案梯次右側面板「下載結案資料」綠色卡片按鈕）。`isDownloadingClosedData` bool 管理 disabled 與 spinner 狀態。

## GetClosedProjectExportDataAsync（ProjectService.cs 行 1206）

SQL 結構：單一 `QueryMultiple` 讀取 4 段結果集。

| Sect | 說明 |
|------|------|
| 1 | 梯次 Meta（ProjectName / ExamLevel / ProjectType），僅 `ClosedAt IS NOT NULL` 才有結果 |
| 2 | 母題 + 子題 UNION ALL，條件 `Status IN (9,10,11,12)` + `IsDeleted=0`，各以 OUTER APPLY 取三個 ReviewStage 的最後一筆 Assignment |
| 3 | 命題進度（每位命題教師 × TypeId × Granularity × Level），CWT 走 MasterAdopt/SubAdopt CTE，LCT TypeId=7 另外從 SubAdopt 補子題列 |
| 4 | 審題進度（審題% Stage=2 / 總召集人 Stage=3），用 `CONCAT(QuestionId, '-', SubQuestionId)` DISTINCT 去重 |

排序鍵（Sect 2 UNION ALL）：`QuestionTypeId, SortMasterId, SortSubOrder`（同題型相鄰、子題緊接母題）。

## 主要 DTO（Models/ProjectModels.cs 行 275）

```csharp
sealed record ClosedProjectExportData(
    byte   ProjectType,        // 0=CWT, 1=LCT
    string ProjectName,
    string ExamLevelLabel,     // LCT → "" ; CWT → 初/中/中高/高/優
    IReadOnlyList<ClosedExportRow>     Rows,
    IReadOnlyList<MemberJobStatsRow>   JobStats   // 第 2 sheet 資料
)

sealed record MemberJobStatsRow(
    string TeacherName,
    string RoleName,
    int    RoleSortKey,        // 命題教師=1, 審題%=2, 總召集人=3, 其他=99
    bool   IsReviewerRow,      // true → C~H merge 顯示 ReviewSummary
    IReadOnlyList<string> Cells,    // 命題教師：CWT 6 元素 / LCT 7 元素；審題類：空陣列
    string ReviewSummary       // 審題類：「審題進度：X/Y」；命題類：""
)

sealed record ClosedExportRow(
    string    DisplayCode,      // 母題=QuestionCode；子題=QuestionCode-SortOrder:D2
    int       QuestionTypeId,
    bool      IsSubQuestion,
    string    DifficultyLabel,  // CWT 0/1/2→易/中/難；LCT 1~5→難度一~五；NULL→－
    string    CreatorName,
    string    Summary,          // StripHtmlLite 後前 40 字 + …
    DateTime? UpdatedAt,
    // 三審各一組：ReviewerName / ReviewedAt / Decision (byte?)
    ...
)
```

## BuildClosedExportWorkbook（Projects.razor 行 1792）

Sheet 1「結案資料」：
- 字型微軟正黑體 size 14
- `DefaultRowHeightInPoints = 25`（主行實際 37.5，設在 row.Height）
- 10 欄（A-J），欄寬對應題碼/題型/難度/命師/摘要/修題時間/互審/專審/總審
- ROW1 梯次名稱（B:C 跨欄）；ROW2 專案等級（LCT 跳過不寫 ROW2，直接 ROW3 表頭）
- ROW3 表頭：G3:H3 合併「互審」，I3:J3 合併「總審」
- 每題 2 列成對：A4:A5 / B4:B5 / C4:C5 / D4:D5 各跨 2 列；G5:H5 合併=審題時間；I5:J5 合併=決策時間
- FinalDecision 1 或 2 → 採用（綠粗體）；3 → 不採用（紅粗體）

Sheet 2「職務任務統計」（行 2060 附近）：
- 8 欄（A-H）：姓名/身分/一般單選/閱讀母/閱讀子/長文/短文母/短文子（CWT）或 姓名/身分/難度一~五/聽力題組母/聽力題組子（LCT 7 欄）
- 命題教師列：各欄填 "X/Y" 或 "—"（QuotaY=0 或無此欄時）
- 審題類列：C:H（CWT）或 C:I（LCT）`AddMergedRegion`，填「審題進度：X/Y」

## 私有 helper（ProjectService.cs）

| 方法 | 行 | 說明 |
|------|----|------|
| `BuildJobStats` | 1500 | composeRaw + reviewRaw → List<MemberJobStatsRow>，最終 OrderBy(RoleSortKey, TeacherName) |
| `BuildCwtCells` | 1546 | CWT 6 元素：按 (TypeId, Granularity) 取 DoneX/QuotaY |
| `BuildLctCells` | 1565 | LCT 7 元素：TypeId=6 按 Level，TypeId=7 按 Granularity |
| `BuildExamLevelLabel` | 1592 | byte? 0~4 → 初/中/中高/高/優；其他 → "" |
| `BuildDifficultyLabel` | 1607 | CWT byte 0/1/2 → 易/中/難；LCT null→"－", 1~5→難度一~五 |
| `BuildSummary` | 1632 | StripHtmlLite 後截 40 字 + "…" |
| `StripHtmlLite` | 1641 | 逐字掃 `<>` 括號去標籤 + HtmlDecode，不依賴 HtmlAgilityPack |

## 私有 sealed classes（ProjectService.cs 行 1655）

- `ClosedExportMetaRow`（ProjectName / ExamLevel / ProjectType）
- `ClosedExportRawRow`（17 個欄位，含 SubSortOrder / IsSubQuestion / Difficulty / Stem / ArticleContent）
- `ClosedExportComposeStatRow`（UserId / DisplayName / RoleName / QuestionTypeId / Granularity / Level / QuotaY / DoneX）
- `ClosedExportReviewStatRow`（ReviewerId / DisplayName / RoleName / ReviewStage / AssignedY / DoneX）

## LCT 相容說明

`BuildClosedExportWorkbook` 以 `data.ProjectType == 1` 判斷 LCT，切換 Sheet 2 欄數（6→7）及 Sheet 1 ROW2 是否省略。ProjectType 從 Sect 1 SQL 帶回，已同步存在 `ClosedProjectExportData.ProjectType`。
