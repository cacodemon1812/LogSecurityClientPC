using System.Text.Json;
using PolicyCollector.Backend.Api.Models;
using PolicyCollector.Backend.Data.Repositories;

namespace PolicyCollector.Backend.Api.Endpoints;

public static class DiffEndpoints
{
    public static void MapDiffEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/hosts/{hostname}/diff", GetDiff)
           .Produces<DiffResponse>(200)
           .Produces(404)
           .WithName("GetDiff");
    }

    private static async Task<IResult> GetDiff(
        string hostname,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        SnapshotRepository snapshots,
        ConfigChangeRepository changes,
        CancellationToken ct)
    {
        var toSnapshot = to.HasValue
            ? await snapshots.GetNearestAsync(hostname, to.Value, ct)
            : await snapshots.GetLatestAsync(hostname, ct);

        if (toSnapshot is null) return Results.NotFound();

        var fromSnapshot = from.HasValue
            ? await snapshots.GetNearestAsync(hostname, from.Value, ct)
            : await snapshots.GetPreviousAsync(hostname, toSnapshot.Id, ct);

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
