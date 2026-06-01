using System.Data;
using Dapper;

namespace MT.Services;

/// <summary>
/// 「梯次當前進行中工作階段」共用查詢。
///
/// 原本同一段 SQL（SELECT TOP 1 PhaseCode … WHERE PhaseCode &gt; 1 AND 今天落於 Start/EndDate）
/// 在 QuestionService / ReviewService 共抄 4 份；現收斂於此，日後改階段判定規則只需改一處。
///
/// 注意：本查詢只回「單純的當前 PhaseCode（byte?）」。其他變體邏輯不同，刻意不併入：
///   - 回 ProjectPhaseInfo（含 PhaseName / EndDate / DaysLeft）
///   - 多選 EndDate 欄
///   - 含急件 Threshold 條件（首頁急件提醒）
///   - 已結案以 ClosedAt 為基準
///   - 找階段「斷層」（EndDate &lt; 今天）
/// </summary>
public static class ProjectPhaseQuery
{
    public const string CurrentPhaseCodeSql = """
        SELECT TOP 1 PhaseCode
        FROM dbo.MT_ProjectPhases
        WHERE ProjectId = @ProjectId
          AND PhaseCode > 1
          AND CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
        ORDER BY SortOrder;
        """;

    /// <summary>
    /// 取當前工作階段 PhaseCode（排除 PhaseCode=1 產學框架）；無進行中階段回 null。
    /// 可選帶交易（在既有 transaction 內查詢時傳入 tx）。
    /// </summary>
    public static Task<byte?> GetCurrentPhaseCodeAsync(
        IDbConnection conn, int projectId, IDbTransaction? tx = null)
        => conn.ExecuteScalarAsync<byte?>(CurrentPhaseCodeSql, new { ProjectId = projectId }, tx);
}
