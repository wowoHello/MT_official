using System.Security.Claims;
using MT.Services;

namespace MT.Endpoints;

/// <summary>
/// 聘書 Canvas 繪製相關的三個 minimal API endpoint，集中於此檔避免 Program.cs 肥大。
/// 由 Program.cs 用 <c>app.MapAppointmentCertEndpoints()</c> 單行掛載。
///
/// - POST /api/appointment-cert/upload                       — 接 JS Canvas toBlob 後的 jpeg，寫檔到 wwwroot/files/ 並 UPDATE FileName
/// - GET  /api/appointment-cert/download/{projectId}         — 當前 user 下載自己在指定梯次的聘書（1 份直回 jpg、2+ zip）
/// - GET  /api/appointment-cert/download/{projectId}/user/{userId} — admin 視角下載他人聘書（教師管理 / 專案管理頁）；需有 teachers 或 projects 模組權限
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
            IWebHostEnvironment env) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "需要 form-data。" });

            var form = await request.ReadFormAsync();
            var certIdStr = form["certId"].ToString();
            var file = form.Files.GetFile("file");

            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "未收到檔案。" });
            if (file.Length > 5 * 1024 * 1024)    return Results.BadRequest(new { error = "檔案不可超過 5MB。" });
            if (!int.TryParse(certIdStr, out var certId)) return Results.BadRequest(new { error = "certId 無效。" });

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            var ok = await appointmentSvc.SaveDrawnFileAsync(certId, ms.ToArray(), env.WebRootPath);
            return ok
                ? Results.Ok(new { certId })
                : Results.NotFound(new { error = "聘書紀錄不存在或已撤銷。" });
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
}
