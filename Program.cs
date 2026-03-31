using MT.Components;
using MT.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(90);
        options.SlidingExpiration = true;

        // --- 方案 B：隱藏 ReturnUrl ---
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                context.Response.Redirect(options.LoginPath);
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

// 註冊自定義服務
builder.Services.AddScoped<ICaptchaService, CaptchaService>();
builder.Services.AddScoped<IDatabaseService, DatabaseService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IProjectService, ProjectService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ====== Auth Endpoints（Cookie 寫入必須在獨立 HTTP 請求中執行）======

// 登入：由 Blazor 元件驗證後暫存資料，此端點完成 Cookie 寫入
app.MapGet("/auth/login", async (string key, IAuthService authService, HttpContext context) =>
{
    if (await authService.CompleteSignInAsync(key, context))
        return Results.Redirect("/home");

    return Results.Redirect("/login");
});

// 登出：清除 Cookie 並導回登入頁
app.MapGet("/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

// Quill 編輯器圖片上傳 API
app.MapPost("/api/upload", async (HttpRequest request, IWebHostEnvironment env) =>
{
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "未選擇檔案" });

    if (file.Length > 5 * 1024 * 1024)
        return Results.BadRequest(new { error = "圖片大小不可超過 5MB" });

    var allowedTypes = new[] { "image/png", "image/jpeg", "image/gif", "image/webp" };
    if (!allowedTypes.Contains(file.ContentType))
        return Results.BadRequest(new { error = "僅支援 PNG、JPEG、GIF、WebP 格式" });

    var uploadsDir = Path.Combine(env.WebRootPath, "uploads");
    Directory.CreateDirectory(uploadsDir);

    var ext = Path.GetExtension(file.FileName);
    var fileName = $"{Guid.NewGuid():N}{ext}";
    var filePath = Path.Combine(uploadsDir, fileName);

    using var stream = new FileStream(filePath, FileMode.Create);
    await file.CopyToAsync(stream);

    return Results.Ok(new { url = $"/uploads/{fileName}" });
}).DisableAntiforgery().RequireAuthorization();

app.Run();
