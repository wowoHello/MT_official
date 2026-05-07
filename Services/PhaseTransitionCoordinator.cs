using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace MT.Services;

// ============================================================
//  PhaseTransitionCoordinator
//  集中觸發「命題階段結束」+「後續階段升級」兩組 idempotent 寫入。
//
//  問題背景：
//    CwtList / Reviews / Overview 三個頁面都需要保證題目 Status
//    與當前階段對齊，但讓每個頁面 OnParametersSetAsync 都自己 try/catch
//    呼叫 EnsureXxxAsync，會：
//      1) 雜湊不一致：每處 try/catch 寫法不同、有的吞例外、有的 toast、有的 log
//      2) 重複 SQL：同 user 切換 tab 兩次就跑兩次 transaction
//      3) Race：兩個 tab 同時開可能撞 SERIALIZABLE deadlock
//
//  做法（三層防護）：
//    層一（最快）：IMemoryCache cache check — 60 秒短路，O(1)，lock-free
//    層二（並發鎖）：per-projectId SemaphoreSlim(1,1)，防同一瞬間多個 caller 同時通過 cache
//    層三（最慢）：cache double-check after 拿到 sem，確保 sem 等待期間另一 caller 已更新 cache
//
//  注意：去重視窗很短（60 秒）— 結案、階段切換等業務時點仍會被下次呼叫
//        正確觸發；目的只是擋掉「同一頁面在 60 秒內被反覆 render 觸發」的雜訊。
// ============================================================

public interface IPhaseTransitionCoordinator
{
    /// <summary>
    /// 確保指定專案的階段轉換已執行。Idempotent；60 秒內重複呼叫會直接短路。
    /// 內部錯誤會被 ILogger 記錄但不向外丟出（避免阻擋頁面載入）。
    /// </summary>
    Task EnsureAsync(int projectId);
}

public class PhaseTransitionCoordinator(
    IServiceProvider serviceProvider,
    IMemoryCache cache,
    ILogger<PhaseTransitionCoordinator> logger) : IPhaseTransitionCoordinator
{
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromSeconds(60);

    // per-projectId SemaphoreSlim(1,1)：確保同一 project 只有一個 caller 在跑 SQL
    // ConcurrentDictionary：Singleton 生命週期內只建立一次，value 不會被 GC
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _semaphores = new();

    public async Task EnsureAsync(int projectId)
    {
        var key = $"phase-tx:{projectId}";

        // 層一：cache 快速短路（lock-free）
        if (cache.TryGetValue(key, out _)) return;

        // 層二：取得 per-projectId sem，確保同時只有一個 caller 進入 SQL 段落
        var sem = _semaphores.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        try
        {
            // 層三：double-check — 等 sem 期間，前一個 caller 已寫好 cache
            if (cache.TryGetValue(key, out _)) return;

            // 用 IServiceProvider 取 scoped 服務 — 此 coordinator 為 Singleton，
            // 不能直接注入 IQuestionService（Scoped），會 lifetime mismatch
            using var scope = serviceProvider.CreateScope();
            var questionService = scope.ServiceProvider.GetRequiredService<IQuestionService>();

            try
            {
                await questionService.EnsureCompositionPhaseClosedAsync(projectId);
            }
            catch (InvalidOperationException ex)
            {
                // 命題教師不足 2 人 — warning 級別（業務正常情境）
                logger.LogWarning(ex, "EnsureCompositionPhaseClosedAsync skipped for project {ProjectId}: {Reason}",
                    projectId, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "EnsureCompositionPhaseClosedAsync failed for project {ProjectId}", projectId);
            }

            try
            {
                await questionService.EnsurePhaseTransitionAsync(projectId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "EnsurePhaseTransitionAsync failed for project {ProjectId}", projectId);
            }

            // 短期 cache 防雜訊；超過 60 秒任何後續呼叫會重跑（仍 idempotent）
            cache.Set(key, 1, DeduplicationWindow);
        }
        finally
        {
            // 無論成功或例外，一定釋放 sem
            sem.Release();
        }
    }
}
