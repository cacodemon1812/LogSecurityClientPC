using System.Diagnostics;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class DefenderCollector : ICollector<DefenderResult>
{
    private readonly WmiQuery _wmi;
    private readonly ILogger<DefenderCollector> _logger;

    public DefenderCollector(WmiQuery wmi, ILogger<DefenderCollector> logger)
    {
        _wmi = wmi;
        _logger = logger;
    }

    public string ModuleName => "Defender";

    public async Task<CollectorResult<DefenderResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var statusTask = _wmi.QueryAsync(
                "MSFT_MpComputerStatus",
                properties: new[]
                {
                    "AntivirusEnabled",
                    "RealTimeProtectionEnabled",
                    "NISEnabled",
                    "AntivirusSignatureVersion",
                    "AntispywareSignatureVersion"
                },
                namespacePath: @"root\Microsoft\Windows\Defender",
                ct: ct);

            var prefTask = _wmi.QueryAsync(
                "MSFT_MpPreference",
                properties: new[]
                {
                    "MAPSReporting"
                },
                namespacePath: @"root\Microsoft\Windows\Defender",
                ct: ct);

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
        IReadOnlyList<IReadOnlyDictionary<string, object?>> pref,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> status)
    {
        var statusObj = status.FirstOrDefault();
        if (statusObj is null)
            return new DefenderResult();

        var prefObj = pref.FirstOrDefault();

        var avSig = statusObj.TryGetValue("AntivirusSignatureVersion", out var avSigObj)
            ? avSigObj?.ToString()
            : null;
        var asSig = statusObj.TryGetValue("AntispywareSignatureVersion", out var asSigObj)
            ? asSigObj?.ToString()
            : null;

        bool? cloudProtection = null;
        if (prefObj is not null && prefObj.TryGetValue("MAPSReporting", out var mapsObj))
        {
            if (mapsObj is int mapsInt)
                cloudProtection = mapsInt > 0;
            else if (mapsObj is long mapsLong)
                cloudProtection = mapsLong > 0;
            else if (int.TryParse(mapsObj?.ToString(), out var parsed))
                cloudProtection = parsed > 0;
        }

        bool? antivirusEnabled = null;
        if (statusObj.TryGetValue("AntivirusEnabled", out var avObj) && avObj is bool av)
            antivirusEnabled = av;

        bool? realTimeProtection = null;
        if (statusObj.TryGetValue("RealTimeProtectionEnabled", out var rtpObj) && rtpObj is bool rtp)
            realTimeProtection = rtp;

        // Fallback to NIS state if MAPSReporting is missing.
        if (cloudProtection is null && statusObj.TryGetValue("NISEnabled", out var nisObj) && nisObj is bool nis)
            cloudProtection = nis;

        return new DefenderResult
        {
            AntivirusEnabled = antivirusEnabled,
            RealTimeProtection = realTimeProtection,
            CloudProtection = cloudProtection,
            SignatureVersion = !string.IsNullOrEmpty(avSig) ? avSig : asSig
        };
    }
}
