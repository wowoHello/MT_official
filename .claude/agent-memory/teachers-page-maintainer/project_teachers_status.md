---
name: Teachers.razor 完成度狀態
description: Teachers.razor 頁面已通過完整 Code Review（2026-05-04），記錄所有已知問題與符合規範項目
type: project
---

Teachers.razor 於 2026-05-04 進行正式 Code Review，結果歸檔至 `D:\MTrefer\pageFinal_doc\Teachers.md`。

**已完成的核心功能（全部符合 cwt-teacher-rules.md）：**
- 四統計卡片（總數/啟用/停用/本梯次）
- 左側列表（DebouncedSearchInput 搜尋、三按鈕狀態篩選、角色 Badge）
- 右側詳情四 Tab：基本資料 / 命題歷程 / 審題歷程 / 參與專案
- Slide-over 三區塊 EditForm（基本資料、任教背景、帳號設定）
- 加入梯次 Modal（Transaction 原子性、停用帳號防呆）
- 啟用/停用切換（SweetAlert2 確認 + AuditLog）
- 重設密碼（IsFirstLogin = 1 + LockoutUntil 清除）
- 梯次切換感應（OnParametersSetAsync previousProjectId 防抖）
- 移除梯次（Cascade 刪除 MemberQuotas → MemberRoles → Members）

**已知技術債（Code Review 2026-05-04 識別）：**
- [TM-01] EditForm 缺 DataAnnotationsValidator，驗證由 SweetAlert 手動處理，TeacherFormModel 無 DataAnnotation Attribute
- [TM-02] ProjectDropdownItem 定義於 AnnouncementModels.cs，跨模型依賴（建議移至 ProjectModels.cs）
- [TM-03/04] HandleSaveTeacher 儲存按鈕為 type="button" 而非 type="submit"，有兩處不必要的 StateHasChanged()
- [TM-05] ToggleTeacherStatusAsync 的查詢+更新未包在 Transaction（競態條件風險低但不一致）
- [TM-06] TeacherProjectItem.EffectiveStatus 使用三參數 Resolve，未含 CompositionStartDate，與儀表板邏輯略有差異
- [TM-08] 帳號狀態 Radio 使用原生 input 而非 Blazor InputRadio
- [TM-10] composeFilterProjectId/reviewFilterProjectId 為 string 型別，int.Parse 有潛在風險

**TeacherService.cs 未提交修改（2026-05-04）：**
- 將角色查詢字串從「新創教師」改為「預設教師」
- 需確認 MT_Roles 實際角色名稱後提交，否則新增教師功能會拋出例外

**Why:** 建立完整評估記錄，追蹤技術債進度，避免未來重複評估或誤判完成度。
**How to apply:** 若使用者提議修改 Teachers 頁面時，先比對此清單；若使用者詢問「為什麼驗證用 SweetAlert」——這是已知技術債 TM-01，可在計畫書中提出改進。
