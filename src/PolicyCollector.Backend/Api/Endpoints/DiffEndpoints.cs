using System.Text.Json;
using PolicyCollector.Backend.Api.Models;
using PolicyCollector.Backend.Data.Repositories;

namespace PolicyCollector.Backend.Api.Endpoints;

public static class DiffEndpoints
{
    public static void MapDiffEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/hosts/{hostId:guid}/diff", GetDiff)
           .Produces<DiffResponse>(200)
           .Produces(404)
           .WithName("GetDiff");
    }

    private static async Task<IResult> GetDiff(
        Guid hostId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        SnapshotRepository snapshots,
        ConfigChangeRepository changes,
        CancellationToken ct)
    {
        var toSnapshot = to.HasValue
            ? await snapshots.GetNearestByHostIdAsync(hostId, to.Value, ct)
            : await snapshots.GetLatestByHostIdAsync(hostId, ct);

        if (toSnapshot is null) return Results.NotFound();

        var hostname = toSnapshot.Hostname; // hostname used internally for config_changes lookup

        var fromSnapshot = from.HasValue
            ? await snapshots.GetNearestByHostIdAsync(hostId, from.Value, ct)
            : await snapshots.GetPreviousByHostIdAsync(hostId, toSnapshot.Id, ct);

        if (fromSnapshot is null)
            return Results.Ok(new DiffResponse(hostname,
                null, toSnapshot.Id, null, toSnapshot.CollectedAt, []));

        var changeList = await changes.GetBetweenSnapshotsAsync(
            hostname, fromSnapshot.Id, toSnapshot.Id, ct);

        return Results.Ok(new DiffResponse(
            hostname,
            fromSnapshot.Id,
            toSnapshot.Id,
            fromSnapshot.CollectedAt,
            toSnapshot.CollectedAt,
            changeList));
    }
}
