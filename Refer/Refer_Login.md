📄 頁面規格：登入與首頁導航 (Login.razor & Home.razor)
🎯 核心目標
提供高安全性的使用者身分驗證，支援圖形驗證碼防機器人、90 天免登入機制，並整合忘記密碼流程。登入成功後，依據角色權限動態載入首頁模組。

🗄️ 關聯資料表 (Database Tables)
核心驗證：dbo.MT_Users, dbo.MT_Roles

權限動態選單：dbo.MT_RolePermissions, dbo.MT_Modules

忘記密碼：dbo.MT_PasswordResetTokens

系統紀錄：dbo.MT_LoginLogs (登入歷程), dbo.MT_AuditLogs (關鍵行為稽核)

⚙️ 功能模組與業務邏輯
1. 一般帳密登入 (Login Flow)
觸發條件：使用者點擊「登入」按鈕。

輸入資料：

Username (必填)：字串格式。

Password (必填)：明文字串（前端傳輸前或後端接收後進行加密）。

Captcha (必填)：6 碼英數字（比對 Canvas 隨機產生的驗證碼）。

RememberMe (選填)：布林值 (Checkbox)。

處理邏輯：

驗證碼比對：檢查使用者輸入的 Captcha 是否與系統 Canvas 產生的字串相符（忽略大小寫）。

密碼雜湊：將輸入的 Password 使用 SHA256 演算法轉換為 32 Bytes 雜湊值。

身分核對：查詢 MT_Users 表，條件為 Username = 帳號 且 PasswordHash = 雜湊值。

狀態檢查：確認該帳號的 Status（0:停用, 1:啟用, 2:鎖定）。

預期輸出 / 狀態改變：

登入成功：

更新 MT_Users.LastLoginAt 為當前時間。

若 RememberMe = true，發行效期為 90 天的 HttpOnly Cookie；否則發行 Session 級別（關閉瀏覽器失效）的 Cookie。

寫入 MT_LoginLogs：IsSuccess = 1，記錄 IpAddress 與 UserAgent。

寫入 MT_AuditLogs：Action = 3 (登入)，TargetType = 0 (Users)。

跳轉至 Home.razor。

異常處理 (Edge Cases)：

IF 驗證碼輸入錯誤 THEN 清空密碼與驗證碼欄位，提示「驗證碼錯誤」，並重新產生 Canvas 驗證碼。
IF 帳號不存在或密碼錯誤 THEN 寫入 MT_LoginLogs (IsSuccess = 0, FailReason = '帳密錯誤')，提示「帳號或密碼錯誤」。
IF MT_Users.Status != 1 THEN 拒絕登入，提示「此帳號已停用或鎖定，請聯繫管理員」。

2. 忘記密碼 (Forgot Password Flow)
觸發條件：使用者點擊「忘記密碼」按鈕，彈出 Modal 視窗並送出。

輸入資料：

Email (必填)：需符合 Email 格式。

處理邏輯：

信箱檢核：查詢 MT_Users 表是否存在此 Email。

產生憑證：使用 Guid.NewGuid().ToString() 產生一組唯一識別碼。

寫入資料庫：在 MT_PasswordResetTokens 新增一筆紀錄，綁定 UserId、填入 Token，並設定 ExpiresAt (例如 24 小時後失效)，IsUsed = 0。

發送郵件：觸發寄信服務，信件內容包含重設密碼的超連結（例如：/ResetPassword?token={Token}）。

異常處理 (Edge Cases)：

IF 信箱不存在於 MT_Users THEN 基於資安不應明確提示信箱不存在，應統一回覆：「若信箱存在於系統中，您將會收到一封重設密碼的信件」。

3. 登入後首頁導航與權限載入 (Home.razor Initialization)
觸發條件：成功登入並跳轉至首頁。

處理邏輯：

讀取當前登入者的 UserId 與關聯的 RoleId。

權限清單獲取：JOIN MT_RolePermissions 與 MT_Modules，條件為 RoleId = 登入者角色 且 MT_RolePermissions.IsEnabled = 1 且 MT_Modules.IsActive = 1。

UI 渲染：依據取得的 MT_Modules.ModuleKey 決定側邊欄功能選單與首頁 Dashboard 區塊的顯示與隱藏。