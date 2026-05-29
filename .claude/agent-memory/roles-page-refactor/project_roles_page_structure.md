---
name: Roles 頁面當前真實結構（2026-05-29 更新）
description: Roles.razor / RoleService.cs / RoleModels.cs 的實際結構摘要，含三個 Modal、SignalR 廣播、歷史優化紀錄、關鍵設計決策
type: project
---

## 檔案清單（三檔規則合規狀況）

1. `Components/Pages/Roles.razor` — UI + @code（**1317 行**，2026-05-29 驗證）
2. `Services/RoleService.cs` — 商業邏輯 + Dapper 查詢（**1142 行**，2026-05-29 驗證）
3. `Models/RoleModels.cs` — 主要 ViewModel/DTO（**225 行**，2026-05-29 驗證）
4. `Models/ModulePermission.cs` — 獨立小檔（13 行）；被 AuthService / AnnouncementService / MembershipService / AppointmentCertEndpoints 等多個 Service 引用，**並非僅服務 Roles 頁面**，不適合強行合併入 RoleModels.cs
5. `Models/RoleTag.cs` — 獨立 record（8 行），`record RoleTag(string Name, int Category)`；被 RoleService / ProjectService / TeacherService 引用，同樣跨頁面共用

**Why 5 檔現況：** ModulePermission.cs 和 RoleTag.cs 實際上是**全站共用**的小型 Model，不是 Roles 頁面專屬的技術債，合併入 RoleModels.cs 反而會讓其他 Service 依賴錯誤的命名空間。維持現狀是正確的。

## 兩大 Tab 結構

- **Tab A（人員帳號管理）**：左右分割版面，左側 `InternalAccountItem` 清單，右側 `AccountDetailDto` 詳情面板。統計卡片 4 張（帳號總數/啟用中/停用中/系統管理員）。Filter 狀態：`accountKeyword`（關鍵字）+ `accountStatusFilter`（全部/active/inactive）。FilteredAccounts 是 C# LINQ 計算屬性，不走 DB。
- **Tab B（角色權限管理）**：`RoleCardItem` 卡片 Grid（1/2/3 欄響應式），統計卡片 3 張（角色總數/內部角色/外部角色）。卡片顯示模組 Badge 最多 6 個，超過則顯示 `+N`。

## 三個 Modal

1. **帳號 SlideOver**（`CustomModal.ModalType.SlideOver`，max-w-xl）：新增/編輯內部人員，欄位含姓名、登入帳號（編輯時 disabled）、信箱（選填）、身份別下拉（僅顯示 Category=0 的內部角色，`internalRoleOptions`）、公司職稱（選填）、帳號狀態 radio（啟用/停用）、備註。新增時顯示預設密碼提示 `01024304`。EditForm + DataAnnotationsValidator 已設定，但 CreateAccountRequest/UpdateAccountRequest **無 [Required] Data Annotation**，必填驗證靠 @code 手動判斷（SaveAccount 方法）。
2. **角色 Modal**（`CustomModal.ModalType.Center`）：新增/編輯/檢視角色。欄位含角色名稱、分類（0=內部/1=外部 radio）、角色描述、功能模組 Toggle 清單（`permissionToggles`）。預設角色（IsDefault=true）進入為唯讀模式（`isRoleReadOnly=true`），顯示 amber 警示橫條，所有欄位 disabled。
3. **角色使用者清單 Modal**（`CustomModal.ModalType.Center`）：點擊角色卡片上的「X 位使用者」開啟（UserCount=0 時按鈕 disabled）。Source=0 為系統角色（MT_Users.RoleId），Source=1 為梯次指派（MT_ProjectMemberRoles，含 ProjectName/ProjectCode）。底部顯示一段說明文字，指引要刪除角色先至對應管理頁面改派人員。

## @code 區塊狀態欄位（Roles.razor 行 721 起）

### Tab A 狀態
- `currentTab`：`"accounts"` / `"roles"`
- `allAccounts: List<InternalAccountItem>`，`selectedAccount: AccountDetailDto?`，`selectedAccountId: int?`
- `accountKeyword: string`，`accountStatusFilter: string`（"all"/"active"/"inactive"）
- `isLoadingAccounts`，`isLoadingAccountDetail`，`isAccountActionBusy`
- `newAccountModel: CreateAccountRequest`（初始化 Status=1），`internalRoleOptions`，`isSavingAccount`，`isAccountEditMode`，`editingAccountId: int?`

### Tab B 狀態
- `allRoles: List<RoleCardItem>`，`isLoadingRoles`
- `showRoleUsersModal`，`roleUsers: List<RoleUserItem>`，`roleUsersRoleName`，`roleUsersCategory`，`isLoadingRoleUsers`
- `newRoleModel: CreateRoleRequest`，`permissionToggles: List<RolePermissionToggle>`（在 LoadModulesAsync 初始化）
- `isSavingRole`，`isRoleEditMode`，`isRoleReadOnly`，`editingRoleId: int?`
- `RoleModalTitle` 計算屬性（三態：「檢視角色」/「編輯角色」/「新增角色」）

### OnInitializedAsync 初始化順序
```
LoadModulesAsync() → LoadInternalRoleOptionsAsync() → LoadRolesAsync() → LoadAccountsAsync()
```

## RoleService 主要公開 Method（IRoleService 介面，1142 行）

### Tab A — 人員帳號管理
- `GetInternalAccountsAsync()` — `WHERE r.Category = 0 ORDER BY u.Status DESC, u.CreatedAt DESC`
- `GetAccountDetailAsync(int userId)` — 含 EnabledModules（`SELECT … FROM MT_RolePermissions rp INNER JOIN MT_Modules m … WHERE rp.IsEnabled = 1 AND m.IsActive = 1`）
- `CreateAccountAsync(CreateAccountRequest, int operatorId)` — PBKDF2 雜湊、EnsureRoleIsInternalAsync、OUTPUT INSERTED.Id、SqlException 2601/2627 翻譯
- `UpdateAccountAsync(UpdateAccountRequest, int operatorId)` — 含 SqlException 翻譯（Email 唯一）、ReadAccountSnapshotAsync 取 OldValue、BroadcastRoleChangedAsync
- `ToggleAccountStatusAsync(int userId, int operatorId)` — 自己不能停用自己（userId == operatorId 擋下）；狀態 before.Status==1 → next=0，否則 next=1
- `ResetAccountPasswordAsync(int userId, int operatorId)` — 重設為 `01024304`（PBKDF2），同時 `IsFirstLogin=1`，`LockoutUntil=NULL`

### Tab B — 角色權限管理
- `GetRolesAsync()` — UserCount = MT_Users.RoleId 計數 + MT_ProjectMemberRoles（DISTINCT UserId，排除 MT_Users 已計算者）；另一條 SQL 拉 RolePermissions 在 C# 端配對 EnabledModules
- `GetRoleDetailAsync(int roleId)` — `FROM MT_Modules LEFT JOIN MT_RolePermissions … WHERE m.IsActive = 1`，`COALESCE(rp.IsEnabled, CAST(0 AS BIT)) AS IsEnabled`，取完整 Toggle 清單（含 false 模組）
- `CreateRoleAsync(CreateRoleRequest, int operatorId)` — EnsureRoleNameUniqueAsync + BEGIN TRAN + INSERT Roles + MergeRolePermissionsAsync + BuildPermissionSnapshotAsync + WriteAuditAsync + COMMIT + BroadcastRoleChangedAsync
- `UpdateRoleAsync(UpdateRoleRequest, int operatorId)` — EnsureRoleEditableAsync + EnsureRoleNameUniqueAsync + ReadRoleSnapshotAsync（取 OldValue）+ BEGIN TRAN + UPDATE Roles + MergeRolePermissionsAsync + permissionDiff（added/removed ModuleKey 陣列）+ WriteAuditAsync + COMMIT + BroadcastRoleChangedAsync
- `DeleteRoleAsync(int roleId, int operatorId)` — EnsureRoleEditableAsync + userCountSql（MT_Users.RoleId + MT_ProjectMemberRoles）有使用者擋下 + ReadRoleSnapshotAsync + BEGIN TRAN + DELETE RolePermissions + DELETE Roles（WHERE IsDefault=0 防呆）+ WriteAuditAsync（oldValue=完整快照，newValue=null）+ COMMIT + BroadcastRoleChangedAsync
- `GetRoleUsersAsync(int roleId)` — UNION ALL：Source=0（MT_Users WHERE RoleId）+ Source=1（MT_ProjectMemberRoles JOIN MT_ProjectMembers JOIN MT_Projects，含 ProjectName/ProjectCode）
- `GetInternalRoleOptionsAsync()` — `WHERE Category = 0 ORDER BY IsDefault DESC, Id`
- `GetActiveModulesAsync()` — `WHERE IsActive = 1 ORDER BY SortOrder`

### 共用
- `GetUserModuleCardsAsync(int userId, int? projectId)` — thin wrapper 委派至 `_membership.GetUserModuleCardsAsync`（30 秒 TTL cache）
- `GetUserProfileAsync(int userId, int? projectId?)` — 含 `List<RoleTag> ProjectRoles`（梯次內所有身份，FROM MT_ProjectMemberRoles）
- `ChangeOwnPasswordAsync(int userId, string oldPassword, string newPassword)` — VerifyPassword 驗舊密碼、驗新舊不同、HashPassword 雜湊、IsFirstLogin=0、WriteAuditAsync

### 私有輔助
- `BroadcastRoleChangedAsync()` — 先 `_membership.InvalidateAll()` 清 cache，再 SignalR `_hubContext.Clients.All.SendAsync("ReceiveRoleChanged")`，失敗 LogWarning
- `MergeRolePermissionsAsync(conn, trans, roleId, permissions)` — DELETE + 動態拼 VALUES 單一 INSERT（2 round-trip）；Permissions TINYINT 欄位交由 DB 預設值，不寫入
- `WriteAuditAsync(...)` — instance method（需要 `_httpContextAccessor` 取 IP），ProjectId=NULL（角色/帳號操作不綁梯次），呼叫 `ClientIpResolver.Resolve`
- `ReadAccountSnapshotAsync(conn, userId)` — 快照型別：`sealed class AccountAuditSnapshot`（Id/Username/DisplayName/Email/RoleId/RoleName/Status/CompanyTitle/Note）
- `ReadRoleSnapshotAsync(conn, roleId)` — 快照型別：`sealed class RoleAuditSnapshot`（Id/Name/Category/Description + `List<PermissionAuditEntry> Permissions`）
- `BuildPermissionSnapshotAsync(conn, permissions)` — 查 MT_Modules WHERE Id IN @Ids，回傳 `List<PermissionAuditEntry>`
- `EnsureRoleIsInternalAsync(conn, roleId)` — 確認 Category=0
- `EnsureRoleNameUniqueAsync(conn, name, excludeRoleId)` — **SELECT COUNT(*) 預檢**（MT_Roles.Name 沒有 UNIQUE 索引，與 MT_Users Username/Email 不同）
- `EnsureRoleEditableAsync(conn, roleId)` — `SELECT IsDefault … WHERE Id = @Id`，IsDefault=true 擋下
- `ValidateAccountRequired` / `ValidateRoleRequired`：靜態方法，必填驗證拋 ArgumentException
- `NormalizeStatus(int)` / `StatusText(int)` / `CategoryText(int)`：靜態輔助

## 私有 Audit 內部類別（行 1112-1140）

```
sealed class AccountAuditSnapshot  { Id, Username, DisplayName, Email, RoleId, RoleName, Status, CompanyTitle, Note }
sealed class RoleAuditSnapshot     { Id, Name, Category, Description, List<PermissionAuditEntry> Permissions }
sealed class PermissionAuditEntry  { ModuleId, ModuleKey, ModuleName, IsEnabled }
```

這三個 class 僅在 RoleService 內部使用，不對外曝露。

## RoleModels.cs 主要類別（225 行）

| 類別 | 用途 |
|------|------|
| `InternalAccountItem` | 左側清單（含 IsDefaultRole，無 Note/IsFirstLogin） |
| `AccountDetailDto` | 右側詳情（含 Note/IsFirstLogin/CreatedAt/LastLoginAt/EnabledModules） |
| `CreateAccountRequest` | 新增表單 DTO（無 [Required] Annotation，前端驗證靠 @code） |
| `UpdateAccountRequest` | 編輯表單 DTO（含 Id，無 Username） |
| `RoleCardItem` | 角色卡片（UserCount/EnabledModuleCount/EnabledModules） |
| `RoleModuleBadge` | 角色卡片上的模組 Badge（含 Icon/ColorClass/BgColorClass） |
| `RoleDetailDto` | 角色 Modal 詳情（含完整 `List<RolePermissionToggle> Permissions`） |
| `RolePermissionToggle` | Toggle UI（ModuleId/ModuleKey/Name/Icon/PageUrl/IsEnabled） |
| `CreateRoleRequest` | 新增角色 DTO（含 `List<RolePermissionInput> Permissions`） |
| `UpdateRoleRequest` | 編輯角色 DTO（含 Id） |
| `RolePermissionInput` | 寫入權限用（ModuleId/IsEnabled） |
| `RoleUserItem` | 角色使用者清單（Source=0/1，ProjectName/ProjectCode 僅 Source=1 有值） |
| `RoleOption` | 身份別下拉用（Id/Name/Category/IsDefault） |
| `ModuleItem` | Toggle 清單來源（含 PageUrl/Description） |
| `UserProfileDto` | 個人資料 Modal（含 `List<RoleTag> ProjectRoles`） |
| `UserModuleCard` | 首頁功能卡片（含 IsEnabled） |

## 安全性整合

- `CreateAccountAsync`：`AuthService.HashPassword(DefaultInternalPassword)` — `01024304` 用 PBKDF2 雜湊（`const string DefaultInternalPassword = "01024304"`）
- `ResetAccountPasswordAsync`：同上，LockoutUntil=NULL，IsFirstLogin=1
- `ChangeOwnPasswordAsync`：`AuthService.VerifyPassword(oldPassword, currentHash)` 驗舊密碼 + 驗新舊不同 + `AuthService.HashPassword(newPassword)` 雜湊新密碼，最小 6 碼
- SqlException 2601/2627 翻譯：Username/Email 含「Email」字串判斷分流翻譯

## 效能整合（第三波 #18）

`MergeRolePermissionsAsync`：
- 改前：`foreach` N 次 INSERT = N+1 round-trip
- 改後：1 DELETE + 動態 `INSERT … VALUES (@RoleId, @M0, @E0), (@RoleId, @M1, @E1), …` = 2 round-trip
- 觸發點：`CreateRoleAsync`、`UpdateRoleAsync`

## IMembershipService 整合（第二波 #7）

- `GetUserModuleCardsAsync` 已是 thin wrapper，SQL 全在 `MembershipService.cs`
- `BroadcastRoleChangedAsync` 先 `_membership.InvalidateAll()`，再 SignalR 廣播
- 觸發 InvalidateAll 的方法：`UpdateAccountAsync`、`ToggleAccountStatusAsync`（均透過 BroadcastRoleChangedAsync 間接呼叫）、`CreateRoleAsync`、`UpdateRoleAsync`、`DeleteRoleAsync`

## DB 資料表對照（2026-05-29 程式碼確認）

| 功能 | 資料表 | 關鍵約束 |
|------|--------|---------|
| 角色定義 | MT_Roles | Category TINYINT（0=內部/1=外部）、IsDefault BIT、**Name 無 UNIQUE 索引**（靠 EnsureRoleNameUniqueAsync SELECT COUNT 預檢）|
| 功能模組 | MT_Modules | `UQ_MT_Modules_ModuleKey`（小寫）、IsActive BIT |
| 角色與模組對應 | MT_RolePermissions | `UQ_MT_RolePermissions_RoleId_ModuleId`；IsEnabled BIT；Permissions TINYINT（預留，預設 1，UI 未使用）|
| 使用者帳號 | MT_Users | `UQ_MT_Users_Username`（filtered）、`UQ_MT_Users_Email`（filtered）；PasswordHash nvarchar(150)（PBKDF2 格式）；Status 0=停用/1=啟用/2=鎖定 |
| 梯次角色指派 | MT_ProjectMemberRoles | FK → MT_Roles.Id；FK → MT_ProjectMembers.Id |
| 稽核紀錄 | MT_AuditLogs | ProjectId=NULL 表示跨梯次操作；OldValue/NewValue 均為 JSON |

## 稽核日誌完整覆蓋（before/after 快照）

所有 CRUD 操作均寫入 MT_AuditLogs（ProjectId=NULL），呼叫 `AuditLogJsonHelper.Serialize`：

| 方法 | OldValue | NewValue | targetDisplayName |
|------|---------|---------|-------------------|
| CreateAccountAsync | 無 | 含 username/displayName/email/roleId/roleName/status/targetDisplayName | ✅ |
| UpdateAccountAsync | 快照（displayName/email/roleId/roleName/status/companyTitle/note） | 同 + targetDisplayName | ✅ |
| ToggleAccountStatusAsync | status/statusText | status/statusText/targetDisplayName | ✅ |
| ResetAccountPasswordAsync | 無 | passwordReset:true/targetDisplayName | ✅ |
| CreateRoleAsync | 無 | name/category/categoryText/description/permissions[]/targetDisplayName | ✅ |
| UpdateRoleAsync | name/category/categoryText/description/permissions[] | 同 + permissionDiff.added[]/permissionDiff.removed[]/targetDisplayName | ✅ |
| DeleteRoleAsync | 完整快照（name/category/categoryText/description/permissions[]）/targetDisplayName | null | ✅ |
| ChangeOwnPasswordAsync | 無 | passwordChanged:true/targetDisplayName | ✅ |

## 8 個功能模組

- 模組清單動態來自 `MT_Modules` 資料表（`GetActiveModulesAsync`），不是硬編碼
- 每個模組都是**單純 ON/OFF 開關**，無「僅瀏覽/瀏覽與編輯」兩級分權（公告模組亦同）
- `MT_RolePermissions.Permissions TINYINT` 是 DB 預留空間，目前 UI/Service 完全不使用
- 角色卡片 Header 右上顯示 `EnabledModuleCount / permissionToggles.Count`（動態計算，非硬編碼 8）

## 預設角色 vs 自訂角色

- **預設角色**（`MT_Roles.IsDefault = true`）：角色 Modal 開啟為完全唯讀（`isRoleReadOnly=true`），顯示 amber 警示橫條；角色卡片僅顯示「檢視」按鈕（無刪除）；EnsureRoleEditableAsync 在 Update/Delete 路徑雙重防呆
- **自訂角色**（`IsDefault = false`）：可新增/編輯/刪除；刪除前 UI 和 Service 都計算 MT_Users.RoleId + MT_ProjectMemberRoles 引用數，有使用者先 UI 攔截（Swal warning），Service 端亦擋

**Why：**
- 預設角色：程式碼透過 `IsDefault=0` WHERE 條件 + DB 層防呆保護，UI 層透過 `isRoleReadOnly` 旗標讓所有欄位 disabled
- 刪除前雙層攔截（UI + Service）設計意圖：UI 先友善提示改派後再刪，Service 再兜底，避免 FK 違反

**How to apply：**
- 新增帳號/角色 Model 放 RoleModels.cs；共用 Model（RoleTag/ModulePermission）保持獨立
- PBKDF2 路徑統一走 `AuthService.HashPassword()` / `AuthService.VerifyPassword()`
- 角色名稱重複靠 `EnsureRoleNameUniqueAsync` SELECT COUNT；帳號唯一靠 DB UNIQUE 索引 catch
- 模組權限 Toggle 值透過 `permissionToggles` 狀態傳遞，SaveRole 時 LINQ `.Select` 轉成 `List<RolePermissionInput>`
