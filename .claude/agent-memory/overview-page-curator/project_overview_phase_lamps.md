---
name: 七階段燈號判定條件與顏色規則（PhaseProgressStepper）
description: Overview 列表 7 顆燈號的權威推導邏輯（命題→互審→互修→專審→專修→總審→總修），與 PhaseProgressStepper.razor 對齊
type: project
---

七階段燈號完全由 `Components/Shared/PhaseProgressStepper.razor::ResolveStage(Status, CurrentPhaseCode, HasRepliedThisStage)` 推導，Overview 端**只負責傳三個參數**，不在 Overview.razor 內重畫樣式。

**階段名稱（固定 7 個）**：命題 / 互審 / 互修 / 專審 / 專修 / 總審 / 總修
（注意：Status 流轉是 0~12，但燈號只有 7 顆；Adopted/Archived → stage=8 全綠；Rejected/ClosedNotAdopted → stage=7 卡紅）

**tone 六種權威值**：
| tone | 觸發條件 | 視覺 |
|---|---|---|
| `green` | Adopted(11) / Archived(12) | 全 7 顆 sage 綠 ✓ + 細陰影 |
| `rejected` | Rejected(9) / ClosedNotAdopted(10) | 卡關位置莫蘭迪紅 ✗，之前綠，之後灰 |
| `draftDone` | Status=Completed(1) | 第 1 顆 sage 綠 ✓，後續全灰（等命題教師送審） |
| `draftFailed` | Status=Draft(0) + CurrentPhaseCode ≥ 3 | 第 1 顆莫蘭迪紅 ✗（草稿落隊永久不參與後續），**僅 Overview 觸發**（其他頁面不傳 CurrentPhaseCode） |
| `preDone` | Status ∈ {4,6,8} 但 CurrentPhaseCode < Status | 倒數第 1 顆莫蘭迪藍 ✓（該審完成等待下階段），更早 sage 綠，當前與之後灰 |
| `revisionSent` | Status = CurrentPhaseCode ∈ {4,6,8} + HasRepliedThisStage=true | 修題球本身莫蘭迪藍 ✓（我修完了等下階段審），之前綠，之後灰 |
| `blue`（進行中） | Status ∈ {0,2,3,5,7}（命題中 / 待審 / 各審題鎖定） | 當前 stage 莫蘭迪藍實心 ✎ |
| `red`（修題中） | Status ∈ {4,6,8} 且未送出修題 | 當前 stage 赤陶實心 ✏（terracotta） |

**Status → Stage 映射**：
- Draft(0) → stage=1 / Completed(1) → stage=1 draftDone
- Submitted(2) / PeerReviewing(3) → stage=2 blue（互審）
- PeerEditing(4) → stage=3 red 或 preDone/revisionSent（互修）
- ExpertReviewing(5) → stage=4 blue（專審）
- ExpertEditing(6) → stage=5 red 或 preDone/revisionSent（專修）
- FinalReviewing(7) → stage=6 blue（總審）
- FinalEditing(8) → stage=7 red 或 preDone/revisionSent（總修）
- Adopted(11)/Archived(12) → stage=8 green（落在 7 顆球外 → 全綠）
- Rejected(9)/ClosedNotAdopted(10) → stage=7 rejected

**Why**：
- `preDone`、`revisionSent`、`draftFailed` 三種 tone **只有 Overview 會觸發**（因為只有 Overview 傳 CurrentPhaseCode + HasRepliedThisStage）；其他頁面（Reviews/CwtList）不傳這兩個參數，預設行為與舊版一致，零侵入。
- `awaitNext`（莫蘭迪藍 ✓）刻意與 `green`（sage 綠 ✓）視覺區隔——同為打勾但顏色不同，前者意味「等下一階段」、後者意味「穩定完成」。
- `rejectedRed`（莫蘭迪紅 ✗）刻意與 `red`（赤陶 ✏）視覺區隔——前者「最終駁回」、後者「修題進行中」。

**How to apply**：
- Overview 端要調整燈號樣式 → 改 `PhaseProgressStepper.razor`，不要在 Overview.razor 自畫。
- 新增題目 Status 時，必須同步更新 `ResolveStage` switch 與此記憶。
- 「當前狀態 Badge」（Overview.razor::ResolveDisplayStatus 五規則）與「7 階段燈號」是**兩套獨立邏輯**，雖然規則來源相同（4 維度），但呈現位置與表達方式不同，不要混淆。
