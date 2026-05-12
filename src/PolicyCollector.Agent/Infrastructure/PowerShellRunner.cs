using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PolicyCollector.Agent.Infrastructure;

public sealed class PowerShellRunner : IDisposable
{
    private readonly RunspacePool _pool;
    private readonly ILogger<PowerShellRunner> _logger;

    public PowerShellRunner(ILogger<PowerShellRunner> logger)
    {
        _logger = logger;
        _pool = RunspaceFactory.CreateRunspacePool(1, 3);
        _pool.Open();
    }

    public async Task<IReadOnlyList<PSObject>> RunScriptAsync(
        string script,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var ps = PowerShell.Create(RunspaceMode.NewRunspace);
                ps.RunspacePool = _pool;
                ps.AddScript(script);
                var results = ps.Invoke();

                if (ps.HadErrors)
                {
                    foreach (var err in ps.Streams.Error)
                    {
                        _logger.LogWarning("PowerShell error: {Error}", err);
                    }
                }

                return results.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PowerShell script execution failed");
                return new List<PSObject>().AsReadOnly();
            }
        }, ct);
    }

    public async Task<IReadOnlyList<PSObject>> RunCmdletAsync(
        string cmdlet,
        Dictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var ps = PowerShell.Create(RunspaceMode.NewRunspace);
                ps.RunspacePool = _pool;
                ps.AddCommand(cmdlet);

                if (parameters is not null)
                {
                    foreach (var (key, value) in parameters)
                    {
                        ps.AddParameter(key, value);
                    }
                }

                var results = ps.Invoke();

                if (ps.HadErrors)
                {
                    foreach (var err in ps.Streams.Error)
                    {
                        _logger.LogWarning("PowerShell error: {Error}", err);
                    }
                }

                return results.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PowerShell cmdlet execution failed");
                return new List<PSObject>().AsReadOnly();
            }
        }, ct);
    }

    public void Dispose()
    {
        _pool?.Dispose();
    }
}
