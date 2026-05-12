namespace PolicyCollector.Agent.Config;

public sealed class LocalQueueOptions
{
    public int MaxAgeHours { get; init; } = 168;
    public int MaxEntries { get; init; } = 1000;
    public int RetryIntervalMinutes { get; init; } = 5;
}
