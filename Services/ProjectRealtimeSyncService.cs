namespace MT.Services;

public enum ProjectRealtimeChangeType
{
    Created = 1,
    Updated = 2,
    Deleted = 3
}

public sealed record ProjectRealtimeSyncMessage(
    ProjectRealtimeChangeType ChangeType,
    int? ProjectId = null);

public interface IProjectRealtimeSyncService
{
    event Func<ProjectRealtimeSyncMessage, Task>? ProjectsChanged;
    Task NotifyProjectsChangedAsync(ProjectRealtimeSyncMessage message);
}

public sealed class ProjectRealtimeSyncService : IProjectRealtimeSyncService
{
    private readonly ILogger<ProjectRealtimeSyncService> _logger;

    public ProjectRealtimeSyncService(ILogger<ProjectRealtimeSyncService> logger)
    {
        _logger = logger;
    }

    public event Func<ProjectRealtimeSyncMessage, Task>? ProjectsChanged;

    public async Task NotifyProjectsChangedAsync(ProjectRealtimeSyncMessage message)
    {
        var handlers = ProjectsChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (Func<ProjectRealtimeSyncMessage, Task> handler in handlers.GetInvocationList())
        {
            try
            {
                await handler(message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "處理梯次即時同步事件失敗 ({ChangeType}, ProjectId={ProjectId})", message.ChangeType, message.ProjectId);
            }
        }
    }
}
