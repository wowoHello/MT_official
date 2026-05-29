---
name: 審題頁面現況快照（2026-05）
description: Reviews.razor / ReviewService / ReviewModels 的實作現況摘要，含狀態流轉、三審制度、迴避規則、效能優化歷史。最後對照程式碼全面校正於 2026-05-29。
type: project
---

## 現況摘要（最後更新：2026-05-29 全面校正）

### 三檔行數（wc -l 實際量測）
- `ReviewService.cs`：1702 行
- `Reviews.razor`：1347 行
- `ReviewModels.cs`：327 行

---

### 三審制度現況實作

| 階段 | PhaseCode | ReviewStage enum | 可用決策 | 限制 |
|------|-----------|-----------------|---------|------|
| 互審 | 3 | Mutual=1 | 只能「儲存意見」（SaveCommentDraftAsync） | 無 Approve/Revise/Reject 按鈕 |
| 專審 | 5 | Expert=2 | Approve / Revise | 無 Reject；`SubmitDecisionAsync` step 3 throw InvalidOperationException |
| 總審 | 7/8 | Final=3 | Approve / Revise / Reject | ReturnCount ≥3 解鎖 CanFinalReviewerEdit；第 4 次送審 Reject 強制 Rejected(10) |

三審迴避邏輯落地位置（ReviewService 本身不做分配）：
- **互審迴避**：`QuestionService.EnsureCompositionPhaseClosedAsync`（排除命題者）
- **專審迴避**：`QuestionService.AssignExpertReviewers`（stage1Pairs 排除自己互審過的題）
- **總審迴避**：`QuestionService.AssignFinalReviewers`（排除專審已審過的題）

---

### QuestionStatus 完整編碼（v3.0，SentBack 已廢用）

| Status | 常數名 | 說明 |
|--------|--------|------|
| 0 | Draft | 命題草稿 |
| 1 | Completed | 命題完成 |
| 2 | Submitted | 命題送審 |
| 3 | PeerReviewing | 互審中（鎖定）|
| 4 | PeerEditing | 互審修題中 |
| 5 | ExpertReviewing | 專審中（鎖定）|
| 6 | ExpertEditing | 專審修題中 |
| 7 | FinalReviewing | 總審中（鎖定）|
| 8 | FinalEditing | 總審修題中 |
| 9 | Adopted | 採用 |
| 10 | Rejected | 不採用 |
| 11 | ClosedNotAdopted | 結案未採用（原12，前移）|
| 12 | Archived | 結案入庫（原13，前移）|

`HistoryTabStatuses = [9, 10, 11, 12]` — 審核結果與歷史 Tab 的篩選範圍（ReviewService.GetHistoryAsync SQL IN 條件與 FilteredHistory LINQ 皆用此）

---

### IsHistorical 計算方式（GetMyAssignmentsAsync C# 端 switch，行 234-243）

```csharp
phaseCode switch
{
    3 => stage != ReviewStage.Mutual,   // 互審 active
    4 => stage != ReviewStage.Mutual,   // 互修：Mutual 行仍 active（老師查看意見）
    5 => stage != ReviewStage.Expert,   // 專審 active
    6 => stage != ReviewStage.Expert,   // 專修：Expert 行仍 active
    7 => stage != ReviewStage.Final,    // 總審 active
    8 => stage != ReviewStage.Final,    // 總修：Final 行仍 active（PhaseCode=8 bug 已修）
    _ => false                          // 其他：保守全顯示
}
```

---

### ReviewService 公開方法清單

| 方法 | 說明 |
|------|------|
| `GetMyAssignmentsAsync(projectId, reviewerUserId)` | ROW_NUMBER CTE 去重，排除 Status IN (9,10,11,12)；2 條 SQL 順序執行（無 MARS）；回傳 ReviewAssignmentListResult(Items, CurrentStage) |
| `GetHistoryAsync(projectId)` | UNION ALL 兩段（母題 + 子題），IN HistoryTabStatuses；子題不檢查 q.Status |
| `GetModalDataAsync(questionId, subQuestionId, currentUserId)` | 第三波 #14：QueryMultipleAsync 7 result-sets；先調 QuestionService.GetByIdAsync（獨立連線） |
| `GetHistoryByQuestionIdAsync(questionId, subQuestionId)` | 複用 LoadHistoryAsync(aggregateAllUnits=false)；供 OverviewService 精準模式 |
| `SaveCommentDraftAsync(req, operatorUserId)` | 互審：設 DecidedAt + ReviewStatus=Completed + 寫 AuditLog；非互審：只更新 Comment（guard: Decision IS NULL）|
| `SubmitDecisionAsync(req, operatorUserId)` | 8 步驟 tx：驗證→規則檢查→寫 Assignment→查 PhaseCode→MapDecisionToQuestionStatus→寫 Status→BumpReturnCount→寫 AuditLog |
| `FinalReviewerEditAndDecideAsync(req, operatorUserId)` | Plan_021 單 tx：驗證→讀舊 Status→UPDATE 題目所有欄位→UPDATE Status(9/10)→UpsertFinalSubQuestionsAsync→UPDATE Assignment→2 筆 AuditLog |

---

### GetModalDataAsync 7 個 result-set（行 361-478，megaSql）

```
#1 lastEdit   — AuditLogs 最後真實編輯時間（UserId NOT NULL / Action IN 0,1 / 排除 SystemAutoAuditReasons）
#2 creator    — q.CreatorId → MT_Users.DisplayName
#3 myAssignment — TOP 1 ORDER BY ReviewStage DESC, Id DESC，NULL-safe SubQuestionId 比對
#4 history    — ReviewAssignments UNION ALL RevisionReplies，DecidedAt IS NOT NULL，精準模式
#5 similar    — MT_SimilarityChecks JOIN MT_Questions，ORDER BY SimilarityScore DESC
#6 returnCount — MT_ReviewReturnCounts，TOP 1 ORDER BY Id DESC，NULL-safe SubQuestionId
#7 siblings   — Stage 由 inner subquery 自動推導（無 Assignment 時回 0 列）
```

`question.UpdatedAt` 以 #1 結果覆寫（fallback: question.CreatedAt）

---

### MapDecisionToQuestionStatus（私有靜態，行 900-919）

| PhaseCode | Decision | 新 Status |
|-----------|----------|-----------|
| 7 | Approve | 9（Adopted）|
| 7 | Revise/Reject | null（Buffer，PhaseCode=8 開始時批次分流）|
| 8 | Approve | 9（Adopted）|
| 8 | Revise | 8（FinalEditing）|
| 8 | Reject | null（Caller 依 ReturnCount 判定 8 或 10）|

第 4 次送審強制決策（SubmitDecisionAsync 行 765-787）：
PhaseCode=8 + Final + Reject + existingReturnCount >= 3 → newUnitStatus = Rejected(10)，跳過 BumpReturnCount

---

### BumpReturnCountAsync（私有靜態，行 933-956）

MERGE HOLDLOCK，NULL-safe 比對 (QuestionId, SubQuestionId)。ReturnCount+1 >= 3 時 CanEditByReviewer=1。
觸發條件：`!isResubmit && Stage==Final && (Revise||Reject) && newUnitStatus != Rejected`

---

### Stage B-4 子題獨立決策機制

- `GetMyAssignmentsAsync` SQL（行 139-145）：母題列依 q.Status，子題列依 sq.Status，各自獨立判斷是否排除
- `SubmitDecisionAsync`（行 793-823）：母題 → MT_Questions.Status；子題 → MT_SubQuestions.Status + 條件寫 DecidedAt
- `FinalReviewerEditAndDecideAsync`（行 1099-1116）：同上路由
- `BumpReturnCountAsync`：按單元（QuestionId + SubQuestionId）各自計次
- 母題 Reject 不再級聯子題（已移除）；結案時依母題狀態做最終處置

---

### UpsertFinalSubQuestionsAsync（私有靜態，行 1222-1323）

N+1 模式（SELECT existingIds + foreach UPDATE）。三種題型分支：
- ReadGroup：更新 Stem/CorrectAnswer/OptionA-D/Analysis
- ShortGroup：更新 Stem/Analysis（無 CorrectAnswer）
- ListenGroup：更新 Stem/CorrectAnswer/OptionA-D/Analysis

---

### SystemAutoAuditReasons 排除清單（12 個，行 85-99）

CompositionPhaseEnded / PeerReviewingPhaseStart / PeerEditingPhaseStart / ExpertReviewingPhaseStart / ExpertEditingPhaseStart / FinalReviewingPhaseStart / FinalEditingPhaseStart / ExpertReviewAssigned / FinalReviewAssigned / ExpertReviewerPoolEmpty / FinalReviewerPoolEmpty / FinalReviewerPoolUnderQuota

保留顯示的 Reason：`Revision`（老師修題）/ `FinalEditingResubmit`（老師修題後送審）

---

### LoadHistoryAsync（私有靜態，行 1340-1445）

兩模式：
- `aggregateAllUnits=false`（精準）：ISNULL(-1) NULL-safe 過濾單一單元；GetHistoryByQuestionIdAsync / GetModalDataAsync #4 inline 精準 SQL 用此模式
- `aggregateAllUnits=true`（彙整）：UnitInfo CTE，LEFT JOIN 組 UnitDisplayCode；OverviewService 管理員視角用此模式

兩個資料源（Kind=1/2 UNION ALL）：MT_ReviewAssignments / MT_RevisionReplies；DecidedAt IS NOT NULL 判定已完成。

---

### ApplyFinalReturnSequence（私有靜態，行 1458-1478）

對總審 Revise/Reject 意見依時間升冪加序號：「總審第一次退回」...「總審第三次退回」。
idx >= 4 且 Reject → 標「總審最終不採用」。

---

### ReviewModels.cs 公開型別清單（327 行）

**Enum（3 個）**
- `ReviewStage`：Mutual=1 / Expert=2 / Final=3
- `ReviewTaskStatus`：Pending=0 / Reviewing=1 / Completed=2
- `ReviewDecision`：Approve=1 / Revise=2 / Reject=3
- `ReviewHistoryKind`：QuestionEvent=1 / ReviewComment=2 / RevisionReply=3

**DTO（9 個）**
- `ReviewModalData`：Question / CreatorName / MyAssignment / History / SimilarQuestions / FinalReturnCount / CanFinalReviewerEdit / SiblingUnits
- `ReviewSiblingUnit`：AssignmentId / SubQuestionId / SortOrder / Stage / Status / Decision
- `ReviewAssignmentInfo`：Id / SubQuestionId / Stage / Status / Decision / Comment / DecidedAt / CreatedAt；`IsDecided` / `IsCompleted` computed props
- `ReviewHistoryEntry`：Kind / At / ActorName / Label / ContentHtml / UnitDisplayCode
- `ReviewSimilarityEntry`：ComparedQuestionId / ComparedQuestionCode / SimilarityScore / Determination / SummaryText
- `ReviewListItem`：AssignmentId / QuestionId / QuestionCode / SubQuestionId / SubSortOrder / TypeKey / Level / Difficulty / FixedDifficulty / SummaryText / Stage / Decision / Status / UnitStatus / LastEditedAt / FinalReturnCount / CanFinalReviewerEdit / IsHistorical；`DisplayCode` / `IsCompleted` computed
- `ReviewAssignmentListResult`：sealed record(Items, CurrentStage)
- `ReviewHistoryItem`：QuestionId / QuestionCode / SubQuestionId / SubSortOrder / TypeKey / Level / Difficulty / FixedDifficulty / SummaryText / FinalStatus / FinalDecidedAt；`DisplayCode` computed
- `SaveReviewCommentRequest` / `SubmitReviewDecisionRequest` / `FinalReviewerEditRequest`（3 個請求 DTO）

**ReviewService 私有 DTO（8 個 sealed class，行 1584-1701）**
FinalEditAssignMeta / AssignmentListRow / HistoryListRow / AssignmentDto / HistoryUnionRow / SimilarityRow / SiblingUnitRow / ReturnCountDto / AssignmentMetaDto（實際 9 個，含 AssignmentMetaForSave）

---

### Reviews.razor 頁面狀態管理（@code 行 812-1347）

**CascadingParameter**
- `CurrentProject`（ProjectSwitcherItem）— 切換 trigger
- `AuthState`（Task<AuthenticationState>）

**關鍵狀態欄位**
- `currentTab`：review / history
- `loadedProjectId`：guard 防 OnParametersSetAsync 重複觸發（Decision/Save 後呼叫 ReloadAsync = 直接 LoadProjectAsync）
- `currentUserId`：第一次載入時從 AuthState 取出，後續不再重算
- `currentPhase`（ProjectPhaseInfo）/ `phases`（List<ProjectPhaseInfo>）— LoadPhasesAsync 填入
- `IsCompositionPhase`：`currentPhase?.PhaseCode == 2`（命題階段顯示等待畫面）
- `CurrentReviewStage`：PhaseCode→3/5/7 各對應 Mutual/Expert/Final，其他 null
- `assignments`（List<ReviewListItem>）/ `historyItems`（List<ReviewHistoryItem>）— LoadProjectAsync 內 Task.WhenAll 並行載入
- `cachedAssignments` / `cachedHistory`：篩選快取（InvalidateFilter 後下次存取重算）

**Plan_021 FinalEdit Modal 狀態欄位**
- `showFinalEditModal` / `finalEditFormData`（QuestionFormData）/ `finalEditAssignmentId`
- `finalEditSidebarCollapsed` / `isFinalEditSaving` / `finalEditPendingAction`（"approve"/"reject"）
- `finalEditInvalidFields`（HashSet<string>）

**背景 PhaseCoordinator 模式**
`RunPhaseCoordinatorBackgroundAsync`：fire-and-forget，完成後 InvokeAsync 切回 UI thread reload 兩個列表。失敗 LogWarning 吞掉，不影響使用者。

**FilteredAssignments 篩選邏輯（行 1259-1273）**
預設 `queryStatus="all"` 時：`CurrentReviewStage is null || !a.IsHistorical`（排除歷史紀錄）。
歷史紀錄需切「歷史紀錄」選項才顯示（Plan_013 §3.4）。

---

### 審題作業區列表操作按鈕邏輯（行 483-505）

```
historical      → 「檢視」灰
!IsCompleted    → 「審題」sage 綠
isEditUnlocked  → 「編輯題目」terracotta 橘
else            → 「檢視」灰
```

`isEditUnlocked` 條件（行 490-493）：
`item.CanFinalReviewerEdit && item.Stage == ReviewStage.Final && item.IsCompleted && item.UnitStatus == QuestionStatus.FinalReviewing`
（UnitStatus=7 守門，避免總召修題中 UnitStatus=8 時再度編輯）

---

### 列表狀態 pill 對照（行 452-473）

| 條件 | 文字 | CSS |
|------|------|-----|
| IsHistorical | 歷史紀錄 | gray |
| Completed + Mutual | 已審閱 | morandi |
| Completed + Approve | 已審：採用 | sage |
| Completed + Revise + PhaseCode=8 | 已退回 | terracotta |
| Completed + Reject + PhaseCode=8 | 已退回 | terracotta |
| Completed + Revise | 已審：改後採用 | terracotta |
| Completed + Reject | 已審：不採用 | red-700 |
| Pending | 待審 | amber |

---

### Shared 元件清單

`Components/Shared/ReviewForms/`（6 個）：
- ReviewModal / ReviewActionPanel / ReviewDecisionBar / ReviewQuestionDisplay / ReviewHistoryTimeline / ReviewSimilarityBanner

`Components/Shared/RevisionForms/`（3 個）：
- RevisionReferencePanel / RevisionSlideOver / RevisionReplyEditor

---

### SummaryHtml CASE 語句（全站統一）

```sql
CASE
    WHEN q.QuestionTypeId IN (3, 5, 7) THEN q.ArticleContent
    WHEN q.QuestionTypeId = 4          THEN COALESCE(NULLIF(q.Stem, ''), q.ArticleContent)
    ELSE q.Stem
END
```

ReviewService 4 處對齊：GetMyAssignmentsAsync / GetHistoryAsync / LoadHistoryAsync（彙整）/ GetModalDataAsync（#5 similar）

---

### CWT / LCT 雙模式對 Reviews 影響

Reviews.razor 和 ReviewService.cs **無任何 ProjectType / LCT / CWT 分支**，三審制度/迴避/ReturnCount 完全共用。
唯一差異：等級 label 篩選下拉（行 244-253）：`CurrentProject?.ProjectType == ProjectType.Lct ? ListenLevelLabels : GeneralLevelLabels`

---

### 效能改善歷史

- **第三波 #14（2026-05-15）**：GetModalDataAsync 9 round-trip → QueryMultipleAsync 7 result-sets = 3-5 round-trip
- **第二波 #9 bug 修補（2026-05-15）**：LoadHistoryAsync UnitInfo CTE `sq.QuestionId` → `sq.ParentQuestionId`（2 處）
- **UpsertFinalSubQuestionsAsync**：SELECT existingIds + foreach UPDATE（N+1），子題數通常 2-5 個

**Why:** 2026-05-29 對照三個完整檔案全面校正，確認行數/方法簽章/SQL 結構/UI 邏輯均與 code 一致。
**How to apply:** 未來任何改動三審決策流程、退回計次、Status 流轉前，先以本記憶確認當前實作細節，再評估改動範圍。
