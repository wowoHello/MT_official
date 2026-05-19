---
name: Teachers.razor 完成度狀態
description: Teachers.razor 頁面程式碼現況（2026-05-17 全面重讀），記錄架構、各波次改動、技術債與關鍵實作細節
type: project
---

## 檔案規模（2026-05-19 複核）

- `Components/Pages/Teachers.razor`：**1,971 行**（UI + @code block；含批次匯入 Slide-over 大幅增加）
- `Services/TeacherService.cs`：**1,264 行**（含 ParseExcelAsync + ImportTeachersAsync + ExcelHelper）
- `Models/TeacherModels.cs`：**355 行**（含 BatchImportRow / BatchImportRowStatus / BatchImportRowResult 三個批次匯入類別）

三檔案規則完全符合。TeacherService 注入 5 個依賴：`IDatabaseService`、`ILogger`、`IHttpContextAccessor`、`IQuestionTypeCatalog`、`IAppointmentService`。

---

## 頁面架構

**統計卡片（4 張）**：人才庫總數 / 帳號啟用中 / 帳號停用中 / 本梯次參與教師。

**左側列表（lg:w-[380px] xl:w-[420px]）**：
- `DebouncedSearchInput` 搜尋（姓名、TeacherCode、學校、信箱）
- 三按鈕篩選：全部 / 啟用中 / 停用中（`listFilter` 字串變數）
- 列表項目含：頭像首字、狀態圓點（sage=啟用、terracotta=停用）、姓名、TeacherCode、學校、角色 Badge（`StatusBadge` 元件，Category=0 為內部紫、Category=1 為外部琥珀）
- 未參與當前梯次顯示「未參與此梯次」灰色標籤

**右側詳情面板（hidden lg:flex，背景 bg-oatmeal）**：
- 頂部資訊卡：大頭像（72px）、姓名、狀態 Badge、TeacherCode、Email
- 操作按鈕（垂直排列 3 個）：「編輯資料」、「停用/啟用帳號」、「重設密碼」
- 四個 Tab（`detailTab` 字串）：
  - `info`（基本資料）
  - `compose`（命題歷程）
  - `review`（審題歷程）
  - `projects`（參與專案）

**Tab: 基本資料（info）**：
- 個人資料子區塊：姓名、性別（GenderText）、信箱、電話、身分證（MaskedIdNumber，前3後3遮罩）、帳號建立日、首次登入狀態（IsFirstLogin）、最後登入
- 任教背景子區塊：學校、系所/科別、職稱、專長領域、教學年資、最高學歷（EducationText）
- 備註子區塊（有值才顯示）
- **注意**：任教背景是「基本資料」Tab 的子區塊，不是獨立 Tab

**Tab: 命題歷程（compose）**—— 第三波 #15 已加分頁：
- 4 張統計卡片：累計產出、已採用、不採用、審查中（`TeacherComposeStats`）
- 跨專案篩選下拉（`composeFilterProjectId`，string 型別，`@bind:after="OnComposeFilterChangedAsync"`，切換時 reset page=1）
- Header 顯示「共 N 筆」（`composeResult.TotalCount`）
- 歷程列表欄位：QuestionCode（font-mono）、TypeName（由 Catalog 補）、LevelText、ProjectName、狀態 Badge
- 分頁器：`<Pagination CurrentPage="@composePage" TotalPages="@composeResult.PageCount" IsLoading="@isLoadingCompose" OnPageChanged="OnComposePageChangedAsync" />`

**Tab: 審題歷程（review）**—— 第三波 #15 已加分頁：
- 3 張統計卡片：審題總數、已完成、待審中（`TeacherReviewStats`）
- 跨專案篩選下拉（`reviewFilterProjectId`，string 型別，`@bind:after="OnReviewFilterChangedAsync"`）
- Header 顯示「共 N 筆」（`reviewResult.TotalCount`）
- 歷程列表：雙 Badge（階段 Badge `GetStageBadgeClass` + 決策 Badge `GetDecisionBadgeClass`）
  - 階段 Badge：互審=藍、專審=紫、總審=琥珀；結案後顯示「已結案」灰色
  - 決策 Badge：結案後依 FinalQuestionStatus（12=採用綠、其他=不採用赤陶）；未結案依中間決策（1=通過綠、2=修正黃、3=退回赤陶）
- 分頁器：`<Pagination CurrentPage="@reviewPage" TotalPages="@reviewResult.PageCount" IsLoading="@isLoadingReview" OnPageChanged="OnReviewPageChangedAsync" />`

**Tab: 參與專案（projects）**：
- 右上角「加入梯次」按鈕（`OpenAssignModal`）
- 每張專案卡片：狀態標籤（準備中/進行中/已結案）、ProjectCode、ProjectYear + ProjectName、角色 Badge 列、命題數 + 採用數 + 採用率
- 僅非結案梯次顯示「移除」按鈕（`HandleRemoveFromProject`）
- **下載聘書功能（新，2026-05-15 之後）**：若 `project.HasDownloadableCerts == true`，顯示：
  - 「編輯聘期」按鈕（`OpenEditPeriodModal(userId, projectId)`）
  - 「下載聘書」連結（`api/appointment-cert/download/{projectId}/user/{userId}`，target=_blank）
  - 此功能依賴 `IAppointmentService`，TeacherService 注入後批次查 `GetDownloadableProjectIdsForUserAsync`

**SlideOver（CustomModal, ModalType.SlideOver, max-w-2xl）**：
- 頂部提醒 Banner：教師姓名、任教學校、職稱用於產聘書，請正確填寫
- 三區塊 EditForm（無 DataAnnotationsValidator）：
  1. 基本資料（姓名必填、性別、信箱-新增必填-編輯 disabled、電話、身分證）
  2. 任教背景（學校必填、系所、職稱下拉-固定選項、專長、年資 InputNumber、學歷下拉）
  3. 帳號設定（帳號狀態原生 `input type="radio"`、備註 InputTextArea、預設密碼說明）

**加入梯次 Modal（CustomModal, ModalType.Center）**：
- 梯次下拉（`GetAvailableProjectsAsync`，排除已加入 + 已結案）
- 角色 Checkbox 列（`GetExternalRoleOptionsAsync`，Category=1 外部人員角色）
- Transaction 原子性：MT_ProjectMembers → MT_ProjectMemberRoles → `SyncCertificatesAsync`（聘書 metadata 同步）

---

## 各波次改動歷史（TeacherService 相關）

### 第一波 #1 — PBKDF2 密碼雜湊
`CreateTeacherAsync`（行 719）與 `ResetTeacherPasswordAsync`（行 935）皆呼叫靜態方法 `AuthService.HashPassword(DefaultTeacherPassword)`，不再自己做 SHA256。Hash 格式為 `PBKDF2.v1$<iter>$<salt>$<hash>`。

### 第二波 #8 — QuestionTypeCatalog 快取
`GetTeacherComposeHistoryAsync`（行 259 listSql）與 `GetTeacherReviewHistoryAsync` 皆已移除 `INNER JOIN dbo.MT_QuestionTypes qt`，SELECT 只取 `QuestionTypeId`，在 C# 端 foreach 用 `_typeCatalog.GetName(id)` 補 `TypeName`。Models 的 `TeacherComposeItem` 與 `TeacherReviewItem` 各有 `QuestionTypeId` 欄位。

### 第三波 #15 — OFFSET FETCH 分頁
兩個歷程方法加 `int page = 1, int pageSize = 10` 參數，回傳分頁結果類別：
- `TeacherComposeHistoryResult`：Items / TotalCount / Page / PageSize / `PageCount`（計算屬性）
- `TeacherReviewHistoryResult`：同上模式

SQL pattern：COUNT + `ORDER BY ... OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY`

UI 狀態：`composePage`、`reviewPage`（int）、`isLoadingCompose`、`isLoadingReview`（bool）、`composeResult: TeacherComposeHistoryResult`、`reviewResult: TeacherReviewHistoryResult`。

---

## ITeacherService 方法清單

| 分類 | 方法 |
|---|---|
| 列表/統計 | `GetTeacherListAsync(projectId?)`, `GetTeacherStatsAsync(projectId?)` |
| 詳情 | `GetTeacherDetailAsync(teacherId)` |
| 命題歷程 | `GetTeacherComposeStatsAsync(userId, projectId?)`, `GetTeacherComposeHistoryAsync(userId, projectId?, page, pageSize)` |
| 審題歷程 | `GetTeacherReviewStatsAsync(userId, projectId?)`, `GetTeacherReviewHistoryAsync(userId, projectId?, page, pageSize)` |
| 參與專案 | `GetTeacherProjectsAsync(userId)`, `GetAvailableProjectsAsync(userId)`, `GetExternalRoleOptionsAsync()`, `AssignToProjectAsync(req, operatorId)`, `RemoveFromProjectAsync(userId, projectId, operatorId)` |
| CRUD + 帳號 | `CreateTeacherAsync(req, operatorId)`, `UpdateTeacherAsync(req, operatorId)`, `ToggleTeacherStatusAsync(teacherId, operatorId)`, `ResetTeacherPasswordAsync(teacherId, operatorId)` |
| 批次匯入 | `ParseExcelAsync(Stream fileStream)`, `ImportTeachersAsync(IReadOnlyList<BatchImportRow> rows, int operatorId)` |

---

## 關鍵業務規則與設計決策

**預設密碼常數**：`DefaultTeacherPassword = "CSF@01024304"`（TeacherService 第 49 行）。
- UI 所有提示文字顯示 `CSF@01024304`
- 重設密碼後同時將 `IsFirstLogin = 1`、`LockoutUntil = NULL`
- **重要**：cwt-teacher-rules.md 規格書寫 `Cwt2026!`，但程式碼實作為 `CSF@01024304`，以程式碼為準

**TeacherCode 格式**：`T` + 民國年（西元年 - 1911）+ 3 碼流水號，如 `T115001`。

**新增教師 Email 沿用邏輯**：
- 若 Email 已在 MT_Users 存在，自動沿用既有帳號（不建新 MT_Users，僅插入 MT_Teachers）
- 前端以 `info` toast 告知，非 error
- 若既有使用者已在教師人才庫 → 拋出例外阻止重複新增

**新建教師預設角色**：查詢名稱為 `N'預設教師'` 的角色 ID。若角色不存在拋出例外。

**教師管理 vs 人員帳號管理定位**：
- 教師管理（此頁）：**外部人員**（命題教師、審題委員），帳號以**信箱**登入，預設密碼 `CSF@01024304`
- 人員帳號管理（Roles 頁）：**內部人員**（CWT 職員、管理員），帳號以自訂帳號登入，預設密碼公司統編 `01024304`

**參與專案 StartDate 來源**：MT_ProjectPhases MIN(StartDate)，若無 Phase 資料 fallback 為 SYSDATETIME()，用於 `ProjectStatusHelper.Resolve(StartDate, EndDate, ClosedAt)`。

**梯次切換感應**：`OnParametersSetAsync` 比對 `previousProjectId` 防重複刷新，切換後有已選教師則重新呼叫 `SelectTeacher`（角色 Badge 依梯次不同）。切換時 `composePage = 1; reviewPage = 1` reset。

**移除梯次 Cascade 刪除順序**：MT_MemberQuotas → MT_ProjectMemberRoles → MT_ProjectMembers（含 AuditLog）。

**AuditLog 規則**（Teachers 操作）：ProjectId 一律 NULL（跨梯次活動）；TargetType = `AuditTargetType.Teachers`（byte）。

**命題歷程統計**：AdoptedCount `IN (9, 12)`，RejectedCount `IN (10, 11)`，ReviewingCount `BETWEEN 1 AND 8`。`StatusClosedAdopted=12` / `StatusClosedNotAdopted=11`（TeacherService 常數）。

---

## 批次匯入功能（2026-05-18 計畫書已落地）

計畫書 `D:\IISWebSize\MT\.claude\rules\Plan_TeacherBatchImport_2026-05-18.md` 描述的功能已全部實作：
- Teachers.razor 右上角「批次匯入」按鈕 → 全寬 Slide-over（`ImportSlideOver`）
- 三步驟：Step A（上傳）→ Step B（預覽+驗證）→ Step C（結果摘要）
- 三色列（Valid/Warning/Error）+ checkbox 勾選邏輯
- TeacherService 新增 `ParseExcelAsync` + `ImportTeachersAsync`（NPOI XSSFWorkbook 讀 .xlsx）
- Models 新增 `BatchImportRow` / `BatchImportRowStatus` / `BatchImportRowResult` 三個類別
- 公版範本放置於 `wwwroot/temp/教師資料匯入_公版.xlsx`（需手動複製）

---

## 已知技術債（2026-05-19 複核）

- **TM-01**：EditForm 缺 DataAnnotationsValidator，驗證改用手動 SweetAlert（`HandleSaveTeacher` 開頭 3 個 if）。`TeacherFormModel` 無任何 DataAnnotation Attribute。
- **TM-02**：`ProjectDropdownItem` 定義於 AnnouncementModels.cs（跨模型依賴）。
- **TM-03**：儲存按鈕為 `type="button"` 而非 `type="submit"`。
- **TM-04**：`HandleSaveTeacher` 與 `HandleAssignProject` 各有 1 處不必要的 `StateHasChanged()`。
- **TM-05**：`ToggleTeacherStatusAsync` 查詢+更新未包在 Transaction（低風險競態條件）。
- **TM-08**：帳號狀態 Radio 使用原生 `input type="radio"` 而非 Blazor `InputRadio`。
- **TM-10**：`composeFilterProjectId` / `reviewFilterProjectId` 為 string 型別，`int.Parse()` 轉換（非數字字串仍可能例外）。
- **TM-11（新）**：`AssignToProjectAsync` 中角色逐一 INSERT（N 次），未改成批次 INSERT（第三波 #18 類似問題已在 RoleService 修，但 TeacherService 這裡未跟進）。

**已解除技術債**：
- TM-06（2026-05-08 修正）：預設角色查詢改為 `N'預設教師'`。
- TM-07（2026-05-13 複核）：狀態碼不一致疑慮已確認無誤。

**Why:** 提供完整評估記錄，追蹤技術債進度，避免未來重複評估或誤判完成度。
**How to apply:** 若使用者提議修改 Teachers 頁面，先比對此清單；若詢問驗證問題，這是已知技術債 TM-01，可在計畫書中提出改進。
