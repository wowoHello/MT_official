---
name: Announcements 頁面當前完整狀態
description: 2026-05-20 讀碼驗證：三檔行數、DB 欄位型別、TogglePin 樂觀更新、UI 篩選狀態、已知技術債
type: project
---

## 三檔現況（2026-05-20 確認，行數與 2026-05-17 一致）

| 檔案 | 行數量級 | 主要職責 |
|------|---------|---------|
| `Components/Pages/Announcements.razor` | ~884 行 | UI 列表 + SlideOver + 檢視 Modal + @code 邏輯 |
| `Services/AnnouncementService.cs` | ~441 行 | 9 個 Service 方法 + EnsureCanEditAsync 權限門鎖 |
| `Models/AnnouncementModels.cs` | ~100 行 | 2 個 Enum + 5 個 DTO/Model class |

## DB 欄位型別（MT_Announcements，2026-05-20 schema 確認）

| 欄位 | 型別 | 備註 |
|------|------|------|
| Id | INT IDENTITY(1,1) | PK |
| Category | TINYINT NOT NULL | 1-4 |
| Status | TINYINT NOT NULL DEFAULT(0) | 0=草稿, 1=發佈 |
| ProjectId | INT NULL | FK→MT_Projects.Id；NULL=全站廣播 |
| PublishDate | datetime2(7) NOT NULL | 注意是 datetime2，非 datetime |
| UnpublishDate | datetime2(7) NULL | NULL=不自動下架 |
| IsPinned | BIT NOT NULL DEFAULT(0) | |
| Title | NVARCHAR(200) NOT NULL | 上限 200 字元 |
| Content | NVARCHAR(MAX) NOT NULL | HTML 富文本 |
| AuthorId | INT NOT NULL | FK→MT_Users.Id |
| CreatedAt | datetime2(7) NOT NULL DEFAULT(sysdatetime()) | |
| UpdatedAt | datetime2(7) NOT NULL DEFAULT(sysdatetime()) | |

無任何索引（除 PK）。FK 約束：AuthorId→MT_Users.Id、ProjectId→MT_Projects.Id。

**Why:** 記錄 datetime2 型別很重要：若日後有精確度相關 bug（如秒數比較誤差），先確認 C# DateTime 的 Kind 與 SQL datetime2 的對應。

## 四種分類（Category TINYINT，Enum AnnouncementCategory）

| 值 | 名稱 | Tailwind 顏色 | 排序優先 |
|----|------|-------------|---------|
| 1 | 系統公告 | `bg-blue-100 text-blue-700` | 最高 |
| 2 | 命題公告 | `bg-emerald-100 text-emerald-700` | 2 |
| 3 | 審題公告 | `bg-purple-100 text-purple-700` | 3 |
| 4 | 其它 | `bg-gray-100 text-gray-600` | 最低 |

## 三種狀態（設計決策）

**DB 只存兩種**：`MT_Announcements.Status TINYINT` → 0=草稿、1=發佈（沒有 Status=2）。

第三種「已下架」是前端 `AnnouncementListItem.DisplayStatus` computed property 由日期推導：
- Status=0 → 草稿
- Status=1 + 今天 < PublishDate → 未發佈（規格書未列，但實作有此狀態）
- Status=1 + 在有效期間 → 已發佈
- Status=1 + 今天 > UnpublishDate → 已下架

**Why:** 只存兩種是刻意設計，避免狀態冗餘；已下架由「發佈 + 超過 UnpublishDate」推導，永遠與日期一致，不會有 Status 與日期不同步的問題。

## 自動下架機制（重要設計細節）

`AutoUnpinExpiredAsync` 在每次頁面進入時（`OnParametersSetAsync` 第一次通過權限後）執行：
- SQL 條件：`IsPinned=1 AND (Status=0 OR (Status=1 AND SYSDATETIME() > UnpublishDate))`
- **只取消置頂（IsPinned→0），不更改 Status**（因 DB 沒有 Status=2）
- 同時批次寫 AuditLog（SQL SERVER 內含 INSERT...SELECT 一趟搞定）

**注意**：自動下架「未持久化」是設計決策，不是 bug。已下架公告下次頁面載入後仍可自動從日期推導出正確狀態。

## UI 狀態篩選選項（filterDisplayStatus 下拉）

實作有五個選項（規格書只提三種）：
- `all`（所有狀態）
- `已發佈`
- `未發佈`（規格書未列，但實作存在）
- `草稿`
- `已下架`

篩選以 `DisplayStatus` 計算屬性比對，不是 DB Status 欄位直接比對。

**How to apply:** 若未來要調整篩選選項，需同時確認 `DisplayStatus` computed property 的回傳字串值與 `filterDisplayStatus` 的選項值一致。

## TogglePin 樂觀更新模式（HandleTogglePin）

置頂切換採用**樂觀更新**：
1. 立即修改前端 `announcements` list 中的 IsPinned（UI 瞬間反應）
2. 同時發出 Toast 通知（fire-and-forget）
3. 呼叫 `TogglePinAsync` 後端同步
4. 失敗時重新 `LoadDataAsync()` 還原正確狀態

**Why:** 置頂切換是低風險操作，樂觀更新讓年長使用者感受到立即回饋，不用等後端確認。失敗率極低，且有 LoadDataAsync 兜底。

**How to apply:** 若未來要在其他欄位做類似的快速切換（如發佈/下架），可參考此模式；但涉及複雜業務規則的操作不應套用此模式。

## 表單日期防呆機制（OnPublishDateChanged / OnUnpublishDateChanged）

- 上架日期改動後：若上架日期 > 下架日期 → 清空下架日期 + SweetAlert2 提示
- 下架日期改動後：若下架日期 < 上架日期 → 清空下架日期 + SweetAlert2 提示
- SaveAndPublish 時額外檢查：若已置頂但下架日期已過 → 自動取消置頂並提示
- SaveAsDraft 時：若已勾置頂 → 自動取消置頂並提示
- Backdrop 點擊自動存草稿時：三個必填欄位（Title/PublishDate/Content）任一空白 → 不觸發存草稿

`TruncateSeconds` 方法統一截斷秒數，避免 datetime-local 輸入（精度到分鐘）與 DB 比對時產生誤差。

## 排序規則（實際實作 vs 規格書差異）

實際排序（FilteredList 計算屬性內）：
`置頂 DESC > DisplayStatus (已發佈=0/未發佈=1/草稿=2/已下架=3) > Category ASC > PublishDate DESC`

規格書（cwt-ac-rules.md）：「置頂 > 已發佈 > 草稿 > 已下架 > 系統公告 > 命題公告 > 審題公告 > 發佈日由新到舊」

**差異**：實作多了「未發佈」（排序值=1，介於已發佈和草稿之間）。

## 第一波 #5：ModuleKey lowercase 修補（已完成）

**背景**：第一波效能審查發現 `LOWER(ModuleKey)` 讓 DB 索引失效。

原本 `EnsureCanEditAsync` 寫法（已廢棄）：
```sql
WHERE LOWER(m.ModuleKey) = 'announcements'  -- 索引失效！
```

修補後（含兩個面向）：
1. DB 端：`UPDATE MT_Modules SET ModuleKey = LOWER(ModuleKey)` 統一 lowercase
2. 程式端：`WHERE m.ModuleKey = 'announcements'`（直接比對，走 UQ_MT_Modules_ModuleKey 索引）

**How to apply:** 若未來要修改 EnsureCanEditAsync，記得 ModuleKey 一律小寫字串，不能用 LOWER() 包欄位。

## 第二波 #7：EnsureCanEditAsync 改用 IMembershipService（已完成）

**改前**（約 35 行內聯 SQL）：
- 直接查 MT_RolePermissions + MT_Users + MT_ProjectMembers UNION
- 每次寫入操作都打一次 DB

**改後**（1 行 method call）：
```csharp
var canEdit = await _membership.HasModulePermissionAsync(operatorId, null, "announcements");
```

- 走 `IMembershipService` 30 秒短 TTL Cache（Singleton IMemoryCache）
- 連續編輯多筆公告時免重複查 DB
- ModuleKey 傳入 `"announcements"`（小寫，與 DB 一致）

**Why:** 第二波共用基底層改造目標之一，讓 5 處散落的權限查詢統一走 MembershipService。

**How to apply:** 若未來需要呼叫 EnsureCanEditAsync 相同邏輯，直接注入 IMembershipService，不要回頭寫內聯 SQL。

## 權限比對設計（重要，防止未來迴歸）

頁面端 `HasAnnouncementPermission()` 比對方式：
```csharp
ModuleCards?.Any(m =>
    string.Equals(m.PageUrl, "announcements", StringComparison.OrdinalIgnoreCase)
    && m.IsEnabled) == true;
```
用 `PageUrl`（而非 `ModuleKey`），與 `MainLayout.EnforceModuleAccess` 保持一致。
生命週期用 `OnParametersSetAsync`（非 OnInitializedAsync），因 CascadingParameter ModuleCards 異步載入。

## 與首頁公告連動機制

`GetHomeAnnouncementsAsync(projectId)` 方法供 `HomeService` 調用：
- 只回傳 Status=1 且 PublishDate<=NOW 且（UnpublishDate IS NULL OR UnpublishDate>=NOW）
- 支援 ProjectId 篩選（NULL=全站廣播也顯示）
- 排序：IsPinned DESC, PublishDate DESC

## AuditLog 規範（寫入時必須遵守）

- `ProjectId` 欄位：一律 NULL（公告 CUD 屬於跨梯次活動）
- `NewValue`（Create/Update）：JSON 含 targetDisplayName = model.Title
- `OldValue`（Delete）：JSON 含 title、category、isPinned、projectId、targetDisplayName

## Service 方法清單

| 方法 | 說明 |
|------|------|
| `GetAnnouncementListAsync()` | 全部公告（AuthorName、ProjectName JOIN，ORDER BY IsPinned DESC, PublishDate DESC） |
| `GetAnnouncementEditAsync(id)` | 單筆編輯 DTO（不含 AuthorName、ProjectName） |
| `GetProjectDropdownAsync()` | 梯次下拉（IsDeleted=0，Year DESC） |
| `CreateAsync(model, userId)` | 新增 + AuditLog（transaction） |
| `UpdateAsync(id, model, userId)` | 更新 + AuditLog（transaction） |
| `TogglePinAsync(id, userId)` | 置頂切換（OUTPUT INSERTED 取新值）+ AuditLog |
| `AutoUnpinExpiredAsync(userId)` | 批次取消置頂 + 批次 AuditLog（單一 SQL） |
| `DeleteAsync(id, userId)` | 先 AuditLog 再 DELETE + transaction |
| `GetHomeAnnouncementsAsync(projectId)` | 首頁用，僅已發佈且在期間 |

所有寫入前呼叫 `EnsureCanEditAsync`（走 IMembershipService）。

## CWT / LCT 雙模式與公告頁的關係（2026-05-21 新增）

`MT_Projects` 新增兩個欄位（已在 DB schema 確認，`ProjectModels.cs` 已實作）：

| 欄位 | 型別 | 說明 |
|------|------|------|
| `ProjectType` | TINYINT NOT NULL DEFAULT(0) | 0=CWT（全民中檢，7種題型），1=LCT（聽力中心，難度一~五） |
| `ExamLevel` | TINYINT NULL | CWT 統一命題等級：0=初等、1=中等、2=中高等、3=高等、4=優等；LCT 模式 NULL |

**公告頁面影響評估**：

目前 `GetProjectDropdownAsync()` 只拉 `SELECT Id, Name FROM MT_Projects WHERE IsDeleted=0 ORDER BY Year DESC, Name`，**不區分 CWT/LCT**，所有梯次同一下拉混列。

是否需要調整下拉分組（例如「CWT 梯次」/ 「LCT 梯次」optgroup），**需待使用者確認業務需求**，目前不主動修改。

**Why:** 使用者提到命題類型現在區分 CWT 與 LCT，這影響梯次下拉的呈現方式；但公告功能本身（分類/狀態/置頂/內容）與 ProjectType 無關，不需要因此修改公告業務邏輯。

**How to apply:** 若未來決定在公告表單梯次下拉中分組顯示 CWT/LCT，只需修改 `GetProjectDropdownAsync()` SQL（加入 `ProjectType` 欄位）與 `ProjectDropdownItem` DTO（加入 `ProjectType` 屬性），Razor 端改用 `<optgroup>`。

## 已知技術債（2026-05-21 更新）

| 優先 | 缺口 | 狀態 |
|:---:|------|------|
| P1 | 使用說明手冊顯示硬編碼假資料（3 筆 PDF），未接 MT_UserGuideFiles | 未解決 |
| P2 | InlineQuillEditor.razor 缺少 12 個中文標點符號快速插入列 UI | 未解決 |
| P2 | 表單無「狀態下拉」欄位（無法手動設為已下架；已下架僅由日期決定） | 設計決策，可接受 |
| P3 | GetProjectDropdownAsync 下拉未分組 CWT/LCT | 待確認業務需求 |

**已修復缺口**（供歷史參考）：
- P0：`HasAnnouncementPermission()` 改用 PageUrl 比對（已修復）
- 第一波 #5：LOWER(ModuleKey) 索引失效（已修復）
- 第二波 #7：EnsureCanEditAsync 改 IMembershipService（已完成）
