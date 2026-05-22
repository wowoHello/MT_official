---
name: CWT/LCT 雙模式專案類型與題型過濾
description: MT_Projects.ProjectType 區分 CWT/LCT、CwtList 已感知並過濾 7 題型；配額 Granularity 已加欄位但 Service/UI 尚未拆母/子顯示
type: project
---

**事實 1：DB 已加兩個關鍵欄位（2026-05-21 部署）**

| 表 | 欄位 | 型別 | 用途 |
|---|---|---|---|
| `MT_Projects` | `ProjectType` TINYINT NOT NULL DEFAULT 0 | 0=CWT、1=LCT |
| `MT_Projects` | `ExamLevel` TINYINT NULL | CWT 統一命題等級（0=初/1=中/2=中高/3=高/4=優）；LCT 為 NULL |
| `MT_ProjectTargets` | `Granularity` TINYINT NOT NULL DEFAULT 0 | 0=母題或單題、1=子題 |
| `MT_MemberQuotas` | `Granularity` TINYINT NOT NULL DEFAULT 0 | 同上（補刀加入，原 migration 漏） |

migration 檔（已執行，非 git 追蹤）：
- `.claude/rules/sql/migrate_project_type_and_granularity.sql`
- `.claude/rules/sql/migrate_memberquotas_granularity.sql`（補上 MT_MemberQuotas 漏的欄位）
- `.claude/rules/sql/fix_granularity_description.sql`（修描述：實作只用 0/1，不是 0/1/2）

**事實 2：CwtList.razor + QuestionAttributesSidebar 已感知 ProjectType + ExamLevel**

- `ProjectSwitcherItem` / `ProjectListItem` / `ProjectDetailDto` / `Create/UpdateProjectRequest` / `ProjectEditDto` 全部新增 `ProjectType` + `ExamLevel` 欄位
- `CwtList.razor` 透過 `[CascadingParameter(Name = "CurrentProject")] ProjectSwitcherItem` 取得 CurrentProject
- 篩選列題型下拉用 `QuestionConstants.GetVisibleTypeIdToKeyForProject(projectType, examLevel)` 過濾：
  - **CWT**：排除聽力類（Listen / ListenGroup），再依 ExamLevel 過濾不相容題型（`TypeLevels[key]` 不含該等級則隱藏）
  - **LCT**：只回聽力類（Listen + ListenGroup）
  - HiddenTypeIds = [2]（精選單選題暫不開放）兩模式都隱藏
- `QuestionAttributesSidebar` 接收 `[Parameter] ProjectType` / `ExamLevel`，並設 `IsLevelLockedByProject = (ProjectType == Cwt && ExamLevel.HasValue)` → CWT 模式下 Level 下拉鎖定為專案等級，title「此 CWT 專案統一命題等級，不可單題變更」
- 新題自動帶入 `formData.Level = CurrentProject.ExamLevel.Value`（CwtList:1122-1142 兩處）

**事實 3：⚠️ 配額系統尚未拆母/子顯示**

`Services/QuestionService.GetMyQuotaProgressAsync`（行 80-110）的 SQL：
```sql
SELECT mq.QuestionTypeId, mq.QuotaCount AS Target, COUNT(q.Id) AS Completed
FROM MT_MemberQuotas mq INNER JOIN MT_ProjectMembers pm ...
LEFT JOIN MT_Questions q ON q.CreatorId = pm.UserId AND ... Status >= 1
GROUP BY mq.QuestionTypeId, mq.QuotaCount;
```
- **未讀 `mq.Granularity`、未讀 `mq.Level`、GROUP BY 沒分組到粒度/難度**
- `QuotaProgressItem` Model 也沒有 `Granularity` / `Level` 欄位
- 配額卡片 UI（CwtList:1407 `QuotaCard`）仍以「一題型 = 一張卡」呈現

**對 CwtList 的影響範圍評估：**

1. **題型篩選 / 新題下拉 / Level 鎖定 → 已完成**（透過 `GetVisibleTypeIdToKeyForProject` + IsLevelLockedByProject）
2. **配額卡片（CWT 模式短文/閱讀題組母/子拆分） → 待擴充**：
   - SQL 需加 `mq.Granularity` 進 SELECT/GROUP BY
   - 同題型若同時存在 Granularity=0 (母) 與 Granularity=1 (子) 配額，需呈現 2 張卡或 1 張卡內雙進度
   - Completed 算法需要把母題（`MT_Questions`）vs 子題（`MT_SubQuestions`）分開統計
3. **LCT 模式配額卡片 → 待擴充**：
   - LCT 教師配額是「難度一/二/三/四/五」級的，需用 `mq.Level` 區分
   - 目前 SQL 也沒讀 `mq.Level`，會把 5 個難度的配額合併
4. **三 Tab（命題作業/審修作業/審核結果） → 不需區分**：流程一致，狀態碼也一致

**Why:**
- CWT 是傳統 7 題型混合命題，閱讀題組 / 短文題組的母題 1 個但底下有 N 個子題，過去配額只算「整題」會與實際入庫題數對不上。
- LCT 是新增的「兩種類型」聽力命題模式，按難度分配教師工作量（A 老師命難度一 10 題、B 老師命難度三 8 題等）。

**How to apply:**
- 觸碰配額相關功能前，先確認該專案是 CWT 還是 LCT
- 修改 `GetMyQuotaProgressAsync` 時，回傳 Model 必須加 `Granularity` + `Level`，並讓 UI 區分顯示
- 動到「等級」相關 UI 時，CWT 用 `GeneralLevelLabels`，LCT 用 `ListenLevelLabels`「難度一～五」
- CWT 統一等級鎖定的關鍵旗標：`IsLevelLockedByProject = ProjectType == Cwt && ExamLevel.HasValue`
- 新增專案編輯/詳情頁面已適配；命題任務頁的「Level 鎖定」+「題型過濾」已適配；剩配額顯示與計算待補
