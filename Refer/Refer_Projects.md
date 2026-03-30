## 📄 頁面名稱：命題專案管理 - 新增專案 (Projects.razor)

### 🎯 核心目標 (Core Objective)
建立全新命題梯次（專案）。提供高度自動化的表單體驗，包含：時程階段的自動推算聯動、目標題數設定、外部教師指派，以及命題配額的精準分配與數量檢核，確保專案啟動時的資料完整性。

### 🗄️ 關聯資料表 (Related Tables)
- **專案主檔與時程**：`dbo.MT_Projects`, `dbo.MT_ProjectPhases`
- **題數目標**：`dbo.MT_ProjectTargets`, `dbo.MT_QuestionTypes`
- **人員與身分指派**：`dbo.MT_Teachers` (結合 `MT_Users`), `dbo.MT_Roles`, `dbo.MT_ProjectMembers`, `dbo.MT_ProjectMemberRoles`
- **人員配額**：`dbo.MT_MemberQuotas`

---

### ⚙️ 功能模組與業務邏輯 (Functional Modules & Business Logic)

#### 1. 專案基本資料 (Basic Information)
* **輸入資料**：
    * `Year` (所屬年度 - 必填)：整數 (例如 2026)。
    * `Name` (專案名稱 - 必填)：字串。
    * `School` (合作學校 - 選填)：字串。
* **處理邏輯**：
    * 前端表單綁定。後端儲存時，需自動生成 `ProjectCode` (例如依據年度+流水號生成，如 P2026001)。
    * 預設寫入狀態 `Status = 0` (準備中)。

#### 2. 時程規劃設定與聯動推算 (Timeline Phasing & Auto-Calculation)
* **觸發條件**：使用者設定第一階段「產學計畫區間」的「開始日」，或手動修改任一階段的「結束日」。
* **處理邏輯 (前端動態計算)**：
    1. 系統定義 8 個固定階段 (`MT_ProjectPhases.PhaseCode` 1~8)。
    2. **向下連動推算**：當設定階段 $N$ 的「開始日」時，自動依據預設天數 (如截圖：100天、30天、7天等) 計算出階段 $N$ 的「結束日」。
    3. **接續連動**：階段 $N$ 的「結束日」一旦確定，系統自動將階段 $N+1$ 的「開始日」設定為**階段 $N$ 結束日 + 1 天**，並依此骨牌效應往下推算剩餘階段。
    4. **手動覆寫**：允許使用者單獨修改任一日期，修改後依然觸發「接續連動」邏輯更新後續日期。
* **預期輸出**：產生 8 筆完整的 Phase 日期區間，準備寫入 `dbo.MT_ProjectPhases`。

#### 3. 專案需求題數設定 (Project Target Configuration)
* **處理邏輯**：
    1. 讀取 `dbo.MT_QuestionTypes`，動態生成各題型 (一般單選、精選單選、閱讀題組...) 的輸入框。
    2. 使用者輸入各題型所需總目標數量 (`TargetCount`)。
* **預期輸出**：產生一組專案整體需求目標，準備寫入 `dbo.MT_ProjectTargets`。

#### 4. 人員指派 (Personnel Assignment)
* **觸發條件**：點擊新增人員或使用搜尋框。
* **資料來源 (Dropdown Data)**：
    * **人員選單**：聯合查詢 `dbo.MT_Users` 與 `dbo.MT_Teachers` (抓取姓名與 T 開頭編號)。
    * **身分選單**：查詢 `dbo.MT_Roles`，條件為 `Category = 1` (外部人員)，如：命題教師、互審教師、專審委員等。
* **處理邏輯**：
    1. 支援單一教師配置**多重身分** (對應寫入 `MT_ProjectMemberRoles`)。
    2. 只有被賦予「命題教師」身分的人員，才會出現在下方的「人員命題數量配置」區塊中。

#### 5. 人員命題數量配置與狀態檢核 (Quota Allocation & Validation)
* **觸發條件**：點擊「依命題教師人數平均分配」或手動調整各教師配額。
* **處理邏輯**：
    1. **平均分配演算**：取得 `MT_ProjectTargets` (目標題數) 與 `N` (命題教師總數)。針對每一題型，將配額 `QuotaCount` = (目標題數 / N)。若有餘數 (例如 10題分給 3人)，則依序分配剩餘數量給前幾位教師 (如：4, 3, 3)，確保總和不變。
    2. **即時檢核機制 (UI 狀態指示)**：
        * 計算公式：`Σ(某題型各教師配額)` VS `該題型專案總需求數`。
        * > **IF** `已分配 == 需分配` **THEN** 該題型亮綠燈 (✔)，顯示「數量驗證正確」。
        * > **IF** `已分配 != 需分配` **THEN** 該題型亮紅燈 (✖)，顯示「檢查未通過，數量不符」。
* **防呆機制**：必須**所有題型皆顯示綠燈**，且必填欄位均有值時，底部的「儲存 / 建立專案」按鈕才允許點擊。

#### 6. 資料儲存與交易處理 (DB Transaction Save)
* **觸發條件**：點擊「儲存」並通過前端檢核。
* **後端處理邏輯 (嚴格執行 SQL Transaction)**：
    1. `INSERT INTO dbo.MT_Projects` (取得剛建立的 `ProjectId`)
    2. 迴圈 `INSERT INTO dbo.MT_ProjectPhases` (寫入 8 個階段日期)
    3. 迴圈 `INSERT INTO dbo.MT_ProjectTargets` (寫入各題型總目標)
    4. 迴圈 `INSERT INTO dbo.MT_ProjectMembers` (寫入專案成員)
        * 針對每個成員，迴圈 `INSERT INTO dbo.MT_ProjectMemberRoles` (寫入多重任務角色)
        * > **IF** 角色包含命題教師 **THEN** 迴圈 `INSERT INTO dbo.MT_MemberQuotas` (寫入該員各題型配額)
    5. 若上述任一步驟失敗，執行 `ROLLBACK`；全部成功則 `COMMIT`。
    6. 同步寫入 `dbo.MT_AuditLogs` (紀錄 Action = 0 建立專案)。