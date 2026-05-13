---
name: Overview 頁面可檢視角色層級
description: US-006 規格界定的命題總覽頁面可見角色，與 Roles 模組權限矩陣對齊
type: project
---

**Overview.razor (/overview) 可檢視角色（US-006 規格）**：
1. 管理者（系統管理員）
2. 計畫主持人
3. 總召老師
4. 教務管理者

**特性**：管理視角頁面、唯讀、**外部教師（命題教師、審題委員）禁止進入**——與 `feedback_external_user_permissions.md` 規則一致，UI 引導動作絕對不可連到此頁。

**Why**：Overview 是管理員監控全梯次題目流程的儀表板（含真實姓名審題歷程、復原刪除題目等敏感操作），命題/審題教師看了會破壞匿名審題機制與工作流程焦點。

**How to apply**：
- 在 `Components/Shared/` 寫導覽元件、首頁卡片、Dashboard 跳轉時，若觀察到當前使用者為外部教師（命題教師 / 審題委員），絕對不顯示「命題總覽」入口。
- 權限矩陣對應 `RoleService.cs` 的 `ModulePermission` 第 3 項「命題總覽」，預設角色固定關閉（命題教師/審題委員/總召的「總召」例外為開啟）。
- Overview 內唯一寫入動作 = 復原已軟刪除題目（`RestoreAsync`），需有寫入權限的角色才能執行（目前 razor 端透過 AuthState 取 NameIdentifier，沒有額外角色檢查，依賴頁面進入點權限管控）。
