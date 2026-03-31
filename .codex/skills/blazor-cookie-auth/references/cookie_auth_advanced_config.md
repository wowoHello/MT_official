# Cookie Authentication 進階配置選項

除了 `Program.cs` 中基本的 Cookie Authentication 配置外，還有許多進階選項可以調整，以滿足更複雜的需求。

## 1. 配置選項 (AuthenticationOptions)

在 `AddCookie` 方法中，您可以透過 `options` 參數設定多種行為：

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "CWT_Auth";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30); // 設定 Cookie 的有效期限為 30 分鐘
        options.SlidingExpiration = true; // 啟用滑動過期，每次請求都會重設 Cookie 有效期限
        options.ReturnUrlParameter = CookieAuthenticationDefaults.ReturnUrlParameter; // 登入後返回原始頁面的參數名稱
        options.Cookie.HttpOnly = true; // 建議設定為 true，防止客戶端腳本存取 Cookie
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // 建議設定為 Always，確保只透過 HTTPS 傳送 Cookie
        options.Cookie.SameSite = SameSiteMode.Lax; // 設定 SameSite 屬性，防止 CSRF 攻擊

        // 事件處理：可以攔截認證流程中的事件，進行自定義處理
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnValidatePrincipal = async context =>
            {
                // 可以在這裡重新驗證使用者身份或更新 Claims
                // 例如：檢查使用者是否被禁用，或從資料庫獲取最新的角色資訊
                var userPrincipal = context.Principal;
                if (userPrincipal?.Identity?.IsAuthenticated == true)
                {
                    var userId = userPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    // 假設有一個服務可以檢查使用者狀態或重新載入 Claims
                    // var updatedClaims = await _userService.GetUpdatedClaimsAsync(userId);
                    // var newIdentity = new ClaimsIdentity(updatedClaims, context.Scheme.Name);
                    // context.ReplacePrincipal(new ClaimsPrincipal(newIdentity));
                }
            }
        };
    });
```

## 2. 滑動過期 (SlidingExpiration)

當 `SlidingExpiration` 設定為 `true` 時，如果使用者在 `ExpireTimeSpan` 設定的時間內發出了請求，Cookie 的過期時間會被重設。這意味著只要使用者保持活躍，他們就不會被登出。如果使用者在 `ExpireTimeSpan` 內沒有任何活動，Cookie 將會過期，使用者需要重新登入。

## 3. Cookie 安全性 (CookieSecurePolicy, HttpOnly, SameSite)

- `options.Cookie.HttpOnly = true;`：防止客戶端腳本（如 JavaScript）存取 Cookie，有助於防範跨站腳本攻擊 (XSS)。
- `options.Cookie.SecurePolicy = CookieSecurePolicy.Always;`：確保 Cookie 只透過 HTTPS 連線傳送。在生產環境中，這是一個重要的安全設定。
- `options.Cookie.SameSite = SameSiteMode.Lax;`：設定 SameSite 屬性，有助於防範跨站請求偽造 (CSRF) 攻擊。`Lax` 模式允許在頂級導航和 GET 請求中發送 Cookie，但在第三方上下文中會限制發送。

## 4. 事件處理 (Events)

`CookieAuthenticationEvents` 允許您在認證流程的不同階段插入自定義邏輯。常用的事件包括：

- `OnRedirectToLogin`：當使用者未經授權嘗試存取受保護資源時觸發，可以自定義重定向行為。
- `OnRedirectToAccessDenied`：當使用者被授權但沒有足夠權限存取資源時觸發。
- `OnSigningIn`：在建立認證 Cookie 之前觸發。
- `OnSignedIn`：在建立認證 Cookie 之後觸發。
- `OnSigningOut`：在刪除認證 Cookie 之前觸發。
- `OnValidatePrincipal`：定期觸發，允許您重新驗證使用者的身份或更新其 Claims。這對於實現即時的角色/權限變更非常有用，而無需使用者重新登入。

透過這些進階配置，您可以更精確地控制 Blazor 應用程式中的 Cookie Authentication 行為，提升安全性和使用者體驗。
