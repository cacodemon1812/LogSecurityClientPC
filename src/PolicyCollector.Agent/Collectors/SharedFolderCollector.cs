using System.Diagnostics;
using System.Management.Automation;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class SharedFolderCollector : ICollector<SharedFoldersResult>
{
    private readonly WmiQuery _wmi;
    private readonly PowerShellRunner _ps;
    private readonly ILogger<SharedFolderCollector> _logger;

    public SharedFolderCollector(WmiQuery wmi, PowerShellRunner ps, ILogger<SharedFolderCollector> logger)
    {
        _wmi = wmi;
        _ps = ps;
        _logger = logger;
    }

    public string ModuleName => "SharedFolders";

    public async Task<CollectorResult<SharedFoldersResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var sharesTask = _wmi.QueryAsync(
                "Win32_Share",
                properties: ["Name", "Path", "Description", "Type", "MaximumAllowed"],
                ct: ct);

            var accessTask = _ps.RunScriptAsync(
                "Get-SmbShareAccess -ErrorAction SilentlyContinue | " +
                "Select-Object Name, AccountName, AccessControlType, AccessRight",
                ct);

            await Task.WhenAll(sharesTask, accessTask);

            // Build access lookup: share name → list of access entries
            var accessMap = new Dictionary<string, List<ShareAccess>>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in accessTask.Result)
            {
                var shareName = obj.Properties["Name"]?.Value?.ToString();
                if (string.IsNullOrEmpty(shareName)) continue;

                if (!accessMap.TryGetValue(shareName, out var list))
                {
                    list = [];
                    accessMap[shareName] = list;
                }

                list.Add(new ShareAccess
                {
                    Account           = obj.Properties["AccountName"]?.Value?.ToString(),
                    AccessControlType = obj.Properties["AccessControlType"]?.Value?.ToString(),
                    AccessRight       = obj.Properties["AccessRight"]?.Value?.ToString()
                });
            }

            var shares = sharesTask.Result.Select(r =>
            {
                var name = r.TryGetValue("Name", out var n) ? n?.ToString() : null;
                return new SharedFolder
                {
                    Name           = name,
                    Path           = r.TryGetValue("Path", out var p) ? p?.ToString() : null,
                    Description    = r.TryGetValue("Description", out var d) ? d?.ToString() : null,
                    Type           = r.TryGetValue("Type", out var t) ? Convert.ToUInt32(t ?? 0u) : 0u,
                    MaxConnections = r.TryGetValue("MaximumAllowed", out var mc) ? Convert.ToUInt32(mc ?? 0u) : 0u,
                    Access         = name is not null && accessMap.TryGetValue(name, out var acc) ? acc : []
                };
            }).ToList();

            return CollectorResult<SharedFoldersResult>.Ok(
                new SharedFoldersResult { Shares = shares }, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SharedFolders collection failed");
            return CollectorResult<SharedFoldersResult>.Fail("SharedFolders collection failed", ex.ToString());
        }
    }
}
