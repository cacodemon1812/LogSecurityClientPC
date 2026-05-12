namespace PolicyCollector.Agent.Config;

public sealed class TransportOptions
{
    public string BackendUrl { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxRetries { get; init; } = 5;
    public int InitialRetryDelaySeconds { get; init; } = 10;
    public bool UseMtls { get; init; } = false;
    public string ClientCertStore { get; init; } = "LocalMachine";
    public string ClientCertThumbprint { get; init; } = string.Empty;
}
