using System.Diagnostics;
using System.Management.Automation;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class DefenderCollector : ICollector<DefenderResult>
{
    private readonly PowerShellRunner _ps;
    private readonly ILogger<DefenderCollector> _logger;

    public DefenderCollector(PowerShellRunner ps, ILogger<DefenderCollector> logger)
    {
        _ps = ps;
        _logger = logger;
    }

    public string ModuleName => "Defender";

    public async Task<CollectorResult<DefenderResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var prefTask = _ps.RunScriptAsync(
                "Get-MpPreference | Select-Object DisableRealtimeMonitoring,MAPSReporting,SubmitSamplesConsent | ConvertTo-Json -Compress",
                ct);

            var statusTask = _ps.RunScriptAsync(
                "Get-MpComputerStatus | Select-Object AntivirusEnabled,RealTimeProtectionEnabled,AntispywareEnabled,NISEnabled,AntivirusSignatureVersion,AntispywareSignatureVersion | ConvertTo-Json -Compress",
                ct);

            await Task.WhenAll(prefTask, statusTask);

            var result = ParseDefenderResult(prefTask.Result, statusTask.Result);
            return CollectorResult<DefenderResult>.Ok(result, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Defender collection failed (may not be installed)");
            return CollectorResult<DefenderResult>.Fail("Defender collection failed", ex.ToString());
        }
    }

    private DefenderResult ParseDefenderResult(
        IReadOnlyList<PSObject> pref,
        IReadOnlyList<PSObject> status)
    {
        if (status.FirstOrDefault() is not PSObject statusObj)
            return new DefenderResult();

        var avSig = statusObj.Properties["AntivirusSignatureVersion"]?.Value?.ToString();
        var asSig = statusObj.Properties["AntispywareSignatureVersion"]?.Value?.ToString();

        return new DefenderResult
        {
            AntivirusEnabled    = statusObj.Properties["AntivirusEnabled"]?.Value is bool av && av,
            RealTimeProtection  = statusObj.Properties["RealTimeProtectionEnabled"]?.Value is bool rtp && rtp,
            CloudProtection     = statusObj.Properties["NISEnabled"]?.Value is bool nis && nis,
            SignatureVersion    = !string.IsNullOrEmpty(avSig) ? avSig : asSig
        };
    }
}
