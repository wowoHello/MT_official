---
name: Roles 頁面當前真實結構（2026-05-08）
description: Roles.razor / RoleService.cs / RoleModels.cs 的實際結構摘要，含三個 Modal、SignalR 廣播、以及檔案數量違規
type: project
---

## 檔案清單（5 個，違反三檔規則）

1. `Components/Pages/Roles.razor` — UI + @code（1317 行）
2. `Services/RoleService.cs` — 商業邏輯 + Dapper 查詢
3. `Models/RoleModels.cs` — 主要 ViewModel/DTO（225 行）
4. `Models/ModulePermission.cs` — 獨立檔案（13 行），只有一個 class，理應合併入 RoleModels.cs
5. `Models/RoleTag.cs` — 獨立 record（8 行），只有 `record RoleTag(string Name, int Category)`，理應合併入 RoleModels.cs

**Why 違規：** ModulePermission.cs 和 RoleTag.cs 應集中在 RoleModels.cs，但目前分拆為獨立檔案，下次整理時可合併。

## 兩大 Tab 結構

- **Tab A（人員帳號管理）**：左右分割版面，左側 `InternalAccountItem` 清單（寬 380-420px），右側 `AccountDetailDto` 詳情面板。統計卡片 4 張（帳號總數/啟用中/停用中/系統管理員）。
- **Tab B（角色權限管理）**：`RoleCardItem` 卡片 Grid（1/2/3 欄響應式），統計卡片 3 張（角色總數/內部角色/外部角色）。

## 三個 Modal

1. **帳號 SlideOver**（`CustomModal.ModalType.SlideOver`，max-w-xl）：新增/編輯內部人員，欄位包含姓名、登入帳號（編輯時不可改）、信箱（選填）、身份別下拉（僅顯示 Category=0 的內部角色）、公司職稱（選填）、帳號狀態 radio（啟用/停用）、備註。新增時顯示預設密碼提示 `01024304`。
2. **角色 Modal**（`CustomModal.ModalType.Center`）：新增/編輯/檢視角色。欄位包含角色名稱、分類（0=內部/1=外部）、角色描述、8 個功能模組 Toggle。預設角色進入此 Modal 為唯讀模式（isRoleReadOnly=true），所有欄位 disabled，按鈕只有「關閉」。
3. **角色使用者清單 Modal**（`CustomModal.ModalType.Center`）：點擊角色卡片上的「X 位使用者」開啟，列出使用此角色的人員（含梯次指派來源）。Source=0 為系統角色，Source=1 為梯次指派。

## 帳號動作按鈕（詳情面板右側）

- 「編輯資料」→ 填充 newAccountModel 後開啟 SlideOver
- 「停用/啟用帳號」→ SweetAlert2 確認 → `ToggleAccountStatusAsync`
- 「重設密碼」→ 重設為 `01024304` 公司統編，強制首次登入

## SignalR 廣播

- `RoleService.UpdateRoleAsync` / `CreateRoleAsync` / `DeleteRoleAsync` 成功後，呼叫 `_hubContext.Clients.All.SendAsync("ReceiveRoleChanged")`
- 目的：通知所有連線客戶端更新模組權限顯示，不需手動重整

## 關鍵 Model 類別

- `InternalAccountItem`：左側清單用（無 Note/IsFirstLogin）
- `AccountDetailDto`：右側詳情用（含 Note/IsFirstLogin/CreatedAt/LastLoginAt/EnabledModules）
- `RoleCardItem`：角色卡片（含 UserCount/EnabledModuleCount/EnabledModules 前 6 個 Badge）
- `RoleDetailDto`：角色 Modal 用（含完整 Permissions 清單）
- `RolePermissionToggle`：Toggle UI 元件資料（ModuleId/ModuleKey/Name/Icon/PageUrl/IsEnabled）
- `RoleUserItem`：角色使用者清單（Source=0 系統角色 / Source=1 梯次指派）
- `UserProfileDto`：個人資料 Modal 用（含 `List<RoleTag> ProjectRoles`，跨梯次身分標籤）
- `UserModuleCard`：首頁功能卡片用（含 IsEnabled 判斷當前使用者是否有此模組存取權）

## 功能模組數量

頁面顯示總計來自 `permissionToggles.Count`（由 `GetActiveModulesAsync()` 從 `MT_Modules` 資料表讀取，不是硬編碼 8）

**How to apply:**
- 下次新增 Roles 相關功能前，先確認這 5 個檔案的結構是否仍與此描述一致
- 若需新增 Model 類別，優先放入 RoleModels.cs，避免再產生更多獨立小檔案
