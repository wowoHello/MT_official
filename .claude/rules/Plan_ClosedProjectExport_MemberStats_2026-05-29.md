# 命題專案結案資料 — 新增第 2 sheet「職務任務統計」計畫書

> **計畫日期**：2026-05-29
> **作者**：Jay 與 Claude 共同設計
> **狀態**：⏳ 等待 user 同意動工
> **影響範圍**：CWT 版本結案匯出（既有 `Projects.razor.HandleDownloadClosedDataAsync` 流程）
> **前置依賴**：`Plan_ClosedProjectExport_2026-05-27.md` 第 1 sheet 試題列表已完成
> **DEMO 參考**：Teachers.razor 教師匯出格式（每人一列 + 任務數/總數）

---

## 一、需求拍板（已確認）

| # | 議題 | 結論 |
|---|---|---|
| Q1 | 第 2 sheet 位置 | **B**：在同一 .xlsx 內另起 sheet「職務任務統計」 |
| Q2 | 題型欄位定義 | **A**：固定 6 欄（CWT 完整題型粒度），無資料填「—」 |
| Q3 | 是否加總計欄 | 不加 |
| Q4 | 一人多職呈現 | 每身分一列；DB UNIQUE 約束保證同梯次同身分不重複 |
| Q5 | 命題 Y 來源 | `SUM(MT_MemberQuotas.QuotaCount)` |
| Q6 | 互審（Stage=1）進度 | **A**：不顯示。命題教師列只看命題進度 |
| Q7 | 命題 X 的 Status 條件 | **C**：`Status IN (9, 10, 11, 12)` — 已送到三審有結局者（throughput 語意） |
| Q8 | 表內列順序 | **A**：身分為主（命題教師 → 審題類 → 總召集人 → 其他），同身分按姓名 |
| Q9 | 「審題」角色匹配 | **A**：`r.Name LIKE N'審題%'`（沿用 TeacherService 既有模式） |
| 特殊 | 審題類列格式 | 6 個題型欄 **AddMergedRegion 成單一 cell** 顯示「審題進度：X/Y」 |

---

## 二、第 2 sheet Excel 樣式

### 2.1 結構

```
工作表名：「職務任務統計」
DefaultRowHeightInPoints：25（與 sheet 1 一致）
表頭列高：25
資料列列高：25（不需 sheet 1 那種 37.5 因為單行顯示）

欄寬（NPOI 256-units）：
  A=教師姓名     18 × 256
  B=身分         14 × 256
  C=一般單選題   14 × 256
  D=閱讀題組母題 16 × 256
  E=閱讀題組子題 16 × 256
  F=長文題目     14 × 256
  G=短文題組母題 16 × 256
  H=短文題組子題 16 × 256
```

### 2.2 表頭列（ROW1）

| Cell | 內容 | 樣式 |
|---|---|---|
| A1 | 教師姓名 | `headerStyle`（既有，重用） |
| B1 | 身分 | `headerStyle` |
| C1 | 一般單選題 | `headerStyle` |
| D1 | 閱讀題組母題 | `headerStyle` |
| E1 | 閱讀題組子題 | `headerStyle` |
| F1 | 長文題目 | `headerStyle` |
| G1 | 短文題組母題 | `headerStyle` |
| H1 | 短文題組子題 | `headerStyle` |

### 2.3 資料列

**命題教師列**（每位命題教師一列）：

| Cell | 內容 | 樣式 |
|---|---|---|
| A | 教師姓名 | `mainCenterStyle`（既有） |
| B | 「命題教師」 | `mainCenterStyle` |
| C | 「X/Y」或「—」（無配額時） | `mainCenterStyle` |
| D~H | 同 C 邏輯，依該題型粒度配額決定 | `mainCenterStyle` |

**審題類列**（審題委員 / 總召集人 / 其他 LIKE 審題% 的角色）：

| Cell | 內容 | 樣式 |
|---|---|---|
| A | 教師姓名 | `mainCenterStyle` |
| B | 角色名稱（DB 原值，如「審題委員」「總召集人」） | `mainCenterStyle` |
| C2:H2 | **AddMergedRegion** → 單一文字「審題進度：X/Y」 | `mainCenterStyle` |

### 2.4 顯示規則

- 配額 Y=0 或無此題型配額 → 該 cell 填「—」
- X/Y 用 tabular nums 字型（不重新設樣式，沿用 mainCenterStyle 內既有字型）
- 無題型配額或 X=Y=0 仍顯示「0/0」嗎？→ **顯示 0/0**（資料誠實，不省略；user 可從報告看出此人未獲配額）

---

## 三、Service 端設計

### 3.1 DTO 變更（`Models/ProjectModels.cs`）

新增 `MemberJobStatsRow` record 並擴充 `ClosedProjectExportData`：

```csharp
public sealed record MemberJobStatsRow(
    string TeacherName,
    string RoleName,
    int    RoleSortKey,             // 用於排序（命題教師=1、審題%=2、總召集人=3、其他=99）
    bool   IsReviewerRow,           // true → 審題類列（C~H merge）
    string GeneralCell,             // "X/Y" 或 "—"
    string ReadGroupMasterCell,
    string ReadGroupSubCell,
    string LongTextCell,
    string ShortGroupMasterCell,
    string ShortGroupSubCell,
    string ReviewSummary            // 給審題類列用（"審題進度：27/27"）；命題類為空字串
);

public sealed record ClosedProjectExportData(
    byte    ProjectType,
    string  ProjectName,
    string  ExamLevelLabel,
    IReadOnlyList<ClosedExportRow>     Rows,
    IReadOnlyList<MemberJobStatsRow>   JobStats   // 新增
);
```

由於 `ClosedProjectExportData` 是 sealed record，加 property 需要更新建構呼叫點 `ProjectService.cs:1374` 一處。

### 3.2 Service 方法擴充

`GetClosedProjectExportDataAsync` 內 `QueryMultiple` 由 2 段擴為 4 段：

```
Sect 1（既有）：梯次 Meta
Sect 2（既有）：試題 + 子題 + 三審結果（UNION ALL）
Sect 3（新增）：命題進度（每人 × 題型 × 粒度 一列）
Sect 4（新增）：審題進度（每人 × Stage 一列）
```

C# 端在讀完 Sect 3/4 之後做 pivot，組成 `IReadOnlyList<MemberJobStatsRow>`。

### 3.3 SQL Sect 3（命題進度）

```sql
;WITH MasterAdopt AS (
    SELECT q.CreatorId, q.QuestionTypeId, COUNT(*) AS Cnt
    FROM dbo.MT_Questions q
    WHERE q.ProjectId = @ProjectId
      AND q.IsDeleted = 0
      AND q.Status IN (9, 10, 11, 12)
    GROUP BY q.CreatorId, q.QuestionTypeId
),
SubAdopt AS (
    SELECT qp.CreatorId, qp.QuestionTypeId, COUNT(*) AS Cnt
    FROM dbo.MT_SubQuestions sq
    INNER JOIN dbo.MT_Questions qp ON qp.Id = sq.ParentQuestionId
    WHERE qp.ProjectId = @ProjectId
      AND qp.IsDeleted = 0
      AND sq.IsDeleted = 0
      AND sq.Status IN (9, 10, 11, 12)
    GROUP BY qp.CreatorId, qp.QuestionTypeId
)
SELECT
    pm.UserId,
    ISNULL(u.DisplayName, N'未知') AS DisplayName,
    pmr.RoleId,
    r.Name AS RoleName,
    mq.QuestionTypeId,
    mq.Granularity,
    SUM(mq.QuotaCount) AS QuotaY,
    CASE WHEN mq.Granularity = 0
         THEN ISNULL(MAX(ma.Cnt), 0)
         ELSE ISNULL(MAX(sa.Cnt), 0) END AS DoneX
FROM dbo.MT_ProjectMembers pm
INNER JOIN dbo.MT_Users u                ON u.Id = pm.UserId
INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
INNER JOIN dbo.MT_Roles r                ON r.Id = pmr.RoleId
INNER JOIN dbo.MT_MemberQuotas mq        ON mq.ProjectMemberId = pm.Id
LEFT JOIN  MasterAdopt ma                ON ma.CreatorId = pm.UserId AND ma.QuestionTypeId = mq.QuestionTypeId
LEFT JOIN  SubAdopt sa                   ON sa.CreatorId = pm.UserId AND sa.QuestionTypeId = mq.QuestionTypeId
WHERE pm.ProjectId = @ProjectId
  AND r.Name = N'命題教師'
GROUP BY pm.UserId, u.DisplayName, pmr.RoleId, r.Name,
         mq.QuestionTypeId, mq.Granularity
ORDER BY u.DisplayName, mq.QuestionTypeId, mq.Granularity;
```

### 3.4 SQL Sect 4（審題進度）

```sql
SELECT
    ra.ReviewerId,
    ISNULL(u.DisplayName, N'未知') AS DisplayName,
    pmr.RoleId,
    r.Name AS RoleName,
    ra.ReviewStage,
    COUNT(DISTINCT CASE WHEN ra.SubQuestionId IS NULL
                        THEN ra.QuestionId END) AS AssignedY,
    COUNT(DISTINCT CASE
        WHEN ra.SubQuestionId IS NULL AND (
             (ra.ReviewStage = 1 AND ra.ReviewStatus = 2) OR
             (ra.ReviewStage IN (2, 3) AND ra.DecidedAt IS NOT NULL))
        THEN ra.QuestionId END) AS DoneX
FROM dbo.MT_ReviewAssignments ra
INNER JOIN dbo.MT_Users u ON u.Id = ra.ReviewerId
INNER JOIN dbo.MT_ProjectMembers pm
        ON pm.ProjectId = ra.ProjectId AND pm.UserId = ra.ReviewerId
INNER JOIN dbo.MT_ProjectMemberRoles pmr ON pmr.ProjectMemberId = pm.Id
INNER JOIN dbo.MT_Roles r ON r.Id = pmr.RoleId
WHERE ra.ProjectId = @ProjectId
  AND (r.Name LIKE N'審題%' OR r.Name = N'總召集人')
  -- Stage 對應角色（避免錯誤計入：審題委員只算 Stage 1/2、總召集人只算 Stage 3）
  AND (
      (r.Name LIKE N'審題%'    AND ra.ReviewStage IN (1, 2)) OR
      (r.Name = N'總召集人'    AND ra.ReviewStage = 3)
  )
GROUP BY ra.ReviewerId, u.DisplayName, pmr.RoleId, r.Name, ra.ReviewStage;
```

> ⚠️ **設計決議**：審題委員的「審題進度」涵蓋 Stage=1（互審旁觀者）+ Stage=2（專審） 還是只 Stage=2？
> 看 demo 圖王小華「審題委員 27/27」沒區分，**目前 SQL 設計涵蓋 Stage 1+2**。如果你要只算 Stage=2（真正的專審），改 SQL 條件即可。

### 3.5 C# 端 pivot 邏輯

```csharp
// Sect 3 結果 → 按 (UserId, RoleId) 分組
var creatorGroups = sect3Rows
    .GroupBy(r => new { r.UserId, r.DisplayName, r.RoleName });

foreach (var grp in creatorGroups)
{
    // 6 個固定欄位填值
    var general    = grp.FirstOrDefault(x => x.QuestionTypeId == 1 && x.Granularity == 0);
    var readMaster = grp.FirstOrDefault(x => x.QuestionTypeId == 3 && x.Granularity == 0);
    var readSub    = grp.FirstOrDefault(x => x.QuestionTypeId == 3 && x.Granularity == 1);
    var longText   = grp.FirstOrDefault(x => x.QuestionTypeId == 4 && x.Granularity == 0);
    var shortMaster= grp.FirstOrDefault(x => x.QuestionTypeId == 5 && x.Granularity == 0);
    var shortSub   = grp.FirstOrDefault(x => x.QuestionTypeId == 5 && x.Granularity == 1);

    rows.Add(new MemberJobStatsRow(
        grp.Key.DisplayName, "命題教師", RoleSortKey: 1, IsReviewerRow: false,
        FormatCell(general), FormatCell(readMaster), FormatCell(readSub),
        FormatCell(longText), FormatCell(shortMaster), FormatCell(shortSub),
        ReviewSummary: ""
    ));
}

// Sect 4 結果 → 按 (UserId, RoleId) 分組（同一人不同角色 = 不同 RoleId）
var reviewerGroups = sect4Rows
    .GroupBy(r => new { r.ReviewerId, r.DisplayName, r.RoleName });

foreach (var grp in reviewerGroups)
{
    int totalY = grp.Sum(x => x.AssignedY);
    int totalX = grp.Sum(x => x.DoneX);
    int sortKey = grp.Key.RoleName == "總召集人" ? 3 : 2;

    rows.Add(new MemberJobStatsRow(
        grp.Key.DisplayName, grp.Key.RoleName, sortKey, IsReviewerRow: true,
        "", "", "", "", "", "",   // 6 個題型欄空，merge 時不會被用到
        ReviewSummary: $"審題進度：{totalX}/{totalY}"
    ));
}

// 排序：身分為主（RoleSortKey），姓名為次
return rows.OrderBy(r => r.RoleSortKey).ThenBy(r => r.TeacherName).ToList();

static string FormatCell(QuotaRow? r)
    => r is null || r.QuotaY == 0 ? "—" : $"{r.DoneX}/{r.QuotaY}";
```

---

## 四、UI 端（`Projects.razor.BuildClosedExportWorkbook`）

### 4.1 整合點

於行 1996（資料區 foreach 結束）之後、行 1998（`MemoryStream`）之前插入：

```csharp
// ── Sheet 2：職務任務統計 ──
var sheet2 = workbook.CreateSheet("職務任務統計");
sheet2.DefaultRowHeightInPoints = 25;

// 欄寬（8 欄）
sheet2.SetColumnWidth(0, 18 * 256);   // A 教師姓名
sheet2.SetColumnWidth(1, 14 * 256);   // B 身分
sheet2.SetColumnWidth(2, 14 * 256);   // C 一般單選題
sheet2.SetColumnWidth(3, 16 * 256);   // D 閱讀母
sheet2.SetColumnWidth(4, 16 * 256);   // E 閱讀子
sheet2.SetColumnWidth(5, 14 * 256);   // F 長文
sheet2.SetColumnWidth(6, 16 * 256);   // G 短文母
sheet2.SetColumnWidth(7, 16 * 256);   // H 短文子

// 表頭 ROW1
var header2 = sheet2.CreateRow(0);
SetCell(header2, 0, "教師姓名", headerStyle);
SetCell(header2, 1, "身分",     headerStyle);
SetCell(header2, 2, "一般單選題",     headerStyle);
SetCell(header2, 3, "閱讀題組母題",   headerStyle);
SetCell(header2, 4, "閱讀題組子題",   headerStyle);
SetCell(header2, 5, "長文題目",       headerStyle);
SetCell(header2, 6, "短文題組母題",   headerStyle);
SetCell(header2, 7, "短文題組子題",   headerStyle);

// 資料區
int rowIdx2 = 1;
foreach (var s in data.JobStats)
{
    var dataRow = sheet2.CreateRow(rowIdx2);
    SetCell(dataRow, 0, s.TeacherName, mainCenterStyle);
    SetCell(dataRow, 1, s.RoleName,    mainCenterStyle);

    if (s.IsReviewerRow)
    {
        // C2:H2 合併成單一 cell 顯示審題進度
        SetCell(dataRow, 2, s.ReviewSummary, mainCenterStyle);
        for (int c = 3; c <= 7; c++) SetCell(dataRow, c, "", mainCenterStyle);
        sheet2.AddMergedRegion(new CellRangeAddress(rowIdx2, rowIdx2, 2, 7));
    }
    else
    {
        SetCell(dataRow, 2, s.GeneralCell,           mainCenterStyle);
        SetCell(dataRow, 3, s.ReadGroupMasterCell,   mainCenterStyle);
        SetCell(dataRow, 4, s.ReadGroupSubCell,      mainCenterStyle);
        SetCell(dataRow, 5, s.LongTextCell,          mainCenterStyle);
        SetCell(dataRow, 6, s.ShortGroupMasterCell,  mainCenterStyle);
        SetCell(dataRow, 7, s.ShortGroupSubCell,     mainCenterStyle);
    }

    rowIdx2++;
}
```

NPOI `XSSFWorkbook.Write()` 自動把兩個 sheet 都寫進同一個 `.xlsx`，JS 下載端 0 改動。

---

## 五、影響檔案 + LOC

| 檔案 | 改動 | 預估 LOC |
|---|---|---|
| `Models/ProjectModels.cs` | 新增 `MemberJobStatsRow` record + `ClosedProjectExportData.JobStats` property | ~30 |
| `Services/ProjectService.cs` | `GetClosedProjectExportDataAsync` 加 Sect 3/4 SQL + C# pivot | ~100 |
| `Components/Pages/Projects.razor` | `BuildClosedExportWorkbook` 加 sheet 2 組裝段 | ~60 |
| **合計** | | **~190** |

不改：
- `IProjectService` 介面（簽章不變，僅 record property 擴充）
- 任何其他 .razor / .cs
- DB schema
- AuditLog
- JS interop

---

## 六、邊界條件與 Gotcha

| 案例 | 處理方式 |
|---|---|
| 命題教師沒有任何配額（MemberQuotas 0 列） | Sect 3 SQL `INNER JOIN MT_MemberQuotas` 自動排除，此人不出現 |
| 命題教師有配額但 0 採用 | Sect 3 LEFT JOIN ma/sa 取 NULL → 顯示「0/Y」 |
| 配額 = 0 的題型（理論上不應存在） | `FormatCell` 邏輯：QuotaY=0 → 顯示「—」 |
| 一人同時為命題教師 + 審題委員 | Sect 3 出 1 列（命題教師）+ Sect 4 出 1 列（審題委員） → 表內 2 列 |
| `MT_ReviewAssignments` 同題同人總召退回重新分配（多筆紀錄） | `COUNT(DISTINCT QuestionId)` 去重 |
| 子題層級的 ReviewAssignment（`SubQuestionId IS NOT NULL`） | Sect 4 已用 `WHERE SubQuestionId IS NULL` 過濾，只算母題層分配（與 demo 圖「題目」單位一致） |
| 自訂角色（非預設四種） | Sect 3 WHERE 限 `命題教師`，Sect 4 限 `審題%` + `總召集人`。自訂角色（如「計畫主持人」）不會出現在表內 |
| LCT 梯次 | 本次只做 CWT。LCT 走另一條 export 路徑，不受影響 |
| 沒有任何 JobStats 資料 | 第 2 sheet 仍會建立，只有表頭沒有資料列（合理） |

---

## 七、Verification Plan

| # | 案例 | 步驟 | 預期 |
|---|---|---|---|
| 1 | 進行中梯次按鈕不顯示 | 開 Projects 頁，選進行中梯次 | 「下載結案資料」按鈕不可見（既有條件）|
| 2 | 已結案 CWT 梯次匯出 | 點按鈕 | 下載 `結案資料_{ProjectName}_{yyyyMMdd_HHmm}.xlsx` |
| 3 | Sheet 1（試題）格式維持 | 用 Excel 開檔，切到 sheet 1 | 與既有匯出格式完全一致（無 regression）|
| 4 | Sheet 2 結構 | 切到 sheet 2「職務任務統計」 | 8 欄表頭 + 命題教師列填 6 個題型欄 + 審題類列 C:H 合併 |
| 5 | 命題教師 X/Y 正確 | 找一位命題教師檢查 | X = 該題型已採用題數（Status IN 9,10,11,12）、Y = SUM(MemberQuotas.QuotaCount) |
| 6 | 母/子粒度分開 | 短文/閱讀題組教師 | D 欄母題 X/Y、E 欄子題 X/Y 各自獨立計算 |
| 7 | 一人多職 | 找一位同時為審題委員 + 總召集人的人 | 表內出現 2 列，數字各自獨立 |
| 8 | 列順序 | 觀察整表 | 命題教師全部在上，審題類接續，總召集人在最後 |
| 9 | 無配額題型顯示「—」 | 一位命題教師只配「一般單選題」 | C 欄填「X/Y」、D~H 全部「—」 |
| 10 | 審題類列 merge | 切到 sheet 2 點審題類列的 C 欄 | Excel 顯示 C2:H2 為合併儲存格，內容「審題進度：X/Y」 |
| 11 | 自訂角色不出現 | 該梯次若有「計畫主持人」 | 表內無此人此身分 |
| 12 | Build | dotnet build | 0 警告 0 錯誤 |

---

## 八、實作階段（單一 commit 可交付）

| 階段 | 內容 | 預估 | 檔案 |
|---|---|---|---|
| 1 | DTO + Sect 3/4 SQL + pivot 邏輯 | 2.5h | ProjectModels.cs, ProjectService.cs |
| 2 | Sheet 2 NPOI 組裝 | 1.5h | Projects.razor |
| 3 | 驗證 | 1h | dotnet build + 開實際結案梯次匯出實測 |
| **合計** | | **~5h** | |

---

## 九、未來擴展（非本次範圍）

- **LCT 版本**：等使用者出 demo 後另開計畫
- **加總計欄**：本次不加，未來如有需求另議
- **互審進度顯示**：本次不顯示，若以後想看命題教師的互審 throughput，加第 9 欄「互審進度」即可（既有 SQL 增加 Stage=1 統計，pivot 加欄即可）
- **匯出 PDF / CSV**：本次仍是 .xlsx
