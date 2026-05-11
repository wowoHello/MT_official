using Microsoft.AspNetCore.Http;

namespace MT.Services;

/// <summary>
/// 共用的客戶端 IP 解析邏輯。所有寫入 MT_AuditLogs / MT_LoginLogs 等稽核紀錄的 Service 統一從這裡取得 IP，
/// 避免每個 Service 各自實作格式不一致或欄位長度溢位。
/// </summary>
public static class ClientIpResolver
{
    private const int MaxIpLength = 50;   // 對齊 MT_AuditLogs / MT_LoginLogs 的 IpAddress NVARCHAR(50)

    /// <summary>
    /// 從目前 HttpContext 解析客戶端 IP；無 HttpContext（例如背景作業）或解析失敗時回 null。
    /// 解析優先序：X-Forwarded-For 第一個 → RemoteIpAddress。
    /// </summary>
    public static string? Resolve(IHttpContextAccessor httpContextAccessor)
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null) return null;

        // 反向代理場景（如 IIS、Nginx）優先吃 X-Forwarded-For 第一個 IP；
        // 直連場景則 fallback 為 RemoteIpAddress（通常是 IPv4 / IPv6 來源位址）。
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            return Truncate(forwardedFor.Split(',')[0].Trim());
        }

        return Truncate(context.Connection.RemoteIpAddress?.ToString());
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Length <= MaxIpLength ? value : value[..MaxIpLength];
    }
}
