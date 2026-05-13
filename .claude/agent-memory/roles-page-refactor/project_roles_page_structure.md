---
name: Roles 頁面當前真實結構（2026-05-13 更新）
description: Roles.razor / RoleService.cs / RoleModels.cs 的實際結構摘要，含三個 Modal、SignalR 廣播、以及關鍵設計決策
type: project
---

## 檔案清單（三檔規則合規狀況）

1. `Components/Pages/Roles.razor` — UI + @code（1317 行）
2. `Services/RoleService.cs` — 商業邏輯 + Dapper 查詢（1161 行）
3. `Models/RoleModels.cs` — 主要 ViewModel/DTO（225 行）
4. `Models/ModulePermission.cs` — 獨立小檔（13 行），僅有一個 class，待機合併入 RoleModels.cs
5. `Models/RoleTag.cs` — 獨立 record（8 行），`record RoleTag(string Name, int Category)`，待機合併入 RoleModels.cs

**Why 違規：** ModulePermission.cs 和 RoleTag.cs 應集中在 RoleModels.cs，但目前分拆。下次整理時可合併，屬低優先。

## 兩大 Tab 結構

- **Tab A（人員帳號管理）**：左右分割版面，左側 `InternalAccountItem` 清單（寬 380-420px），右側 `AccountDetailDto` 詳情面板。統計卡片 4 張（帳號總數/啟用中/停用中/系統管理員）。
- **Tab B（角色權限管理）**：`RoleCardItem` 卡片 Grid（1/2/3 欄響應式），統計卡片 3 張（角色總數/內部角色/外部角色）。

## 三個 Modal

1. **帳號 SlideOver**（`CustomModal.ModalType.SlideOver`，max-w-xl）：新增/編輯內部人員，欄位含姓名、登入帳號（編輯時不可改）、信箱（選填）、身份別下拉（僅顯示 Category=0 的內部角色）、公司職稱（選填）、帳號狀態 radio（啟用/停用）、備註。新增時顯示預設密碼提示 `01024304`。
2. **角色 Modal**（`CustomModal.ModalType.Center`）：新增/編輯/檢視角色。欄位含角色名稱、分類（0=內部/1=外部）、角色描述、功能模組 Toggle 清單。預設角色進入為唯讀模式（isRoleReadOnly=true），所有欄位 disabled。
3. **角色使用者清單 Modal**（`CustomModal.ModalType.Center`）：點擊角色卡片上的「X 位使用者」開啟。Source=0 為系統角色（MT_Users.RoleId），Source=1 為梯次指派（MT_ProjectMemberRoles）。

## 帳號動作按鈕（詳情面板右側）

- 「編輯資料」→ 填充 newAccountModel 後開啟 SlideOver
- 「停用/啟用帳號」→ SweetAlert2 確認 → `ToggleAccountStatusAsync`
- 「重設密碼」→ 重設為 `01024304`，強制首次登入標記

## SignalR 廣播

- `UpdateRoleAsync` / `CreateRoleAsync` / `DeleteRoleAsync` 成功後呼叫 `SendAsync("ReceiveRoleChanged")`
- 通知所有連線客戶端更新模組權限顯示

## 關鍵 Model 類別

- `InternalAccountItem`：左側清單用（無 Note/IsFirstLogin）
- `AccountDetailDto`：右側詳情用（含 Note/IsFirstLogin/CreatedAt/LastLoginAt/EnabledModules）
- `RoleCardItem`：角色卡片（含 UserCount/EnabledModuleCount/EnabledModules）
- `RoleDetailDto`：角色 Modal 用（含完整 Permissions 清單）
- `RolePermissionToggle`：Toggle UI 元件資料（ModuleId/ModuleKey/Name/Icon/PageUrl/IsEnabled）
- `RolePermissionInput`：寫入權限用（ModuleId/IsEnabled）
- `RoleUserItem`：角色使用者清單（Source=0/1 兩種來源）
- `UserProfileDto`：個人資料 Modal 用（含 `List<RoleTag> ProjectRoles`）
- `UserModuleCard`：首頁功能卡片用（含 IsEnabled）

## 重要設計決策（2026-05-13 網站功能介紹.md 確認）

- **模組數量動態載入**：`permissionToggles.Count` 來自 `MT_Modules` 資料表，不是硬編碼 8
- **模組權限只有 ON/OFF**：每個模組都是單純開關，沒有「僅瀏覽/瀏覽與編輯」兩級分權；公告模組同樣如此
- **MT_RolePermissions 有 `Permissions TINYINT` 欄位**（DB 層面保留擴充空間，值 0=檢視/1=編輯），但目前 UI 層完全不使用此欄位，MergeRolePermissionsAsync 也未寫入此欄，由 DB 預設值（1=編輯）處理
- **角色刪除前置確認**：先計算 MT_Users.RoleId + MT_ProjectMemberRoles 使用引用數，有使用者即擋下

## 資料表對照

| 功能 | 資料表 |
|------|--------|
| 角色定義 | MT_Roles（Category:0=內部/1=外部，IsDefault 鎖定預設角色） |
| 功能模組 | MT_Modules（ModuleKey 唯一，IsActive 控制顯示） |
| 角色與模組對應 | MT_RolePermissions（IsEnabled BIT，Permissions TINYINT 保留未用） |
| 使用者帳號 | MT_Users（Status:0=停用/1=啟用，IsFirstLogin，LockoutUntil） |
| 梯次角色指派 | MT_ProjectMemberRoles（外部教師在梯次中的角色） |

**How to apply:**
- 若未來需實作「僅瀏覽/瀏覽與編輯」，MT_RolePermissions.Permissions 欄位已準備好，需同步更新 RolePermissionToggle、RolePermissionInput 及 MergeRolePermissionsAsync
- 新增 Model 類別優先放入 RoleModels.cs，避免再產生小檔案
