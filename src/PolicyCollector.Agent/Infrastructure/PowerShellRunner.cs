using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PolicyCollector.Agent.Infrastructure;

public sealed class PowerShellRunner : IDisposable
{
    private RunspacePool? _pool;
    private readonly ILogger<PowerShellRunner> _logger;
    private readonly object _poolLock = new();
    private readonly object _availabilityLock = new();
    private bool _poolInitFailed;
    private bool _powerShellUnavailable;
    private bool _powerShellUnavailableLogged;

    public PowerShellRunner(ILogger<PowerShellRunner> logger)
    {
        _logger = logger;
        // Defer pool initialization to first use (lazy loading)
    }

    private RunspacePool? EnsurePool()
    {
        if (_poolInitFailed || _powerShellUnavailable)
            return null;

        if (_pool is not null)
            return _pool;

        lock (_poolLock)
        {
            if (_pool is not null)
                return _pool;

            if (_poolInitFailed)
                return null;

            try
            {
                var initialSessionState = InitialSessionState.CreateDefault2();
                _pool = RunspaceFactory.CreateRunspacePool(1, 3, initialSessionState, host: null);
                _pool.Open();
                _logger.LogInformation("PowerShell RunspacePool initialized successfully");
                return _pool;
            }
            catch (Exception ex)
            {
                _poolInitFailed = true;
                MarkPowerShellUnavailable(ex, "PowerShell runtime is unavailable in this service context. PowerShell-based collectors will be skipped.");
                return null;
            }
        }
    }

    private void MarkPowerShellUnavailable(Exception ex, string message)
    {
        lock (_availabilityLock)
        {
            _powerShellUnavailable = true;
            if (_powerShellUnavailableLogged)
                return;

            _logger.LogWarning("{Message} Reason: {Reason}", message, ex.Message);
            _powerShellUnavailableLogged = true;
        }
    }

    private Runspace? CreateFallbackRunspace()
    {
        if (_powerShellUnavailable)
            return null;

        try
        {
            var initialSessionState = InitialSessionState.CreateDefault2();
            var runspace = RunspaceFactory.CreateRunspace(initialSessionState);
            runspace.Open();
            return runspace;
        }
        catch (Exception ex)
        {
            MarkPowerShellUnavailable(ex, "PowerShell runtime is unavailable in this service context. PowerShell-based collectors will be skipped.");
            return null;
        }
    }

    public async Task<IReadOnlyList<PSObject>> RunScriptAsync(
        string script,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            Runspace? runspace = null;
            try
            {
                using var ps = PowerShell.Create();
                var pool = EnsurePool();

                if (pool is not null)
                {
                    ps.RunspacePool = pool;
                }
                else
                {
                    runspace = CreateFallbackRunspace();
                    if (runspace is null)
                        return new List<PSObject>().AsReadOnly();

                    ps.Runspace = runspace;
                }

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
            finally
            {
                runspace?.Dispose();
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
            Runspace? runspace = null;
            try
            {
                using var ps = PowerShell.Create();
                var pool = EnsurePool();

                if (pool is not null)
                {
                    ps.RunspacePool = pool;
                }
                else
                {
                    runspace = CreateFallbackRunspace();
                    if (runspace is null)
                        return new List<PSObject>().AsReadOnly();

                    ps.Runspace = runspace;
                }

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
            finally
            {
                runspace?.Dispose();
            }
        }, ct);
    }

    public void Dispose()
    {
        _pool?.Dispose();
    }
}
