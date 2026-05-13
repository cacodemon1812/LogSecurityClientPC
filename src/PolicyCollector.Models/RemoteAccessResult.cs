using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class RemoteAccessResult
{
    [JsonPropertyName("winrm")] public WinRmSettings WinRm { get; init; } = new();
    [JsonPropertyName("openssh")] public OpenSshSettings OpenSsh { get; init; } = new();
    [JsonPropertyName("telnet_server")] public bool TelnetServer { get; init; }
}

public sealed class WinRmSettings
{
    [JsonPropertyName("service_status")] public string? ServiceStatus { get; init; }
    [JsonPropertyName("allow_basic_auth")] public bool AllowBasicAuth { get; init; }
    [JsonPropertyName("allow_unencrypted")] public bool AllowUnencrypted { get; init; }
    [JsonPropertyName("allow_remote_shell_access")] public bool AllowRemoteShellAccess { get; init; }
}

public sealed class OpenSshSettings
{
    [JsonPropertyName("installed")] public bool Installed { get; init; }
    [JsonPropertyName("service_status")] public string? ServiceStatus { get; init; }
    [JsonPropertyName("default_shell")] public string? DefaultShell { get; init; }
}
