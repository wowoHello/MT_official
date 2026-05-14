using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace MT.Services;

/// <summary>
/// 7 種題型字典的記憶體快取（Singleton）。
///
/// ⚠️ 資料源 MT_QuestionTypes 設計上「永不變動」（種子用 IDENTITY_INSERT 鎖 Id 1-7，
///    程式碼多處硬編碼 QuestionTypeCodes / TypeIdToKey）。
///    若 DBA 直接改 DB 字典，需手動 IIS AppPool Recycle，或未來開放後台管理頁面
///    呼叫 ReloadAsync() 才會同步到記憶體。
///
/// 啟動時一次載入記憶體，全站 SQL 不再 JOIN 這張靜態小表。
/// </summary>
public interface IQuestionTypeCatalog
{
    IReadOnlyList<QuestionTypeEntry> All { get; }

    /// <summary>找不到回空字串（不丟例外，避免被 SQL JOIN 行為差異絆倒）。</summary>
    string GetName(int id);

    QuestionTypeEntry? Get(int id);

    /// <summary>後台若日後開放字典維護，呼叫此方法即時刷新快取。</summary>
    Task ReloadAsync();
}

public sealed record QuestionTypeEntry(int Id, string Name, string? Icon, int SortOrder);

public sealed class QuestionTypeCatalog : IQuestionTypeCatalog
{
    private readonly IServiceScopeFactory _scopeFactory;
    private QuestionTypeEntry[] _all = [];
    private Dictionary<int, QuestionTypeEntry> _byId = new();

    public QuestionTypeCatalog(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public IReadOnlyList<QuestionTypeEntry> All => _all;

    public string GetName(int id) =>
        _byId.TryGetValue(id, out var entry) ? entry.Name : string.Empty;

    public QuestionTypeEntry? Get(int id) =>
        _byId.TryGetValue(id, out var entry) ? entry : null;

    public async Task ReloadAsync()
    {
        // Singleton 取 Scoped IDatabaseService 必須走 ScopeFactory
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        using var conn = db.CreateConnection();

        var rows = (await conn.QueryAsync<QuestionTypeEntry>(
            "SELECT Id, Name, Icon, SortOrder FROM dbo.MT_QuestionTypes ORDER BY SortOrder, Id;"))
            .ToArray();

        // 寫入用整批替換策略，避免讀者看到半新半舊的中間狀態
        _all = rows;
        _byId = rows.ToDictionary(r => r.Id);
    }
}
