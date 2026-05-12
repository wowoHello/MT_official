using System.Data;
using Dapper;
using MT.Models;

namespace MT.Services;

/// <summary>
/// 題目附圖（MT_QuestionImages）的 DB 存取共用 helper。
/// 由 QuestionService（建立 / 編輯 / 修題）與 ReviewService（總召自改）共用，避免 SQL 重複。
///
/// 涵蓋：
///   - 母題層級附圖（SubQuestionId IS NULL）：UpsertMaster / LoadMaster
///   - 子題層級附圖（SubQuestionId 對應 MT_SubQuestions.Id）：UpsertSub / LoadSub
///
/// SubQuestionIndex（UI 端 0-based form 位置）與 SubQuestionId（DB Id）之間的轉換
/// 在 Upsert/Load 兩端各自做一次，呼叫端只看到 SubQuestionIndex。
/// </summary>
internal static class QuestionImagePersistence
{
    // ==================================================================
    //  母題層級
    // ==================================================================

    /// <summary>
    /// 寫入指定題目的母題附圖：先 DELETE 該題目所有 <c>SubQuestionId IS NULL</c> 的紀錄，
    /// 再依 UI 順序 INSERT 新清單（過濾空 ImagePath，並重新編號 SortOrder）。
    /// 必須在現有 transaction 中執行（與 MT_Questions UPDATE 同 transaction 保持一致性）。
    /// </summary>
    public static async Task UpsertMasterAsync(
        IDbConnection conn, IDbTransaction tx,
        int questionId, List<QuestionImage> images)
    {
        await conn.ExecuteAsync(
            "DELETE FROM dbo.MT_QuestionImages WHERE QuestionId = @Id AND SubQuestionId IS NULL",
            new { Id = questionId }, tx);

        var masterImages = images
            .Where(i => i.SubQuestionIndex is null && !string.IsNullOrWhiteSpace(i.ImagePath))
            .ToList();
        if (masterImages.Count == 0) return;

        const string insertSql = """
            INSERT INTO dbo.MT_QuestionImages (QuestionId, SubQuestionId, FieldType, ImagePath, SortOrder)
            VALUES (@QuestionId, NULL, @FieldType, @ImagePath, @SortOrder);
            """;

        // SortOrder 依 (FieldType) 群組各自重新編號 1, 2, 3...
        foreach (var grp in masterImages.GroupBy(i => i.FieldType))
        {
            byte order = 1;
            foreach (var img in grp)
            {
                await conn.ExecuteAsync(insertSql, new
                {
                    QuestionId = questionId,
                    img.FieldType,
                    img.ImagePath,
                    SortOrder = order++
                }, tx);
            }
        }
    }

    /// <summary>讀取指定題目的母題附圖，依 FieldType + SortOrder 排序。SubQuestionIndex 為 null。</summary>
    public static async Task<List<QuestionImage>> LoadMasterAsync(IDbConnection conn, int questionId)
    {
        const string sql = """
            SELECT Id, FieldType, ImagePath, SortOrder
            FROM dbo.MT_QuestionImages
            WHERE QuestionId = @Id AND SubQuestionId IS NULL
            ORDER BY FieldType, SortOrder;
            """;
        var rows = await conn.QueryAsync<QuestionImage>(sql, new { Id = questionId });
        return rows.AsList();
    }

    // ==================================================================
    //  子題層級（題組型）
    // ==================================================================

    /// <summary>
    /// 寫入此母題下所有子題的附圖：先 DELETE 屬於該母題任一子題的所有 row，再依 form 位置寫入。
    /// 呼叫前必須先跑過 InsertSubQuestionsAsync / UpsertSubQuestionsAsync，子題的 sq.Id 才會被填入。
    ///
    /// 對應關係：QuestionImage.SubQuestionIndex（form 位置）→ subQuestionDbIds[idx]（DB Id）
    /// </summary>
    public static async Task UpsertSubAsync(
        IDbConnection conn, IDbTransaction tx,
        int parentQuestionId, IReadOnlyList<int> subQuestionDbIds, List<QuestionImage> images)
    {
        // 1. 清掉此母題下所有子題附圖（含已軟刪除子題的孤兒 row）
        await conn.ExecuteAsync("""
            DELETE FROM dbo.MT_QuestionImages
            WHERE SubQuestionId IN (
                SELECT Id FROM dbo.MT_SubQuestions WHERE ParentQuestionId = @PId
            );
            """, new { PId = parentQuestionId }, tx);

        // 2. 過濾出有效子題附圖：SubQuestionIndex 必須對應到一個有效 DB id
        var validImages = images
            .Where(i => i.SubQuestionIndex is int idx
                     && idx >= 0 && idx < subQuestionDbIds.Count
                     && subQuestionDbIds[idx] > 0
                     && !string.IsNullOrWhiteSpace(i.ImagePath))
            .ToList();
        if (validImages.Count == 0) return;

        const string insertSql = """
            INSERT INTO dbo.MT_QuestionImages (QuestionId, SubQuestionId, FieldType, ImagePath, SortOrder)
            VALUES (NULL, @SubId, @FieldType, @ImagePath, @SortOrder);
            """;

        // SortOrder 依 (SubQuestionIndex, FieldType) 群組各自重新編號
        foreach (var grp in validImages.GroupBy(i => (i.SubQuestionIndex!.Value, i.FieldType)))
        {
            byte order = 1;
            foreach (var img in grp)
            {
                await conn.ExecuteAsync(insertSql, new
                {
                    SubId = subQuestionDbIds[grp.Key.Item1],
                    img.FieldType,
                    img.ImagePath,
                    SortOrder = order++
                }, tx);
            }
        }
    }

    /// <summary>
    /// 讀取此母題下所有子題附圖，並依傳入的 subQuestionDbIds 陣列反查 form 位置（SubQuestionIndex）。
    /// 找不到對應位置（例如該 SubQuestionId 已被軟刪除）的 row 會被忽略。
    /// </summary>
    public static async Task<List<QuestionImage>> LoadSubAsync(
        IDbConnection conn, int parentQuestionId, IReadOnlyList<int> subQuestionDbIds)
    {
        if (subQuestionDbIds.Count == 0) return [];

        const string sql = """
            SELECT Id, SubQuestionId, FieldType, ImagePath, SortOrder
            FROM dbo.MT_QuestionImages
            WHERE SubQuestionId IN (
                SELECT Id FROM dbo.MT_SubQuestions
                WHERE ParentQuestionId = @PId AND IsDeleted = 0
            )
            ORDER BY SubQuestionId, FieldType, SortOrder;
            """;
        var rows = await conn.QueryAsync<SubImageRow>(sql, new { PId = parentQuestionId });

        // 建立 SubQuestionId → form index 反查表
        var dbIdToIdx = new Dictionary<int, int>();
        for (int i = 0; i < subQuestionDbIds.Count; i++)
        {
            if (subQuestionDbIds[i] > 0)
                dbIdToIdx[subQuestionDbIds[i]] = i;
        }

        return rows
            .Where(r => dbIdToIdx.ContainsKey(r.SubQuestionId))
            .Select(r => new QuestionImage
            {
                Id               = r.Id,
                FieldType        = r.FieldType,
                SubQuestionIndex = dbIdToIdx[r.SubQuestionId],
                ImagePath        = r.ImagePath,
                SortOrder        = r.SortOrder
            })
            .ToList();
    }

    private sealed class SubImageRow
    {
        public int    Id            { get; set; }
        public int    SubQuestionId { get; set; }
        public byte   FieldType     { get; set; }
        public string ImagePath     { get; set; } = "";
        public byte   SortOrder     { get; set; }
    }
}
