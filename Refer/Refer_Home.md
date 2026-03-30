## 📄 頁面名稱：首頁與權限儀表板 (Home.razor)

### 🎯 核心目標 (Core Objective)
作為使用者登入後的第一個導覽樞紐。根據登入者的身分與權限，動態渲染對應的功能模組入口（8 個區塊按鈕），並提供即時的系統資訊（公告）與個人化任務追蹤（急件 / 警示看板）。

### 🗄️ 關聯資料表 (Related Tables)
- **基礎身分與權限**：`dbo.MT_Users`, `dbo.MT_Roles`, `dbo.MT_RolePermissions`, `dbo.MT_Modules`
- **全域與專案公告**：`dbo.MT_Announcements`
- **個人任務與專案警示**：`dbo.MT_Projects`, `dbo.MT_ProjectPhases`, `dbo.MT_ProjectMembers`, `dbo.MT_ProjectMemberRoles`, `dbo.MT_Questions`

---

### ⚙️ 功能模組與業務邏輯 (Functional Modules & Business Logic)

#### 1. 動態功能選單 (8 個區塊按鈕渲染)
* **觸發條件**：頁面載入時 (OnInitializedAsync)。
* **處理邏輯**：
    1. 取得目前登入者的 `UserId`，並查出對應的 `RoleId` (`MT_Users`) 與角色類型 (`MT_Roles.Category`)。
    2. 聯合查詢 `dbo.MT_RolePermissions` 與 `dbo.MT_Modules`，篩選出符合以下條件的資料：
        * `RoleId` = 登入者的 RoleId
        * `MT_RolePermissions.IsEnabled = 1`
        * `MT_Modules.IsActive = 1`
    3. 依據 `MT_Modules.SortOrder` 排序，將查詢到的模組對應渲染成首頁的區塊按鈕。
* **預期呈現之按鈕 (受權限控管)**：
    * 命題儀表板 / 命題專案管理 / 命題總覽 / 命題任務 / 審題任務 / 教師管理系統 / 角色與權限管理 / 系統公告與使用說明

#### 2. 公告與通知看板 (Announcements Board)
* **觸發條件**：頁面載入時。
* **處理邏輯**：
    1. 檢查登入者在 `MT_RolePermissions` 中的 `AnnouncementPerm` 權限（判斷是否顯示該區塊或顯示新增/編輯按鈕）。
    2. 查詢 `dbo.MT_Announcements`，篩選條件為：
        * `Status = 1` (發佈狀態)
        * `PublishDate` <= 系統當前時間
        * `UnpublishDate` IS NULL 或 >= 系統當前時間
        * (可選) 若使用者只屬於特定專案，需結合 `ProjectId` 進行篩選，或顯示 `Category = 1` (系統全域公告)。
    3. 列表排序優先依照 `IsPinned = 1` (置頂) 降序排列，接著依 `PublishDate` 降序排列。

#### 3. 急件 / 到期警示看板 (Urgent / Deadline Alerts Board)
* **觸發條件**：頁面載入時。此區塊高度依賴登入者的「個人化專案參與狀況」。
* **處理邏輯**：
    1. **識別參與專案與身分**：
        * 查詢 `dbo.MT_ProjectMembers` 找出登入者 (`UserId`) 參與的所有進行中專案 (`MT_Projects.Status = 1`)。
        * 透過 `dbo.MT_ProjectMemberRoles` 確認該員在各專案中的具體任務 (如：命題者、一審人員等)。
    2. **階段到期警示 (Phase Alerts)**：
        * 關聯 `dbo.MT_ProjectPhases`，撈取與該員相關專案的階段時程。
        * > **IF** 某階段的 `EndDate` 距離今天小於等於 X 天 (例如 3 天) **THEN** 產生一筆「[專案名稱] OOO 階段即將於 YYYY/MM/DD 截止」的警示。
    3. **個人任務積壓警示 (Task Alerts)**：
        * 關聯 `dbo.MT_Questions`，依據該員的任務身分統計未完成工作。
        * **命題者視角**：統計 `CreatorId` = 登入者 且 `Status` 為「退回修改」或尚未達標的題目數量。
        * **審題者視角**：統計分派給該登入者 (`dbo.MT_ReviewAssignments.ReviewerId`) 且 `ReviewStatus = 0` (待審) 的題目數量。
* **預期輸出 / 狀態改變**：
    * 於畫面上條列式呈現紅/黃燈警示，點擊各項警示可直接跳轉至對應的任務處理頁面（如命題任務頁或審題任務頁）。