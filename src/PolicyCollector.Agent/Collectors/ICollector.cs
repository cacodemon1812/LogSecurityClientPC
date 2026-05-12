namespace PolicyCollector.Agent.Collectors;

public interface ICollector<TResult>
{
    string ModuleName { get; }
    Task<CollectorResult<TResult>> CollectAsync(CancellationToken ct);
}
