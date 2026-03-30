# 命題專案(梯次)新增與儲存實作計畫 (SaveProject)

目標：在 `Projects.razor` 實作「儲存專案」的驗證邏輯，並建立後端 `ProjectService` 將專案主檔資料與相關設定寫入資料庫 (`MT_Projects` 等關聯表)。

## User Review Required

> [!IMPORTANT]
> **寫入資料表範圍確認：**
> 您在需求中提到：「若必填都完成了，將內容記錄到資料表 **MT_Projects** 內」。
> 但根據您的規格文件 `Refer_Projects.md`，新增一個專案時需要透過 SQL 交易 (Transaction) 一次性連動寫入以下六張表：
> 1. `MT_Projects` (主檔)
> 2. `MT_ProjectPhases` (8 階段時程)
> 3. `MT_ProjectTargets` (各題型目標數量)
> 4. `MT_ProjectMembers` (參與成員)
> 5. `MT_ProjectMemberRoles` (成員的多重角色)
> 6. `MT_MemberQuotas` (命題教師的數量配額)
>
> **請問這次的實作，是希望「先單純寫入主檔 MT_Projects 測試就好」，還是要「一次性完成上述全部 6 張表的 Transaction 完整交易寫入邏輯」？** (下方預設計畫已包含一次完成所有表的設計，請您確認)

## Proposed Changes

---

### UI 與驗證層 (Blazor UI)

#### [MODIFY] [Projects.razor](file:///d:/IISWebSize/MT/Components/Pages/Projects.razor)
在 `SaveProject()` 方法中加入以下前端防呆與驗證邏輯：
1. **必填欄位檢查**：確保「所屬年度 (`Year`)」、「專案名稱 (`Name`)」不為空。
2. **時程日期檢查**：確保「產學計畫區間」(階段 1) 以及後續階段的 Start 與 End 都有自動試算或填寫完成。
3. **數量配額檢核**：防呆阻擋，若有「未滿足配額」的紅燈，則跳出 SweetAlert 錯誤提示阻擋送出。
4. **準備 DTO (Data Transfer Object)**：將前端綁定的 `formModel`、`stages`、`allocationTeachers` 封裝為 `CreateProjectRequest` 物件。
5. **呼叫 Service**：執行 `await ProjectService.CreateProjectAsync(dto)`，成功後關閉 Modal 並重新載入列表，跳出成功訊息。

---

### DTO 模型層 (Data Models)

#### [NEW] [Models/ProjectModels.cs](file:///d:/IISWebSize/MT/Models/ProjectModels.cs)
建立用於傳遞專案新增資料的結構：
1. `CreateProjectRequest`：包含 Name, Year, School, CreatedBy 等主檔資訊。
2. `ProjectPhaseDto`：傳遞 8 個階段的 起/迄 日期。
3. `ProjectTargetDto`：傳遞 7 款題型的目標需求數。
4. `ProjectMemberAllocationDto`：傳遞該專案各成員的 身分別 及各題型配額。

---

### 資料存取服務層 (Data Access Layer)

#### [NEW] [Services/ProjectService.cs](file:///d:/IISWebSize/MT/Services/ProjectService.cs)
建立 `IProjectService` 介面及其對應實作：
1. **`CreateProjectAsync(CreateProjectRequest req)` 方法**：
   - 開啟 SqlConnection 與 SqlTransaction (`BEGIN TRAN`)。
   - `INSERT INTO MT_Projects` 取得 `SCOPE_IDENTITY() AS ProjectId`。
   - 根據設計好的 DTO 列舉，使用 Dapper 批次新增 `MT_ProjectPhases` 與 `MT_ProjectTargets`。
   - 迴圈處理勾選的教師，寫入 `MT_ProjectMembers` 取得 `MemberId`，再寫入包含的身分 `MT_ProjectMemberRoles` 以及對應配額 `MT_MemberQuotas`。
   - 同步寫入一筆 `MT_AuditLogs` (Action=0 建立專案)。
   - 若過程無錯誤則 `COMMIT`，否則拋出例外並 `ROLLBACK`。

#### [MODIFY] [Program.cs](file:///d:/IISWebSize/MT/Program.cs)
將 `IProjectService` 註冊至 DI 容器：
- 加入 `builder.Services.AddScoped<IProjectService, ProjectService>();` 讓元件可依賴注入。

## Open Questions

1. 在 `Projects.razor` 裡存檔時，需要記錄建立者 (`CreatedBy`)，目前前端是否已經有辦法全域取得當前使用者的 `UserId`，還是先以 `Admin (Id=1)` 作為預設？
2. 在寫入 `MT_Projects` 時，有一個必填但無法讓使用者填寫的欄位 `ProjectCode (NVARCHAR 20 唯一)`，請問您希望後端如何自動產生？(例如：`P + 系統年份 + 3碼流水號` => `P2026001`)

## Verification Plan

### Manual Verification
1. 在畫面上點擊「新增專案」，但不填名稱即按下「儲存專案」，確認系統會成功阻擋。
2. 在目標題數與教師數量未完全對應齊平 (出現紅字) 的狀態下按「儲存專案」，確認系統會跳出提示阻擋。
3. 填寫完整正確資料後，確認可以成功呼叫儲存邏輯。
4. 進 DB 檢查 `MT_Projects` 與另外五個子表是否正確以 Transaction 一併寫入紀錄。
