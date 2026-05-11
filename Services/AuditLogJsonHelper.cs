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

    /// <summary>
    /// 從 audit 的 OldValue/NewValue JSON 中抽取目標的可讀名稱（給 LOG 列表頁顯示）。
    /// 解析優先序：targetDisplayName → 各 TargetType 對應 key → null（呼叫端 fallback 為「已刪除」）。
    /// 同時嘗試 camelCase 與 PascalCase 兩種 key 命名。
    /// </summary>
    public static string? TryExtractTargetName(byte targetType, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        // 統一優先讀 targetDisplayName（RoleService 寫法），再依 TargetType 退到對應欄位
        string secondaryKey = targetType switch
        {
            0 or 5 => "displayName",   // Users / Teachers
            1 or 2 => "name",          // Roles / Projects
            3      => "questionCode",  // Questions
            4      => "title",         // Announcements
            _      => "name"
        };

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var key in new[] { "targetDisplayName", secondaryKey })
            {
                foreach (var variant in new[] { key, char.ToUpperInvariant(key[0]) + key[1..] })
                {
                    if (doc.RootElement.TryGetProperty(variant, out var prop)
                        && prop.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(prop.GetString()))
                    {
                        return prop.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // JSON 損壞或非標準格式（早期純字串寫入時），回 null 讓呼叫端 fallback
        }
        return null;
    }
}
