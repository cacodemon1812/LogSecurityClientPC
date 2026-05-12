using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class GpoResult
{
    [JsonPropertyName("last_refresh")] public DateTimeOffset? LastRefresh { get; init; }
    [JsonPropertyName("refresh_status")] public string RefreshStatus { get; init; } = "Unknown";
    [JsonPropertyName("computer_gpos")] public List<GpoEntry> ComputerGpos { get; init; } = [];
    [JsonPropertyName("user_gpos")] public List<GpoEntry> UserGpos { get; init; } = [];
    [JsonPropertyName("cse_results")] public List<CseResult> CseResults { get; init; } = [];
}

public sealed class GpoEntry
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("guid")] public string? Guid { get; init; }
    [JsonPropertyName("link_path")] public string? LinkPath { get; init; }
    [JsonPropertyName("link_order")] public int LinkOrder { get; init; }
    [JsonPropertyName("applied")] public bool Applied { get; init; }
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonPropertyName("version_ad")] public int? VersionAd { get; init; }
    [JsonPropertyName("version_sysvol")] public int? VersionSysvol { get; init; }
}

public sealed class CseResult
{
    [JsonPropertyName("cse_name")] public string CseName { get; init; } = string.Empty;
    [JsonPropertyName("extension_guid")] public string? ExtensionGuid { get; init; }
    [JsonPropertyName("last_success")] public DateTimeOffset? LastSuccess { get; init; }
    [JsonPropertyName("last_error")] public string? LastError { get; init; }
    [JsonPropertyName("error_code")] public int ErrorCode { get; init; }
}
