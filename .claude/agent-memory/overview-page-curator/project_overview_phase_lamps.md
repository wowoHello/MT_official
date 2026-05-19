---
name: 七階段燈號判定條件與顏色規則（PhaseProgressStepper）
description: Overview 列表 7 顆燈號的權威推導邏輯（命題→互審→互修→專審→專修→總審→總修），與 PhaseProgressStepper.razor 對齊（含 2026-05-19 新增的 reviewDone tone）
type: project
---

七階段燈號完全由 `Components/Shared/PhaseProgressStepper.razor::ResolveStage(Status, CurrentPhaseCode, HasRepliedThisStage, AllReviewersResponded)` 推導，Overview 端**只負責傳四個參數**，不在 Overview.razor 內重畫樣式。

**階段名稱（固定 7 個）**：命題 / 互審 / 互修 / 專審 / 專修 / 總審 / 總修
（注意：Status 流轉是 0~12，但燈號只有 7 顆；Adopted/Archived → stage=8 全綠；Rejected/ClosedNotAdopted → stage=7 卡紅）

**tone 七種權威值**（2026-05-19 新增 `reviewDone`）：
| tone | 觸發條件 | 視覺 |
|---|---|---|
| `green` | Adopted(11) / Archived(12) | 全 7 顆 sage 綠 ✓ + 細陰影 |
| `rejected` | Rejected(9) / ClosedNotAdopted(10) | 卡關位置莫蘭迪紅 ✗，之前綠，之後灰 |
| `draftDone` | Status=Completed(1) | 第 1 顆 sage 綠 ✓，後續全灰（等命題教師送審） |
| `draftFailed` | Status=Draft(0) + CurrentPhaseCode ≥ 3 | 第 1 顆莫蘭迪紅 ✗（草稿落隊永久不參與後續），**僅 Overview 觸發**（其他頁面不傳 CurrentPhaseCode） |
| `preDone` | Status ∈ {4,6,8} 但 CurrentPhaseCode < Status | 倒數第 1 顆莫蘭迪藍 ✓（該審完成等待下階段），更早 sage 綠，當前與之後灰 |
| `revisionSent` | Status = CurrentPhaseCode ∈ {4,6,8} + HasRepliedThisStage=true | 修題球本身莫蘭迪藍 ✓（我修完了等下階段審），之前綠，之後灰 |
| **`reviewDone`** | **AllReviewersResponded=true + Status ∈ {2,3,5,7} + CurrentPhaseCode ∈ {3,5,7} 對應** | **當前審題球（2/4/6）莫蘭迪藍 ✓（全體已給意見，等階段時程推進），之前綠，之後灰，僅 Overview 觸發** |
| `blue`（進行中） | Status ∈ {0,2,3,5,7}（命題中 / 待審 / 各審題鎖定） | 當前 stage 莫蘭迪藍實心 ✎ |
| `red`（修題中） | Status ∈ {4,6,8} 且未送出修題 | 當前 stage 赤陶實心 ✏（terracotta） |

**Status → Stage 映射**：
- Draft(0) → stage=1 / Completed(1) → stage=1 draftDone
- Submitted(2) / PeerReviewing(3) → stage=2 blue（互審）或 `reviewDone`（A.6）
- PeerEditing(4) → stage=3 red 或 preDone/revisionSent（互修）
- ExpertReviewing(5) → stage=4 blue（專審）或 `reviewDone`（A.6）
- ExpertEditing(6) → stage=5 red 或 preDone/revisionSent（專修）
- FinalReviewing(7) → stage=6 blue（總審）或 `reviewDone`（A.6）
- FinalEditing(8) → stage=7 red 或 preDone/revisionSent（總修）
- Adopted(11)/Archived(12) → stage=8 green（落在 7 顆球外 → 全綠）
- Rejected(9)/ClosedNotAdopted(10) → stage=7 rejected

**ResolveStage 規則優先順序（命中即回）**：
1. **D（draftFailed）**：Draft + PhaseCode ≥ 3 → (1, "draftFailed")
2. **A（preDone）**：Status ∈ {4,6,8} 且 PhaseCode < Status → 對應 (3/5/7, "preDone")
3. **A.5（revisionSent）**：Status = PhaseCode ∈ {4,6,8} + HasRepliedThisStage=true → (3/5/7, "revisionSent")
4. **A.6（reviewDone，2026-05-19 新增）**：AllReviewersResponded=true 且
   - (Submitted/PeerReviewing) + PhaseCode=3 → (2, "reviewDone")
   - ExpertReviewing + PhaseCode=5 → (4, "reviewDone")
   - FinalReviewing + PhaseCode=7 → (6, "reviewDone")
5. **B/C**：原 switch 對應 (blue/red/green/rejected/draftDone)

**Why**：
- `preDone`、`revisionSent`、`draftFailed`、`reviewDone` 四種 tone **只有 Overview 會觸發**（因為只有 Overview 傳 CurrentPhaseCode + HasRepliedThisStage + AllReviewersResponded）；其他頁面（Reviews/CwtList）不傳這幾個參數，預設行為與舊版一致，零侵入。
- `awaitNext`（莫蘭迪藍 ✓）刻意與 `green`（sage 綠 ✓）視覺區隔——同為打勾但顏色不同，前者意味「等下一階段」、後者意味「穩定完成」。
- `reviewDone` 與 `revisionSent` 視覺效果一致（都是當前球 awaitNext / 之前 green / 之後 gray），分開命名僅為語意清晰、未來除錯易追蹤；前者表「審題完成等推進」、後者表「修題完成等推進」。
- `rejectedRed`（莫蘭迪紅 ✗）刻意與 `red`（赤陶 ✏）視覺區隔——前者「最終駁回」、後者「修題進行中」。

**How to apply**：
- Overview 端要調整燈號樣式 → 改 `PhaseProgressStepper.razor`，不要在 Overview.razor 自畫。
- 新增題目 Status 時，必須同步更新 `ResolveStage` switch 與此記憶。
- 「當前狀態 Badge」（Overview.razor::ResolveDisplayStatus 五規則）與「7 階段燈號」是**兩套獨立邏輯**，雖然規則來源相同（4 維度），但呈現位置與表達方式不同，不要混淆。
- `AllReviewersResponded` 在 razor 端傳入時是已查好的 bool（在 foreach 頂端 `result.AllReviewersResponded.GetValueOrDefault((item.Id, item.SubQuestionId))` 取出），不要在元件內部重新查 dict。
