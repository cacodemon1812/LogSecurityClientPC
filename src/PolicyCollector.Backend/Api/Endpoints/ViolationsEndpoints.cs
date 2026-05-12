using PolicyCollector.Backend.Api.Models;
using PolicyCollector.Backend.Data.Repositories;

namespace PolicyCollector.Backend.Api.Endpoints;

public static class ViolationsEndpoints
{
    public static void MapViolationsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/policy/violations", GetViolations)
           .Produces<PaginatedResponse<ViolationDto>>(200)
           .WithName("GetViolations");
    }

    private static async Task<IResult> GetViolations(
        [FromQuery] string? hostname,
        [FromQuery] string? severity,
        [FromQuery] string? ruleId,
        [FromQuery] bool resolved = false,
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        ViolationRepository violations = null!,
        CancellationToken ct = default)
    {
        if (size > 200) size = 200;
        if (page < 1) page = 1;

        var (total, items) = await violations.GetPagedAsync(
            hostname, severity, ruleId, resolved, page, size, ct);

        var dtos = items.Select(v => new ViolationDto(
            v.Id, v.SnapshotId, v.Hostname, v.DetectedAt,
            v.RuleId, v.Severity, v.Message, v.Expected, v.Actual,
            v.Resolved, v.ResolvedAt)).ToList();

        return Results.Ok(new PaginatedResponse<ViolationDto>(total, page, size, dtos));
    }
}
