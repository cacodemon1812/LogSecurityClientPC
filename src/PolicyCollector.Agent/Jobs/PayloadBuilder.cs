namespace PolicyCollector.Agent.Jobs;

public static class PayloadBuilder
{
    public static CollectionPayload Build(
        DateTimeOffset collectedAt,
        HostInfo? host,
        GpoResult? gpo,
        SecPolicyResult? secPolicy,
        FirewallResult? firewall,
        DefenderResult? defender,
        List<BitLockerVolume>? bitLocker,
        List<AppEntry>? applications,
        List<AppxEntry>? appxPackages,
        List<ServiceEntry>? services,
        List<TaskEntry>? scheduledTasks,
        List<StartupEntry>? startupEntries,
        AdInfo? activeDirectory,
        RegistryAuditResult? registryAudit) =>
        new()
        {
            CollectionId    = Guid.NewGuid().ToString(),
            CollectedAt     = collectedAt,
            Host            = host,
            Gpo             = gpo,
            SecurityPolicy  = secPolicy,
            Firewall        = firewall,
            Defender        = defender,
            BitLocker       = bitLocker,
            Applications    = applications,
            AppxPackages    = appxPackages,
            Services        = services,
            ScheduledTasks  = scheduledTasks,
            StartupEntries  = startupEntries,
            ActiveDirectory = activeDirectory,
            RegistryAudit   = registryAudit
        };
}
