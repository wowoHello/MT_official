namespace MT.Models;

/// </summary>
/// 專案梯次即時同步訊息（SignalR）。
/// 作用：管理員對專案做了增/刪/改，立刻廣播給所有線上使用者，讓他們的「梯次切換器」自動刷新，不用手動重整。
/// </summary>
public enum ProjectRealtimeChangeType
{
    Created = 1,
    Updated = 2,
    Deleted = 3
}

public sealed record ProjectRealtimeSyncMessage(
    ProjectRealtimeChangeType ChangeType,
    int? ProjectId = null);