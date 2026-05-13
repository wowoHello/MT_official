# Plan_022 / Stage B-4：題組子題獨立流轉

> **編號**：Plan_022
> **別名**：Stage B-4（接續 B-1 列拆分 / B-2 題組綁定分配 / B-3 ReviewModal 拆列）
> **撰寫日**：2026-05-13
> **狀態**：待確認 → 待實作

---

## 一、Why 為什麼要做

目前狀態（Stage A + B-1~B-3 完成後）：
- 題組類試題（readGroup/shortGroup/listenGroup）的母題 + N 個子題各有獨立 `MT_ReviewAssignments` 列
- ReviewModal 可母題列／子題列各自開啟
- 但 **`SubmitDecisionAsync` 只寫 `MT_Questions.Status`（母題）**
- **`MT_SubQuestions.Status` 欄位（Stage A 預埋）從未被寫入**
- 子題目前完全跟著母題狀態跑

使用者已拍板的設計規則：

| 問題 | 答案 |
|----|----|
| Q1：母題在總修判 Approve，子題如何？ | **子題仍須各自被總審獨立判定** — 母題與子題互不影響 |
| Q2：結案入庫時部分採用怎處理？ | **母題 Approve → 只入採用子題；母題 Reject → 整題組丟棄** |
| Q3：母題 Revise 時子題如何？ | **每個單元（母題 + 每個子題）獨立進行退回再審流程，最後各自做 Approve / Reject 終裁** |

簡單說：**「題組類試題的母題與每個子題是平行的審題單元」**。

---

## 二、目標

1. 子題在審題階段能各自被 Approve / Revise / Reject，且各自獨立寫入 `MT_SubQuestions.Status`
2. 修題期老師看到「哪個單元被退回就解鎖哪個單元」，其他單元仍鎖定
3. 結案入庫時按單元篩選：母題 Adopted → 入該題組（但只入子題 Adopted 的）；母題 Rejected → 整題組丟棄
4. 互審/專審不下決策 / 不採用的舊規則保留，只是「決策對象」從題改成單元

---

## 三、拆三個 Sub-Plan

### B-4-1：Service 層 — 子題狀態獨立寫入 + 退回計數獨立

**目標**：每筆 Assignment 決策時，依 `SubQuestionId` 路由到對應的狀態欄位。

**動到的檔案**：
- `Services/ReviewService.cs` — `SubmitDecisionAsync`、`MapDecisionToQuestionStatus`、退回計數
- `Services/QuestionService.cs` — 確認子題進專審 / 總審時 `SubQuestions.Status` 與 `Question.Status` 同步升級
- `Services/PhaseTransitionCoordinator.cs`（如必要）— 階段切換時批次升級子題 Status

**關鍵變更**：

1. `SubmitDecisionAsync` 內依 `meta.SubQuestionId` 路由：
   ```
   if (SubQuestionId is null) → UPDATE MT_Questions.Status
   else                       → UPDATE MT_SubQuestions.Status
   ```
2. **移除現有的「母題 Reject 級聯子題」邏輯**（line 671-696），因為 Q3 拍板「每個單元獨立」
3. `MT_ReviewReturnCounts` 帶上 `SubQuestionId`，按單元各自累計
4. 第 3 輪 Reject 判定（`PhaseCode=8 + ReturnCount>=2 → Rejected`）改為查該單元的退回次數
5. `EnsureExpertReviewingPhaseAsync` / `EnsureFinalReviewingPhaseAsync` 階段升級時：題組類需同步把 `MT_SubQuestions.Status` 從 Buffer 升到對應審題 Status
6. `ResubmitAfterExpertEditingAsync` / `ResubmitAfterFinalEditingAsync` 老師按完成送審時，只把該單元（含未採用單元）重新建立下一輪 Assignment；已 Adopted 的單元不重建

**驗證**：
- 模擬「母題退回、子題 1 採用、子題 2 退回」場景，確認三個單元 Status 互不影響
- 確認退回次數獨立計算

---

### B-4-2：UI 層 — CwtList 審修作業區單元級拆列 + 修題期單元級解鎖

**目標**：命題者在「審修作業區」看到的是**單元列**而非題列，每個單元獨立判斷可否進入修題。

**動到的檔案**：
- `Components/Pages/CwtList.razor` — 審修作業區列拆分
- `Services/QuestionService.cs` — `GetRevisionListAsync`（或目前對應的方法）改回 N+1 列（同 B-1 的審題列表模式）
- `Components/Shared/RevisionForms/RevisionSlideOver.razor` — 進入修題時鎖定其他單元、只解開該單元
- `Models/QuestionModels.cs` — `RevisionSlideOverData` 新增 `TargetSubQuestionId` 路由欄位

**關鍵變更**：

1. 審修作業區列表：題組類拆成 N+1 列（母題 + 子題）
   - 每列顯示「題號 / 單元編碼（如 Q-115-00032 第 1 子題）/ Status / 操作」
   - 鎖定審查中 → 顯示「檢視」
   - 修題中（單元 Status = 4/6/8）→ 顯示「進入修題」
2. `RevisionSlideOver` 開啟時帶 `SubQuestionId`：
   - 進入該單元的編輯模式
   - 其他單元的 QuillField 強制 readonly + 灰色遮罩
3. 操作 Quill 編輯器時：依 `currentEditingSubId` 控制能改哪些欄位
4. 「完成送審」按鈕只送出當前單元 → `ResubmitSingleUnitAsync(questionId, subId)`

**驗證**：
- 模擬「子題 2 進修題」場景，確認 UI 上只有子題 2 的欄位可編輯
- 確認送審後該單元 Status 進入下一輪 Buffer，其他單元維持

---

### B-4-3：結案層 — 按單元篩選入庫

**目標**：`CloseProjectAsync` 結案時，題組類按單元篩選決定誰入庫。

**動到的檔案**：
- `Services/ProjectService.cs`（或 `QuestionService.CloseProjectAsync`）

**關鍵變更**：

1. 單題類（QuestionType 1/2/4/6）→ 維持「`Question.Status == Adopted(9)` 升 Archived(12)」
2. 題組類（QuestionType 3/5/7）：
   ```
   if (Question.Status != Adopted) {
       Question.Status = Rejected(10)
       所有子題 Status = Rejected(10)
   } else {
       Question.Status = Archived(12)
       子題 Status == Adopted → 升 Archived(12)
       子題 Status != Adopted → 降 Rejected(10)
   }
   ```
3. 寫 AuditLog 記錄結案時每題每子題的最終狀態（給後續題庫匯入用）

**驗證**：
- 模擬「母題採用 + 子題 1 採用 + 子題 2 退回（未進終裁）」場景結案 → 子題 2 應該被結案標 Rejected
- 模擬「母題不採用」場景結案 → 整題組（含子題）全 Rejected

---

## 四、邊界 / 例外情境

| 情境 | 處理方式 |
|----|----|
| 互審階段子題決策？ | 互審本來就不下決策（只給意見），子題亦同 |
| 專審階段子題被 Revise？ | 該子題進 ExpertEditing(6)，老師可在修題期單獨修該子題 |
| 子題進總審後第 3 輪退回？ | 該子題單獨進「總召代修」流程（Plan_021），不影響其他子題 |
| 結案時某子題仍在 FinalEditing 沒完成？ | 比照「非 Adopted」處理，標 Rejected 丟棄 |
| 命題階段子題 Status？ | 跟母題同步流轉（Status=0/1/2），分配時才開始解耦 |
| 子題的解析/題幹欄位修改要不要影響母題狀態？ | 不影響。每個單元的 Quill 內容寫回各自的 `MT_SubQuestions` 欄位 |

---

## 五、影響範圍盤點

**確認需要動**：
- `Services/ReviewService.cs`（核心）
- `Services/QuestionService.cs`（階段升級 + 修題重送審）
- `Components/Pages/CwtList.razor`（審修作業區拆列）
- `Components/Shared/RevisionForms/RevisionSlideOver.razor`（單元級解鎖）
- `Services/ProjectService.cs` 結案邏輯

**確認不需要動**：
- 命題作業區（命題階段子題 Status 仍跟母題，不需要拆列）
- 互審 / 給意見流程（不下決策的場景）
- Overview 七階段燈號（沿用題層級燈號；子題顆粒度延後）
- Dashboard 統計（題數仍以題為單位）
- 公告 / 角色 / 教師 / 專案管理（無關）

**可能需要動**：
- `Services/OverviewService.cs` — 若燈號要呈現「題組類子題分歧狀態」，需新增徽章
- 審題列表的「題型 / 編碼欄」顯示 — 子題列要顯示「Q-115-00032.1」之類的子編碼

---

## 六、實作順序建議

1. **先做 B-4-1**（Service 層）— 不動 UI，可用後台 SQL 模擬決策驗證
2. **再做 B-4-2**（UI 層）— Service 完成後 UI 才有東西可串
3. **最後做 B-4-3**（結案層）— 結案時機晚，最後驗

每個 sub-plan 完成後做一次手動驗證 + 編譯通過再繼續下一個。

---

## 七、待使用者確認

- 此 Plan 整體方向是否 OK？
- 拆三個 sub-plan 是否合理？或要一次做完？
- B-4-1 先動工嗎？
