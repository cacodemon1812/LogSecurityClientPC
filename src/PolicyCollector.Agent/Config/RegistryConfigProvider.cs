namespace PolicyCollector.Agent.Config;

public sealed class RegistryConfigProvider : ConfigurationProvider, IConfigurationSource
{
    private const string RegistryPath = @"SOFTWARE\Policies\PolicyCollector";
    private readonly ILogger<RegistryConfigProvider> _logger;

    public RegistryConfigProvider(ILogger<RegistryConfigProvider> logger)
    {
        _logger = logger;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public override void Load()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryPath);
            if (key is null)
            {
                _logger.LogDebug("No GPO registry key found at {Path}", RegistryPath);
                return;
            }

            // BackendUrl
            if (key.GetValue("BackendUrl") is string backendUrl && !string.IsNullOrEmpty(backendUrl))
            {
                Data["Transport:BackendUrl"] = backendUrl;
                _logger.LogInformation("Registry override: BackendUrl = {Url}", backendUrl);
            }

            // IntervalMinutes
            if (key.GetValue("IntervalMinutes") is int interval && interval > 0)
            {
                Data["Agent:IntervalMinutes"] = interval.ToString();
                _logger.LogInformation("Registry override: IntervalMinutes = {Interval}", interval);
            }

            // Modules - HKLM\...\PolicyCollector\Modules\
            using var modulesKey = key.OpenSubKey("Modules");
            if (modulesKey is not null)
            {
                foreach (var moduleName in modulesKey.GetValueNames())
                {
                    var enabled = modulesKey.GetValue(moduleName) is int val && val == 1;
                    Data[$"Agent:Modules:{moduleName}"] = enabled.ToString();
                }
                _logger.LogDebug("Registry override: module settings loaded");
            }

            // mTLS settings
            if (key.GetValue("UseMtls") is int mtls)
            {
                Data["Transport:UseMtls"] = (mtls == 1).ToString();
                _logger.LogInformation("Registry override: UseMtls = {Mtls}", mtls == 1);
            }

            if (key.GetValue("ClientCertThumbprint") is string thumbprint && !string.IsNullOrEmpty(thumbprint))
            {
                Data["Transport:ClientCertThumbprint"] = thumbprint;
                _logger.LogInformation("Registry override: ClientCertThumbprint set");
            }

            if (key.GetValue("ClientCertStore") is string certStore && !string.IsNullOrEmpty(certStore))
            {
                Data["Transport:ClientCertStore"] = certStore;
                _logger.LogInformation("Registry override: ClientCertStore = {Store}", certStore);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load registry configuration from {Path}", RegistryPath);
        }
    }
}
