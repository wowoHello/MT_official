using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MT.Services;

namespace MT.Endpoints;

/// <summary>
/// 認證相關 HTTP endpoints。Razor 元件無法直接寫 Cookie，必須走真正的 HTTP request；
/// 因此 Login.razor 先呼叫 AuthService.PrepareSignIn 暫存 ClaimsPrincipal，
/// 再 NavigationManager.NavigateTo("/auth/login?key=...", forceLoad: true) 觸發本 endpoint
/// 完成 CompleteSignInAsync 寫 Cookie。
///
/// 登出同理走 /auth/logout 完成 SignOutAsync 並寫稽核（失敗不阻擋登出）。
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        // 由 Login.razor 跳轉至此完成 Cookie 寫入；首次登入導 first-login-password，否則導首頁
        app.MapGet("/auth/login", async (string key, IAuthService authService, HttpContext context) =>
        {
            var (success, isFirstLogin) = await authService.CompleteSignInAsync(key, context);

            if (!success)
                return Results.Redirect("~/login");

            return Results.Redirect(isFirstLogin ? "~/first-login-password" : "~/");
        });

        // 清除目前登入 Cookie，並回登入頁（登出前寫入稽核紀錄；失敗不應阻擋登出）
        app.MapGet("/auth/logout", async (IAuthService authService, HttpContext context) =>
        {
            try
            {
                var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(userIdClaim, out var userId))
                {
                    var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
                             ?? context.Connection.RemoteIpAddress?.ToString();
                    await authService.LogLogoutAsync(userId, ip);
                }
            }
            catch
            {
                // 稽核紀錄寫入失敗不影響登出
            }

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("~/login");
        });
    }
}
