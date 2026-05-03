using System.Text.Encodings.Web;
using System.Text.Json;

namespace MT.Services;

/// <summary>
/// 統一 MT_AuditLogs 的 JSON 序列化規格：camelCase + 中文不 escape。
/// 全系統所有寫入 OldValue / NewValue 的 Service 都應改用此 Helper，避免格式分歧。
/// </summary>
public static class AuditLogJsonHelper
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>序列化任意物件為 audit 用 JSON 字串；null 直接回傳 null。</summary>
    public static string? Serialize(object? payload)
        => payload is null ? null : JsonSerializer.Serialize(payload, Options);
}
