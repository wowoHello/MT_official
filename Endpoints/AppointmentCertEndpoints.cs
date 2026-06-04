using System.Security.Claims;
using MT.Services;

namespace MT.Endpoints;

/// <summary>
/// 聘書 Canvas 繪製相關的三個 minimal API endpoint，集中於此檔避免 Program.cs 肥大。
///
/// - POST /api/appointment-cert/upload                       — 寫檔到 wwwroot/files/ 並 UPDATE FileName
/// - GET  /api/appointment-cert/download/{projectId}         — 下載自己在指定梯次的聘書（1 份直回 jpg、2+ zip）
/// - GET  /api/appointment-cert/download/{projectId}/user/{userId} — 下載他人聘書（教師管理 / 專案管理頁）；需有 teachers 或 projects 模組權限
/// </summary>
public static class AppointmentCertEndpoints
{
    public static void MapAppointmentCertEndpoints(this WebApplication app)
    {
        // ====================================================================
        //  聘書 Canvas 繪製上傳
        // ====================================================================
        app.MapPost("/api/appointment-cert/upload", async (
            HttpRequest request,
            IAppointmentService appointmentSvc,
            IMembershipService membership,
            IWebHostEnvironment env,
            HttpContext httpContext) =>
        {
            var requesterIdStr = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(requesterIdStr, out var requesterId)) return Results.Unauthorized();

            if (!request.HasFormContentType) return Results.BadRequest(new { error = "需要 form-data。" });

            var form = await request.ReadFormAsync();
            var certIdStr = form["certId"].ToString();
            var file = form.Files.GetFile("file");

            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "未收到檔案。" });
            if (file.Length > 5 * 1024 * 1024)    return Results.BadRequest(new { error = "檔案不可超過 5MB。" });
            if (!int.TryParse(certIdStr, out var certId)) return Results.BadRequest(new { error = "certId 無效。" });

            var normalizedContentType = file.ContentType?.ToLowerInvariant();
            var ext = normalizedContentType switch
            {
                "image/jpeg" => ".jpg",
                "image/jpg"  => ".jpg",
                "image/png"  => ".png",
                _            => null
            };
            if (ext is null) return Results.BadRequest(new { error = "僅接受 JPG 或 PNG 圖片。" });

            var canManageCertificates =
                await membership.HasModulePermissionAsync(requesterId, projectId: null, "teachers")
                || await membership.HasModulePermissionAsync(requesterId, projectId: null, "projects");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var imageBytes = ms.ToArray();
            if (!HasExpectedImageSignature(imageBytes, ext))
                return Results.BadRequest(new { error = "檔案內容不是有效的 JPG 或 PNG 圖片。" });

            var ok = await appointmentSvc.SaveDrawnFileAsync(
                certId,
                imageBytes,
                env.WebRootPath,
                requesterId,
                canManageCertificates,
                ext);
            return ok
                ? Results.Ok(new { certId })
                : Results.NotFound(new { error = "聘書紀錄不存在、已撤銷或無權限操作。" });
        })
        .DisableAntiforgery()
        .RequireAuthorization();

        // ====================================================================
        //  下載：當前 user 自己的（MainLayout「下載本梯次聘書」用）
        // ====================================================================
        app.MapGet("/api/appointment-cert/download/{projectId:int}", async (
            int projectId,
            IAppointmentService appointmentSvc,
            IWebHostEnvironment env,
            HttpContext httpContext) =>
        {
            var userIdStr = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdStr, out var userId)) return Results.Unauthorized();

            var file = await appointmentSvc.BuildDownloadForUserProjectAsync(userId, projectId, env.WebRootPath);
            if (file is null) return Results.NotFound();

            return Results.File(file.Bytes, file.ContentType, file.FileName);
        })
        .RequireAuthorization();

        // ====================================================================
        //  下載：admin 視角下載他人聘書（教師詳細頁、專案詳情人員列表）
        //  權限：當前 user 必須擁有 teachers 或 projects 模組權限（避免 URL 偽造撈他人聘書）
        // ====================================================================
        app.MapGet("/api/appointment-cert/download/{projectId:int}/user/{userId:int}", async (
            int projectId,
            int userId,
            IAppointmentService appointmentSvc,
            IMembershipService membership,
            IWebHostEnvironment env,
            HttpContext httpContext) =>
        {
            var requesterIdStr = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(requesterIdStr, out var requesterId)) return Results.Unauthorized();

            // 自己下載自己 → 直接放行；下載他人 → 必須有教師或專案管理權限
            if (requesterId != userId)
            {
                var hasPerm = await membership.HasModulePermissionAsync(requesterId, projectId: null, "teachers")
                           || await membership.HasModulePermissionAsync(requesterId, projectId: null, "projects");
                if (!hasPerm) return Results.Forbid();
            }

            var file = await appointmentSvc.BuildDownloadForUserProjectAsync(userId, projectId, env.WebRootPath);
            if (file is null) return Results.NotFound();

            return Results.File(file.Bytes, file.ContentType, file.FileName);
        })
        .RequireAuthorization();
    }

    private static bool HasExpectedImageSignature(byte[] bytes, string extension)
    {
        if (extension == ".jpg")
        {
            return bytes.Length >= 3
                && bytes[0] == 0xFF
                && bytes[1] == 0xD8
                && bytes[2] == 0xFF;
        }

        if (extension == ".png")
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

        return false;
    }
}
