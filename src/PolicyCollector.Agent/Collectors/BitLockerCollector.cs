using System.Diagnostics;
using System.Management.Automation;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class BitLockerCollector : ICollector<List<BitLockerVolume>>
{
    private readonly PowerShellRunner _ps;
    private readonly ILogger<BitLockerCollector> _logger;

    public BitLockerCollector(PowerShellRunner ps, ILogger<BitLockerCollector> logger)
    {
        _ps = ps;
        _logger = logger;
    }

    public string ModuleName => "BitLocker";

    public async Task<CollectorResult<List<BitLockerVolume>>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var objects = await _ps.RunScriptAsync(
                "Get-BitLockerVolume | Select-Object MountPoint,VolumeStatus,EncryptionMethod,ProtectionStatus,EncryptionPercentage | ConvertTo-Json -Compress",
                ct);

            var volumes = ParseVolumes(objects);
            return CollectorResult<List<BitLockerVolume>>.Ok(volumes, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BitLocker collection failed (may not be available)");
            return CollectorResult<List<BitLockerVolume>>.Fail("BitLocker collection failed", ex.ToString());
        }
    }

    private List<BitLockerVolume> ParseVolumes(IReadOnlyList<PSObject> objects)
    {
        var volumes = new List<BitLockerVolume>();

        foreach (var obj in objects)
        {
            if (obj?.Properties == null) continue;

            volumes.Add(new BitLockerVolume
            {
                Volume = obj.Properties["MountPoint"]?.Value?.ToString(),
                Status = obj.Properties["VolumeStatus"]?.Value?.ToString(),
                EncryptionMethod = obj.Properties["EncryptionMethod"]?.Value?.ToString(),
                ProtectionStatus = obj.Properties["ProtectionStatus"]?.Value?.ToString()
            });
        }

        return volumes;
    }
}
