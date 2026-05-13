using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class AdInfo
{
    [JsonPropertyName("domain_controller")] public string? DomainController { get; init; }
    [JsonPropertyName("site_name")] public string? SiteName { get; init; }
    [JsonPropertyName("ou_path")] public string? OuPath { get; init; }
    [JsonPropertyName("kerberos_available")] public bool KerberosAvailable { get; init; }
}
