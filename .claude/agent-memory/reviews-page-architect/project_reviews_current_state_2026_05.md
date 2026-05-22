---
name: 審題頁面現況快照（2026-05）
description: Reviews.razor / ReviewService / ReviewModels 於 2026-05-19 的實作現況摘要，含狀態流轉、三審制度、迴避邏輯、效能優化歷史
type: project
---

## 現況摘要（最後更新：2026-05-21）

### 三審制度現況實作（正確落地）
- **互審（ReviewStage.Mutual=1）**：PhaseCode=3；分配排除命題者自身；ReviewDecisionBar 只顯示「儲存意見」/「修改意見」，無採用/退回鈕
- **專審（ReviewStage.Expert=2）**：PhaseCode=5；有 Approve / Revise，無 Reject；迴避邏輯在 QuestionService.AssignExpertReviewers
- **總審（ReviewStage.Final=3）**：PhaseCode=7；完整三選項（採用/改後採用/不採用）；退回上限由 MT_ReviewReturnCounts 追蹤

### QuestionStatus 完整編碼（v3.0 重編後，SentBack 已廢用）
| Status | 常數名 | 說明 |
|--------|-------|------|
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
| 11 | ClosedNotAdopted | 結案未採用（原12，前移） |
| 12 | Archived | 結案入庫（原13，前移） |

- `HistoryTabStatuses = [9, 10, 11, 12]` — 審核結果與歷史 Tab 的篩選範圍

### IsHistorical 判斷方式（從 Service 端計算，UI 端直接讀取）
PhaseCode → Stage 對照（在 GetMyAssignmentsAsync 中設值）：
- PhaseCode=3（互審）/ 4（互修）→ Mutual 為 active；其餘 Historical
- PhaseCode=5（專審）/ 6（專修）→ Expert 為 active
- PhaseCode=7（總審）/ 8（總修）→ Final 為 active（PhaseCode=8 時 Final 也是 active，舊版 bug 已修）
- 其他 PhaseCode → 全部保守顯示（false）

### MT_ReviewReturnCounts 退回次數邏輯
- ReturnCount >= 2：Modal 頂部顯示「已退回 2 次」紅色預警徽章
- BumpReturnCount 後 ReturnCount+1 >= 3 → CanEditByReviewer=1 → 解鎖「編輯題目」橘鈕
- 母題與子題各自獨立計次（SubQuestionId NULL-safe 比對）

### 三檔行數量級（2026-05-20 量測）
- `ReviewService.cs`：1689 行（比上次量測多 16 行）
- `Reviews.razor`：1333 行（比上次量測多 23 行）
- `ReviewModels.cs`：327 行（比上次量測多 16 行）
- `Components/Shared/ReviewForms/`：6 個元件（ReviewModal / ReviewActionPanel / ReviewDecisionBar / ReviewQuestionDisplay / ReviewHistoryTimeline / ReviewSimilarityBanner）

### ReviewService 關鍵方法（現況）
- `GetMyAssignmentsAsync`：ROW_NUMBER CTE 去重同單元多筆，排除 Status IN (9,10,11,12)
- `GetHistoryAsync`：篩 Status IN HistoryTabStatuses，歷史 Tab 專用
- `GetModalDataAsync`：**第三波 #14 改寫後**，原 9 round-trip → QueryMultipleAsync 7 result-sets，總計 3-5 round-trip（GetByIdAsync 自帶 2-4 + 主連線 1）
- `GetHistoryByQuestionIdAsync`：供 OverviewService 使用，aggregateAllUnits=false 精準模式
- `LoadHistoryAsync`（private）：供 OverviewService 彙整模式（aggregateAllUnits=true，含 UnitInfo CTE）
- `SubmitDecisionAsync`：寫入 Assignment + 觸發 BumpReturnCount；題目 Status 流轉在本方法內（非 PhaseTransitionCoordinator）
- `FinalReviewerEditAndDecideAsync`（Plan_021）：單 tx 內 UPDATE 題目+Status(9/10)+Assignment+2筆 AuditLog
- `SaveCommentDraftAsync`：互審只寫 Comment + ReviewStatus=Completed；非互審只寫 Comment（Decision IS NULL guard）

### 重要語意修正（2026-05-19 批次改動）

#### `DecidedAt IS NOT NULL` 取代 `Comment IS NOT NULL` 作為「已決策」判定
- **舊語意**：`Comment IS NOT NULL` 判斷審題者是否已給意見，但 Comment 是可重複編輯的草稿欄位，不代表已送出決策
- **新語意**：`DecidedAt IS NOT NULL` 才是真正送出決策的時間戳，草稿狀態 DecidedAt = NULL
- 影響位置：GetMyAssignmentsAsync（行 401）/ LoadHistoryAsync（行 1369）/ GetHistoryAsync 內多處
- `#4 history inline SQL` 的 WHERE 條件亦已統一為 `AND ra.DecidedAt IS NOT NULL`（含精準模式與彙整模式）

#### SummaryHtml CASE 語句加入 TypeId=4（長文題目）fallback
全站 SummaryHtml 計算邏輯（GetMyAssignmentsAsync / GetHistoryAsync / LoadHistoryAsync / GetModalDataAsync similar）統一為：
```sql
CASE
    WHEN q.QuestionTypeId IN (3, 5, 7) THEN q.ArticleContent          -- 題組母題
    WHEN q.QuestionTypeId = 4          THEN COALESCE(NULLIF(q.Stem, ''), q.ArticleContent)  -- 長文題：Stem 優先 fallback ArticleContent
    ELSE q.Stem
END
```
共 4 處已對齊（行 158, 269, 427, 1499）。

#### 互審匿名標籤統一
- `RevisionReferencePanel.razor`（修題端）使用 `AnnotationActorLabel.Anonymize((ReviewStage)r.Stage)`
- 互審階段匿名顯示「命題教師」而非「審題委員」
- 審題端（ReviewActionPanel）的劃記列表不顯示身份標籤（自己看自己，位置由左側高亮表達）

#### 總召代修鎖定題型
- `Reviews.razor`（行 655）`FinalReviewerEditAndDecideAsync` 開啟的編輯面板傳入 `LockQuestionType="true"`
- `RevisionSlideOver.razor`（行 163）同樣傳入 `LockQuestionType="true"`
- `QuestionAttributesSidebar` 的題型 `<select>` 在 `LockQuestionType=true` 時 `disabled`，tooltip 顯示「修題階段不可變更題型」

### 效能優化歷史（影響 ReviewService 的部分）

#### 第二波 #6 — `vw_QuestionRoundStartedAt` View（2026-05-15）
原本「上次總審退回時間 MAX」的 correlated subquery 在全站 13 處複製；ReviewService 本身為 1 處單元級保留（行 2062 附近，含 SubQuestionId NULL-safe 比對，view 涵蓋不到）。
**顯著消費點在 QuestionService / DashboardService / OverviewService / HomeService**，ReviewService 的消費點保留使用原始 scalar subquery 因有子題 SubQuestionId 比對的特殊需求。

#### 第二波 #9 — LoadHistoryAsync bug 修補（2026-05-15）
施工 QuestionService.ListAsync CTE 重寫時，實測觸發 `SqlException: 無效的資料行名稱 'QuestionId'`。
**根本原因**：`ReviewService.cs:1209-1210` LoadHistoryAsync 的 UnitInfo CTE 寫 `sq.QuestionId`，但 MT_SubQuestions 正確欄位名是 `ParentQuestionId`。
**觸發路徑**：Overview 頁詳情視角（aggregateAllUnits=true），命題任務頁不會觸發。
**修補**：2 處 `sq.QuestionId` → `sq.ParentQuestionId`。
此 bug 與 #9 改動無關，是既有 bug 因實測暴露。

#### 第三波 #14 — GetModalDataAsync QueryMultipleAsync 重寫（2026-05-15）
- 改寫前：GetByIdAsync（2-4）+ 7 個獨立 SELECT = 最多 11 round-trip
- 改寫後：GetByIdAsync（2-4）+ 單一 QueryMultipleAsync（7 result-sets）= 3-5 round-trip
- 7 個 result-set：#1 lastEdit / #2 creator / #3 myAssignment / #4 history（精準模式 inline SQL）/ #5 similar / #6 returnCount / #7 siblings（Stage 內嵌 subquery，無 Assignment 時自然 0 列）
- siblings Stage 自帶 subquery：不需先讀 myAssignment 知道 Stage，內嵌 `WHERE ra.ReviewStage = (SELECT TOP 1 ReviewStage FROM ... WHERE ReviewerId = @ReviewerId)`
- LoadHistoryAsync / LoadSimilaritiesAsync 兩個 helper **保留**，供 OverviewService 彙整模式使用

#### 未處理（待第四波評估）
- `UpsertFinalSubQuestionsAsync`：N+1 模式（SELECT existingIds + foreach UPDATE），觸發頻率低（僅總召代修子題路徑）+ 子題數量通常 2-5 個，ROI 偏低暫不動
- 罐頭訊息：目前 8 則寫死在 ReviewActionPanel.razor，第四波計畫新增 `MT_CannedComments` 資料表讓管理員可編輯

### SystemAutoAuditReasons 排除清單（12 個）
系統批次寫入的 Reason 字串（UserId=NULL），從 ReviewHistoryTimeline 與「最後編輯時間」計算中排除，避免污染歷程顯示。
例：CompositionPhaseEnded / ExpertReviewAssigned / FinalReviewAssigned 等。

### ReviewDecisionBar 五種配置
1. **互審**：[儲存意見] / [修改意見]（無決策按鈕）
2. **專審**：[儲存草稿] + [改後採用] + [採用]（無 Reject）
3. **總審**：[儲存草稿] + [改後採用] + [不採用] + [採用]
4. **總召第 3 次（CanFinalReviewerEdit）**：[儲存草稿] + [編輯題目] + [不採用] + [採用]
5. **IsHistorical / 已決策**：唯讀提示列 + 關閉按鈕

### 聽力題組等級/難度顯示（commit 1b815f6 新增）
- 審題作業區列表：ListenGroup 子題用 FixedDifficulty 取 ListenLevelLabels（難度三/難度四）；母題顯示「—」
- 歷史 Tab：一律以 QuestionId 一列呈現，ListenGroup 無等級無難度顯示 em dash
- 規則：TypeKey != ListenGroup 才套用 Difficulty pill

### Reviews.razor OnInitializedAsync 優化（commit 1b815f6）
- 載入階段改全並行：phasesTask + assignmentsTask + historyTask + memberTask 同時 await Task.WhenAll
- PhaseCoordinator 背景執行（可能耗時 5~10 秒），完成後 InvokeAsync 刷新列表

### 兩個 Tab 現況
- **審題作業區**：inline 摘要 bar（本區題目/待處理/已審題/歷史紀錄）；PhaseCode=2 隱藏改顯示等待提示
- **審核結果與歷史**：3 張統計卡片（全部/採用/不採用）+ 以 QuestionId 一列顯示（不拆子題）

### 三審迴避邏輯落地位置
- **互審迴避**：QuestionService.EnsureCompositionPhaseClosedAsync（分配時排除命題者）
- **專審迴避**：QuestionService.AssignExpertReviewers（排除自己命的題）
- **總審迴避**：QuestionService.AssignFinalReviewers（排除專審已審過的題，給其他總召）
- ReviewService 本身不做分配，只做列表查詢與決策寫入

### SiblingUnits 兄弟單元機制
- 題組類（TypeId IN 3,5,7）開啟 Modal 時，GetModalDataAsync 撈同題同 Stage 同 Reviewer 的所有子題
- 用於母題列顯示「子題索引卡」狀態（完成/待審）
- 非題組或無 Assignment 時，SiblingUnits 為空 List

### Stage B-4 子題獨立決策機制（現況已落地）
- `GetMyAssignmentsAsync` SQL：`AND (SubQuestionId IS NULL AND q.Status NOT IN (9,10,11,12) OR SubQuestionId IS NOT NULL AND sq.Status NOT IN (9,10,11,12))` — 母題與子題用各自 Status 篩除最終態
- `GetHistoryAsync` SQL：UNION ALL 兩段（母題段 + 子題段），子題段不檢查 q.Status，母題採用後已決策子題仍獨立呈現
- `SubmitDecisionAsync`：母題單元 → 更新 MT_Questions.Status；子題單元 → 更新 MT_SubQuestions.Status（Adopted/Rejected/Archived 同時寫 DecidedAt）
- `ReviewListItem.UnitStatus`：Service 端計算，母題取 q.Status、子題取 sq.Status，UI 守門用（例如「編輯題目」鈕僅在 UnitStatus=7 時解鎖）
- `BumpReturnCountAsync`：按單元（QuestionId + SubQuestionId）各自計次，MERGE + HOLDLOCK 防並發

### 第 4 次送審強制最終決策邏輯（SubmitDecisionAsync 步驟 6-1）
- PhaseCode=8 + Final Stage + Reject + existingReturnCount >= 3 → newUnitStatus 直接設 Rejected(10)，不再 Bump
- 避免 ReturnCount 變 4 導致歷程顯示「總審第四次退回」語意錯誤

### MapDecisionToQuestionStatus 狀態流轉規則（私有方法，集中維護）
| PhaseCode | Decision | 新 Status |
|-----------|----------|-----------|
| 7（首輪）| Approve | 9（Adopted）|
| 7（首輪）| Revise/Reject | null（Buffer，等 PhaseCode=8 批次分流）|
| 8（次輪+）| Approve | 9（Adopted）|
| 8（次輪+）| Revise | 8（FinalEditing，退回老師修）|
| 8（次輪+）| Reject | null（Caller 依 ReturnCount 判定 8 或 10）|

### FilteredAssignments 預設篩選行為（Plan_013 §3.4）
- 預設 `queryStatus="all"` 只顯示非歷史紀錄（`!a.IsHistorical`），避免不同階段分配堆疊誤解
- 使用者可切換「歷史紀錄」選項單獨查看 IsHistorical=true 的項目

### PhaseCoordinator 背景執行模式（OnInitializedAsync 優化）
- `RunPhaseCoordinatorBackgroundAsync`：fire-and-forget，60 秒 cache hit 即刻返回
- 主流程不等 Coordinator（5~10 秒互審/專審/總審分配），先用 DB 現況渲染
- Coordinator 完成後 `InvokeAsync` 切回 UI thread reload 兩個列表
- 已切換專案的防護：`if (CurrentProject?.Id != projectId) return`

### CWT / LCT 雙模式對 Reviews 頁面的影響（2026-05-21 新增）

#### DB schema 確認
- `MT_Projects.ProjectType TINYINT NOT NULL DEFAULT 0`：0=CWT（全民中檢，7 種題型）、1=LCT（聽力中心，按難度一~五）
- `MT_Projects.ExamLevel TINYINT NULL`：CWT 統一命題等級 0~4；**LCT 模式 NULL**
- `MT_ReviewAssignments` / `MT_ReviewReturnCounts` **無任何 ProjectType 欄位**，三審制度兩種類型共用同一資料表
- `Granularity TINYINT`：MT_ProjectTargets / MT_MemberQuotas 有此欄位，0=母題/單題，1=子題；LCT 模式一律為 0

#### Reviews.razor / ReviewService.cs 現況對 CWT/LCT 的支援
- Reviews.razor：**完全未讀取 ProjectType**，三審制度邏輯不區分 CWT/LCT
- ReviewService.cs（grep 驗證）：**無任何 ProjectType / LCT / CWT 分支**
- 迴避邏輯、ReturnCount、PhaseCode 判斷：**全部 CWT/LCT 共用，無差異實作**

#### 待釐清的業務問題（尚未與使用者確認）
目前無資訊說明 LCT 的三審制度是否與 CWT 相同，以下為需待使用者決策的問題：
1. LCT 是否也有「互審 → 專審 → 總審」三個階段？還是只有一審或兩審？
2. LCT 迴避規則是否與 CWT 相同（自己命的不自審、總召不拿自己專審過的）？
3. LCT 的題目等級欄位顯示方式（Level 標籤）：CWT 顯示初/中/中高/高/優，LCT 無等級 → 審題列表等級 badge 如何顯示？
4. LCT 子題是否有 FixedDifficulty 機制（聽力題組特有）？

#### 目前安全假設（僅基於 DB schema 推導，未確認）
- DB schema 層面：三審共用同一張資料表，暗示設計上 LCT 也走三審流程
- ExamLevel=NULL（LCT）時，ReviewListItem.Level=null，前端 badge 已有 `@if (item.Level.HasValue)` guard，不會崩潰
- LCT 無 Granularity=1 子題 → SiblingUnits 為空 List → 母題列不顯示子題索引卡，邏輯正確

**Why:** 2026-05-21 調查 CWT/LCT 雙模式對審題頁面的影響。DB schema 已有 ProjectType 欄位但 ReviewService 完全未讀取，需待使用者確認 LCT 三審規則後評估是否需要新增分支邏輯。
**How to apply:** 後續任何涉及 Reviews 頁面 + LCT 支援的任務，先確認上方「待釐清業務問題」是否已有答案，再決定是否在 ReviewService 加 ProjectType 讀取與分支邏輯。若 LCT 三審規則與 CWT 完全相同，則 Reviews 頁面**不需要任何改動**。
