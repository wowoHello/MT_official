---
name: 命題總覽修題階段燈號雙態呈現
description: PhaseProgressStepper 對修題階段（4/6/8）的雙態樣式約定，使用者已驗證
type: feedback
---

修題階段（互修/專修/總修）的 7 階段燈號採「雙態呈現」，由 `Components/Shared/PhaseProgressStepper.razor` 統一處理：
- **送出修題後**：藍勾 + 綠色「已送出」badge（HasRepliedThisStage=true）
- **尚未送出**：紅筆圖示 + 橘色「修題中」badge（HasRepliedThisStage=false）

**Why**：使用者已驗證此呈現方式可一眼辨識題目是否已被命題教師送出修題說明，避免管理員把「已送出但梯次未推進」與「老師還沒動」搞混。

**How to apply**：Overview.razor 列表中 `<PhaseProgressStepper Status=... CurrentPhaseCode=... HasRepliedThisStage=... />` 不要在 Overview 端自己重畫修題燈號樣式，所有顏色與圖示邏輯都在 PhaseProgressStepper 元件內；如需調整改 Shared 元件而非 Overview。Overview 端的「當前狀態」Badge 是另一回事（走 ResolveDisplayStatus 五規則），不要混淆兩者。
