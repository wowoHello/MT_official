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

            var ext = NormalizeImageExtension(file.ContentType);
            if (ext is null)
                return Results.BadRequest(new { error = "僅支援 PNG、JPEG、GIF、WebP 圖片。" });

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();
            if (!HasExpectedImageSignature(bytes, ext))
                return Results.BadRequest(new { error = "檔案內容不是有效圖片。" });

            var uploadsDir = Path.Combine(env.WebRootPath, "uploads");
            Directory.CreateDirectory(uploadsDir);

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            await File.WriteAllBytesAsync(filePath, bytes);

            // 回相對路徑（無開頭 '/'），靠 <base href> 自動解析 PathBase，
            // 與 IIS 子應用程式部署完全解耦（不再依賴 Configuration["PathBase"] 設定）
            return Results.Ok(new { url = $"uploads/{fileName}" });
        })
        .DisableAntiforgery()
        .RequireAuthorization();

        // ====================================================================
        //  聽力題音檔上傳
        //  各家瀏覽器送 MIME 不一致，因此同時以副檔名當作判斷依據
        // ====================================================================
        app.MapPost("/api/upload-audio", async (HttpRequest request, IWebHostEnvironment env) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "未收到上傳檔案。" });

            if (file.Length > 10 * 1024 * 1024)
                return Results.BadRequest(new { error = "音檔大小不可超過 10MB。" });

            var ext = NormalizeAudioExtension(file.ContentType, Path.GetExtension(file.FileName));
            if (ext is null)
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

    private static string? NormalizeImageExtension(string? contentType)
        => contentType?.Trim().ToLowerInvariant() switch
        {
            "image/png"  => ".png",
            "image/jpeg" => ".jpg",
            "image/jpg"  => ".jpg",
            "image/gif"  => ".gif",
            "image/webp" => ".webp",
            _            => null
        };

    private static bool HasExpectedImageSignature(byte[] bytes, string ext)
    {
        if (ext == ".png")
        {
            return bytes.Length >= 8
                && bytes[0] == 0x89
                && bytes[1] == 0x50
                && bytes[2] == 0x4E
                && bytes[3] == 0x47
                && bytes[4] == 0x0D
                && bytes[5] == 0x0A
                && bytes[6] == 0x1A
                && bytes[7] == 0x0A;
        }

        if (ext == ".jpg")
        {
            return bytes.Length >= 3
                && bytes[0] == 0xFF
                && bytes[1] == 0xD8
                && bytes[2] == 0xFF;
        }

        if (ext == ".gif")
        {
            return bytes.Length >= 6
                && bytes[0] == 0x47
                && bytes[1] == 0x49
                && bytes[2] == 0x46
                && bytes[3] == 0x38
                && (bytes[4] == 0x37 || bytes[4] == 0x39)
                && bytes[5] == 0x61;
        }

        if (ext == ".webp")
        {
            return bytes.Length >= 12
                && bytes[0] == 0x52
                && bytes[1] == 0x49
                && bytes[2] == 0x46
                && bytes[3] == 0x46
                && bytes[8] == 0x57
                && bytes[9] == 0x45
                && bytes[10] == 0x42
                && bytes[11] == 0x50;
        }

        return false;
    }

    private static string? NormalizeAudioExtension(string? contentType, string? originalExtension)
    {
        var mimeExt = contentType?.Trim().ToLowerInvariant() switch
        {
            "audio/mpeg" or "audio/mp3" => ".mp3",
            "audio/wav" or "audio/wave" or "audio/x-wav" or "audio/vnd.wave" => ".wav",
            "audio/ogg" or "application/ogg" => ".ogg",
            "audio/mp4" or "audio/x-m4a" or "audio/m4a" => ".m4a",
            _ => null
        };

        var fileExt = originalExtension?.Trim().ToLowerInvariant();
        if (fileExt is not (".mp3" or ".wav" or ".ogg" or ".m4a"))
        {
            return mimeExt;
        }

        return mimeExt ?? fileExt;
    }
}
