---
name: CWT/LCT 雙模式專案類型與題型過濾
description: MT_Projects.ProjectType 區分 CWT/LCT；題型過濾、Level 鎖定、配額母/子拆分皆已完成
type: project
---

**事實 1：DB 已加關鍵欄位（2026-05-21 部署）**

| 表 | 欄位 | 型別 | 用途 |
|---|---|---|---|
| `MT_Projects` | `ProjectType` TINYINT NOT NULL DEFAULT 0 | 0=CWT、1=LCT |
| `MT_Projects` | `ExamLevel` TINYINT NULL | CWT 統一命題等級（0=初/1=中/2=中高/3=高/4=優）；LCT 為 NULL |
| `MT_ProjectTargets` | `Granularity` TINYINT NOT NULL DEFAULT 0 | 0=母題或單題、1=子題 |
| `MT_MemberQuotas` | `Granularity` TINYINT NOT NULL DEFAULT 0 | 同上 |
| `MT_MemberQuotas` | `Level` TINYINT NULL | LCT 聽力測驗（TypeId=6）按難度配額時為 1-5 |

**事實 2：CwtList.razor + QuestionAttributesSidebar 已感知 ProjectType + ExamLevel**

- `CwtList.razor` 透過 `[CascadingParameter(Name = "CurrentProject")] ProjectSwitcherItem` 取得 CurrentProject
- 篩選列題型下拉用 `QuestionConstants.GetVisibleTypeIdToKeyForProject(projectType, examLevel)` 過濾：
  - **CWT**：排除聽力類（Listen / ListenGroup），再依 ExamLevel 過濾不相容題型（`TypeLevels[key]` 不含該等級則隱藏）
  - **LCT**：只回聽力類（Listen + ListenGroup）
  - HiddenTypeIds = [2]（精選單選題暫不開放）兩模式都隱藏
- `QuestionAttributesSidebar` 接收 `ProjectType` / `ExamLevel` 參數，並設 `IsLevelLockedByProject = (ProjectType == Cwt && ExamLevel.HasValue)`
- 新題自動帶入 `formData.Level = CurrentProject.ExamLevel.Value`（CwtList:1131-1156，含 `OnQuestionTypeChanged` 切題型時也保留鎖定 Level）
- LCT 由配額卡 `OpenComposeModal(typeKey, q.Level)` 帶入該難度，老師不必手動再選

**事實 3：配額系統已完整支援母/子 + LCT 難度拆分（先前記憶說「尚未實作」已過時）**

`Models/QuestionModels.cs` 的 `QuotaProgressItem` 已加：
```csharp
public byte? Level { get; set; }        // LCT 聽力按難度時 1-5
public byte Granularity { get; set; }   // 0=母題或單題、1=子題
```

`QuestionService.GetMyQuotaProgressAsync`（QuestionService:80-143）**已重寫為三段 QueryMultiple**：
1. 配額列：含 Level / Granularity 維度
2. Questions 統計：按 TypeId + Level GROUP BY
3. SubQuestions 統計：按母題的 TypeId GROUP BY

C# 端 `ComputeQuotaCompleted` 依四種 case 計算 Completed：
- LCT 單題（Level 非 NULL, Granularity=0）：按 TypeId+Level 取 Questions
- CWT 子題（Granularity=1）：按 TypeId 取 SubQuestions
- 其他（CWT 母題/單題、LCT 聽力題組）：按 TypeId 合計 Questions

回傳順序：`SortOrder → Level → Granularity`（LCT 難度卡保持難一到難五順序）。

**事實 4：配額卡片 UI 已實作雙段配對（CwtList:1420-1574）**

- `BuildQuotaCardEntries()` 把 quotaProgress 重組為「呈現用配對」：
  - 題組類（TypeId 3 閱讀 / 5 短文）母+子合併為一張雙段卡
  - 其他配額列維持一列一卡
- `QuotaCard(q)` 普通單段卡（標題後綴 `GetQuotaSuffix(q)` 處理 LCT 難度 / 子題 / 題組母題的標籤）
- `QuotaCardWithSub(master, sub)` 題組類雙段卡：母題段 + 子題段，雙段皆達標才出「已達標」badge
- 達標時點擊仍可進入新增（超量命題允許）

**Why:**
- 表單上母+子是同一次送出，分兩張卡會讓使用者誤以為要分別動作
- CWT 統一等級鎖定的關鍵旗標：`IsLevelLockedByProject = ProjectType == Cwt && ExamLevel.HasValue`

**How to apply:**
- 觸碰配額相關功能前，先確認該專案是 CWT 還是 LCT
- 修改 `QuotaProgressItem` 結構時，UI 端有 `QuotaCard` / `QuotaCardWithSub` / `GetQuotaSuffix` 三處要對齊
- LCT 配額卡點擊 `OpenComposeModal(typeKey, q.Level)` 帶 Level；CWT 一律帶 ExamLevel
- LCT 聽力題組（TypeId=7）配額計算用 Questions（母題），非 SubQuestions — 與 CWT 題組相反

**剩餘技術債（非雙模式問題）：**
- 配額卡片只顯示前 7 個 grid（lg:grid-cols-7）；若 LCT 教師同時被分配多種 TypeId+Level 配額可能超過 7 個會折行（目前未見實際發生）
