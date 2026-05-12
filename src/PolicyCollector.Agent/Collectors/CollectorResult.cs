namespace PolicyCollector.Agent.Collectors;

public sealed class CollectorResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorDetail { get; init; }
    public TimeSpan Duration { get; init; }

    public static CollectorResult<T> Ok(T data, TimeSpan duration) =>
        new() { Success = true, Data = data, Duration = duration };

    public static CollectorResult<T> Fail(string message, string? detail = null) =>
        new() { Success = false, ErrorMessage = message, ErrorDetail = detail };
}
