using System.Diagnostics;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class LocalAccountCollector : ICollector<LocalAccountsResult>
{
    private readonly WmiQuery _wmi;
    private readonly ProcessRunner _process;
    private readonly ILogger<LocalAccountCollector> _logger;

    public LocalAccountCollector(WmiQuery wmi, ProcessRunner process, ILogger<LocalAccountCollector> logger)
    {
        _wmi = wmi;
        _process = process;
        _logger = logger;
    }

    public string ModuleName => "LocalAccounts";

    public async Task<CollectorResult<LocalAccountsResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var accountsTask = _wmi.QueryAsync(
                "Win32_UserAccount",
                condition: "LocalAccount=True",
                properties: ["Name", "SID", "Disabled", "PasswordExpires",
                             "Description", "AccountType"],
                ct: ct);

            var adminsTask = _process.RunAsync(
                "net.exe",
                "localgroup Administrators",
                TimeSpan.FromSeconds(10), ct);

            await Task.WhenAll(accountsTask, adminsTask);

            var accounts = accountsTask.Result.Select(r => new LocalAccount
            {
                Name            = r.TryGetValue("Name", out var n)     ? n?.ToString() : null,
                Sid             = r.TryGetValue("SID", out var sid)    ? sid?.ToString() : null,
                Enabled         = r.TryGetValue("Disabled", out var d) && d is false,
                PasswordExpires = r.TryGetValue("PasswordExpires", out var pe) && pe is true,
                Description     = r.TryGetValue("Description", out var desc) ? desc?.ToString() : null,
                IsBuiltinAdmin  = r.TryGetValue("AccountType", out var at)
                                  && Convert.ToUInt32(at ?? 0) == 512
            }).ToList();

            var admins = ParseAdminGroupOutput(adminsTask.Result.Stdout);

            return CollectorResult<LocalAccountsResult>.Ok(
                new LocalAccountsResult { Accounts = accounts, Administrators = admins },
                sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LocalAccounts collection failed");
            return CollectorResult<LocalAccountsResult>.Fail("LocalAccounts collection failed", ex.ToString());
        }
    }

    private static List<LocalGroupMember> ParseAdminGroupOutput(string output)
    {
        var members = new List<LocalGroupMember>();
        var lines = output.Split('\n');

        // Output format: header, separator line, members..., final "The command completed successfully." line
        var inMembers = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("---", StringComparison.Ordinal))
            {
                inMembers = true;
                continue;
            }

            if (!inMembers) continue;
            if (line.StartsWith("The command", StringComparison.OrdinalIgnoreCase)) break;

            members.Add(new LocalGroupMember
            {
                Name     = line,
                IsDomain = line.Contains('\\'),
                Type     = line.Contains('\\') ? "Domain" : "Local"
            });
        }

        return members;
    }
}
