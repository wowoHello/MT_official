---
name: Projects SignalR 與 PhaseTransitionCoordinator
description: ProjectsHub 設計、PhaseTransitionCoordinator 三層防護機制說明
type: project
---

## ProjectsHub（`Hubs/ProjectsHub.cs`）
- 純 Hub 殼，目前無方法定義
- 掛載於 `/hubs/projects`
- ProjectService 注入 `IHubContext<ProjectsHub>` 用於廣播

## PhaseTransitionCoordinator（`Services/PhaseTransitionCoordinator.cs`）

**用途：** 確保「命題階段結束」與「後續階段升級」的 DB 寫入只跑一次，避免 CwtList / Reviews / Overview 三頁面各自重複觸發 SQL。

**三層防護：**
1. IMemoryCache（60 秒去重，lock-free）
2. per-projectId SemaphoreSlim(1,1)（並發鎖）
3. cache double-check（拿到 sem 後再確認，避免 race）

**Cache key 格式：** `phase-tx:{projectId}:{phaseCode}`
- 階段邊界跨過後 phaseCode 變動 → key 不同 → 立即跑升級

**生命週期：**
- 本身為 Singleton；內部透過 IServiceProvider.CreateScope() 取 IQuestionService（Scoped）
- 呼叫 questionService.EnsureCompositionPhaseClosedAsync(projectId)

**重要：** Projects.razor 本身沒有使用 PhaseTransitionCoordinator；該 coordinator 由 CwtList、Reviews、Overview 頁面在 OnParametersSetAsync 呼叫。Projects.razor 只使用 IProjectService。
