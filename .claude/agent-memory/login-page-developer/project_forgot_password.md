---
name: 忘記密碼流程細節
description: 忘記密碼 Modal、Token 機制、fire-and-forget 寄信、防呆規則、重發冷卻倒數
type: project
---

## 完整流程

1. 使用者點「忘記密碼？」→ 開啟 Modal（同頁面 inline，不跳轉）
2. 輸入信箱 → 前端先用 `System.Net.Mail.MailAddress` 驗證格式（不需 Regex）
3. 呼叫 `PasswordResetService.RequestPasswordResetAsync(email, Navigation.BaseUri)`
4. Service 查 `MT_Users` (Email)；若找不到或停用帳號，**仍回傳成功**（防止信箱枚舉攻擊）
5. 在交易內：先作廢該使用者所有未使用 Token，再插入新 Token（Guid.NewGuid().ToString("N")）
6. 交易 commit 後立即回傳成功給前端（不等寄信）
7. `_ = Task.Run(...)` fire-and-forget 背景寄信；失敗只記 `ILogger.LogError`，不拋回前端

**Why fire-and-forget（第三波 #16）：** SMTP 同步等待造成前端卡 1-3 秒。Token 有 10 分鐘 ExpiresAt，收不到信時使用者重申請即可；申請新 token 時舊 token 會自動作廢。舊版在寄信失敗時有 rollback 作廢 token 的邏輯，已移除。

## Token 規格

- 有效期：10 分鐘（`ResetTokenLifetimeMinutes = 10`）
- 格式：32 字元 Hex GUID（`Guid.NewGuid().ToString("N")`）
- 連結格式：`{baseUri}resetpassword?token={encoded_token}`
- 驗證頁面：`ResetPassword.razor`（`/resetpassword`）
- Token 使用後 `IsUsed = 1`（一次性），`MT_PasswordResetTokens` 有 UNIQUE 索引

## 新舊密碼防呆

- `ResetPasswordAsync` 與 `ChangePasswordAsync` 皆有舊密碼比對
- 使用 `AuthService.VerifyPassword(newPassword, currentHash)` 對當前儲存的 PBKDF2 或舊 SHA256 hash 驗證
- 回傳 `SamePassword = true` 時，UI 顯示「新密碼不可與舊密碼相同」紅字（欄位邊框紅色）
- `ClearSamePasswordError()` 綁在 `@bind-Value:after`，使用者開始輸入就清除紅色

## UI 狀態機

- `resetLinkSent = false`：顯示信箱輸入畫面
- `resetLinkSent = true`：顯示「請查收信箱」確認畫面（顯示已寄出的 email 地址）
- 重發冷卻：60 秒倒數，`CancellationTokenSource` 管理，Modal 關閉時 `Cancel()` + `Dispose()` 立即取消
- `IsResetResendDisabled = isSendingReset || resetResendCountdown > 0`

## SMTP 設定（目前狀態 — 2026-05-25 確認）

- **EmailService 已改讀 `appsettings.json` 的 `Smtp` 區段**（第一波 #2 已完成，非暫緩）
- 設定 key：`Smtp:Server`（預設 smtp.gmail.com）、`Smtp:Port`（預設 587）、`Smtp:User`、`Smtp:Password`、`Smtp:FromDisplayName`（預設 "CWT 命題工作平臺"）
- `EnsureSmtpConfigured()` 保護：若 User 或 Password 為空，拋出「系統尚未完成 SMTP 設定，暫時無法寄送通知信。」
- EmailService 建構函式注入 `IConfiguration`，無任何硬編碼帳密

## ResetPassword.razor 的 Token 驗證邏輯

- `OnInitializedAsync`：呼叫 `ValidateTokenAsync(token)` 驗證 token
- token 無效/已使用/過期 → 顯示錯誤訊息，`OnAfterRenderAsync` 再用 SweetAlert2 彈窗後導回 `/login`
- token 有效 → 顯示新密碼輸入表單
- 重設成功後導向 `/login`（`forceLoad: true`）
