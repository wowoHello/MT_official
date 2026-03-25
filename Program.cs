using MT.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();


// 註冊自定義服務
builder.Services.AddScoped<MT.Services.ICaptchaService, MT.Services.CaptchaService>();
//builder.Services.AddScoped<MT.Services.IEmailService, MT.Services.EmailService>();
// 註冊資料庫測試服務
builder.Services.AddScoped<MT.Services.IDatabaseService, MT.Services.DatabaseService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

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
}).DisableAntiforgery();

app.Run();
