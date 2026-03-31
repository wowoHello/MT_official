namespace MT.Models;

public enum ProjectRealtimeChangeType
{
    Created = 1,
    Updated = 2,
    Deleted = 3
}

public sealed record ProjectRealtimeSyncMessage(
    ProjectRealtimeChangeType ChangeType,
    int? ProjectId = null);
