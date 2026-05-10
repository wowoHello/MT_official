---
name: Teachers.razor 完成度狀態
description: Teachers.razor 頁面實際程式碼狀態（2026-05-08 全面重讀比對），記錄架構、技術債與關鍵實作細節
type: project
---

## 頁面架構（已驗證，以程式碼為準）

**統計卡片（4 張）**：人才庫總數 / 帳號啟用中 / 帳號停用中 / 本梯次參與教師。

**左側列表（lg:w-[380px] xl:w-[420px]）**：
- `DebouncedSearchInput` 搜尋（姓名、TeacherCode、學校、信箱）
- 三按鈕篩選：全部 / 啟用中 / 停用中（`listFilter` 字串變數）
- 列表項目含：頭像首字、狀態圓點（sage=啟用、terracotta=停用）、姓名、TeacherCode、學校、角色 Badge（`StatusBadge` 元件，Category=0 為內部紫、Category=1 為外部琥珀）
- 未參與當前梯次顯示「未參與此梯次」灰色標籤

**右側詳情面板（hidden lg:flex，背景 bg-oatmeal）**：
- 頂部資訊卡：大頭像（72px）、姓名、狀態 Badge、TeacherCode、Email
- 操作按鈕（垂直排列 3 個）：「編輯資料」、「停用/啟用帳號」、「重設密碼」
- 四個 Tab（`detailTab` 字串）：`info`（基本資料）、`compose`（命題歷程）、`review`（審題歷程）、`projects`（參與專案）

**Tab: 基本資料（info）**：
- 個人資料區塊：姓名、性別（GenderText）、信箱、電話、身分證（MaskedIdNumber，前3後3遮罩）、帳號建立日、首次登入狀態（IsFirstLogin）、最後登入
- 任教背景區塊：學校、系所/科別、職稱、專長領域、教學年資、最高學歷（EducationText）
- 備註區塊（有值才顯示）

**Tab: 命題歷程（compose）**：
- 4 張統計卡片：累計產出、已採用、不採用、審查中（對應 `TeacherComposeStats`）
- 跨專案篩選下拉（`composeFilterProjectId`，string 型別，`@bind:after="LoadComposeHistoryAsync"`）
- 歷程列表：QuestionCode（font-mono）、TypeName、LevelText、ProjectName、狀態 Badge（`GetQuestionStatusBadge`/`GetQuestionStatusText`）

**Tab: 審題歷程（review）**：
- 3 張統計卡片：審題總數、已完成、待審中（對應 `TeacherReviewStats`）
- 跨專案篩選下拉（`reviewFilterProjectId`，string 型別）
- 歷程列表：QuestionCode、TypeName、LevelText、ProjectName、階段 Badge（`GetStageBadgeClass`）、決策 Badge（`GetDecisionBadgeClass`）
- 結案後：階段 Badge 改灰色「已結案」，決策 Badge 依 FinalQuestionStatus（12=採用綠、其他=不採用赤陶）

**Tab: 參與專案（projects）**：
- 右上角「加入梯次」按鈕（`OpenAssignModal`，停用帳號會被擋）
- 每張專案卡片：狀態標籤（準備中/進行中/已結案）、ProjectCode、ProjectYear + ProjectName、角色 Badge 列、命題數 + 採用數 + 採用率（QuestionCount=0 時不顯示採用率）
- 僅非結案梯次顯示「移除」按鈕（`HandleRemoveFromProject`）

**SlideOver（CustomModal, ModalType.SlideOver, max-w-2xl）**：
- 三區塊 EditForm：① 基本資料（姓名必填、性別、信箱-新增時必填-編輯時 disabled、電話、身分證）、② 任教背景（學校必填、系所、職稱下拉、專長、年資 InputNumber、學歷下拉）、③ 帳號設定（帳號狀態用原生 `input type="radio"` 非 Blazor InputRadio、備註 InputTextArea）
- 儲存按鈕：Footer `type="button"` 直呼 `HandleSaveTeacher()`（非 OnValidSubmit）
- 預設密碼：`CSF@01024304`（非 `Cwt2026!`）

**加入梯次 Modal（CustomModal, ModalType.Center）**：
- 梯次下拉（`GetAvailableProjectsAsync`，排除已加入 + 已結案）
- 角色 Checkbox 列（`GetExternalRoleOptionsAsync`，Category=1 外部人員角色）
- Transaction 原子性：MT_ProjectMembers → MT_ProjectMemberRoles

---

## 關鍵實作細節（程式碼驗證）

**預設密碼常數**：`DefaultTeacherPassword = "CSF@01024304"`（TeacherService 第 48 行）。UI 上所有提示文字也顯示 CSF@01024304，**不是** `Cwt2026!`。

**TeacherCode 格式**：`T` + 民國年 + 3 碼流水號，例如 `T115001`（自動計算 MAX+1）。

**新增教師 Email 沿用邏輯**：若 Email 已在 MT_Users 存在（Username 或 Email 欄位比對），自動沿用既有帳號，不建新 MT_Users，並以 `info` toast 告知使用者（非 error）。

**狀態碼對應**（命題歷程 GetQuestionStatusText）：
- 0=草稿、1=完成、2=送審、3=互審中、4=互修中、5=專審中、6=專修中、7=總審中、8=總修中
- 12=採用（StatusClosedAdopted）、13=不採用（StatusClosedNotAdopted）
- 注意：TeacherService 常數命名為 `StatusClosedAdopted=12` / `StatusClosedNotAdopted=11`（服務層），但 UI Helper `GetQuestionStatusBadge` 中「採用=12、不採用=13」——這裡存在潛在不一致，需後續確認。

**命題歷程統計查詢**（AdoptedCount）：`Status IN (9, 12)`，RejectedCount：`Status IN (10, 11)`。

**參與專案 StartDate**：由 MT_ProjectPhases MIN(StartDate) 取得，若無 Phase 資料則 fallback 為 SYSDATETIME()，用於 `ProjectStatusHelper.Resolve`（兩參數版本：StartDate + EndDate + ClosedAt）。

**梯次切換感應**：`OnParametersSetAsync` 比對 `previousProjectId` 防重複刷新，切換後若有已選教師則重新呼叫 `SelectTeacher`。

**移除梯次 Cascade 刪除順序**：MT_MemberQuotas → MT_ProjectMemberRoles → MT_ProjectMembers。

**ReviewStats 查詢**：
- CompletedCount：`ReviewStatus = 2`
- PendingCount：`ReviewStatus IN (0, 1)`

---

## 已知技術債（Code Review 2026-05-04 識別，截至 2026-05-08 仍未修復）

- **TM-01**：EditForm 缺 DataAnnotationsValidator，驗證改用手動 SweetAlert 判斷（HandleSaveTeacher 開頭 3 個 if 判斷）。TeacherFormModel 無任何 DataAnnotation Attribute。
- **TM-02**：`ProjectDropdownItem` 定義於 AnnouncementModels.cs，跨模型依賴（建議移至 ProjectModels.cs）。
- **TM-03**：儲存按鈕為 `type="button"` 而非 `type="submit"`（Footer 第 652 行）。
- **TM-04**：HandleSaveTeacher 內（第 992 行）與 HandleAssignProject 內（第 1148 行）各有 1 處不必要的 `StateHasChanged()`。
- **TM-05**：ToggleTeacherStatusAsync 的查詢+更新未包在 Transaction（競態條件低風險）。
- **TM-06**：TeacherProjectItem.EffectiveStatus 使用 `ProjectStatusHelper.Resolve(StartDate, EndDate, ClosedAt)` 三參數版本，EffectiveStatus 的 StartDate 來自 MT_ProjectPhases MIN。
- **TM-08**：帳號狀態 Radio 使用原生 `input type="radio"` 而非 Blazor `InputRadio`。
- **TM-10**：`composeFilterProjectId` / `reviewFilterProjectId` 為 string 型別，使用 `int.Parse()` 轉換有潛在例外風險（空字串已有 `string.IsNullOrEmpty` 保護，但非空非數字字串仍可能拋例外）。

**TeacherService.cs 角色查詢（2026-05-04 標記，截至 2026-05-08 已修正）**：
- 第 631 行已改為查詢 `N'預設教師'`（非舊版的「新創教師」），此項技術債已解除。

**Why:** 提供完整評估記錄，追蹤技術債進度，避免未來重複評估或誤判完成度。
**How to apply:** 若使用者提議修改 Teachers 頁面時，先比對此清單；若使用者詢問「為什麼驗證用 SweetAlert」——這是已知技術債 TM-01，可在計畫書中提出改進。
