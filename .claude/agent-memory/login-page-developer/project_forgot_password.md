---
name: 忘記密碼流程細節
description: 忘記密碼 Modal、Token 機制、fire-and-forget 寄信、防呆規則、重發冷卻倒數（2026-05-29 現況快照）
type: project
---

## 完整流程

1. 使用者點「忘記密碼？」→ 開啟 Modal（同頁面 inline，不跳轉）
2. 輸入信箱 → 前端先用 `System.Net.Mail.MailAddress` 驗證格式（不需 Regex，例外即為無效格式）
3. 呼叫 `PasswordResetService.RequestPasswordResetAsync(email, Navigation.BaseUri)`
4. Service 查 `MT_Users` (Email)；若找不到或 `Status != 1` 或 Email 空白，**仍回傳成功**（防枚舉攻擊）
5. 在交易（`BeginTransactionAsync`）內：
   - 先作廢該使用者所有未使用 Token（`UPDATE IsUsed=1 WHERE IsUsed=0`）
   - 再插入新 Token（`Guid.NewGuid().ToString("N")`，32 字元 Hex）
6. 交易 commit 後立即回傳成功給前端（不等寄信）
7. `_ = Task.Run(...)` fire-and-forget 背景寄信；失敗只記 `ILogger.LogError`，不拋回前端

**Why fire-and-forget（第三波 #16）：** SMTP 同步等待造成前端卡 1-3 秒。Token 有 10 分鐘 ExpiresAt，收不到信時使用者重申請即可；申請新 token 時舊 token 自動作廢。

重要：背景 Task 中的 `emailService` 和 `logger` 以 local 變數 capture（非 lambda 直接 capture instance field），避免 service scope dispose 後失效。

## Token 規格

- 有效期：10 分鐘（`ResetTokenLifetimeMinutes = 10`）
- 格式：32 字元 Hex GUID（`Guid.NewGuid().ToString("N")`）
- 連結格式：`{baseUri}resetpassword?token={Uri.EscapeDataString(token)}`
- 驗證頁面：`ResetPassword.razor`（路由 `/resetpassword?token=xxx`，`[AllowAnonymous]`）
- Token 使用後 `IsUsed=1`（一次性），`MT_PasswordResetTokens.Token` 有 UNIQUE NONCLUSTERED INDEX

## 新舊密碼防呆

- `ResetPasswordAsync`（token 流程）與 `ChangePasswordAsync`（直接改密碼）皆有舊密碼比對
- 使用 `AuthService.VerifyPassword(newPassword, currentHash)` 對當前儲存的 PBKDF2 或舊 SHA256 hash 驗證
- 回傳 `SamePassword=true` 時，UI 設 `samePasswordError=true`，欄位下方顯示紅字「新密碼不可與舊密碼相同」（欄位邊框紅色）
- `ClearSamePasswordError()` 綁在 `@bind-Value:after`，使用者開始輸入新密碼欄位就清除紅色

## ResetPasswordAsync 交易邏輯

在 Transaction 內**再次**驗證 token（防止併發）：
1. 查 Token → 若 null / IsUsed / 過期 → 回傳失敗
2. 查當前 PasswordHash 做新舊密碼比對
3. `UPDATE MT_Users SET PasswordHash=@h, IsFirstLogin=0, UpdatedAt=SYSDATETIME()`
4. `UPDATE MT_PasswordResetTokens SET IsUsed=1 WHERE UserId=@uid AND IsUsed=0`
5. Commit

## UI 狀態機（Login.razor 忘記密碼 Modal）

- `resetLinkSent = false`：顯示信箱輸入畫面
- `resetLinkSent = true`：顯示「申請已送出」確認畫面（顯示 `resetEmailSentTo` 地址）
- 重發冷卻：60 秒倒數，`CancellationTokenSource resetResendCountdownCts` 管理
  - Modal 關閉時呼叫 `Cancel()` + `Dispose()` + 重設 `resetResendCountdown=0`
  - `IsResetResendDisabled = isSendingReset || resetResendCountdown > 0`
- `OpenForgotPwdModal()`：`ResetForgotPasswordModalState(clearEmail: true)` 清空所有狀態含 email
- `CloseForgotPwdModal()`：`ResetForgotPasswordModalState(clearEmail: false)` 保留 email（下次開 modal 不用重打）

## SMTP 設定（EmailService）

- 讀 `appsettings.json` 的 `Smtp` 區段：`Smtp:Server`（預設 smtp.gmail.com）、`Smtp:Port`（預設 587）、`Smtp:User`、`Smtp:Password`、`Smtp:FromDisplayName`（預設 "CWT 命題工作平臺"）
- `EnsureSmtpConfigured()` 保護：若 User 或 Password 空，拋出 `InvalidOperationException`（「系統尚未完成 SMTP 設定，暫時無法寄送通知信。」）
- 前端 `SendResetLink()` 有 `catch (InvalidOperationException ex)` 獨立顯示 SweetAlert「系統設定未完成」

## ResetPassword.razor 的 Token 驗證邏輯

- `OnInitializedAsync`：呼叫 `ValidateTokenAsync(token)` 驗證 token（token 缺失直接顯示失敗）
- token 無效/已使用/過期 → `isTokenValid=false`，`OnAfterRenderAsync` 用 SweetAlert2 彈窗後 `NavigateTo("login", forceLoad: true)`（避免 SSR 期間 JS 呼叫失敗，`hasShownInvalidTokenAlert` 防止重複彈窗）
- token 有效 → 顯示新密碼輸入表單
- 重設成功後 SweetAlert success (timer=1000) → 等 1100ms → `NavigateTo("login", forceLoad: true)`
