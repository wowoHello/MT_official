---
name: Overview CWT/LCT 雙模式整合現況
description: Overview 三檔對 CWT/LCT 雙模式的整合狀態（2026-05-22 確認 razor + service 已落地，Models 仍無引用）
type: project
---

**DB 變動（2026-05-21 已部署）**：
- `MT_Projects.ProjectType TINYINT NOT NULL DEFAULT 0` — 0=CWT、1=LCT
- `MT_Projects.ExamLevel TINYINT NULL` — CWT 用 0~4；LCT 模式 NULL
- `MT_ProjectTargets.Granularity TINYINT NOT NULL DEFAULT 0` — 0=母題或單題、1=子題
- 相關 migration 見 `reference_db_migration_scripts.md`

**Overview 三檔當前整合狀態（2026-05-22 grep + 讀 code 確認）**：

| 檔案 | 引用點 | 狀態 |
|---|---|---|
| `Overview.razor:94-100` | 題型下拉改用 `GetVisibleTypeIdToKeyForProject(CurrentProject?.ProjectType ?? ProjectType.Cwt, CurrentProject?.ExamLevel)` | ✅ 完成 |
| `Overview.razor:218` | LevelLabel 傳入 `CurrentProject?.ProjectType ?? ProjectType.Cwt` | ✅ 完成 |
| `Overview.razor:300` | SlideOver 詳情 LevelLabel 傳入 ProjectType | ✅ 完成 |
| `OverviewService.cs:40, 620-629` | `LevelLabel(typeKey, level, projectType = ProjectType.Cwt)` 簽章已擴充 | ✅ 完成 |
| `OverviewModels.cs` | 仍無 ProjectType / ExamLevel 引用（Filter / Result DTO 不需要，由 razor 從 CurrentProject 直接讀） | ✅ 預期行為 |

**LevelLabel 規則**：`useListenLabels = projectType == ProjectType.Lct || typeKey is Listen or ListenGroup`。LCT 梯次一律走聽力等級用語（難度一~五）；CWT 梯次只在聽力題型走 ListenLevelLabels、其餘 GeneralLevelLabels。

**仍未處理的次要項目**：
- 統計卡片是否要區分「母題層」vs「子題層」配額？目前 `BuildOverviewCountsAsync.TypeIdCounts` 只依母題列累計（SubId IS NULL）避免題組類重複加總，但未針對 Granularity 區分配額卡（待釐清需求）。
- LCT 梯次的 PhaseProgressStepper 7 階段標籤目前 CWT/LCT 共用；若使用者反映 LCT 流程不同需要客製化標籤，再評估。

**CurrentProject 引用入口**：`Overview.razor:415-416` 注入 `[CascadingParameter(Name = "CurrentProject")] ProjectSwitcherItem? CurrentProject`，ProjectSwitcherItem 已帶 `ProjectType` + `ExamLevel`（ProjectModels.cs:141-144），razor 端零改動可直接讀取。

**How to apply**：CWT/LCT 整合本身已落地，無待動作。未來若 Overview 要新增「跨 CWT/LCT 模式」的功能（如統一報表），先確認 vw_QuestionRoundStartedAt 是否需要加 ProjectType 維度（目前 view 不含此條件）。
