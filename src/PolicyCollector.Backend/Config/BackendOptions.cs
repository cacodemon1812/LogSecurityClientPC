namespace PolicyCollector.Backend.Config;

public sealed class BackendOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string? HmacSecret { get; init; }
    public bool HmacRequired { get; init; } = false;
    public string SupportedSchemaVersion { get; init; } = "1.0";
    public string? AlertWebhookUrl { get; init; }

    // JWT for dashboard UI authentication
    public string JwtSecret { get; init; } = "policycollector-default-jwt-secret-change-in-production!!";
    public int JwtExpiryMinutes { get; init; } = 480;
    public string[] CorsOrigins { get; init; } = ["http://localhost:3000"];
}
