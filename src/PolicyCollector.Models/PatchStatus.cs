using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class PatchStatus
{
    [JsonPropertyName("auto_update_options")] public int AutoUpdateOptions { get; init; }
    [JsonPropertyName("no_auto_update")] public bool NoAutoUpdate { get; init; }
    [JsonPropertyName("wsus_server")] public string? WsusServer { get; init; }
    [JsonPropertyName("last_success_install")] public string? LastSuccessInstall { get; init; }
    [JsonPropertyName("last_success_detect")] public string? LastSuccessDetect { get; init; }
    [JsonPropertyName("hotfix_count")] public int HotfixCount { get; init; }
    [JsonPropertyName("hotfixes")] public List<HotfixEntry> Hotfixes { get; init; } = [];
}

public sealed class HotfixEntry
{
    [JsonPropertyName("hotfix_id")] public string? HotfixId { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("installed_on")] public string? InstalledOn { get; init; }
}
