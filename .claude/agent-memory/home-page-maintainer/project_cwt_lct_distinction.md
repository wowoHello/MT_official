---
name: CWT/LCT 專案類型區分對 Home 頁影響
description: MT_Projects.ProjectType 欄位（0=CWT, 1=LCT）在 DB 與程式碼的實現方式，及對首頁的影響分析
type: project
---

## DB Schema（2026-05-21 查核）

`MT_Projects` 表新增了兩個欄位：
- `ProjectType TINYINT NOT NULL DEFAULT 0` — 0=CWT（全民中檢），1=LCT（聽力中心）
- `ExamLevel TINYINT NULL` — CWT 統一等級（0=初等/1=中等/2=中高等/3=高等/4=優等）；LCT 模式為 NULL

`MT_Questions` 表**沒有** `ProjectType` 欄位，題目的類型透過關聯的 `ProjectId` → `MT_Projects.ProjectType` 推導。

## ProjectModels.cs 中的 enum 定義

```csharp
public enum ProjectType : byte
{
    Cwt = 0,
    Lct = 1
}
```

`ProjectSwitcherItem`（CascadingParameter 傳入 Home）已含 `ProjectType` 與 `ExamLevel` 兩個屬性，切換梯次後可直接讀取。

## 已實作區域（非 Home）

- `ProjectModels.cs`：全部 DTO（CreateProjectRequest / UpdateProjectRequest / ProjectSwitcherItem / ProjectListItem / ProjectDetailItem / ProjectTargetDto）都有 `ProjectType` 與 `ExamLevel`
- `DashboardModels.cs`：圖表 DTO 含 CWT/LCT 雙模式說明（Granularity、Level 欄位）
- `QuestionModels.cs`：`GetEligibleQuestionTypes()` / `GetEligibleExamLevels()` 靜態方法依 `ProjectType` 分流
- `Dashboard.razor` / `CwtList.razor`：已有雙模式渲染邏輯

## Home 頁目前支援狀況（2026-05-21）

**尚未支援 CWT/LCT 區分**，具體表現：

1. **急件警示（HomeService.GetUrgentAlertsAsync）**：
   - 完全不讀取 `ProjectType`，所有 SQL 對 CWT 和 LCT 梯次一視同仁
   - LCT 梯次的配額缺口計算（結果集 #7）是以 `MT_ProjectTargets.TargetCount` 加總；LCT 模式的配額是按難度一~五分組，`Granularity` 欄位有意義，但 HomeService 直接加總不區分
   - 實務上目前可能影響不大（LCT 梯次尚未建立或數量少）

2. **公告看板（HomeService.GetAnnouncementsAsync）**：
   - 委派給 `AnnouncementService.GetHomeAnnouncementsAsync`，以 `ProjectId` 過濾，**不區分 ProjectType**
   - 公告沒有針對 CWT/LCT 的分類，現況合理

3. **功能卡片（ModuleCards）**：
   - 由 MainLayout CascadingValue 傳入，根據角色權限產生，不考慮 ProjectType
   - CWT 與 LCT 梯次的功能模組完全相同，現況合理

## 待辦觀察項（不需立即修改，觀察後決定）

- **#HT-01**：`GetUrgentAlertsAsync` 的結果集 #3（個人配額進度）與結果集 #7（全梯次配額缺口）在 LCT 模式下語意不同：LCT 配額是按難度分組的 `Level` 欄位（`MT_MemberQuotas.Level`），直接加總 `QuotaCount` 不一定能反映真實缺口。待 LCT 梯次實際運作後確認是否需要分流處理。
- **#HT-02**：`CurrentProject` CascadingParameter 已帶入 `ProjectType`，Home.razor 完全沒有讀取使用。若未來急件警示需在 LCT 梯次改變文案（如「難度一~五缺題」），可從 `CurrentProject.ProjectType` 取得，無需改 DB 查詢。

## Why 重要

**Why:** 系統未來會有 CWT 與 LCT 兩種不同的命題流程，且 LCT 題目是依「難度一~五」分組而非「7 種題型」，Home 的急件警示若在 LCT 梯次顯示「7 題型缺口」可能讓使用者困惑。

**How to apply:** 新增急件警示功能、修改配額缺口顯示時，需先確認當前梯次 `CurrentProject.ProjectType` 是 CWT 還是 LCT，再決定顯示邏輯。DB SQL 改動最小（只需 JOIN ProjectType 欄位），C# 端加一個分支即可。
