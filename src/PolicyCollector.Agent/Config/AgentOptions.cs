namespace PolicyCollector.Agent.Config;

public sealed class AgentOptions
{
    public int IntervalMinutes { get; init; } = 60;
    public bool CollectOnStartup { get; init; } = true;
    public int CollectorTimeoutSeconds { get; init; } = 30;
    public ModuleOptions Modules { get; init; } = new();
}

public sealed class ModuleOptions
{
    public bool GPO { get; init; } = true;
    public bool SecurityPolicy { get; init; } = true;
    public bool Firewall { get; init; } = true;
    public bool Defender { get; init; } = true;
    public bool BitLocker { get; init; } = true;
    public bool AppInventory { get; init; } = true;
    public bool AppxPackages { get; init; } = false;
    public bool Services { get; init; } = true;
    public bool ScheduledTasks { get; init; } = true;
    public bool StartupEntries { get; init; } = true;
}
