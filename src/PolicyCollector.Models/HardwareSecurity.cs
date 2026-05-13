using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class HardwareSecurity
{
    [JsonPropertyName("secure_boot_enabled")] public bool? SecureBootEnabled { get; init; }
    [JsonPropertyName("uefi_mode")] public bool UefiMode { get; init; }
    [JsonPropertyName("tpm_present")] public bool TpmPresent { get; init; }
    [JsonPropertyName("tpm_enabled")] public bool TpmEnabled { get; init; }
    [JsonPropertyName("tpm_activated")] public bool TpmActivated { get; init; }
    [JsonPropertyName("tpm_version")] public string? TpmVersion { get; init; }
    [JsonPropertyName("vbs_status")] public int VbsStatus { get; init; }
    [JsonPropertyName("hvci_status")] public int HvciStatus { get; init; }
}
