using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class WiFiCollector : ICollector<WiFiResult>
{
    private readonly ProcessRunner _process;
    private readonly PowerShellRunner _ps;
    private readonly ILogger<WiFiCollector> _logger;

    public WiFiCollector(ProcessRunner process, PowerShellRunner ps, ILogger<WiFiCollector> logger)
    {
        _process = process;
        _ps = ps;
        _logger = logger;
    }

    public string ModuleName => "WiFi";

    public async Task<CollectorResult<WiFiResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Run netsh and Get-NetConnectionProfile in parallel
            var showProfilesTask = _process.RunAsync(
                "netsh.exe", "wlan show profiles",
                TimeSpan.FromSeconds(10), ct);
            var connectionProfilesTask = _ps.RunScriptAsync(
                "Get-NetConnectionProfile | Select-Object Name, InterfaceAlias, NetworkCategory | ConvertTo-Json -Compress",
                ct);

            await Task.WhenAll(showProfilesTask, connectionProfilesTask);

            var profileNames = ParseProfileNames(showProfilesTask.Result.Stdout);

            if (profileNames.Count == 0 && showProfilesTask.Result.ExitCode != 0)
                _logger.LogWarning(
                    "netsh wlan show profiles returned exit code {ExitCode} — WiFi adapter may be absent or WLAN service disabled",
                    showProfilesTask.Result.ExitCode);

            var activeConnections = ParseConnections(connectionProfilesTask.Result);
            var connectedSsids = new HashSet<string>(
                activeConnections.Select(c => c.NetworkName ?? ""),
                StringComparer.OrdinalIgnoreCase);

            // Get details for each profile sequentially (netsh WLAN API is single-threaded under the hood)
            var profiles = new List<WiFiProfile>();
            foreach (var ssid in profileNames)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // Escape double-quotes in SSID name to avoid shell injection
                    var safeSsid = ssid.Replace("\"", "\\\"");
                    var r = await _process.RunAsync(
                        "netsh.exe",
                        $"wlan show profile name=\"{safeSsid}\" key=clear",
                        TimeSpan.FromSeconds(5), ct);
                    profiles.Add(ParseProfile(ssid, r.Stdout, connectedSsids.Contains(ssid)));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get WiFi profile detail for SSID {Ssid}", ssid);
                    profiles.Add(new WiFiProfile { Ssid = ssid, RiskLevel = "unknown" });
                }
            }

            var insecure = profiles
                .Where(p => p.RiskLevel is "critical" or "high")
                .Select(p => p.Ssid!)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            return CollectorResult<WiFiResult>.Ok(new WiFiResult
            {
                Profiles           = profiles,
                ActiveConnections  = activeConnections,
                HasInsecureProfile = insecure.Count > 0,
                InsecureSsids      = insecure.Count > 0 ? insecure : null
            }, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi collection failed");
            return CollectorResult<WiFiResult>.Fail("WiFi collection failed", ex.ToString());
        }
    }

    // netsh output (English locale):
    //   "    All User Profile     : MyNetwork"
    // Vietnamese locale uses different label but always has "Profile" somewhere in the key part
    private static List<string> ParseProfileNames(string stdout)
    {
        var names = new List<string>();
        foreach (var line in stdout.Split('\n'))
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0) continue;

            var key = line[..colonIdx].Trim();
            if (!key.Contains("Profile", StringComparison.OrdinalIgnoreCase)) continue;

            var value = line[(colonIdx + 1)..].Trim();
            if (!string.IsNullOrEmpty(value))
                names.Add(value);
        }
        return names;
    }

    private static WiFiProfile ParseProfile(string ssid, string stdout, bool isConnected)
    {
        string? auth   = null;
        string? cipher = null;

        foreach (var line in stdout.Split('\n'))
        {
            var trimmed = line.Trim();
            if (auth is null)
            {
                var m = Regex.Match(trimmed, @"^Authentication\s*:\s*(.+)$", RegexOptions.IgnoreCase);
                if (m.Success) auth = m.Groups[1].Value.Trim();
            }
            if (cipher is null)
            {
                // netsh reports "Cipher" on the data-encryption line
                var m = Regex.Match(trimmed, @"^Cipher\s*:\s*(.+)$", RegexOptions.IgnoreCase);
                if (m.Success) cipher = m.Groups[1].Value.Trim();
            }
        }

        return new WiFiProfile
        {
            Ssid           = ssid,
            Authentication = auth,
            Cipher         = cipher,
            RiskLevel      = ClassifyRisk(auth, cipher),
            IsConnected    = isConnected
        };
    }

    // Risk classification based on auth/cipher combination
    // standard: WPA2+CCMP minimum; WEP/Open = critical
    private static string ClassifyRisk(string? auth, string? cipher)
    {
        if (string.IsNullOrWhiteSpace(auth) ||
            auth.Equals("Open", StringComparison.OrdinalIgnoreCase))
            return "critical";

        if (auth.StartsWith("WEP", StringComparison.OrdinalIgnoreCase) ||
            cipher?.StartsWith("WEP", StringComparison.OrdinalIgnoreCase) == true)
            return "critical";

        // WPA v1 (neither WPA2 nor WPA3) — TKIP MIC attack possible
        if (!auth.StartsWith("WPA2", StringComparison.OrdinalIgnoreCase) &&
            !auth.StartsWith("WPA3", StringComparison.OrdinalIgnoreCase) &&
            auth.StartsWith("WPA", StringComparison.OrdinalIgnoreCase))
            return "high";

        // WPA2 but using TKIP instead of CCMP (AES)
        if (auth.StartsWith("WPA2", StringComparison.OrdinalIgnoreCase) &&
            cipher?.Equals("TKIP", StringComparison.OrdinalIgnoreCase) == true)
            return "medium";

        return "safe";
    }

    private static List<WiFiConnection> ParseConnections(IReadOnlyList<PSObject> psObjects)
    {
        var connections = new List<WiFiConnection>();
        try
        {
            var json = psObjects.FirstOrDefault()?.BaseObject as string;
            if (string.IsNullOrEmpty(json)) return connections;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
                foreach (var item in root.EnumerateArray())
                    connections.Add(MapConnection(item));
            else if (root.ValueKind == JsonValueKind.Object)
                connections.Add(MapConnection(root));
        }
        catch { /* best-effort — powershell may be unavailable */ }
        return connections;
    }

    private static WiFiConnection MapConnection(JsonElement e) => new()
    {
        NetworkName    = e.TryGetProperty("Name",            out var n)  ? n.GetString()   : null,
        InterfaceAlias = e.TryGetProperty("InterfaceAlias",  out var ia) ? ia.GetString()  : null,
        Category       = e.TryGetProperty("NetworkCategory", out var nc) ? nc.ToString()   : null,
    };
}
