# 公告綁定梯次 — Searchable Combobox + 複選功能計畫書

> **計畫日期**：2026-05-29
> **作者**：Jay 與 Claude 共同設計
> **狀態**：⏳ 等待 user 同意動工
> **影響範圍**：公告系統 DB schema + Model + Service（9 個 SQL）+ UI（SlideOver combobox + 列表顯示）+ AuditLog JSON

---

## 一、需求拍板（已確認）

| # | 議題 | 結論 |
|---|---|---|
| Q1 | UI 形式 | **Searchable Combobox**：搜尋框 + 分組（進行中 / 準備中 / 已結案）+ 「全站廣播」固定置頂 |
| Q2 | 已結案分組 | 預設**摺疊** |
| Q3 | 列表項目元資料 | 年度 + ProjectType 徽章（CWT/LCT） |
| Q4 | 複選需求 | **要**：單筆公告可綁多個梯次 |
| Q5 | DB schema | **X 新增 junction table** `MT_AnnouncementProjects` |
| Q6 | 全站 vs 指定互斥 | **A 互斥**：選全站清空其他、選任一梯次取消全站 |
| Q7 | 動工階段 | **A 一次到位**：DB + Model + Service + UI + AuditLog 單一 commit |
| Q8 | 教師可見性 | **A 交集**：公告綁定梯次包含當前切到的梯次 → 可見（沿用現行「切到當前梯次」邏輯延伸） |

---

## 二、DB Schema 變更

### 2.1 新增 junction table

```sql
CREATE TABLE [dbo].[MT_AnnouncementProjects](
    [Id]              [int] IDENTITY(1,1) NOT NULL,
    [AnnouncementId]  [int] NOT NULL,
    [ProjectId]       [int] NOT NULL,
    [CreatedAt]       [datetime2](7) NOT NULL DEFAULT (SYSDATETIME()),
    CONSTRAINT [PK_MT_AnnouncementProjects] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_MT_AnnouncementProjects_Pair] UNIQUE ([AnnouncementId], [ProjectId])
);

CREATE NONCLUSTERED INDEX [IX_MT_AnnouncementProjects_AnnouncementId]
    ON [dbo].[MT_AnnouncementProjects] ([AnnouncementId]);

CREATE NONCLUSTERED INDEX [IX_MT_AnnouncementProjects_ProjectId]
    ON [dbo].[MT_AnnouncementProjects] ([ProjectId]);
```

> 不加 FK constraint（與既有 `MT_Announcements.ProjectId` 風格一致 — 該欄目前也沒 FK）。`IsDeleted` 不需要：公告刪除時 cascade 由 application 端負責（DELETE junction 然後 DELETE announcement）。

### 2.2 既有資料 migration

```sql
-- 現有 MT_Announcements.ProjectId 非 NULL 的列搬到 junction table
INSERT INTO dbo.MT_AnnouncementProjects (AnnouncementId, ProjectId)
SELECT Id, ProjectId
FROM dbo.MT_Announcements
WHERE ProjectId IS NOT NULL;
```

### 2.3 `MT_Announcements.ProjectId` 欄位處置

**保留欄位但停用**（不 drop 欄位）：

理由：
- Drop 欄位風險：歷史 AuditLog `OldValue` JSON 內仍有 `projectId` reference，drop 後若要還原資料無對應
- 保留 + 不再寫入：Service 端 INSERT/UPDATE 都不再帶 ProjectId 參數，新資料一律經 junction
- 既有 NULL 列維持 NULL（全站廣播語意）
- 既有非 NULL 列已搬入 junction，欄位仍有舊值（reference only，不再讀取）

**未來如果 DB 整理可以 drop**，本次不動。

---

## 三、Model 變更（`Models/AnnouncementModels.cs`）

### 3.1 改動清單

| Class | 改動 |
|---|---|
| `AnnouncementListItem` | `int? ProjectId` + `string ProjectName` → `IReadOnlyList<int> ProjectIds` + `IReadOnlyList<string> ProjectNames`；新增 computed property `string ProjectBindingDisplay`（「全站廣播」/「CWT 初等測試」/「CWT 初等測試 +2」） |
| `AnnouncementEditDto` | `int? ProjectId` → `IReadOnlyList<int> ProjectIds`（空 list = 全站廣播） |
| `AnnouncementFormModel` | `int? ProjectId` → `List<int> ProjectIds = []`（空 = 全站廣播） |
| `ProjectDropdownItem` | 加 `byte ProjectType`（CWT/LCT 徽章用）+ `int Year` + `byte LifecycleStatus`（進行中/準備中/已結案分組用） |

### 3.2 新增分組顯示用 DTO

```csharp
public class ProjectGroupedDropdown
{
    public IReadOnlyList<ProjectDropdownItem> Active   { get; init; } = [];   // 進行中
    public IReadOnlyList<ProjectDropdownItem> Preparing { get; init; } = [];  // 準備中
    public IReadOnlyList<ProjectDropdownItem> Closed   { get; init; } = [];   // 已結案
}
```

UI 端依此渲染三個分組區塊。

---

## 四、Service 變更（`Services/AnnouncementService.cs`）

### 4.1 改動清單

| 方法 | 改動 |
|---|---|
| `GetAnnouncementListAsync` | SQL 加 LEFT JOIN junction + STRING_AGG ProjectIds + Names；回填 `AnnouncementListItem.ProjectIds/ProjectNames` |
| `GetAnnouncementEditAsync` | 改 QueryMultiple 2 段：主檔 + junction 表 |
| `GetProjectDropdownAsync` | 改回 `ProjectGroupedDropdown`，依 ProjectLifecycle 分三組；附 Year + ProjectType |
| `CreateAsync` | 1. INSERT MT_Announcements（不帶 ProjectId）→ 取 newId<br>2. 若 `ProjectIds 非空` → 批次 INSERT MT_AnnouncementProjects（VALUES (),(),()）<br>3. AuditLog NewValue 改 `projectIds: [1,3]` 陣列 |
| `UpdateAsync` | 1. UPDATE MT_Announcements（不帶 ProjectId）<br>2. DELETE FROM MT_AnnouncementProjects WHERE AnnouncementId = @Id<br>3. INSERT 新的 ProjectIds 列<br>4. AuditLog NewValue 同上 |
| `DeleteAsync` | 1. SELECT ProjectIds 給 OldValue 用<br>2. DELETE junction (FK cascade 用不上)<br>3. DELETE MT_Announcements<br>4. AuditLog OldValue 帶 `projectIds:[...]` |
| `TogglePinAsync` | 不動（不涉及 ProjectId） |
| `AutoUnpinExpiredAsync` | 不動 |
| `GetHomeAnnouncementsAsync` | SQL WHERE 改：`EXISTS (junction empty) OR EXISTS (junction WHERE ProjectId = @CurrentProjectId)` |

### 4.2 關鍵 SQL 草案

**List 查詢**（含 junction STRING_AGG）：
```sql
SELECT
    a.Id, a.Category, a.Status,
    a.PublishDate, a.UnpublishDate, a.IsPinned,
    a.Title, a.Content, a.CreatedAt,
    u.DisplayName AS AuthorName,
    -- junction 聚合（NULL = 全站廣播）
    STUFF((
        SELECT ',' + CAST(ap.ProjectId AS NVARCHAR(20))
        FROM dbo.MT_AnnouncementProjects ap
        WHERE ap.AnnouncementId = a.Id
        ORDER BY ap.Id
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 1, '') AS ProjectIdsCsv,
    STUFF((
        SELECT N',' + p.Name
        FROM dbo.MT_AnnouncementProjects ap
        INNER JOIN dbo.MT_Projects p ON p.Id = ap.ProjectId
        WHERE ap.AnnouncementId = a.Id
        ORDER BY ap.Id
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 1, '') AS ProjectNamesCsv
FROM dbo.MT_Announcements a
INNER JOIN dbo.MT_Users u ON a.AuthorId = u.Id
ORDER BY a.IsPinned DESC, a.PublishDate DESC;
```
C# 端 split csv 成 List。

> 使用 STUFF + FOR XML 寫法相容性最廣（SQL Server 2017+ 才能用 STRING_AGG，本專案版本不確定）；可改用 STRING_AGG 如果確認版本。

**首頁公告過濾**（Q8 拍板 A 交集邏輯）：
```sql
SELECT a.Id, a.Category, a.Status, a.PublishDate, a.UnpublishDate, a.IsPinned,
       a.Title, a.Content, a.CreatedAt, u.DisplayName AS AuthorName,
       [STRING_AGG ProjectIds, Names 同上]
FROM dbo.MT_Announcements a
INNER JOIN dbo.MT_Users u ON a.AuthorId = u.Id
WHERE a.Status = 1
  AND a.PublishDate <= SYSDATETIME()
  AND (a.UnpublishDate IS NULL OR a.UnpublishDate >= SYSDATETIME())
  AND (
    -- 全站廣播（junction 0 列）
    NOT EXISTS (SELECT 1 FROM dbo.MT_AnnouncementProjects WHERE AnnouncementId = a.Id)
    OR
    -- 公告綁定包含當前梯次
    EXISTS (SELECT 1 FROM dbo.MT_AnnouncementProjects
            WHERE AnnouncementId = a.Id AND ProjectId = @ProjectId)
  )
ORDER BY a.IsPinned DESC, a.PublishDate DESC;
```

**梯次下拉**（分組 + 附 metadata）：
```sql
SELECT
    p.Id, p.Name, ISNULL(p.ProjectType, 0) AS ProjectType, p.Year,
    -- LifecycleStatus 邏輯：ClosedAt 非 NULL = Closed (3)；今天 >= 命題階段 StartDate = Active (2)；否則 Preparing (1)
    CASE
        WHEN p.ClosedAt IS NOT NULL THEN 3
        WHEN ISNULL(comp.StartDate, p.StartDate) <= CAST(SYSDATETIME() AS DATE) THEN 2
        ELSE 1
    END AS LifecycleStatus
FROM dbo.MT_Projects p
OUTER APPLY (
    SELECT TOP 1 StartDate FROM dbo.MT_ProjectPhases
    WHERE ProjectId = p.Id AND PhaseCode = 2
) comp
WHERE p.IsDeleted = 0
ORDER BY LifecycleStatus, p.Year DESC, p.Name;
```
C# 端 GROUP BY LifecycleStatus 拆成 Active/Preparing/Closed 三個 List。

---

## 五、UI 變更（`Components/Pages/Announcements.razor`）

### 5.1 列表「綁定梯次」欄

**位置**：行 175-185（lg:col-span-2 區）

**改前**：
```razor
@if (ann.ProjectId is null) { <span>全站廣播</span> }
else { <span>@ann.ProjectName</span> }
```

**改後**：
```razor
@if (ann.ProjectIds.Count == 0)
{
    <span class="text-sm text-morandi font-bold truncate block">全站廣播</span>
}
else
{
    <span class="text-sm text-gray-500 truncate block"
          title="@string.Join("、", ann.ProjectNames)">
        @ann.ProjectNames[0]
        @if (ann.ProjectIds.Count > 1)
        {
            <span class="ml-1 text-xs text-gray-400">+@(ann.ProjectIds.Count - 1)</span>
        }
    </span>
}
```

「首個梯次 +N」設計（與計畫書 Q3 列表顯示 A 案一致），hover 看完整列表。

### 5.2 SlideOver 內 Combobox

**位置**：行 245-258（綁定顯示梯次 select）

新元件結構（內聯，不抽 shared）：
- Trigger button：顯示 chips（最多 2 個 + 「+N」）或「全站廣播」或「請選擇」
- Popover panel（與時間 popover 同模式 fixed inset-0 backdrop + absolute right-0 top-full）：
  - 頂部：自動 focus 搜尋框（DebouncedSearchInput pattern）
  - 「★ 全站廣播」固定 row（特殊樣式：sage 點 + 分隔線）
  - 分組「進行中 (N)」「準備中 (N)」「已結案 (N)」
  - 已結案分組預設摺疊（點 chevron 展開）
  - 每個 row：checkbox + 梯次名 + ProjectType 徽章（CWT 藍/LCT 紫）+ 年度小字
  - 互斥邏輯：點 ★ 全站廣播 → clear ProjectIds；點任一梯次 → 從 IsGlobal 狀態跳脫（即 ProjectIds 加進來）

### 5.3 ASCII mockup

```
┌─ 綁定顯示梯次 ──────────────────────────────────┐
│ [全站廣播 (預設)                            ▼ ]  │  ← 0 選 顯示這
└────────────────────────────────────────────────┘

┌─ 綁定顯示梯次 ──────────────────────────────────┐
│ [CWT 初等測試 | LCT 聽力 +1                 ▼ ]  │  ← 3 選 顯示 chip + +1
└────────────────────────────────────────────────┘
                ↓ 點開
┌────────────────────────────────────────────────┐
│ 🔍 搜尋梯次...                                  │  ← 自動 focus
├────────────────────────────────────────────────┤
│ ★ 全站廣播（所有梯次）                  [✓]    │  ← 特殊置頂
├────────────────────────────────────────────────┤
│ ▼ 進行中 (3)                                    │
│   [✓] CWT 初等測試      [CWT]   2026          │  ← checkbox + 徽章 + 年
│   [ ] CWT 中等測試      [CWT]   2026          │
│   [✓] LCT 聽力測驗      [LCT]   2026          │
├────────────────────────────────────────────────┤
│ ▼ 準備中 (2)                                    │
│   [✓] 春季聽力檢定      [LCT]   2026          │
│   [ ] 準備中專案範例    [CWT]   2026          │
├────────────────────────────────────────────────┤
│ ▶ 已結案 (15)                                  │  ← 預設摺疊
└────────────────────────────────────────────────┘
```

---

## 六、AuditLog JSON 結構

### 改前
```json
{
  "title": "...",
  "category": 1,
  "isPinned": true,
  "projectId": 5,
  "targetDisplayName": "..."
}
```

### 改後
```json
{
  "title": "...",
  "category": 1,
  "isPinned": true,
  "projectIds": [1, 3, 5],         // 空 [] = 全站廣播
  "targetDisplayName": "..."
}
```

**SystemLogs.razor / Dashboard 對舊版 `projectId` 欄位讀取的程式碼影響**：grep 結果無人讀（只有 AnnouncementService 自己寫進去，但其他地方不從 OldValue/NewValue 解析 projectId 欄位 — 都是看 targetDisplayName）。**0 影響**。

---

## 七、影響檔案 + LOC

| 檔案 | 改動 | 預估 LOC |
|---|---|---|
| `.claude/rules/sql/create_announcement_projects.sql` | 新檔：CREATE TABLE + 既有資料 INSERT migration | ~30 |
| `Models/AnnouncementModels.cs` | ProjectId → ProjectIds、ProjectGroupedDropdown 新類別、ProjectDropdownItem 加欄位 | ~25 |
| `Services/AnnouncementService.cs` | 9 個 SQL 改寫 + 新增 junction CRUD helper（批次 INSERT） | ~180 |
| `Components/Pages/Announcements.razor` | 列表「綁定梯次」欄改 + SlideOver combobox 新元件（約 150 行） | ~200 |
| **合計** | | **~435** |

不改：
- `Services/HomeService.cs` 簽章不變（仍傳 `int? projectId`，內部 SQL 由 AnnouncementService 改）
- `Services/SystemLogService.cs` 不動（只讀 Title）
- `Services/DashboardService.cs` 不動（只讀 Title）
- 其他 .razor 不動

---

## 八、邊界條件 / Gotcha

| 案例 | 處理 |
|---|---|
| 既有資料（截圖兩筆 + 其他）migration 後行為 | NULL → 全站廣播（junction 0 列）；非 NULL → junction 1 列。視覺上「全站廣播」/「單一梯次」行為等同改前 |
| 一個公告綁的某梯次被 IsDeleted=1 軟刪 | junction 仍有列、JOIN 時 LEFT JOIN MT_Projects 取 Name 會回空。建議顯示「（已刪除梯次）」做為 fallback |
| 一個公告綁 0 梯次（junction 空）= 全站廣播 | 與「未指定」同義 |
| Race condition：UpdateAsync 同時兩位編輯者 | 既有 transaction 範圍涵蓋 DELETE + INSERT junction，無多端寫入問題 |
| 「全站」與「指定梯次」互斥 UI 邏輯 | C# 端：勾選任一梯次時不動全站狀態（純由 ProjectIds.Count == 0 推斷是否為全站）；勾選★全站時 `ProjectIds.Clear()` |
| 教師切換梯次後公告列表 | HomeService 仍按當前 projectId 過濾，邏輯與改前一致（只是 SQL 條件改 EXISTS junction） |
| 公告刪除時 junction 列 | DeleteAsync 內 DELETE junction 後再 DELETE 主檔。若 race condition 失敗，junction 留孤兒列（無 FK 約束）— 接受此風險（影響極小，不影響功能） |

---

## 九、Verification Plan

| # | 案例 | 步驟 | 預期 |
|---|---|---|---|
| 1 | Migration 執行 | 在 SSMS 跑 create_announcement_projects.sql | 創 junction table + INSERT 既有非 NULL ProjectId 列 |
| 2 | 既有公告編輯回顯 | 開公告管理頁，編輯既有「綁特定梯次」的公告 | combobox 顯示該梯次勾選、儲存後 junction 列正確更新 |
| 3 | 既有全站公告編輯 | 編輯 ProjectId IS NULL 的公告 | combobox 顯示「★ 全站廣播」勾選 |
| 4 | 新增多選公告 | 新增公告，搜尋並勾選 2-3 個梯次 | DB junction 寫入 N 列、AuditLog NewValue 帶 projectIds 陣列 |
| 5 | 互斥邏輯 | 已勾 2 個梯次，點 ★ 全站廣播 | 2 個梯次自動取消、僅 ★ 保留勾選 |
| 6 | 反向互斥 | 已勾 ★ 全站，點任一梯次 | ★ 自動取消、該梯次勾選 |
| 7 | 列表顯示 | 公告綁 3 個梯次 | 顯示「首個梯次 +2」、hover tooltip 顯示完整列表 |
| 8 | 已結案分組摺疊 | 點開 combobox | 「已結案 (N)」分組標題顯示但內容摺疊；點 chevron 展開 |
| 9 | 搜尋 filter | 輸入「CWT」| 進行中/準備中/已結案三組內均 filter，無命中的組整個隱藏 |
| 10 | 首頁公告過濾 | 教師切到 CWT 初等梯次 | 看到「全站公告 + 綁定包含 CWT 初等的公告」 |
| 11 | 多選首頁公告 | 公告綁 [CWT 初等, LCT 聽力]，教師切 CWT 初等 | 看得到（包含當前梯次） |
| 12 | 多選不包含 | 公告綁 [LCT 聽力]，教師切 CWT 初等 | 看不到 |
| 13 | 刪除公告 | 刪除多選公告 | junction 列也清除、AuditLog OldValue 帶完整 projectIds |
| 14 | Build | dotnet build | 0 警告 0 錯誤 |

---

## 十、實作階段（建議分 4 commit）

| Commit | 內容 | 預估 |
|---|---|---|
| 1 | Migration SQL + Model 結構 + 新類別（DB + Models 不啟用） | 1h |
| 2 | Service 9 個 SQL 重寫（DB schema 已 ready，service 端切換邏輯） | 4h |
| 3 | Razor UI：列表顯示改寫 + SlideOver combobox 新元件 | 4h |
| 4 | 驗證 + 邊界 case 修正 | 2h |
| **合計** | | **~11h** |

但 user 拍板 A 一次到位 → 仍可一個 commit 完成，分階段是內部 commit log 整理。

---

## 十一、後續可改善（非本次範圍）

- ProjectDropdown 結果可進 IMemoryCache（30 秒）— 因 SlideOver 反覆開關時不必重打 DB
- 搜尋 filter 可加「拼音首字母」支援（如打「cwt」找 CWT 系列）— 中文搜尋演算法 scope creep
- 已結案分組可加「年度二級摺疊」（如 100 個已結案分 2026/2025/2024）— 等真的累積到再說

---

**請 user review 本計畫書，特別關注**：
- 第二節 DB schema（保留 MT_Announcements.ProjectId 欄位策略）
- 第八節 邊界條件（特別是被刪梯次的顯示處理）
- 第十節 是否真要一個 commit 完成（或拆 2-3 個 commit 階段交付）

OK 後動工。
