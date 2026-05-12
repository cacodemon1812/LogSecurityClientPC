namespace PolicyCollector.Backend.Config;

public sealed class BackendOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string? HmacSecret { get; init; }
    public bool HmacRequired { get; init; } = false;
    public string SupportedSchemaVersion { get; init; } = "1.0";
    public string? AlertWebhookUrl { get; init; }
}
