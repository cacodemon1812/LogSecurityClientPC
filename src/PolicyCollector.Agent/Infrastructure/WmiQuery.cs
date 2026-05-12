using System.Management;

namespace PolicyCollector.Agent.Infrastructure;

public sealed class WmiQuery : IDisposable
{
    private readonly ILogger<WmiQuery> _logger;

    public WmiQuery(ILogger<WmiQuery> logger) => _logger = logger;

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string wmiClass,
        string? condition = null,
        string[]? properties = null,
        string namespacePath = @"root\cimv2",
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var scope = new ManagementScope(namespacePath);
                scope.Connect();

                var query = new SelectQuery(wmiClass, condition, properties);
                using var searcher = new ManagementObjectSearcher(scope, query);
                var results = new List<IReadOnlyDictionary<string, object?>>();

                foreach (var obj in searcher.Get())
                {
                    if (ct.IsCancellationRequested) break;

                    var dict = new Dictionary<string, object?>();
                    foreach (var prop in obj.Properties)
                    {
                        dict[prop.Name] = prop.Value;
                    }
                    results.Add(dict);
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WMI query failed for {Class}", wmiClass);
                return new List<IReadOnlyDictionary<string, object?>>();
            }
        }, ct);
    }

    public void Dispose()
    {
    }
}
