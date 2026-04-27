using MT.Components;
using MT.Hubs;
using MT.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// =========================================================
// UI 與即時互動
// =========================================================
// Blazor Server 互動式元件
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// SignalR：提供應用程式層級即時同步
builder.Services.AddSignalR();

// =========================================================
// 驗證與授權
// =========================================================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // 未登入時導回登入頁
        options.LoginPath = "/login";

        // 登出後回登入頁
        options.LogoutPath = "/login";

        // Session 預設：未勾「記住登入」時的票據有效時間（滑動延長）
        // 勾選「記住登入」時，於 AuthService.CompleteSignInAsync 顯式覆寫為 90 天絕對期限
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = true;        
    });

builder.Services.AddAuthorization();

// 讓 Razor 元件可透過 CascadingParameter 取得 AuthenticationState
builder.Services.AddCascadingAuthenticationState();

// 供需要存取 HttpContext 的服務或流程使用
builder.Services.AddHttpContextAccessor();

// =========================================================
// 應用程式服務
// =========================================================
// 登入頁 Canvas 驗證碼產生與比對（Login.razor）
builder.Services.AddScoped<ICaptchaService, CaptchaService>();

// Dapper 資料庫連線工廠，所有 Service 透過此取得 SqlConnection
builder.Services.AddScoped<IDatabaseService, DatabaseService>();

// 登入驗證、Cookie 寫入、密碼雜湊、稽核紀錄（Login.razor、/auth/login、/auth/logout）
builder.Services.AddScoped<IAuthService, AuthService>();

// 忘記密碼寄送驗證信（由 PasswordResetService 內部呼叫）
builder.Services.AddScoped<IEmailService, EmailService>();

// 忘記密碼 Token 管理、首次登入強制改密碼（Login.razor、ResetPassword.razor、FirstLoginPassword.razor）
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();

// 命題專案梯次 CRUD、可見梯次查詢（Projects.razor、MainLayout.razor）
builder.Services.AddScoped<IProjectService, ProjectService>();

// 帳號管理、角色權限 CRUD、功能模組權限查詢（Roles.razor、MainLayout.razor）
builder.Services.AddScoped<IRoleService, RoleService>();

// 系統公告 CRUD、自動下架、統計卡片（Announcements.razor、Home.razor）
builder.Services.AddScoped<IAnnouncementService, AnnouncementService>();

// 教師人才庫 CRUD、命題/審題歷程查詢、梯次指派（Teachers.razor）
builder.Services.AddScoped<ITeacherService, TeacherService>();

// 命題任務 CRUD、配額進度、當前階段偵測（CwtList.razor）
builder.Services.AddScoped<IQuestionService, QuestionService>();

// 命題總覽列表彙整與詳情（Overview.razor，內部依賴 IQuestionService）
builder.Services.AddScoped<IOverviewService, OverviewService>();

// 首頁公告載入與急件警示（Home.razor）
builder.Services.AddScoped<IHomeService, HomeService>();

var app = builder.Build();

// =========================================================
// HTTP Pipeline
// =========================================================
// 從設定檔讀取 PathBase（本機不設 = "/"，IIS 子應用程式設 "/MT"）
var pathBase = app.Configuration["PathBase"];
if (!string.IsNullOrEmpty(pathBase))
{
    app.UsePathBase(pathBase);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// 找不到頁面時導到自訂 NotFound 頁
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// =========================================================
// UI / 靜態資源 / 即時通道
// =========================================================
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// 命題專案梯次即時同步 Hub
app.MapHub<ProjectsHub>("/hubs/projects");

// =========================================================
// Auth 相關端點
// =========================================================
// 目的：
// Login.razor 先透過 AuthService.PrepareSignIn 準備登入資料，
// 再導到這個端點，由真正的 HTTP 要求完成 Cookie 寫入。
app.MapGet("/auth/login", async (string key, IAuthService authService, HttpContext context) =>
{
    var (success, isFirstLogin) = await authService.CompleteSignInAsync(key, context);

    if (!success)
        return Results.Redirect("~/login");

    // 首次登入強制改密碼
    return Results.Redirect(isFirstLogin ? "~/first-login-password" : "~/");
});

// 清除目前登入 Cookie，並回登入頁（登出前寫入稽核紀錄）
// 稽核失敗不應阻擋登出流程，因此用 try-catch 保護
app.MapGet("/auth/logout", async (IAuthService authService, HttpContext context) =>
{
    try
    {
        var userIdClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out var userId))
        {
            var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
                     ?? context.Connection.RemoteIpAddress?.ToString();
            await authService.LogAuditAsync(userId, MT.Models.AuditAction.Logout, ip);
        }
    }
    catch
    {
        // 稽核紀錄寫入失敗不影響登出
    }

    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("~/login");
});

// =========================================================
// 編輯器上傳 API
// =========================================================
// 給 Quill 編輯器上傳圖片使用，成功後回傳可存取的靜態網址
app.MapPost("/api/upload", async (HttpRequest request, IWebHostEnvironment env) =>
{
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "未收到上傳檔案。" });
    }

    if (file.Length > 5 * 1024 * 1024)
    {
        return Results.BadRequest(new { error = "檔案大小不可超過 5MB。" });
    }

    var allowedTypes = new[] { "image/png", "image/jpeg", "image/gif", "image/webp" };
    if (!allowedTypes.Contains(file.ContentType))
    {
        return Results.BadRequest(new { error = "僅支援 PNG、JPEG、GIF、WebP 圖片。" });
    }

    var uploadsDir = Path.Combine(env.WebRootPath, "uploads");
    Directory.CreateDirectory(uploadsDir);

    var ext = Path.GetExtension(file.FileName);
    var fileName = $"{Guid.NewGuid():N}{ext}";
    var filePath = Path.Combine(uploadsDir, fileName);

    await using var stream = new FileStream(filePath, FileMode.Create);
    await file.CopyToAsync(stream);

    var pathBase = request.PathBase.HasValue ? request.PathBase.Value : "";
    return Results.Ok(new { url = $"{pathBase}/uploads/{fileName}" });
})
.DisableAntiforgery()
.RequireAuthorization();

app.Run();
