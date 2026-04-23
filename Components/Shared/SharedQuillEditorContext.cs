namespace MT.Components.Shared;

public sealed class SharedQuillEditRequest
{
    public string FieldLabel { get; init; } = "內容";
    public string Placeholder { get; init; } = "點擊此處開始編輯...";
    public string Value { get; init; } = "";
    public Func<string, Task> ValueChanged { get; init; } = _ => Task.CompletedTask;
}

public sealed class SharedQuillEditorContext
{
    internal Func<SharedQuillEditRequest, Task>? OpenEditorAsync { get; set; }

    public bool IsAvailable => OpenEditorAsync is not null;

    public Task OpenAsync(SharedQuillEditRequest request)
        => OpenEditorAsync?.Invoke(request) ?? Task.CompletedTask;
}
