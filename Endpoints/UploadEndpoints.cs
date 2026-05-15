namespace MT.Endpoints;

/// <summary>
/// 編輯器類檔案上傳 endpoints。
/// - POST /api/upload       — Quill 編輯器附圖（5MB / PNG / JPEG / GIF / WebP）
/// - POST /api/upload-audio — 聽力題型音檔（10MB / MP3 / WAV / OGG / M4A）
///
/// 上傳成功回 { url } 給前端組 src；失敗回 { error }。
/// 兩支都 .DisableAntiforgery()（純 multipart/form-data，且已 .RequireAuthorization()）。
/// </summary>
public static class UploadEndpoints
{
    public static void MapUploadEndpoints(this WebApplication app)
    {
        // ====================================================================
        //  Quill 編輯器附圖上傳
        // ====================================================================
        app.MapPost("/api/upload", async (HttpRequest request, IWebHostEnvironment env) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "未收到上傳檔案。" });

            if (file.Length > 5 * 1024 * 1024)
                return Results.BadRequest(new { error = "檔案大小不可超過 5MB。" });

            var allowedTypes = new[] { "image/png", "image/jpeg", "image/gif", "image/webp" };
            if (!allowedTypes.Contains(file.ContentType))
                return Results.BadRequest(new { error = "僅支援 PNG、JPEG、GIF、WebP 圖片。" });

            var uploadsDir = Path.Combine(env.WebRootPath, "uploads");
            Directory.CreateDirectory(uploadsDir);

            var ext = Path.GetExtension(file.FileName);
            var fileName = $"{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);

            // 回相對路徑（無開頭 '/'），靠 <base href> 自動解析 PathBase，
            // 與 IIS 子應用程式部署完全解耦（不再依賴 Configuration["PathBase"] 設定）
            return Results.Ok(new { url = $"uploads/{fileName}" });
        })
        .DisableAntiforgery()
        .RequireAuthorization();

        // ====================================================================
        //  聽力題音檔上傳
        //  各家瀏覽器送 MIME 不一致，因此同時以副檔名兜底
        // ====================================================================
        app.MapPost("/api/upload-audio", async (HttpRequest request, IWebHostEnvironment env) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "未收到上傳檔案。" });

            if (file.Length > 10 * 1024 * 1024)
                return Results.BadRequest(new { error = "音檔大小不可超過 10MB。" });

            var allowedMime = new[]
            {
                "audio/mpeg", "audio/mp3",
                "audio/wav", "audio/wave", "audio/x-wav", "audio/vnd.wave",
                "audio/ogg", "application/ogg",
                "audio/mp4", "audio/x-m4a", "audio/m4a"
            };
            var allowedExt = new[] { ".mp3", ".wav", ".ogg", ".m4a" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExt.Contains(ext) && !allowedMime.Contains(file.ContentType))
                return Results.BadRequest(new { error = "僅支援 MP3、WAV、OGG、M4A 音檔。" });

            var audioDir = Path.Combine(env.WebRootPath, "uploads", "audio");
            Directory.CreateDirectory(audioDir);

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(audioDir, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);

            // 同上，回相對路徑由 <base href> 解析
            return Results.Ok(new { url = $"uploads/audio/{fileName}" });
        })
        .DisableAntiforgery()
        .RequireAuthorization();
    }
}
