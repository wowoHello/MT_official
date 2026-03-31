---
name: blazor-cookie-auth
description: 提供 Blazor 應用程式中輕量級認證與授權的指南，利用 .NET 內建的 Cookie Authentication 搭配自定義資料表（如 MT_Users, MT_Roles），實現高效能且安全的用戶身份驗證與權限管理。適用於需要自定義認證流程、避免 ASP.NET Core Identity 複雜性的 Blazor 開發任務。
---

# 輕量級 Blazor 認證與授權 (Cookie Authentication)

## 🎯 技能目的 (Objective)

本技能旨在提供一套輕量級且高效能的 Blazor 應用程式認證與授權解決方案。透過利用 .NET 內建的 Cookie Authentication 機制，並結合開發者自定義的資料表結構（如 `MT_Users` 和 `MT_Roles`），避免引入 ASP.NET Core Identity 的複雜性，實現彈性且安全的用戶身份驗證與權限管理。

## 🛠️ 技術依賴 (Tech Stack)

- **前端框架**： Blazor (.NET 10) (支援 Blazor Server, Blazor Web App)
- **認證機制**： ASP.NET Core Cookie Authentication
- **資料存取**： Dapper (與自定義資料表整合)
- **資料庫**： 任何支援的關聯式資料庫 (例如 SSMS, MySQL)

## ⚙️ 觸發條件 (Trigger Conditions)

本技能適用於以下情境：

- 開發 Blazor Server 或 Blazor Web App (Auto/SSR) 應用程式，且希望直接在伺服器端處理資料庫操作，無需額外建立 HTTP API 進行認證。
- 已經有自定義的使用者和角色資料表（例如 `MT_Users`, `MT_Roles`），並希望將其整合到 .NET 的認證授權體系中。
- 尋求比 ASP.NET Core Identity 更輕量、更客製化的認證解決方案。

## 🚀 執行標準流程 (Execution Protocol)

### 1. 破除迷思：Blazor 專案需要 API 嗎？

這取決於您的 Blazor 渲染模式 (Render Mode)：

- **Blazor WebAssembly (純前端 SPA)**：您確實需要後端 API 來發放 JWT Token，因為瀏覽器無法直接連線到您的 SQL Server。
- **Blazor Server 或 Blazor Web App (Auto/SSR)**：您的 C# 程式碼是直接在伺服器端執行的！這意味著您可以直接在 Blazor 元件或後端邏輯中呼叫資料庫（例如使用 EF Core 或 Dapper），完全不需要透過 HTTP API 繞一圈。

### 2. .NET 內建的完美解法：Cookie Authentication + Claims

由於您已經精心設計了 `MT_Users` 和 `MT_Roles` 等客製化資料表，最適合您的、且具備高度 DI 支援的作法是使用 Cookie 驗證 (Cookie Authentication)。

#### 步驟 A：在 `Program.cs` 註冊內建的 DI 服務

您只需要短短幾行語法，就能啟動 .NET 內建的驗證機制：

```csharp
// Program.cs

// 註冊 Cookie 驗證服務
builder.Services.AddAuthentication("MyCookieAuth")
    .AddCookie("MyCookieAuth", options =>
    {
        options.Cookie.Name = "CWT_Auth";
        options.LoginPath = "/login"; // 如果未登入，自動導向這裡
        options.AccessDeniedPath = "/access-denied"; // 權限不足導向
    });

builder.Services.AddAuthorization();

// 確保應用程式使用認證和授權中介軟體
app.UseAuthentication();
app.UseAuthorization();
```

#### 步驟 B：登入邏輯（直接注入與發放 Claims）

在您的登入頁面（或後端登入 Service）中，當您去資料庫比對帳號密碼（驗證 `MT_Users.PasswordHash`）成功後，可以直接使用 .NET 內建的 `HttpContext.SignInAsync()`。這就是結合您資料庫設計的實際運用：

```csharp
// LoginService.cs 或 Login.razor.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using YourProject.Data;
using YourProject.Models;

public class LoginService
{
    private readonly IUserRepository _userRepository; // 假設您有使用者資料庫操作的 Repository 或 Service
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LoginService(IUserRepository userRepository, IHttpContextAccessor httpContextAccessor)
    {
        _userRepository = userRepository;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        // 1. 去您的 MT_Users 資料表驗證帳密 (假設驗證成功，取得 user 物件)
        var user = await _userRepository.ValidateUserAsync(username, password); // 假設此方法會驗證密碼並返回 User 物件

        if (user == null) return false; // 驗證失敗

        // 2. 建立 Claims (將資料庫欄位轉化為宣告)
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.RoleName) // 來自您的 MT_Roles 或 User 物件中的角色資訊
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        // 3. 呼叫內建語法發放 Cookie，完成登入！(不需手寫 API)
        await _httpContextAccessor.HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true } // 可選：讓 Cookie 持久化
        );
        return true;
    }
}
```

#### 步驟 C：登出邏輯

登出更簡單，一行內建語法搞定：

```csharp
// Logout.razor.cs 或 LogoutService.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

public class LogoutService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LogoutService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogoutAsync()
    {
        await _httpContextAccessor.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
```

### 3. Blazor 前端的 DI 神器：AuthenticationStateProvider

在 Blazor 畫面中，您也不需要自己寫程式去檢查 Session 或 Cookie。 .NET 提供了一個內建的 DI 服務叫做 `AuthenticationStateProvider`。

您可以直接在 Blazor 元件 (Razor 檔) 中注入它，或是使用內建的 `<AuthorizeView>` 元件來控制畫面顯示：

```razor
@page "/dashboard"

<AuthorizeView Roles="Admin, Editor"> @* 檢查是否為 Admin 或 Editor 角色 *@
    <Authorized>
        <h1>歡迎回來，@context.User.Identity.Name！</h1>
        <p>您的角色是：@string.Join(", ", context.User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value))</p>
        <button>進入命題專案</button>
    </Authorized>
    <NotAuthorized>
        <p>您沒有權限看到這個區塊。</p>
        <a href="/login">請登入</a>
    </NotAuthorized>
</AuthorizeView>

@code {
    // 如果需要在程式碼中獲取使用者資訊，可以注入 AuthenticationStateProvider
    // [CascadingParameter] private Task<AuthenticationState> authenticationStateTask { get; set; }

    // protected override async Task OnInitializedAsync()
    // {
    //     var authState = await authenticationStateTask;
    //     var user = authState.User;

    //     if (user.Identity.IsAuthenticated)
    //     {
    //         // 使用者已登入
    //         var userName = user.Identity.Name;
    //         var roles = user.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
    //     }
    // }
}
```

在這段語法中，`context.User` 就是我們登入時建立的 `ClaimsPrincipal`，系統會自動去檢查他在資料庫對應的 Role，完全不用您手動寫判斷式 (If-Else)！

## 資源

此技能包含以下資源，可作為進一步參考：

### references/

- `cookie_auth_advanced_config.md`: Cookie Authentication 的進階配置選項，例如滑動過期、事件處理等。
- `rbac_claims_mapping.md`: 如何將自定義資料庫中的角色和權限映射到 Claims 的詳細指南。

### templates/

- `program_cs_auth_config.txt`: `Program.cs` 中認證授權配置的範本。
- `login_service_template.txt`: 登入服務的程式碼範本，包含 Claims 發放邏輯。
- `logout_service_template.txt`: 登出服務的程式碼範本。
- `blazor_authorize_page_template.txt`: 包含 `<AuthorizeView>` 元件的 Blazor 頁面範本。

---

**注意**：此技能的 `scripts/` 目錄目前沒有包含任何可執行腳本，因為認證授權主要涉及程式碼撰寫和配置指南。如有需要，未來可根據具體需求添加自動化腳本。
