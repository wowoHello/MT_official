---
name: Roles 頁面當前真實結構（2026-05-22 更新）
description: Roles.razor / RoleService.cs / RoleModels.cs 的實際結構摘要，含三個 Modal、SignalR 廣播、歷史優化紀錄、關鍵設計決策
type: project
---

## 檔案清單（三檔規則合規狀況）

1. `Components/Pages/Roles.razor` — UI + @code（**1316 行**，2026-05-22 驗證）
2. `Services/RoleService.cs` — 商業邏輯 + Dapper 查詢（**1141 行**，2026-05-22 驗證）
3. `Models/RoleModels.cs` — 主要 ViewModel/DTO（**224 行**，2026-05-22 驗證）
4. `Models/ModulePermission.cs` — 獨立小檔（13 行）；**注意**：此檔被 AuthService / AnnouncementService / MembershipService / AppointmentCertEndpoints 等多個 Service 引用，**並非僅服務 Roles 頁面**，不適合強行合併入 RoleModels.cs
5. `Models/RoleTag.cs` — 獨立 record（8 行），`record RoleTag(string Name, int Category)`；被 RoleService / ProjectService / TeacherService 引用，同樣跨頁面共用

**Why 5 檔現況：** ModulePermission.cs 和 RoleTag.cs 實際上是**全站共用**的小型 Model，不是 Roles 頁面專屬的技術債，合併入 RoleModels.cs 反而會讓其他 Service 依賴錯誤的命名空間。維持現狀是正確的。

## 兩大 Tab 結構

- **Tab A（人員帳號管理）**：左右分割版面，左側 `InternalAccountItem` 清單，右側 `AccountDetailDto` 詳情面板。統計卡片 4 張（帳號總數/啟用中/停用中/系統管理員）。
- **Tab B（角色權限管理）**：`RoleCardItem` 卡片 Grid（1/2/3 欄響應式），統計卡片 3 張（角色總數/內部角色/外部角色）。

## 三個 Modal

1. **帳號 SlideOver**（`CustomModal.ModalType.SlideOver`，max-w-xl）：新增/編輯內部人員，欄位含姓名、登入帳號（編輯時不可改）、信箱（選填）、身份別下拉（僅顯示 Category=0 的內部角色）、公司職稱（選填）、帳號狀態 radio（啟用/停用）、備註。新增時顯示預設密碼提示 `01024304`。
2. **角色 Modal**（`CustomModal.ModalType.Center`）：新增/編輯/檢視角色。欄位含角色名稱、分類（0=內部/1=外部）、角色描述、功能模組 Toggle 清單。預設角色（IsDefault=true）進入為唯讀模式（isRoleReadOnly=true），所有欄位 disabled。
3. **角色使用者清單 Modal**（`CustomModal.ModalType.Center`）：點擊角色卡片上的「X 位使用者」開啟。Source=0 為系統角色（MT_Users.RoleId），Source=1 為梯次指派（MT_ProjectMemberRoles）。支援 `roleUsersCategory` 旗標，顯示「教師角色」或「系統角色」標籤。

## RoleService 主要公開 Method 清單（1098 行）

### Tab A — 人員帳號管理
- `GetInternalAccountsAsync()` — 取所有 Category=0 帳號清單
- `GetAccountDetailAsync(int userId)` — 取詳情含 EnabledModules
- `CreateAccountAsync(CreateAccountRequest, int operatorId)` — 含 PBKDF2 雜湊、SqlException 2601/2627 翻譯
- `UpdateAccountAsync(UpdateAccountRequest, int operatorId)` — 含 SqlException 翻譯、before/after 快照 OldValue/NewValue
- `ToggleAccountStatusAsync(int userId, int operatorId)` — 帳號啟用/停用，自己不能停用自己
- `ResetAccountPasswordAsync(int userId, int operatorId)` — 重設為預設密碼 `01024304`（PBKDF2）+ LockoutUntil=NULL

### Tab B — 角色權限管理
- `GetRolesAsync()` — 取角色卡片清單含 UserCount/EnabledModuleCount；UserCount = MT_Users.RoleId + MT_ProjectMemberRoles（DISTINCT UserId，排除已在 MT_Users 計算者）
- `GetRoleDetailAsync(int roleId)` — 取角色含完整 Permissions（所有 MT_Modules LEFT JOIN MT_RolePermissions）
- `CreateRoleAsync(CreateRoleRequest, int operatorId)` — 含 EnsureRoleNameUniqueAsync + MergeRolePermissionsAsync + AuditLog
- `UpdateRoleAsync(UpdateRoleRequest, int operatorId)` — 含 EnsureRoleEditableAsync + MergeRolePermissionsAsync + permissionDiff diff
- `DeleteRoleAsync(int roleId, int operatorId)` — 先計算 MT_Users.RoleId + MT_ProjectMemberRoles 引用數，有使用者即擋下
- `GetRoleUsersAsync(int roleId)` — UNION ALL 兩個來源（MT_Users Source=0，MT_ProjectMemberRoles Source=1 含 ProjectName/ProjectCode）
- `GetInternalRoleOptionsAsync()` — 取 Category=0 角色供帳號身份別下拉
- `GetActiveModulesAsync()` — 取 MT_Modules 動態清單（僅 IsActive=1）

### 共用
- `GetUserModuleCardsAsync(int userId, int? projectId)` — thin wrapper 委派至 IMembershipService（30 秒 TTL cache）
- `GetUserProfileAsync(int userId, int? projectId?)` — 取個人資料，含 `List<RoleTag> ProjectRoles`（梯次內所有身份）
- `ChangeOwnPasswordAsync(int userId, string oldPassword, string newPassword)` — 用 PBKDF2 驗舊密碼 + 雜湊新密碼，最小 6 碼

### 私有輔助
- `BroadcastRoleChangedAsync()` — 先 `_membership.InvalidateAll()` 清 cache，再 SignalR `SendAsync("ReceiveRoleChanged")`，失敗只 LogWarning 不阻擋
- `MergeRolePermissionsAsync(conn, trans, roleId, permissions)` — DELETE + 動態拼 VALUES 單一 INSERT（2 round-trip）
- `WriteAuditAsync(conn, ...)` — instance method，統一寫 MT_AuditLogs，帶 `ClientIpResolver.Resolve(_httpContextAccessor)` IP；ProjectId=NULL（角色/帳號變更不綁梯次）
- `ReadAccountSnapshotAsync(conn, userId)` — 讀取帳號快照供 OldValue 使用
- `ReadRoleSnapshotAsync(conn, roleId)` — 讀取角色快照供 OldValue 使用（含已啟用模組列表）
- `BuildPermissionSnapshotAsync(conn, permissions)` — 組裝 audit 用的權限快照列表
- `EnsureRoleIsInternalAsync(conn, roleId)` — 確認角色 Category=0
- `EnsureRoleNameUniqueAsync(conn, name, excludeRoleId)` — 確認角色名稱不重複（SELECT COUNT 預檢，注意：角色名稱未加 UNIQUE 索引，與帳號不同）
- `EnsureRoleEditableAsync(conn, roleId)` — 確認非預設角色

## 安全性歷史優化（第一波）

### PBKDF2 雜湊接入（取代 SHA256）
- `CreateAccountAsync`：`AuthService.HashPassword(DefaultInternalPassword)` — `01024304` 預設密碼用 PBKDF2 雜湊
- `ResetAccountPasswordAsync`：同上，LockoutUntil 一併清 NULL
- `ChangeOwnPasswordAsync`：`AuthService.VerifyPassword(oldPassword, currentHash)` 驗舊密碼 + `AuthService.HashPassword(newPassword)` 雜湊新密碼
- 格式：`PBKDF2.v1$<iter>$<salt-base64>$<hash-base64>`，與舊 SHA256 Base64 格式自動相容（login auto-upgrade）

### 移除 EnsureUsernameUniqueAsync（第一波 #4）
- 改靠 `UQ_MT_Users_Username` / `UQ_MT_Users_Email` UNIQUE 索引 + catch `SqlException ex when ex.Number is 2601 or 2627` 翻譯成「帳號已存在」/「Email 信箱已存在」
- **MT_Roles.Name 仍用 SELECT COUNT 預檢**（EnsureRoleNameUniqueAsync）—— 角色名稱沒有 UNIQUE 索引，是尚存的技術債

## 效能歷史優化（第三波 #18）

### MergeRolePermissionsAsync 批次 INSERT
- 改前：`foreach` 每個模組權限獨立 INSERT = N 次 + 1 次 DELETE = **N+1 round-trip**
- 改後：1 次 DELETE + 動態拼 `INSERT INTO MT_RolePermissions (RoleId, ModuleId, IsEnabled) VALUES (@RoleId, @M0, @E0), ...` = **2 round-trip**
- `Permissions` 欄位（TINYINT，區塊細部權限）目前交由 DB 預設值（1=編輯）處理，MergeRolePermissionsAsync 未寫入此欄
- 觸發點：`CreateRoleAsync` + `UpdateRoleAsync`

## IMembershipService 整合（第二波 #7）

- `GetUserModuleCardsAsync` 變 thin wrapper，SQL 邏輯全在 `MembershipService.cs`（Scoped + IMemoryCache Singleton，30 秒 TTL）
- `BroadcastRoleChangedAsync` 先呼叫 `_membership.InvalidateAll()` 清除所有快取，再廣播 SignalR
- 觸發 InvalidateAll 的 5 個方法：`UpdateAccountAsync`、`ToggleAccountStatusAsync`、`CreateRoleAsync`、`UpdateRoleAsync`、`DeleteRoleAsync`
- Cache Key 格式：`mem:roles:{userId}:{projectId}` / `mem:cards:{userId}:{projectId}`
- 雙層失效 Token：per-user CancellationTokenSource（InvalidateUser） + 全域 CancellationTokenSource（InvalidateAll）

## SignalR 廣播角色變更機制

- Event 名稱：`"ReceiveRoleChanged"`
- Hub：`ProjectsHub`（`/hubs/projects`），角色/帳號 CUD 成功後透過 `BroadcastRoleChangedAsync` 廣播
- 失敗策略：catch 例外後 LogWarning，不阻擋主流程

## 預設角色 vs 自訂角色

- **預設角色**（`MT_Roles.IsDefault = true`）：命題教師、審題委員（專審）、總召；角色 Modal 開啟為完全唯讀（`isRoleReadOnly=true`），角色卡片僅顯示「檢視」按鈕（無刪除）
- **自訂角色**（`IsDefault = false`）：可新增/編輯/刪除；刪除前計算 MT_Users.RoleId + MT_ProjectMemberRoles 引用數，有使用者即擋下並顯示 Swal warning

## 稽核日誌完整覆蓋（before/after 快照）

所有 CRUD 操作均寫入 MT_AuditLogs（ProjectId = NULL），包含：
- `CreateAccountAsync`：newValue 含 `targetDisplayName`
- `UpdateAccountAsync`：oldValue（快照前）+ newValue（快照後），含 permissionDiff 概念
- `ToggleAccountStatusAsync`：oldValue/newValue 含 statusText
- `ResetAccountPasswordAsync`：newValue 含 `passwordReset: true`
- `CreateRoleAsync`：newValue 含 permissions 列表
- `UpdateRoleAsync`：oldValue + newValue 含 permissionDiff（added/removed 陣列）
- `DeleteRoleAsync`：oldValue 含完整 permissions，newValue = null
- `ChangeOwnPasswordAsync`：newValue 含 `passwordChanged: true`

## 8 個功能模組 Toggle

- 模組清單**動態來自 `MT_Modules` 資料表**（GetActiveModulesAsync），不是硬編碼
- 每個模組都是**單純 ON/OFF 開關**，無「僅瀏覽/瀏覽與編輯」兩級分權（公告模組亦同）
- `MT_RolePermissions.Permissions TINYINT` 是 DB 預留空間（預設值 1），目前 UI/Service 完全不使用

## 關鍵 Model 類別（RoleModels.cs 222 行）

- `InternalAccountItem`：左側清單用（含 IsDefaultRole）
- `AccountDetailDto`：右側詳情用（含 Note/IsFirstLogin/CreatedAt/LastLoginAt/EnabledModules）
- `CreateAccountRequest` / `UpdateAccountRequest`：表單請求 DTO（**無 [Required] Data Annotation，前端驗證靠 @code 手動判斷**）
- `RoleCardItem`：角色卡片（含 UserCount/EnabledModuleCount/EnabledModules）
- `RoleDetailDto`：角色 Modal 用（含完整 Permissions 清單）
- `RolePermissionToggle`：Toggle UI 元件資料（ModuleId/ModuleKey/Name/Icon/PageUrl/IsEnabled）
- `RolePermissionInput`：寫入權限用（ModuleId/IsEnabled）
- `RoleUserItem`：角色使用者清單（Source=0/1，ProjectName/ProjectCode 僅 Source=1 時有值）
- `UserProfileDto`：個人資料 Modal 用（含 `List<RoleTag> ProjectRoles`）
- `UserModuleCard`：首頁功能卡片用（含 IsEnabled）

## 資料表對照（2026-05-21 DB Schema 確認）

| 功能 | 資料表 | 關鍵約束 |
|------|--------|---------|
| 角色定義 | MT_Roles（Category:0=內部/1=外部，IsDefault BIT，預設 0） | **Name 已有 UNIQUE 索引**（2026-05-21 dump 確認已建）|
| 功能模組 | MT_Modules（ModuleKey 唯一小寫，IsActive 控制顯示） | `UQ_MT_Modules_ModuleKey` |
| 角色與模組對應 | MT_RolePermissions（IsEnabled BIT 預設 0，Permissions TINYINT 預設 1） | `UQ_MT_RolePermissions_RoleId_ModuleId` |
| 使用者帳號 | MT_Users（Status:0=停用/1=啟用，IsFirstLogin，LockoutUntil，PasswordHash nvarchar(150) PBKDF2 格式） | `UQ_MT_Users_Username`（filtered）、`UQ_MT_Users_Email`（filtered） |
| 梯次角色指派 | MT_ProjectMemberRoles | FK → MT_Roles.Id |
| 專案類型 | MT_Projects.ProjectType TINYINT：0=CWT、1=LCT；MT_Projects.ExamLevel TINYINT NULL（LCT 時為 NULL） | 預設值 0（CWT） |

## CWT / LCT 雙類型專案對角色系統的影響（2026-05-21 分析）

**MT_Projects 新增欄位（2026-05-21 dump 確認）：**
- `ProjectType TINYINT NOT NULL DEFAULT 0` — 0=CWT、1=LCT
- `ExamLevel TINYINT NULL` — CWT 時為 0=初等/1=中等/2=中高等/3=高等/4=優等；LCT 時為 NULL

**角色系統目前支援狀況：**
- `MT_Roles` 結構**未區分 CWT/LCT**，角色是全站共用（非按 ProjectType 分組）
- `MT_RolePermissions` 與 `MT_Modules` 同樣**沒有 ProjectType 欄位**，8 個功能模組對所有專案類型一視同仁
- 目前如果要新增「LCT 命題教師」「LCT 審題委員」等角色，**現有 schema 架構可以支援**（只需在 MT_Roles 插入新列），不需要修改 DB schema
- 角色分類仍是 Category=0（內部）/ Category=1（外部），LCT 角色沿用此欄位即可

**待決議（需使用者確認）：**
1. LCT 專案是否需要獨立的角色（如「LCT 命題教師」），還是與 CWT 角色共用「命題教師」身分？
2. LCT 專案是否有不同的功能模組需求（例如 LCT 無需「命題儀表板」），還是與 CWT 共用同一套 8 模組？
3. 若需要按 ProjectType 控制模組存取，MT_Modules 需要新增 `ForProjectType TINYINT NULL`（NULL=全類型）

**How to apply：**
- 等使用者確認 LCT 角色策略後再動 DB/程式碼
- 若確定共用角色（最簡化方案），不需修改 Roles.razor / RoleService.cs / RoleModels.cs 任何檔案
- 若確定獨立角色，只需在 MT_Roles 插入新 row + IsDefault=true 固定，不需改 schema

**How to apply：**
- 若未來需實作細部權限（僅瀏覽/瀏覽與編輯），MT_RolePermissions.Permissions 欄位已準備好，需同步更新 RolePermissionToggle、RolePermissionInput 及 MergeRolePermissionsAsync
- 新增 Model 類別優先放入 RoleModels.cs；若是多頁面共用的小型 Model（如 RoleTag、ModulePermission）則放在獨立檔案是正確作法
- PBKDF2 路徑：統一走 `AuthService.HashPassword()` / `AuthService.VerifyPassword()`
- SqlException 2601/2627 翻譯涵蓋帳號唯一、Email 唯一；角色名稱唯一仍靠 SELECT COUNT 預檢

**Why（第三波 #18 決策）：**
保留 DELETE + 單一批次 INSERT（而非 UPSERT/MERGE），因角色模組數量有限（≤10），邏輯最簡單且 transaction 安全。
