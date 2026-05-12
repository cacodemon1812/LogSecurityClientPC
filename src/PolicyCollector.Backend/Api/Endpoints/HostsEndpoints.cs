using System.Text.Json;
using PolicyCollector.Backend.Api.Models;
using PolicyCollector.Backend.Data.Repositories;

namespace PolicyCollector.Backend.Api.Endpoints;

public static class HostsEndpoints
{
    public static void MapHostsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1");

        group.MapGet("/hosts", GetHosts)
             .Produces<PaginatedResponse<HostSummary>>(200)
             .WithName("GetHosts");

        group.MapGet("/hosts/{hostname}/latest", GetLatest)
             .Produces<CollectionPayload>(200)
             .Produces(404)
             .WithName("GetLatestSnapshot");
    }

    private static async Task<IResult> GetHosts(
        [FromQuery] string? domain,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        [FromQuery] string sort = "last_seen",
        [FromQuery] string order = "desc",
        HostRepository hosts = null!,
        ViolationRepository violations = null!,
        CancellationToken ct = default)
    {
        if (size > 200) size = 200;
        if (page < 1) page = 1;

        var (total, items) = await hosts.GetPagedAsync(
            domain, status, sort, order, page, size, ct);

        var now = DateTimeOffset.UtcNow;
        var summaries = new List<HostSummary>();

        foreach (var h in items)
        {
            var hostStatus = h.LastSeen switch
            {
                null => "unknown",
                var ls when ls > now.AddHours(-2) => "online",
                var ls when ls < now.AddHours(-24) => "offline",
                _ => "stale"
            };

            var violationCount = await violations.CountOpenAsync(h.Hostname, ct);

            summaries.Add(new HostSummary(
                h.Hostname, h.Domain, h.OsVersion, h.AgentVersion,
                h.LastSeen, hostStatus, violationCount));
        }

        return Results.Ok(new PaginatedResponse<HostSummary>(total, page, size, summaries));
    }

    private static async Task<IResult> GetLatest(
        string hostname,
        SnapshotRepository snapshots,
        CancellationToken ct)
    {
        var snapshot = await snapshots.GetLatestAsync(hostname, ct);
        if (snapshot is null) return Results.NotFound();

        var payload = JsonSerializer.Deserialize<CollectionPayload>(
            snapshot.PayloadJson, JsonSerializerOptions.Default);

        return Results.Ok(payload);
    }
}
