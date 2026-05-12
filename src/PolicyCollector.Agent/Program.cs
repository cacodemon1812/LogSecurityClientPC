using PolicyCollector.Agent.Collectors;
using PolicyCollector.Agent.Config;
using PolicyCollector.Agent.Infrastructure;
using PolicyCollector.Agent.Jobs;
using PolicyCollector.Agent.Scheduler;
using PolicyCollector.Agent.Transport;
using Serilog;
using Serilog.Settings.Configuration;

var builder = Host.CreateApplicationBuilder(args);

// Registry config provider (GPO override) — uses NullLogger since DI not ready yet
builder.Configuration.Sources.Add(
    new RegistryConfigProvider(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<RegistryConfigProvider>.Instance));

builder.Services.AddWindowsService(options =>
    options.ServiceName = "PolicyCollectorSvc");

// Configuration
builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection("Agent"));
builder.Services.Configure<TransportOptions>(
    builder.Configuration.GetSection("Transport"));
builder.Services.Configure<LocalQueueOptions>(
    builder.Configuration.GetSection("LocalQueue"));

// Serilog — explicitly pass sink assembly so single-file publish works
// (single-file disables DependencyContext auto-discovery)
var serilogOptions = new ConfigurationReaderOptions(
    typeof(Serilog.FileLoggerConfigurationExtensions).Assembly
);
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration, serilogOptions)
    .CreateLogger();

builder.Services.AddSerilog();

// Infrastructure
builder.Services.AddSingleton<PowerShellRunner>();
builder.Services.AddSingleton<WmiQuery>();
builder.Services.AddSingleton<RegistryReader>();
builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddSingleton<SecretsProvider>();

// Collectors
builder.Services.AddSingleton<ICollector<HostInfo>, HostInfoCollector>();
builder.Services.AddSingleton<ICollector<GpoResult>, GpoCollector>();
builder.Services.AddSingleton<ICollector<SecPolicyResult>, SecurityPolicyCollector>();
builder.Services.AddSingleton<ICollector<FirewallResult>, FirewallCollector>();
builder.Services.AddSingleton<ICollector<DefenderResult>, DefenderCollector>();
builder.Services.AddSingleton<ICollector<List<BitLockerVolume>>, BitLockerCollector>();
builder.Services.AddSingleton<ICollector<List<AppEntry>>, AppInventoryCollector>();
builder.Services.AddSingleton<ICollector<List<AppxEntry>>, AppxCollector>();
builder.Services.AddSingleton<ICollector<List<ServiceEntry>>, ServiceCollector>();
builder.Services.AddSingleton<ICollector<List<TaskEntry>>, ScheduledTaskCollector>();
builder.Services.AddSingleton<ICollector<List<StartupEntry>>, StartupCollector>();

// Jobs and Services
builder.Services.AddSingleton<CollectionJob>();
builder.Services.AddSingleton<LocalQueue>();
builder.Services.AddSingleton<ITransport, HttpTransport>();

// HTTP Client for Transport
builder.Services
    .AddHttpClient<ITransport, HttpTransport>(client =>
    {
        var options = builder.Configuration.GetSection("Transport").Get<TransportOptions>();
        if (options?.TimeoutSeconds > 0)
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    })
    .ConfigurePrimaryHttpMessageHandler(services =>
    {
        var options = services.GetRequiredService<IOptions<TransportOptions>>().Value;
        var logger = services.GetRequiredService<ILogger<MtlsHandler>>();
        return new MtlsHandler(options, logger);
    });

// Background Services
builder.Services.AddHostedService<CollectionScheduler>();
builder.Services.AddHostedService<RetryJob>();

using var host = builder.Build();
await host.RunAsync();
