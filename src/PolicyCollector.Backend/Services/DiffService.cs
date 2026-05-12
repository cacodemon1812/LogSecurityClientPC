using PolicyCollector.Backend.Data.Models;

namespace PolicyCollector.Backend.Services;

public sealed class DiffService
{
    public List<ConfigChange> ComputeDiff(CollectionPayload before, CollectionPayload after)
    {
        var changes = new List<ConfigChange>();
        var changedAt = after.CollectedAt;
        var hostname = after.Host?.Hostname ?? "unknown";

        var flatBefore = FlattenPayload(before);
        var flatAfter  = FlattenPayload(after);

        foreach (var (path, newValue) in flatAfter)
        {
            flatBefore.TryGetValue(path, out var oldValue);
            if (oldValue != newValue)
                changes.Add(new ConfigChange
                {
                    Hostname  = hostname,
                    ChangedAt = changedAt,
                    FieldPath = path,
                    OldValue  = oldValue,
                    NewValue  = newValue
                });
        }

        foreach (var (path, oldValue) in flatBefore)
        {
            if (!flatAfter.ContainsKey(path))
                changes.Add(new ConfigChange
                {
                    Hostname  = hostname,
                    ChangedAt = changedAt,
                    FieldPath = path,
                    OldValue  = oldValue,
                    NewValue  = null
                });
        }

        return changes;
    }

    private static Dictionary<string, string> FlattenPayload(CollectionPayload p)
    {
        var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var pw = p.SecurityPolicy?.PasswordPolicy;
        if (pw is not null)
        {
            r["sec.password.min_length"]      = pw.MinLength.ToString();
            r["sec.password.complexity"]      = pw.ComplexityEnabled.ToString();
            r["sec.password.max_age_days"]    = pw.MaxAgeDays.ToString();
            r["sec.password.lockout_thresh"]  = pw.LockoutThreshold.ToString();
            r["sec.password.lockout_dur_min"] = pw.LockoutDurationMin.ToString();
            r["sec.password.history_count"]   = pw.HistoryCount.ToString();
        }

        var uac = p.SecurityPolicy?.Uac;
        if (uac is not null)
        {
            r["sec.uac.enabled"]              = uac.Enabled.ToString();
            r["sec.uac.consent_prompt_level"] = uac.ConsentPromptLevel.ToString();
            r["sec.uac.secure_desktop"]       = uac.SecureDesktop.ToString();
        }

        var protocols = p.SecurityPolicy?.Tls?.Protocols;
        if (protocols is not null)
        {
            r["sec.tls.ssl20"] = protocols.Ssl20.ToString();
            r["sec.tls.ssl30"] = protocols.Ssl30.ToString();
            r["sec.tls.tls10"] = protocols.Tls10.ToString();
            r["sec.tls.tls11"] = protocols.Tls11.ToString();
            r["sec.tls.tls12"] = protocols.Tls12.ToString();
            r["sec.tls.tls13"] = protocols.Tls13.ToString();
        }

        var rdp = p.SecurityPolicy?.Rdp;
        if (rdp is not null)
        {
            r["sec.rdp.enabled"]      = rdp.Enabled.ToString();
            r["sec.rdp.nla_required"] = rdp.NlaRequired.ToString();
            r["sec.rdp.port"]         = rdp.Port.ToString();
        }

        var audit = p.SecurityPolicy?.AuditPolicy;
        if (audit is not null)
        {
            r["sec.audit.logon_logoff"]       = audit.LogonLogoff ?? "";
            r["sec.audit.account_logon"]      = audit.AccountLogon ?? "";
            r["sec.audit.account_management"] = audit.AccountManagement ?? "";
        }

        var fw = p.Firewall?.Profiles;
        if (fw is not null)
        {
            r["fw.domain.enabled"]  = (fw.TryGetValue("Domain",  out var d)  && d?.Enabled == true).ToString();
            r["fw.private.enabled"] = (fw.TryGetValue("Private", out var pr) && pr?.Enabled == true).ToString();
            r["fw.public.enabled"]  = (fw.TryGetValue("Public",  out var pu) && pu?.Enabled == true).ToString();
        }

        var def = p.Defender;
        if (def is not null)
            r["defender.realtime_protection"] = def.RealTimeProtection?.ToString() ?? "unknown";

        r["gpo.computer_gpos"] = p.Gpo?.ComputerGpos?.Count.ToString() ?? "0";
        r["gpo.user_gpos"]     = p.Gpo?.UserGpos?.Count.ToString() ?? "0";

        return r;
    }
}
