using System.Diagnostics;
using System.Text.Json;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class FirewallCollector : ICollector<FirewallResult>
{
    private readonly PowerShellRunner _ps;
    private readonly ILogger<FirewallCollector> _logger;
    private const int MaxRulesExport = 500;

    // High-risk ports that should be monitored — port → (riskLevel, description)
    private static readonly IReadOnlyDictionary<int, (string Level, string Description)> RiskyPortDefs =
        new Dictionary<int, (string, string)>
        {
            [21]    = ("high",     "FTP – cleartext credentials"),
            [22]    = ("medium",   "SSH – brute force risk on workstations"),
            [23]    = ("critical", "Telnet – cleartext protocol"),
            [25]    = ("medium",   "SMTP – spam relay risk"),
            [135]   = ("high",     "RPC/DCOM – lateral movement vector"),
            [137]   = ("high",     "NetBIOS Name Service – NBNS poisoning"),
            [138]   = ("high",     "NetBIOS Datagram – info leak"),
            [139]   = ("high",     "NetBIOS Session – SMB legacy / Pass-the-Hash"),
            [445]   = ("critical", "SMB – EternalBlue / ransomware / lateral movement"),
            [1433]  = ("high",     "SQL Server – direct database access"),
            [1434]  = ("high",     "SQL Server Browser – instance enumeration"),
            [3306]  = ("high",     "MySQL – direct database access"),
            [3389]  = ("high",     "RDP – brute force / BlueKeep"),
            [4444]  = ("critical", "Metasploit default listener"),
            [5900]  = ("high",     "VNC – unencrypted remote desktop"),
            [5985]  = ("high",     "WinRM HTTP – PowerShell remoting"),
            [5986]  = ("medium",   "WinRM HTTPS – PowerShell remoting"),
            [6667]  = ("critical", "IRC – C2 communication indicator"),
            [8080]  = ("medium",   "HTTP alternate/proxy – often misconfigured"),
            [9200]  = ("high",     "Elasticsearch – typically unauthenticated"),
            [27017] = ("high",     "MongoDB – typically unauthenticated"),
        };

    public FirewallCollector(PowerShellRunner ps, ILogger<FirewallCollector> logger)
    {
        _ps = ps;
        _logger = logger;
    }

    public string ModuleName => "Firewall";

    public async Task<CollectorResult<FirewallResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Profiles: no ConvertTo-Json so each PSObject is a real PSCustomObject with properties
            var profilesTask = _ps.RunScriptAsync(
                "Get-NetFirewallProfile | Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction",
                ct);

            // Rules WITH port/protocol/program — pre-build hashtable lookups then join; result is JSON
            var rulesTask = _ps.RunScriptAsync($@"
                $portFilters = @{{}}
                Get-NetFirewallPortFilter | ForEach-Object {{ $portFilters[$_.InstanceID] = $_ }}
                $appFilters = @{{}}
                Get-NetFirewallApplicationFilter | ForEach-Object {{ $appFilters[$_.InstanceID] = $_ }}
                $rules = Get-NetFirewallRule | Where-Object {{ $_.Enabled -eq $true }} |
                    Select-Object -First {MaxRulesExport} | ForEach-Object {{
                        $pf = $portFilters[$_.Name]
                        $af = $appFilters[$_.Name]
                        [PSCustomObject]@{{
                            Name        = $_.Name
                            DisplayName = $_.DisplayName
                            Direction   = $_.Direction.ToString()
                            Action      = $_.Action.ToString()
                            Enabled     = [bool]$_.Enabled
                            Profile     = $_.Profile.ToString()
                            Protocol    = $pf.Protocol
                            LocalPort   = if ($pf.LocalPort)  {{ ($pf.LocalPort  | Where-Object {{ $_ }}) -join ',' }} else {{ $null }}
                            RemotePort  = if ($pf.RemotePort) {{ ($pf.RemotePort | Where-Object {{ $_ }}) -join ',' }} else {{ $null }}
                            Program     = $af.Program
                        }}
                    }}
                if ($rules) {{ $rules | ConvertTo-Json -Compress -Depth 2 }} else {{ '[]' }}",
                ct);

            // Total rule count
            var totalCountTask = _ps.RunScriptAsync(
                "Get-NetFirewallRule | Measure-Object | Select-Object -ExpandProperty Count",
                ct);

            // Listening TCP and UDP ports with owning process
            var listeningTask = _ps.RunScriptAsync(@"
                $procs = @{}
                Get-Process | ForEach-Object { $procs[[int]$_.Id] = $_.Name }
                $tcp = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | ForEach-Object {
                    [PSCustomObject]@{
                        Protocol    = 'TCP'
                        Address     = $_.LocalAddress
                        Port        = $_.LocalPort
                        Pid         = $_.OwningProcess
                        ProcessName = $procs[[int]$_.OwningProcess]
                    }
                }
                $udp = Get-NetUDPEndpoint -ErrorAction SilentlyContinue | ForEach-Object {
                    [PSCustomObject]@{
                        Protocol    = 'UDP'
                        Address     = $_.LocalAddress
                        Port        = $_.LocalPort
                        Pid         = $_.OwningProcess
                        ProcessName = $procs[[int]$_.OwningProcess]
                    }
                }
                $all = @($tcp) + @($udp) | Sort-Object Port
                if ($all) { $all | ConvertTo-Json -Compress -Depth 2 } else { '[]' }",
                ct);

            await Task.WhenAll(profilesTask, rulesTask, totalCountTask, listeningTask);

            var profiles       = ParseProfiles(profilesTask.Result);
            var rules          = ParseRulesJson(rulesTask.Result);
            var listeningPorts = ParseListeningJson(listeningTask.Result);
            var totalRules     = totalCountTask.Result.FirstOrDefault()?.BaseObject is int n ? n : rules.Count;

            var enabledCount  = rules.Count(r => r.Enabled);
            var inboundCount  = rules.Count(r => string.Equals(r.Direction, "Inbound",  StringComparison.OrdinalIgnoreCase));
            var outboundCount = rules.Count(r => string.Equals(r.Direction, "Outbound", StringComparison.OrdinalIgnoreCase));

            var riskyPorts = BuildRiskyPorts(listeningPorts, rules);

            var result = new FirewallResult
            {
                Profiles = profiles,
                RulesSummary = new FirewallRulesSummary
                {
                    Total    = totalRules,
                    Enabled  = enabledCount,
                    Inbound  = inboundCount,
                    Outbound = outboundCount,
                },
                Rules          = rules,
                ListeningPorts = listeningPorts,
                RiskyPorts     = riskyPorts,
            };

            return CollectorResult<FirewallResult>.Ok(result, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firewall collection failed");
            return CollectorResult<FirewallResult>.Fail("Firewall collection failed", ex.ToString());
        }
    }

    // Profiles come back as real PSCustomObjects (no ConvertTo-Json), so Properties access works
    private static Dictionary<string, FirewallProfile> ParseProfiles(IReadOnlyList<PSObject> objects)
    {
        var dict = new Dictionary<string, FirewallProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in objects)
        {
            if (obj?.Properties == null) continue;
            var name = obj.Properties["Name"]?.Value as string;
            if (string.IsNullOrEmpty(name)) continue;

            dict[name] = new FirewallProfile
            {
                Enabled  = obj.Properties["Enabled"]?.Value is bool e && e,
                Inbound  = obj.Properties["DefaultInboundAction"]?.Value?.ToString(),
                Outbound = obj.Properties["DefaultOutboundAction"]?.Value?.ToString(),
            };
        }
        return dict;
    }

    // Rules come back as a JSON string (ConvertTo-Json used in script for complex joins)
    private List<FirewallRule> ParseRulesJson(IReadOnlyList<PSObject> objects)
    {
        var json = objects.FirstOrDefault()?.BaseObject as string ?? "[]";
        if (!json.TrimStart().StartsWith('[')) json = $"[{json}]"; // single-object case → array

        try
        {
            using var doc = JsonDocument.Parse(json);
            var rules = new List<FirewallRule>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                rules.Add(new FirewallRule
                {
                    Name        = el.TryGetStr("Name"),
                    DisplayName = el.TryGetStr("DisplayName"),
                    Direction   = el.TryGetStr("Direction"),
                    Action      = el.TryGetStr("Action"),
                    Enabled     = el.TryGetBool("Enabled") ?? false,
                    Profile     = el.TryGetStr("Profile"),
                    Protocol    = NullIfAny(el.TryGetStr("Protocol")),
                    LocalPort   = NullIfAny(el.TryGetStr("LocalPort")),
                    RemotePort  = NullIfAny(el.TryGetStr("RemotePort")),
                    Program     = NullIfSystem(el.TryGetStr("Program")),
                });
            }
            return rules;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse firewall rules JSON");
            return [];
        }
    }

    private List<ListeningPort> ParseListeningJson(IReadOnlyList<PSObject> objects)
    {
        var json = objects.FirstOrDefault()?.BaseObject as string ?? "[]";
        if (!json.TrimStart().StartsWith('[')) json = $"[{json}]";

        try
        {
            using var doc = JsonDocument.Parse(json);
            var ports = new List<ListeningPort>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                ports.Add(new ListeningPort
                {
                    Protocol    = el.TryGetStr("Protocol"),
                    Address     = el.TryGetStr("Address"),
                    Port        = el.TryGetInt("Port") ?? 0,
                    Pid         = el.TryGetInt("Pid"),
                    ProcessName = el.TryGetStr("ProcessName"),
                });
            }
            // deduplicate: same port+protocol, prefer 0.0.0.0 over loopback
            return ports
                .GroupBy(p => (p.Protocol, p.Port))
                .Select(g => g.OrderBy(p => IsLoopback(p.Address) ? 1 : 0).First())
                .OrderBy(p => p.Port)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse listening ports JSON");
            return [];
        }
    }

    private static List<RiskyPort> BuildRiskyPorts(
        IReadOnlyList<ListeningPort> listening,
        IReadOnlyList<FirewallRule> rules)
    {
        // Build a set of inbound-Allow rule ports for quick lookup
        var inboundAllowPorts = new HashSet<int>();
        foreach (var rule in rules)
        {
            if (!string.Equals(rule.Direction, "Inbound",  StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(rule.Action,    "Allow",    StringComparison.OrdinalIgnoreCase)) continue;

            // "Any" or empty local port means all ports are allowed
            if (string.IsNullOrEmpty(rule.LocalPort) || rule.LocalPort == "Any")
            {
                // broad allow-all rule — mark all risky ports as having an allow rule
                foreach (var rp in RiskyPortDefs.Keys) inboundAllowPorts.Add(rp);
                break;
            }

            foreach (var part in rule.LocalPort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var p)) inboundAllowPorts.Add(p);
            }
        }

        var listeningByPort = listening
            .GroupBy(l => l.Port)
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<RiskyPort>();
        foreach (var (port, (level, desc)) in RiskyPortDefs)
        {
            var isListening = listeningByPort.TryGetValue(port, out var lp);
            var hasRule     = inboundAllowPorts.Contains(port);

            if (!isListening && !hasRule) continue; // not relevant to this endpoint

            result.Add(new RiskyPort
            {
                Port                = port,
                Protocol            = lp?.Protocol ?? "TCP",
                RiskLevel           = level,
                Description         = desc,
                IsListening         = isListening,
                HasInboundAllowRule = hasRule,
                ProcessName         = lp?.ProcessName,
            });
        }

        return [.. result.OrderByDescending(r => RiskOrder(r.RiskLevel)).ThenBy(r => r.Port)];
    }

    private static int RiskOrder(string level) => level switch
    {
        "critical" => 3, "high" => 2, "medium" => 1, _ => 0
    };

    private static bool IsLoopback(string? addr) =>
        addr is "127.0.0.1" or "::1" or "0:0:0:0:0:0:0:1";

    private static string? NullIfAny(string? v) =>
        v is null or "Any" or "NotConfigured" or "" ? null : v;

    private static string? NullIfSystem(string? v) =>
        v is null or "System" or "Any" or "" ? null : v;
}

// Extension helpers for JsonElement to reduce boilerplate
internal static class JsonElementExtensions
{
    internal static string? TryGetStr(this JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }

    internal static bool? TryGetBool(this JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True  => true,
            JsonValueKind.False => false,
            _                   => null,
        };
    }

    internal static int? TryGetInt(this JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i)) return i;
        return null;
    }
}
