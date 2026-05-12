using System.Diagnostics;
using System.Management.Automation;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class FirewallCollector : ICollector<FirewallResult>
{
    private readonly PowerShellRunner _ps;
    private const int MaxRulesExport = 500;

    public FirewallCollector(PowerShellRunner ps)
    {
        _ps = ps;
    }

    public string ModuleName => "Firewall";

    public async Task<CollectorResult<FirewallResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var profileObjects = await _ps.RunScriptAsync(
                "Get-NetFirewallProfile | Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction | ConvertTo-Json -Compress",
                ct);

            var rulesObjects = await _ps.RunScriptAsync($@"
                $rules = Get-NetFirewallRule | Where-Object {{$_.Enabled -eq $true}} |
                    Select-Object -First {MaxRulesExport} Name,DisplayName,Direction,Action,
                                         Enabled,Profile,Description
                $rules | ConvertTo-Json -Compress", ct);

            var totalCountResult = await _ps.RunScriptAsync(
                "Get-NetFirewallRule | Measure-Object | Select-Object -ExpandProperty Count", ct);

            var result = BuildFirewallResult(profileObjects, rulesObjects, totalCountResult);
            return CollectorResult<FirewallResult>.Ok(result, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return CollectorResult<FirewallResult>.Fail("Firewall collection failed", ex.ToString());
        }
    }

    private FirewallResult BuildFirewallResult(
        IReadOnlyList<PSObject> profiles,
        IReadOnlyList<PSObject> rules,
        IReadOnlyList<PSObject> totalCount)
    {
        var profileDict = new Dictionary<string, FirewallProfile>();
        var enabledCount = 0;

        foreach (var profile in profiles)
        {
            if (profile?.Properties == null) continue;

            var name = profile.Properties["Name"]?.Value as string;
            if (string.IsNullOrEmpty(name)) continue;

            profileDict[name] = new FirewallProfile
            {
                Enabled = profile.Properties["Enabled"]?.Value is bool enabled && enabled,
                Inbound = profile.Properties["DefaultInboundAction"]?.Value?.ToString(),
                Outbound = profile.Properties["DefaultOutboundAction"]?.Value?.ToString()
            };
        }

        var rulesList = new List<FirewallRule>();
        foreach (var rule in rules)
        {
            if (rule?.Properties == null) continue;

            if (rule.Properties["Enabled"]?.Value is bool enabled && enabled)
                enabledCount++;

            rulesList.Add(new FirewallRule
            {
                Name = rule.Properties["Name"]?.Value as string,
                DisplayName = rule.Properties["DisplayName"]?.Value as string,
                Direction = rule.Properties["Direction"]?.Value?.ToString(),
                Action = rule.Properties["Action"]?.Value?.ToString(),
                Enabled = rule.Properties["Enabled"]?.Value is bool e && e,
                Profile = rule.Properties["Profile"]?.Value?.ToString()
            });
        }

        var totalRules = totalCount.FirstOrDefault()?.BaseObject is int count ? count : rulesList.Count;

        return new FirewallResult
        {
            Profiles = profileDict,
            RulesSummary = new FirewallRulesSummary
            {
                Total = totalRules,
                Enabled = enabledCount,
                Inbound = rulesList.Count(r => r.Direction?.Contains("Inbound", StringComparison.OrdinalIgnoreCase) ?? false),
                Outbound = rulesList.Count(r => r.Direction?.Contains("Outbound", StringComparison.OrdinalIgnoreCase) ?? false)
            },
            Rules = rulesList.Take(MaxRulesExport).ToList()
        };
    }
}
