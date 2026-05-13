---
name: CwtList 與其他頁面的資料/邏輯關聯點
description: CwtList 與 Projects/Reviews/Overview/Dashboard/Teachers 共用的資料模型與互動規則
type: project
---

CwtList 是命題教師端視角，與管理員端有諸多重疊但語意不同，整理如下。

**1. 與 Projects.razor（命題專案管理）**
- 配額 (`MT_MemberQuotas`) 與目標題數 (`MT_ProjectTargets`) 在 Projects 頁設定，CwtList 只讀。
- 命題階段結束後 Projects 會鎖定人員指派 + 題型題數欄位，CwtList 端則用 `IsCompositionPhaseClosed` 鎖整頁。
- Projects 透過 SignalR `/hubs/projects` 推播給其他連線者，CwtList 不訂閱該 hub（梯次切換由 ProjectSwitcher Cascading 處理）。

**2. 與 Reviews.razor（審題任務）**
- 同一張 `MT_Questions` + `MT_ReviewAssignments` 但視角相反：CwtList 看 `CreatorId = 自己`，Reviews 看 `ReviewerId = 自己`。
- CwtList 的 revision tab 顯示「審題鎖定／修題中／已修題」；Reviews 的審題作業區同階段下顯示「待審／已給意見／已決策」。
- 修題說明 `MT_RevisionReplies` 由 CwtList 寫入，Reviews 的 ReviewModal 顯示為「命題教師回覆」。
- 審題意見 `MT_ReviewAssignments.Comment` 由 Reviews 寫入，CwtList 的 RevisionSlideOver 顯示為「審題委員 A/B/C」（匿名化）。

**3. 與 Overview.razor（命題總覽）**
- Overview 看全梯次所有題目，CwtList 自家視角只看自己命的題目。
- `QuestionListFilter.CreatorId = null` + `IncludeDeleted = true` + `SearchCreatorName = true` 是 Overview 專屬用法；CwtList 一律 `CreatorId = 自己 UserId`。
- Overview 用 `PhaseProgressStepper` 元件呈現七階段燈號，CwtList 不用（只看自己這顆狀態）。
- 兩者共用 `IQuestionService.ListAsync`。

**4. 與 Dashboard.razor（命題儀表板）**
- Dashboard 跨教師統計，CwtList 是個人視角。
- 兩者都會呼叫 `IPhaseTransitionCoordinator.EnsurePhaseTransitionAsync`（Idempotent），由 PhaseCoordinator 防重複觸發。

**5. 與 Teachers.razor（教師管理系統）**
- Teachers 的「命題歷程」分頁 = 該教師跨專案的 CwtList 視角彙整（用 `MT_Questions` filter by CreatorId）。
- 教師啟用/停用、密碼重設都不會立即反映到 CwtList，但會影響登入。

**6. 共用 Service（被多個頁面注入）**
- `IQuestionService`（CwtList / Reviews / Overview / Dashboard / Teachers 都用）
- `IReviewService`（Reviews 主用，CwtList 不用）
- `IPhaseTransitionCoordinator`（CwtList / Reviews / Dashboard 都會在 OnInitialized 時呼叫，避免階段卡住）

**How to apply:**
- 動到 `IQuestionService` 任何方法時，先 grep 跨頁使用點（特別是 ListAsync / GetByIdAsync 的 filter）。
- 修題說明 / 審題意見的匿名化規則是 Reviews 端的需求，CwtList 端只負責「自己看自己歷次修題」（不匿名）+「跨階段審題意見」（匿名 A/B/C），別搞混。
