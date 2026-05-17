---
name: Roles 頁面當前真實結構（2026-05-17 更新）
description: Roles.razor / RoleService.cs / RoleModels.cs 的實際結構摘要，含三個 Modal、SignalR 廣播、歷史優化紀錄、關鍵設計決策
type: project
---

## 檔案清單（三檔規則合規狀況）

1. `Components/Pages/Roles.razor` — UI + @code（1316 行）
2. `Services/RoleService.cs` — 商業邏輯 + Dapper 查詢（1141 行）
3. `Models/RoleModels.cs` — 主要 ViewModel/DTO（224 行）
4. `Models/ModulePermission.cs` — 獨立小檔（約 13 行），待機合併入 RoleModels.cs
5. `Models/RoleTag.cs` — 獨立 record（約 8 行），`record RoleTag(string Name, int Category)`，待機合併入 RoleModels.cs

**Why 違規：** ModulePermission.cs 和 RoleTag.cs 應集中在 RoleModels.cs，目前分拆屬低優先技術債。

## 兩大 Tab 結構

- **Tab A（人員帳號管理）**：左右分割版面，左側 `InternalAccountItem` 清單，右側 `AccountDetailDto` 詳情面板。統計卡片 4 張（帳號總數/啟用中/停用中/系統管理員）。
- **Tab B（角色權限管理）**：`RoleCardItem` 卡片 Grid（1/2/3 欄響應式），統計卡片 3 張（角色總數/內部角色/外部角色）。

## 三個 Modal

1. **帳號 SlideOver**（`CustomModal.ModalType.SlideOver`，max-w-xl）：新增/編輯內部人員，欄位含姓名、登入帳號（編輯時不可改）、信箱（選填）、身份別下拉（僅顯示 Category=0 的內部角色）、公司職稱（選填）、帳號狀態 radio（啟用/停用）、備註。新增時顯示預設密碼提示 `01024304`。
2. **角色 Modal**（`CustomModal.ModalType.Center`）：新增/編輯/檢視角色。欄位含角色名稱、分類（0=內部/1=外部）、角色描述、功能模組 Toggle 清單。預設角色（IsDefault=true）進入為唯讀模式（isRoleReadOnly=true），所有欄位 disabled。
3. **角色使用者清單 Modal**（`CustomModal.ModalType.Center`）：點擊角色卡片上的「X 位使用者」開啟。Source=0 為系統角色（MT_Users.RoleId），Source=1 為梯次指派（MT_ProjectMemberRoles）。

## RoleService 主要公開 Method 清單（1141 行）

### Tab A — 人員帳號管理
- `GetInternalAccountsAsync()` — 取所有 Category=0 帳號清單
- `GetAccountDetailAsync(int userId)` — 取詳情含 EnabledModules
- `CreateAccountAsync(CreateAccountRequest, int operatorId)` — 含 PBKDF2 雜湊、SqlException 2601/2627 翻譯
- `UpdateAccountAsync(UpdateAccountRequest, int operatorId)` — 含 SqlException 翻譯
- `ToggleAccountStatusAsync(int userId, int operatorId)` — 帳號啟用/停用
- `ResetAccountPasswordAsync(int userId, int operatorId)` — 重設為預設密碼 `01024304`（PBKDF2）

### Tab B — 角色權限管理
- `GetRolesAsync()` — 取角色卡片清單含 UserCount/EnabledModuleCount
- `GetRoleDetailAsync(int roleId)` — 取角色含完整 Permissions
- `CreateRoleAsync(CreateRoleRequest, int operatorId)` — 含 MergeRolePermissionsAsync
- `UpdateRoleAsync(UpdateRoleRequest, int operatorId)` — 含 MergeRolePermissionsAsync
- `DeleteRoleAsync(int roleId, int operatorId)` — 先計算引用數，有使用者即擋下
- `GetRoleUsersAsync(int roleId)` — 兩個來源 UNION（MT_Users + MT_ProjectMemberRoles）
- `GetInternalRoleOptionsAsync()` — 取 Category=0 角色供帳號身份別下拉
- `GetActiveModulesAsync()` — 取 MT_Modules 動態清單

### 共用
- `GetUserModuleCardsAsync(int userId, int? projectId)` — thin wrapper 委派至 IMembershipService（30 秒 TTL cache）
- `ChangeOwnPasswordAsync(int userId, string oldPassword, string newPassword)` — 用 PBKDF2 驗舊密碼 + 雜湊新密碼

### 私有輔助
- `BroadcastRoleChangedAsync()` — 先 InvalidateAll cache，再 SignalR SendAsync("ReceiveRoleChanged")，失敗不阻擋
- `MergeRolePermissionsAsync(conn, trans, roleId, permissions)` — DELETE + 批次單一 INSERT
- `WriteAuditAsync(conn, ...)` — 統一寫 MT_AuditLogs，帶 IP、ProjectId=NULL（角色/帳號變更不綁梯次）
- `ValidateAccountRequired(...)` / `ValidateRoleRequired(...)` — 防呆驗證

## 安全性歷史優化（第一波）

### PBKDF2 雜湊接入（取代 SHA256）
- `CreateAccountAsync`：`var passwordHash = AuthService.HashPassword(DefaultInternalPassword)` — `01024304` 預設密碼用 PBKDF2 雜湊
- `ResetAccountPasswordAsync`：同上，重設時重新 PBKDF2
- `ChangeOwnPasswordAsync`：`AuthService.VerifyPassword(oldPassword, currentHash)` 驗舊密碼 + `AuthService.HashPassword(newPassword)` 雜湊新密碼
- 格式：`PBKDF2.v1$<iter>$<salt-base64>$<hash-base64>`，與舊 SHA256 Base64 格式自動相容（login auto-upgrade）

### 移除 EnsureUsernameUniqueAsync（第一波 #4）
- 原本每次新增/編輯帳號前都有一個 `SELECT COUNT` 預檢的 round-trip
- 改靠 `UQ_MT_Users_Username` / `UQ_MT_Users_Email` UNIQUE 索引 + catch `SqlException ex when ex.Number is 2601 or 2627` 翻譯成「帳號已存在」/「Email 信箱已存在」人話
- 每次建立帳號節省一次 round-trip

## 效能歷史優化（第三波 #18）

### MergeRolePermissionsAsync 批次 INSERT
- 改前：`foreach` 每個模組權限獨立 INSERT = 8 次 + 1 次 DELETE = **9 round-trip**
- 改後：1 次 DELETE + 動態拼 `INSERT ... VALUES (),(),...` = **2 round-trip**
- 觸發點：`CreateRoleAsync`（行 561）+ `UpdateRoleAsync`（行 628）
- `Permissions` 欄位（TINYINT，區塊細部權限）目前交由 DB 預設值（1=編輯）處理，未來若需細部權限可在此處加入 args

## IMembershipService 整合（第二波 #7）

- `GetUserModuleCardsAsync` 變 thin wrapper（5 行），SQL 邏輯搬到 `MembershipService.cs`
- `BroadcastRoleChangedAsync` 先呼叫 `_membership.InvalidateAll()` 清除所有快取，再廣播 SignalR
- 觸發 InvalidateAll 的 5 個 RoleService method：`UpdateAccountAsync`、`ToggleAccountStatusAsync`、`CreateRoleAsync`、`UpdateRoleAsync`、`DeleteRoleAsync`
- Cache TTL = 30 秒，角色/帳號異動後立即清除，不用等 TTL 到期

## SignalR 廣播角色變更機制

- Event 名稱：`"ReceiveRoleChanged"`
- Hub：`ProjectsHub`（`/hubs/projects`），角色/帳號 CUD 成功後透過 `BroadcastRoleChangedAsync` 廣播
- 失敗策略：catch 例外後 LogWarning，不阻擋主流程回傳

## 預設角色 vs 自訂角色

- **預設角色**（`MT_Roles.IsDefault = true`）：命題教師、審題委員（專審）、總召；角色 Modal 開啟為完全唯讀（`isRoleReadOnly=true`），所有欄位 disabled，Toggle 不可操作
- **自訂角色**（`IsDefault = false`）：可新增/編輯/刪除，Toggle 自由操作；刪除前計算 MT_Users.RoleId + MT_ProjectMemberRoles 引用數，有使用者即擋下

## 8 個功能模組 Toggle

- 模組清單**動態來自 `MT_Modules` 資料表**（`GetActiveModulesAsync`），不是硬編碼 8 個
- 每個模組都是**單純 ON/OFF 開關**，沒有「僅瀏覽/瀏覽與編輯」兩級分權（公告模組亦同）
- `MT_RolePermissions.Permissions TINYINT` 欄位是 DB 層保留的擴充空間（值 0=檢視/1=編輯），目前 UI 完全不使用，MergeRolePermissionsAsync 未寫入此欄

## 關鍵 Model 類別（RoleModels.cs 224 行）

- `InternalAccountItem`：左側清單用（無 Note/IsFirstLogin）
- `AccountDetailDto`：右側詳情用（含 Note/IsFirstLogin/CreatedAt/LastLoginAt/EnabledModules）
- `RoleCardItem`：角色卡片（含 UserCount/EnabledModuleCount/EnabledModules）
- `RoleDetailDto`：角色 Modal 用（含完整 Permissions 清單）
- `RolePermissionToggle`：Toggle UI 元件資料（ModuleId/ModuleKey/Name/Icon/PageUrl/IsEnabled）
- `RolePermissionInput`：寫入權限用（ModuleId/IsEnabled）
- `RoleUserItem`：角色使用者清單（Source=0/1 兩種來源）
- `UserProfileDto`：個人資料 Modal 用（含 `List<RoleTag> ProjectRoles`）
- `UserModuleCard`：首頁功能卡片用（含 IsEnabled）

## 資料表對照

| 功能 | 資料表 |
|------|--------|
| 角色定義 | MT_Roles（Category:0=內部/1=外部，IsDefault 鎖定預設角色） |
| 功能模組 | MT_Modules（ModuleKey 唯一小寫，IsActive 控制顯示） |
| 角色與模組對應 | MT_RolePermissions（IsEnabled BIT，Permissions TINYINT 保留未用） |
| 使用者帳號 | MT_Users（Status:0=停用/1=啟用，IsFirstLogin，LockoutUntil，PasswordHash nvarchar(150) PBKDF2 格式） |
| 梯次角色指派 | MT_ProjectMemberRoles（外部教師在梯次中的角色） |

**How to apply:**
- 若未來需實作「僅瀏覽/瀏覽與編輯」細部權限，MT_RolePermissions.Permissions 欄位已準備好，需同步更新 RolePermissionToggle、RolePermissionInput 及 MergeRolePermissionsAsync
- 新增 Model 類別優先放入 RoleModels.cs，避免再產生小檔案
- PBKDF2 接入路徑：所有密碼操作統一走 `AuthService.HashPassword()` / `AuthService.VerifyPassword()`，不要在 RoleService 內自行計算
- SqlException 2601/2627 翻譯已涵蓋帳號唯一、Email 唯一兩個場景，不要再加手動預檢 SELECT COUNT

**Why（第三波 #18 決策）：**
保留 DELETE + 單一批次 INSERT 模式（而非 UPSERT/MERGE），因為角色模組數量有限（≤10），DELETE + INSERT 邏輯最簡單且 transaction 安全。
