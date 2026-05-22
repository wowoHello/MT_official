---
name: CWT 與 LCT 命題類型區分
description: MT_Projects.ProjectType 欄位區分 CWT/LCT，對登入流程無直接影響，但影響登入後的梯次資料視角
type: project
---

## 事實

`MT_Projects` 資料表新增了兩個欄位：
- `ProjectType tinyint NOT NULL DEFAULT 0`：0 = CWT（全民中文檢定），1 = LCT
- `ExamLevel tinyint NULL`：CWT 統一命題等級（0=初等、1=中等、2=中高等、3=高等、4=優等）；**LCT 模式時此欄位為 NULL**

## 對登入流程的影響

**無直接影響。** 登入驗證邏輯（`AuthService.ValidateLoginAsync`）只查 `MT_Users`，不讀 `MT_Projects`。Cookie 寫入的 Claims 中也沒有 ProjectType 欄位。

## 對登入後導向的影響

也無影響。`/auth/login` endpoint 只判斷 `IsFirstLogin` 決定導向 `/first-login-password` 或 `/`（首頁），不依 ProjectType 分流。

## 影響範圍在哪裡

ProjectType 影響的是**登入後的命題任務與總覽頁面**：
- CWT 梯次有統一的 ExamLevel（等級）
- LCT 梯次的 ExamLevel 為 NULL，題目的等級由各題自帶
- 配額系統（MT_MemberQuotas）也受 Granularity 欄位影響，這是 2026-05-21 schema 版本新增的欄位

**Why:** 命題系統同時服務 CWT（全民中文檢定）和 LCT（另一種命題類型）兩條業務線，梯次建立時指定類型，後續命題流程各有差異，但登入認證層是共用的，無需區分。

**How to apply:** 若未來有需求讓登入頁顯示「您目前參與的是 CWT/LCT 梯次」類型的訊息，需從 MT_Projects 查詢，而非從 MT_Users 或 Cookie Claims 取得。登入頁本身**不需要任何改動**。
