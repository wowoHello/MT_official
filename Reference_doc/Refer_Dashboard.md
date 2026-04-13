## 📄 頁面名稱：命題儀表板 (Dashboard.razor)

### 🎯 核心目標 (Core Objective)

提供管理者與總召即時掌握「特定命題梯次 (Project)」的進度概況。透過視覺化圖表追蹤題型缺口，並即時列出逾期警示與系統操作歷程，輔助專案控管。

### 🗄️ 關聯資料表 (Related Tables)

- **全域過濾與目標**：`dbo.MT_Projects`, `dbo.MT_ProjectTargets`, `dbo.MT_ProjectPhases`
- **題目與分類統計**：`dbo.MT_Questions`, `dbo.MT_QuestionTypes`
- **任務與警示追蹤**：`dbo.MT_Users`, `dbo.MT_ReviewAssignments`
- **系統紀錄**：`dbo.MT_AuditLogs`

---

### ⚙️ 功能模組與業務邏輯 (Functional Modules & Business Logic)

#### 0. 全域資料過濾 (Global Context)

- **觸發條件**：頁面載入，或使用者切換頂部導覽列的「命題梯次」。
- **處理邏輯**：取得當前選定的 `ProjectId`，以此作為下方所有數據查詢的基礎條件。

#### 1. 核心數據指標卡 (KPI Cards)

- **觸發條件**：載入選定梯次的數據。
- **處理邏輯**：
  1. **總目標題數**：
     - 查詢 `dbo.MT_ProjectTargets`，條件為 `ProjectId`。
     - 加總所有的 `TargetCount`。
  2. **已納入題庫 (採用)**：
     - 查詢 `dbo.MT_Questions`，條件為 `ProjectId` 且狀態為「已結案/已採用」。
  3. **各階段審修中**：
     - 查詢 `dbo.MT_ProjectPhases` 確認「命題階段」的 `EndDate`。
     - > **IF** 當前日期 > 命題階段 `EndDate` **THEN** 統計 `MT_Questions` 中 `Status` 介於「送審」與「最終結案前」的所有題目數量。

#### 2. 題型缺口達成率 (Chart.js - 圓環圖 Doughnut Chart)

- **視覺呈現**：顯示「已達成題數」與「剩餘缺口」，中心或底部顯示「整體達成率 X%」。
- **處理邏輯**：
  1. **目標總數**：撈取該專案 `MT_ProjectTargets` 的 `TargetCount` 總和。
  2. **已產出數**：撈取該專案 `MT_Questions` 中已達標的題目總和。
  3. **剩餘缺口** = 目標總數 - 已產出數 (若小於 0 則以 0 計)。
  4. 將「已產出數」與「剩餘缺口」封裝為陣列，傳遞給前端 Chart.js 渲染圓環圖。

#### 3. 依題型狀態分佈表 (Chart.js - 堆疊長條圖 Stacked Bar Chart)

- **視覺呈現**：X 軸為「7 種題型」，Y 軸為「數量」，圖例 (Legend) 包含「已結案入庫、各階審修中、教師草稿」。
- **處理邏輯**：
  1. 以 `dbo.MT_QuestionTypes` (單選、複選、題組等) 為基準建立 7 個分類。
  2. 聯合查詢 `dbo.MT_Questions`，依據 `QuestionTypeId` 與 `Status` 進行 Group By 分組統計。
  3. 將 `Status` 歸類為三大 Dataset：
     - **已結案入庫** (Status = 最終階段)
     - **各階審修中** (Status = 審核流程中各狀態)
     - **教師草稿** (Status = 0 或被退回編輯中)
  4. 將數據格式化為 Chart.js Stacked 格式輸出。

#### 4. 逾期與緊急待辦 Top 5 (Urgent Tasks Board)

- **觸發條件**：判斷專案各階段期限與未完成任務的教師。
- **處理邏輯**：
  1. 查詢 `dbo.MT_ProjectPhases` 找出即將到期（例如剩餘 3 天內）或已逾期的階段。
  2. 依據階段，查詢對應的未完成人員：
     - 若為「命題階段」：找尋配額 (`MT_MemberQuotas`) 未滿且題目尚未送出的 `CreatorId`。
     - 若為「審題階段」：查詢 `MT_ReviewAssignments` 中 `ReviewStatus = 0 (待審)` 的 `ReviewerId`。
  3. 組合資訊為警告清單（如：「[陳老師] 一審任務逾期 2 天 - 尚餘 5 題」），依緊急程度排序，取 Top 5 顯示。

#### 5. 最新稽核歷程 (Audit Logs Timeline)

- **視覺呈現**：以歷史節點 (Timeline) 方式由新到舊往下排列。
- **處理邏輯**：
  1. 聯合查詢 `dbo.MT_AuditLogs` 與 `dbo.MT_Users` (取得操作者姓名)。
  2. 篩選與該 `ProjectId` 相關的紀錄 (或全域登入紀錄)，按 `CreatedAt` 降序排列。
  3. 將 `Action` (0:建立, 1:修改, 2:刪除, 3:登入, 4:登出) 轉換為對應的語意標籤與顏色圖示顯示。
