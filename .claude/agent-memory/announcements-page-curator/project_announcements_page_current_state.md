---
name: project_announcements_page_current_state
description: 2026-05-29 讀碼精確確認：三檔行數、時間 popover 設計、日期 facade 拆兩格、UI 各欄位、DB 欄位、CRUD 流程、Service 簽章
type: project
---

## 三檔現況（2026-05-29 讀碼）

| 檔案 | 行數 | 主要職責 |
|------|------|---------|
| `Components/Pages/Announcements.razor` | 1056 行 | UI 列表 + SlideOver + 檢視 Modal + @code 邏輯 |
| `Services/AnnouncementService.cs` | 441 行 | 9 個 public 方法 + private AnnouncementDeleteSnapshot class + EnsureCanEditAsync 權限門鎖 |
| `Models/AnnouncementModels.cs` | 100 行 | 2 個 Enum + 5 個 DTO/Model class |

**Why:** Razor 頁面從舊記憶的 884 行增長到 1056 行（+172 行），原因是加入了時間 popover UI（時/分選取器）與日期+時間拆兩格的 facade 屬性群。

## DB 欄位型別（MT_Announcements）

| 欄位 | 型別 | 備註 |
|------|------|------|
| Id | INT IDENTITY(1,1) | PK |
| Category | TINYINT NOT NULL | 1-4 |
| Status | TINYINT NOT NULL DEFAULT(0) | 0=草稿, 1=發佈 |
| ProjectId | INT NULL | FK→MT_Projects.Id；NULL=全站廣播 |
| PublishDate | datetime2(7) NOT NULL | |
| UnpublishDate | datetime2(7) NULL | NULL=不自動下架 |
| IsPinned | BIT NOT NULL DEFAULT(0) | |
| Title | NVARCHAR(200) NOT NULL | |
| Content | NVARCHAR(MAX) NOT NULL | HTML 富文本 |
| AuthorId | INT NOT NULL | FK→MT_Users.Id |
| CreatedAt | datetime2(7) NOT NULL DEFAULT(sysdatetime()) | |
| UpdatedAt | datetime2(7) NOT NULL DEFAULT(sysdatetime()) | |

無任何索引（除 PK）。FK 約束：AuthorId→MT_Users.Id、ProjectId→MT_Projects.Id。

## 四種分類（Category TINYINT，Enum AnnouncementCategory）

| 值 | 名稱 | Tailwind 顏色 |
|----|------|-------------|
| 1 | 系統公告 | `bg-blue-100 text-blue-700` |
| 2 | 命題公告 | `bg-emerald-100 text-emerald-700` |
| 3 | 審題公告 | `bg-purple-100 text-purple-700` |
| 4 | 其它 | `bg-gray-100 text-gray-600` |

## 三種狀態（設計決策）

**DB 只存兩種**：`MT_Announcements.Status TINYINT` → 0=草稿、1=發佈。

第三種「已下架」是前端 `AnnouncementListItem.DisplayStatus` computed property 由日期推導：
- Status=0 → 草稿
- Status=1 + 今天 < PublishDate → 未發佈（規格書未列，但實作有此狀態）
- Status=1 + 在有效期間 → 已發佈
- Status=1 + 今天 > UnpublishDate → 已下架

## 日期/時間 facade 設計（Razor @code 行 552-601）

「上架日期」與「下架日期」各自被拆成「日期格 + 時間格」兩個 input，但 `formModel.PublishDate` 與 `formModel.UnpublishDate` 維持 `DateTime?`（不拆欄位）。

### Facade 屬性（行 553-601）

| facade 屬性 | 綁定目標 | 說明 |
|------------|---------|------|
| `publishDatePart` | `formModel.PublishDate` 的 Date 部分 | `input type="date"` 綁 `@bind` + `@bind:after="OnPublishDateChanged"` |
| `publishTimePart` | `formModel.PublishDate` 的 TimeOfDay 部分 | 由 `SelectPublishTimeAsync` 間接設定 |
| `unpublishDatePart` | `formModel.UnpublishDate` 的 Date 部分 | 同上模式 |
| `unpublishTimePart` | `formModel.UnpublishDate` 的 TimeOfDay 部分 | 由 `SelectUnpublishTimeAsync` 間接設定 |

組裝邏輯：`CombineDateTime(date, time)` — date 為 null 則整個回傳 null；date 有值但 time 無值則預設 00:00。

### 時間 popover（行 283-371）

- 觸發按鈕顯示 `PublishTimeDisplay`（`formModel.PublishDate?.ToString("HH:mm") ?? "--:--"`）
- 點按鈕切換 `showPublishTimePopover` bool
- popover 內雙欄：時（24 格，`HourSlots = Enumerable.Range(0, 24)`）+ 分（12 格，`MinuteSlots = [0, 5, 10, ..., 55]`，5 分鐘間隔）
- 選取時/分後呼叫 `SelectPublishTimeAsync(hour, minute)` → 設 `publishTimePart` → 觸發 `OnPublishDateChanged()` 防呆
- popover 使用 `fixed inset-0 z-[60]` 背景層關閉，z-[70] 內容層顯示
- **不主動關 popover**（設計意圖：使用者可連續調整時/分，點外側才關）

`UnpublishDate` 有相同對稱的 popover 實作（`showUnpublishTimePopover`、`SelectUnpublishTimeAsync`）。

## UI 篩選狀態（filterDisplayStatus 下拉，行 105-112）

五個選項（規格書只提三種）：
- `all`（所有狀態）
- `已發佈`
- `未發佈`（規格書未列）
- `草稿`
- `已下架`

篩選以 `DisplayStatus` computed property 字串比對，不是 DB Status 欄位。

## 排序規則（FilteredList 計算屬性，行 651-658）

```
置頂 DESC → DisplayStatus (已發佈=0/未發佈=1/草稿=2/已下架=3/其他=4) → Category ASC → PublishDate DESC
```

**規格書差異**：規格書（cwt-ac-rules.md）排序未列「未發佈」，實作多了此狀態（排序值=1）。

## 分頁設定（行 533-542）

- `PageSize = 10`（常數）
- `TotalPages`：`Math.Max(1, (int)Math.Ceiling(FilteredList.Count / (double)PageSize))`
- `PagedList`：`FilteredList.Skip(...).Take(PageSize)`
- 篩選條件變更時（setter）自動 `currentPage = 1`

## 表單（SlideOver，行 223-437）

使用 `CustomModal` 元件，Type=`SlideOver`，MaxWidth=`max-w-2xl`，`KeepAlive="true"`，`CloseOnBackdropClick="true"`。

### 兩個區塊

**區塊 1：公告設定**
- 分類下拉（`InputSelect @bind-Value="formModel.Category"`，4 個選項）
- 梯次下拉（`select @bind="formModel.ProjectId"`，第一個空值選項=全站廣播）
- 排程副卡（上架日期 + 時間 popover / 下架日期 + 時間 popover，同一 `bg-morandi/5 rounded-lg` 卡片內）
- 置頂 toggle（`InputCheckbox @bind-Value="formModel.IsPinned"`，自訂 toggle 外觀）

**區塊 2：公告內容**
- 標題（`InputText @bind-Value="formModel.Title"`）
- 內容（`InlineQuillEditor @bind-Value="formModel.Content" EditorClass="min-h-[240px]"`）

### Footer 按鈕

| 按鈕 | 動作 |
|------|------|
| 存為草稿 | `SaveAsDraft()` → Status=Draft |
| 取消 | `showSlideOver = false` |
| 儲存公告 | `SaveAndPublish()` → Status=Published |

## 表單日期防呆（行 768-812）

- `OnPublishDateChanged()`：上架日期 > 下架日期 → 清空下架日期 + SweetAlert2 toast
- `OnUnpublishDateChanged()`：下架日期 < 上架日期 → 清空下架日期 + SweetAlert2 toast
- `ValidateFormAsync()`：Title 空白 / PublishDate 為 null / UnpublishDate < PublishDate / Content 空白（IsContentEmpty）→ SweetAlert2 警告
- `SaveAndPublish()` 額外：若 IsPinned 且 UnpublishDate 已過 → 自動取消置頂並提示
- `SaveAsDraft()` 額外：若 IsPinned → 自動取消置頂並提示
- Backdrop 自動存草稿（`HandleBackdropSaveDraft`）：Title/PublishDate/Content 任一空白 → 不觸發

`TruncateSeconds(DateTime)`：截斷秒數。呼叫點：`OpenNewAnnouncement`（初始值）、`DoSaveAsync`（存檔前）、`HandleBackdropSaveDraft`（自動存草稿前）。

## CRUD 流程

### 新增
`OpenNewAnnouncement()` → 重置 `formModel`（初始 PublishDate=TruncateSeconds(DateTime.Now)）→ `showSlideOver=true`

### 編輯
`OpenEditAnnouncement(id)` → `GetAnnouncementEditAsync(id)` → 填充 formModel → `showSlideOver=true`

### 儲存共用（DoSaveAsync）
1. TruncateSeconds
2. `CloseSlideOverAndWaitAsync()`（先關面板 + 等 300ms 轉場）
3. 取 userId
4. `editingId.HasValue` → UpdateAsync，否則 → CreateAsync
5. Toast 通知 + `LoadDataAsync()`
6. `UnauthorizedAccessException` → toast 警告 + 導向首頁；其他 ex → toast 錯誤

### 置頂切換（HandleTogglePin，行 942-964）
樂觀更新模式：
1. 前端立即修改 `announcements.Find(a => a.Id == id).IsPinned`
2. fire-and-forget Toast
3. `TogglePinAsync(id, userId)`
4. 失敗時 `LoadDataAsync()` 還原

### 刪除（HandleDelete）
SweetAlert2 `swalConfirm` 確認 → `DeleteAsync(ann.Id, userId)` → `LoadDataAsync()` + Toast

### 檢視 Modal
`OpenViewModal(ann)` → `viewingAnnouncement = ann` → `showViewModal=true`

從檢視跳編輯（`ViewToEdit(id)`）：`showViewModal=false` → `Task.Delay(100)` → `OpenEditAnnouncement(id)`

## 生命週期（OnParametersSetAsync，行 666-685）

```
ModuleCards is null → return（等父層 CascadingValue 推送）
!HasAnnouncementPermission() → NavigateTo("/")
!hasInitialized → RunAnnouncementMaintenanceAsync() + LoadDataAsync()
```

`hasInitialized` 確保只在第一次通過後執行初始載入，避免 OnParametersSetAsync 重複觸發。

`RunAnnouncementMaintenanceAsync()`：呼叫 `AutoUnpinExpiredAsync(userId)`，失敗只 LogWarning，不擋頁面載入。

**Why:** OnParametersSetAsync（非 OnInitializedAsync）是因 CascadingParameter ModuleCards 由 MainLayout 非同步載入，OnInitializedAsync 階段 ModuleCards 往往還是 null。

## 自動下架機制（AutoUnpinExpiredAsync，Service 行 291-347）

SQL 條件：`IsPinned=1 AND (Status=DraftStatus OR (Status=PublishedStatus AND SYSDATETIME() > UnpublishDate))`

**只取消置頂（IsPinned→0），不更改 Status**（DB 沒有 Status=2）。

AuditLog 批次寫法：SQL 內用 `INSERT...SELECT FROM @Updated u LEFT JOIN MT_Announcements a`，JSON 用字串拼接而非 `AuditLogJsonHelper.Serialize`：
```sql
N'{"action":"自動取消過期公告置頂","targetDisplayName":"' + REPLACE(ISNULL(a.Title, N''), N'"', N'\"') + N'"}'
```
其他所有方法（Create/Update/Delete/TogglePin）均使用 `AuditLogJsonHelper.Serialize`。

## 首頁公告連動（GetHomeAnnouncementsAsync，Service 行 350-372）

SQL 條件：`Status=1 AND PublishDate<=SYSDATETIME() AND (UnpublishDate IS NULL OR UnpublishDate>=SYSDATETIME()) AND (ProjectId IS NULL OR ProjectId=@ProjectId)`

排序：`IsPinned DESC, PublishDate DESC`

供 `HomeService` 呼叫，回傳 `AnnouncementListItem`（與列表查詢同 DTO）。

## AuditLog 規範

- `ProjectId` 欄位：一律 NULL（公告 CUD 屬於跨梯次活動）
- Create/Update → `NewValue` JSON 含 title、category、isPinned、projectId、targetDisplayName
- Delete → `OldValue` JSON 含 title、category、isPinned、projectId、targetDisplayName
- TogglePin → `NewValue` JSON 含 title、action（"置頂"或"取消置頂"）、isPinned、targetDisplayName

## Service 方法清單與 SQL 結構

| 方法 | SQL 操作 | Transaction | AuditLog |
|------|---------|-------------|---------|
| `GetAnnouncementListAsync()` | SELECT + JOIN Users + LEFT JOIN Projects | 無 | 無 |
| `GetAnnouncementEditAsync(id)` | SELECT WHERE Id | 無 | 無 |
| `GetProjectDropdownAsync()` | SELECT Id, Name WHERE IsDeleted=0 | 無 | 無 |
| `CreateAsync(model, userId)` | INSERT OUTPUT INSERTED.Id | 有 | Create |
| `UpdateAsync(id, model, userId)` | UPDATE SET ... UpdatedAt=SYSDATETIME() | 有 | Update |
| `TogglePinAsync(id, userId)` | UPDATE...OUTPUT INSERTED.IsPinned,Title | 有 | Update |
| `AutoUnpinExpiredAsync(userId)` | DECLARE @Table + UPDATE + INSERT AuditLog + SELECT COUNT | 有 | Update（批次） |
| `DeleteAsync(id, userId)` | SELECT（快照）→ INSERT AuditLog → DELETE | 有 | Delete（先記後刪） |
| `GetHomeAnnouncementsAsync(projectId)` | SELECT WHERE Status=1 AND 日期條件 | 無 | 無 |

所有寫入前呼叫 `EnsureCanEditAsync(operatorId)` → `_membership.HasModulePermissionAsync(operatorId, null, "announcements")`（走 IMembershipService 30 秒 TTL Cache）。

## Service interface 完整簽章

```csharp
Task<List<AnnouncementListItem>> GetAnnouncementListAsync();
Task<AnnouncementEditDto?> GetAnnouncementEditAsync(int id);
Task<List<ProjectDropdownItem>> GetProjectDropdownAsync();
Task<int> CreateAsync(AnnouncementFormModel model, int operatorId);
Task UpdateAsync(int id, AnnouncementFormModel model, int operatorId);
Task TogglePinAsync(int id, int operatorId);
Task<int> AutoUnpinExpiredAsync(int operatorId);
Task DeleteAsync(int id, int operatorId);
Task<List<AnnouncementListItem>> GetHomeAnnouncementsAsync(int? projectId);
```

## Constructor 注入清單（AnnouncementService）

```csharp
IDatabaseService _db
ILogger<AnnouncementService> _logger
IHttpContextAccessor _httpContextAccessor
IMembershipService _membership
```

## Models 類別清單（AnnouncementModels.cs）

| 類別/Enum | 說明 |
|---------|------|
| `AnnouncementCategory` (enum byte) | System=1, Compose=2, Review=3, Other=4 |
| `AnnouncementStatus` (enum byte) | Draft=0, Published=1 |
| `AnnouncementListItem` | 列表 DTO，含 DisplayStatus computed property |
| `AnnouncementStats` | 前端計算統計卡片（Total/Published/Draft/Archived/Pinned） |
| `AnnouncementEditDto` | 單筆編輯載入 DTO（不含 AuthorName/ProjectName） |
| `ProjectDropdownItem` | Id + Name 梯次下拉 |
| `AnnouncementFormModel` | EditForm 表單模型（Category/Status/ProjectId/PublishDate/UnpublishDate/IsPinned/Title/Content）；預設 Category=System, Status=Draft, PublishDate=DateTime.Now |

`AnnouncementDeleteSnapshot` 是 `AnnouncementService.cs` 內的 `private sealed class`（不在 Models 檔案），欄位：Id/Title/Category/IsPinned/ProjectId，僅供 DeleteAsync AuditLog 快照使用。

## 權限比對設計（重要）

頁面端 `HasAnnouncementPermission()`：
```csharp
ModuleCards?.Any(m =>
    string.Equals(m.PageUrl, "announcements", StringComparison.OrdinalIgnoreCase)
    && m.IsEnabled) == true;
```
用 `PageUrl`（而非 `ModuleKey`），與 `MainLayout.EnforceModuleAccess` 一致。

`EnsureCanEditAsync` 改用 `_membership.HasModulePermissionAsync(operatorId, null, "announcements")`（ModuleKey 一律小寫，對應 DB `UPDATE MT_Modules SET ModuleKey = LOWER(ModuleKey)` 的修補）。

## @code 中的 private helper 方法

| 方法 | 行 | 說明 |
|------|---|------|
| `CombineDateTime(date, time)` | 575 | 組裝 DateTime?；date=null→null，time=null→00:00 |
| `TruncateSeconds(DateTime)` | 849 | 截斷秒數（static），避免與 datetime-local input 比對誤差 |
| `SelectPublishTimeAsync(int, int)` | 592 | 設 publishTimePart + 觸發防呆 |
| `SelectUnpublishTimeAsync(int, int)` | 597 | 設 unpublishTimePart + 觸發防呆 |
| `HasAnnouncementPermission()` | 689 | 權限比對（PageUrl，OrdinalIgnoreCase） |
| `GetCurrentUserIdAsync()` | 721 | 從 AuthState ClaimTypes.NameIdentifier 取 userId |
| `ValidateFormAsync()` | 789 | 前端驗證（Title/PublishDate/日期比較/Content） |
| `IsContentEmpty(string?)` | 922 | Regex 去 HTML 後判斷空白 |
| `CloseSlideOverAndWaitAsync()` | 929 | showSlideOver=false + WaitForSlideOverTransitionAsync |
| `WaitForSlideOverTransitionAsync()` | 935 | InvokeAsync(StateHasChanged) + Task.Delay(300ms) |
| `GetCategoryLabel(byte)` | 1030 | switch 1→系統公告...4→其它（static） |
| `GetCategoryClass(byte)` | 1039 | switch → Tailwind CSS 字串（static） |
| `StripHtml(string, int)` | 1049 | Regex 去標籤 + maxLength 截斷（static） |

## 統計卡片（ComputedStats）

由前端 `announcements` 全集 LINQ 計算（不再走 SQL 統計）：
- Total：`announcements.Count`
- Published：`DisplayStatus == "已發佈"` 計數
- Draft：`DisplayStatus == "草稿"` 計數
- Archived：`DisplayStatus == "已下架"` 計數
- Pinned：`IsPinned && DisplayStatus == "已發佈"` 計數（已下架的置頂不計）

## GetProjectDropdownAsync 現況

```sql
SELECT Id, Name FROM dbo.MT_Projects WHERE IsDeleted = 0 ORDER BY Year DESC, Name
```

不區分 CWT/LCT，所有梯次混列於同一下拉。

## 已知未解決的不一致

- `AutoUnpinExpiredAsync` AuditLog JSON 用 SQL 字串拼接（非 `AuditLogJsonHelper.Serialize`），與其他方法不一致。若 Title 含雙引號可能產生格式問題。
- 使用說明手冊顯示的 3 筆 PDF 名稱為硬編碼假資料（非 `MT_UserGuideFiles` 資料表）。

**Why:** 記錄這兩點因為若未來要修改 AutoUnpinExpiredAsync 或接通 UserGuideFiles，需知道這兩個實作細節。
