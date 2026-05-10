---
name: 忘記密碼流程細節
description: 忘記密碼 Modal、Token 機制、防呆規則、重發冷卻倒數
type: project
---

## 完整流程

1. 使用者點「忘記密碼？」→ 開啟 Modal（同頁面 inline，不跳轉）
2. 輸入信箱 → 前端先用 `System.Net.Mail.MailAddress` 驗證格式（不需 Regex）
3. 呼叫 `PasswordResetService.RequestPasswordResetAsync(email, Navigation.BaseUri)`
4. Service 查 `MT_Users` (Email)；若找不到或停用帳號，**仍回傳成功**（防止信箱枚舉攻擊）
5. 產生 `Guid.NewGuid().ToString("N")` 作為 Token，寫入 `MT_PasswordResetTokens`
6. 先作廢該使用者所有未使用 Token，再插入新 Token（交易內完成）
7. 呼叫 `EmailService.SendResetPWEmailAsync` 寄信
8. 寄信失敗 → 作廢剛建立的 Token，拋出 `InvalidOperationException`

## Token 規格

- 有效期：10 分鐘（`ResetTokenLifetimeMinutes = 10`）
- 連結格式：`{baseUri}resetpassword?token={encoded_token}`
- 驗證頁面：`ResetPassword.razor`（`/resetpassword`）
- Token 使用後 `IsUsed = 1`（一次性）

## 新舊密碼防呆

- `ResetPasswordAsync` 與 `ChangePasswordAsync` 皆有舊密碼比對
- 使用 `byte[].SequenceEqual` 比對雜湊
- 回傳 `SamePassword = true` 時，UI 需顯示「新密碼不可與舊密碼相同」紅字
- 密碼雜湊算法：`SHA256`（UTF-16LE 編碼，與 MSSQL `HASHBYTES('SHA2_256', N'...')` 一致）

## UI 狀態機

- `resetLinkSent = false`：顯示信箱輸入畫面
- `resetLinkSent = true`：顯示「請查收信箱」確認畫面
- 重發冷卻：60 秒倒數，`CancellationTokenSource` 管理，Modal 關閉時立即取消倒數
- `IsResetResendDisabled = isSendingReset || resetResendCountdown > 0`

## SMTP 設定

- `appsettings.json` 的 `Smtp:Server`（預設 smtp.gmail.com）、`Smtp:Port`（預設 587）、`Smtp:User`、`Smtp:Password`
- `Smtp:User` 或 `Smtp:Password` 未設定 → 拋出 `InvalidOperationException`，Login.razor 會以 SweetAlert2 顯示「系統設定未完成」錯誤
