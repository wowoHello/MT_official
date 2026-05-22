---
name: Overview CWT/LCT 雙模式整合缺口
description: 2026-05-21 DB 新增 ProjectType/ExamLevel 雙模式欄位，但 Overview 三檔尚未整合的待辦清單
type: project
---

**DB 變動（2026-05-21 已部署）**：
- `MT_Projects.ProjectType TINYINT NOT NULL DEFAULT 0` — 0=CWT（7 種題型）、1=LCT（聽力中心）
- `MT_Projects.ExamLevel TINYINT NULL` — CWT 統一命題等級 0=初等、1=中等、2=中高等、3=高等、4=優等；LCT 模式 NULL
- `MT_ProjectTargets.Granularity TINYINT NOT NULL DEFAULT 0` — 0=母題或單題、1=子題（影響配額計算與卡片）
- 相關 migration：`migrate_project_type_and_granularity.sql`、`migrate_project_exam_level.sql`、`fix_granularity_description.sql`、`migrate_memberquotas_granularity.sql`

**Why**：CWT 全民中檢採 7 種題型 + 統一等級；LCT 聽力中心只用聽力測驗/聽力題組兩種題型 + 各題自選聽力難度一~五。新增雙模式後其他頁面（Projects/CwtList/Dashboard）已整合，唯獨 Overview 未追上。

**How to apply — Overview 待補項目**：

1. **題型下拉過濾**（`Overview.razor:94`）：
   - 現況：`@foreach (var (id, key) in QuestionConstants.VisibleTypeIdToKey)` 顯示全部 7 種題型
   - 應改：呼叫 `QuestionConstants.GetVisibleTypeIdToKeyForProject(CurrentProject.ProjectType, CurrentProject.ExamLevel)` 比照 CwtList:213-215 的作法
   - 效果：CWT 排除聽力類 + 依 ExamLevel 過濾不相容題型；LCT 只顯示聽力測驗 / 聽力題組

2. **LevelLabel 顯示差異**（`OverviewService.LevelLabel`）：
   - CWT：顯示「初等/中等/中高等/高等/優等」
   - LCT：聽力題用「難度一~五」，DashboardService:286 範本是 `$"難度{ChineseOrdinal(t.Level ?? 0)}"`
   - 需要查 CurrentProject.ProjectType 決定 fallback；目前 OverviewService.LevelLabel 簽章為 `(typeKey, level)` 不接 projectType，需擴充

3. **Granularity（子題顆粒度）對 Overview 影響**：
   - Overview 已支援子題獨立成列（IncludeSubRows = true），子題 status / 流程獨立
   - 統計卡片是否要區分「母題層」vs「子題層」配額？目前似乎未針對 Granularity 區分（待釐清需求）

4. **CurrentProject 引用**：`Overview.razor:413` 已注入 `[CascadingParameter] ProjectSwitcherItem? CurrentProject`，ProjectSwitcherItem 已帶 `ProjectType` + `ExamLevel`（ProjectModels.cs:141-144），呼叫端零改動可直接讀取。

**Overview 三檔對 CWT/LCT 引用次數（2026-05-21 grep）**：
- `Overview.razor`：0 次
- `OverviewService.cs`：0 次
- `OverviewModels.cs`：0 次

**待動作前需確認**：使用者是否要 Overview 區分 CWT/LCT，或僅作通用展示（題型下拉若不過濾，LCT 梯次會列出 5 種無關題型供選擇但篩出 0 筆，UX 不佳）。先寫計畫書再實作，不要直接動程式碼。
