using PolicyCollector.Backend.Data.Models;
using PolicyCollector.Backend.Data.Repositories;

namespace PolicyCollector.Backend.Services;

public sealed class ViolationEngine
{
    private readonly PolicyRuleRepository _rules;
    private readonly ILogger<ViolationEngine> _logger;

    public ViolationEngine(PolicyRuleRepository rules, ILogger<ViolationEngine> logger)
    {
        _rules = rules;
        _logger = logger;
    }

    public async Task<List<ViolationEntry>> EvaluateAsync(CollectionPayload payload, CancellationToken ct)
    {
        var enabledRules = await _rules.GetEnabledRulesAsync(ct);
        var violations = new List<ViolationEntry>();
        var hostname = payload.Host?.Hostname ?? "unknown";

        foreach (var rule in enabledRules)
        {
            try
            {
                var violation = rule.RuleId switch
                {
                    "password.min_length"  => CheckPasswordMinLength(payload, rule),
                    "password.complexity"  => CheckPasswordComplexity(payload, rule),
                    "password.max_age"     => CheckPasswordMaxAge(payload, rule),
                    "password.lockout"     => CheckPasswordLockout(payload, rule),
                    "audit.logon"          => CheckAuditLogon(payload, rule),
                    "firewall.disabled"    => CheckFirewallDisabled(payload, rule),
                    "defender.realtime"    => CheckDefenderRealtime(payload, rule),
                    "uac.disabled"         => CheckUacDisabled(payload, rule),
                    "bitlocker.os_volume"  => CheckBitLockerOsVolume(payload, rule),
                    "tls.weak_protocol"    => CheckTlsWeakProtocol(payload, rule),
                    "rdp.nla_disabled"          => CheckRdpNla(payload, rule),
                    "network.risky_port_exposed" => CheckRiskyPortExposed(payload, rule),
                    _                            => null
                };

                if (violation is not null)
                {
                    violation.Hostname = hostname;
                    violations.Add(violation);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Rule {RuleId} evaluation failed for {Host}", rule.RuleId, hostname);
            }
        }

        return violations;
    }

    private static ViolationEntry? CheckPasswordMinLength(CollectionPayload payload, PolicyRule rule)
    {
        var policy = payload.SecurityPolicy?.PasswordPolicy;
        if (policy is null) return null;
        if (policy.MinLength >= 8) return null;

        return Violation(rule, $"Password minimum length is {policy.MinLength}, expected ≥ 8", "≥ 8", policy.MinLength.ToString());
    }

    private static ViolationEntry? CheckPasswordComplexity(CollectionPayload payload, PolicyRule rule)
    {
        var policy = payload.SecurityPolicy?.PasswordPolicy;
        if (policy is null) return null;
        if (policy.ComplexityEnabled) return null;

        return Violation(rule, "Password complexity requirement is disabled", "true", "false");
    }

    private static ViolationEntry? CheckPasswordMaxAge(CollectionPayload payload, PolicyRule rule)
    {
        var policy = payload.SecurityPolicy?.PasswordPolicy;
        if (policy is null) return null;
        if (policy.MaxAgeDays > 0 && policy.MaxAgeDays <= 180) return null;

        return Violation(rule, $"Password max age is {policy.MaxAgeDays} days, expected 1–180", "1–180 days", $"{policy.MaxAgeDays} days");
    }

    private static ViolationEntry? CheckPasswordLockout(CollectionPayload payload, PolicyRule rule)
    {
        var policy = payload.SecurityPolicy?.PasswordPolicy;
        if (policy is null) return null;
        if (policy.LockoutThreshold > 0) return null; // lockout configured → compliant

        return Violation(rule, "Account lockout threshold is 0 (no lockout configured)", "> 0", "0");
    }

    private static ViolationEntry? CheckAuditLogon(CollectionPayload payload, PolicyRule rule)
    {
        var audit = payload.SecurityPolicy?.AuditPolicy;
        if (audit is null) return null;

        // Flag if logon/logoff auditing doesn't include Failure
        var logon = audit.LogonLogoff;
        if (logon is not null &&
            !logon.Contains("Failure", StringComparison.OrdinalIgnoreCase) &&
            !logon.Contains("Success and Failure", StringComparison.OrdinalIgnoreCase))
        {
            return Violation(rule, $"Logon/Logoff audit does not include Failure (current: {logon})", "Success and Failure", logon);
        }

        return null;
    }

    private static ViolationEntry? CheckFirewallDisabled(CollectionPayload payload, PolicyRule rule)
    {
        var fw = payload.Firewall;
        if (fw?.Profiles is null) return null;

        var disabled = new List<string>();
        if (fw.Profiles.TryGetValue("Domain",  out var d)  && d?.Enabled == false) disabled.Add("Domain");
        if (fw.Profiles.TryGetValue("Private", out var pr) && pr?.Enabled == false) disabled.Add("Private");
        if (fw.Profiles.TryGetValue("Public",  out var pu) && pu?.Enabled == false) disabled.Add("Public");

        if (disabled.Count == 0) return null;

        return Violation(rule,
            $"Firewall disabled on profiles: {string.Join(", ", disabled)}",
            "Enabled on all profiles",
            $"Disabled: {string.Join(", ", disabled)}");
    }

    private static ViolationEntry? CheckDefenderRealtime(CollectionPayload payload, PolicyRule rule)
    {
        var defender = payload.Defender;
        if (defender is null) return null;
        if (defender.RealTimeProtection != false) return null; // null = unknown, true = compliant

        return Violation(rule, "Windows Defender real-time protection is disabled", "true", "false");
    }

    private static ViolationEntry? CheckUacDisabled(CollectionPayload payload, PolicyRule rule)
    {
        var uac = payload.SecurityPolicy?.Uac;
        if (uac is null) return null;
        if (uac.Enabled) return null;

        return Violation(rule, "User Account Control (UAC) is disabled", "enabled", "disabled");
    }

    private static ViolationEntry? CheckBitLockerOsVolume(CollectionPayload payload, PolicyRule rule)
    {
        if (payload.BitLocker is null || payload.BitLocker.Count == 0) return null;

        var osVolume = payload.BitLocker.FirstOrDefault(v =>
            v.Volume?.Equals("C:", StringComparison.OrdinalIgnoreCase) == true);

        if (osVolume is null) return null;
        if (osVolume.Status == "FullyEncrypted") return null;

        return Violation(rule,
            $"OS volume C: is not fully encrypted (status: {osVolume.Status})",
            "FullyEncrypted",
            osVolume.Status ?? "Unknown");
    }

    private static ViolationEntry? CheckTlsWeakProtocol(CollectionPayload payload, PolicyRule rule)
    {
        var protocols = payload.SecurityPolicy?.Tls?.Protocols;
        if (protocols is null) return null;

        var issues = new List<string>();
        if (protocols.Ssl20) issues.Add("SSL 2.0 enabled");
        if (protocols.Ssl30) issues.Add("SSL 3.0 enabled");
        if (protocols.Tls10) issues.Add("TLS 1.0 enabled");
        if (protocols.Tls11) issues.Add("TLS 1.1 enabled");

        if (issues.Count == 0) return null;

        return Violation(rule,
            $"Weak protocols active: {string.Join(", ", issues)}",
            "Only TLS 1.2 and TLS 1.3 enabled",
            string.Join(", ", issues));
    }

    private static ViolationEntry? CheckRdpNla(CollectionPayload payload, PolicyRule rule)
    {
        var rdp = payload.SecurityPolicy?.Rdp;
        if (rdp is null) return null;
        if (!rdp.Enabled) return null; // RDP disabled → not applicable
        if (rdp.NlaRequired) return null; // NLA required → compliant

        return Violation(rule,
            "RDP is enabled without Network Level Authentication (NLA)",
            "NlaRequired = true",
            "NlaRequired = false");
    }

    private static ViolationEntry? CheckRiskyPortExposed(CollectionPayload payload, PolicyRule rule)
    {
        var riskyPorts = payload.Firewall?.RiskyPorts;
        if (riskyPorts is null || riskyPorts.Count == 0) return null;

        // Flag ports that are both listening AND have an inbound Allow rule — truly exposed to network
        var exposed = riskyPorts
            .Where(p => p.IsListening && p.HasInboundAllowRule)
            .OrderByDescending(p => p.RiskLevel == "critical" ? 2 : p.RiskLevel == "high" ? 1 : 0)
            .ToList();

        if (exposed.Count == 0) return null;

        var portList = string.Join(", ", exposed.Select(p => $"{p.Port}/{p.Protocol} ({p.Description})"));
        return Violation(rule,
            $"High-risk port(s) exposed via inbound firewall Allow rule: {portList}",
            "No high-risk ports exposed",
            $"{exposed.Count} port(s): {string.Join(", ", exposed.Select(p => p.Port))}");
    }

    private static ViolationEntry Violation(PolicyRule rule, string message, string expected, string actual) =>
        new() { RuleId = rule.RuleId, Severity = rule.Severity, Message = message, Expected = expected, Actual = actual };
}
