using PolicyCollector.Backend.Api.Models;
using PolicyCollector.Backend.Data.Repositories;

namespace PolicyCollector.Backend.Api.Endpoints;

public static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/apps/inventory", GetInventory)
           .Produces<PaginatedResponse<AppInventoryDto>>(200)
           .WithName("GetAppInventory");
    }

    private static async Task<IResult> GetInventory(
        [FromQuery] string? name,
        [FromQuery] string? publisher,
        [FromQuery] string? hostname,
        [FromQuery] int page = 1,
        [FromQuery] int size = 100,
        AppInventoryRepository inventory = null!,
        CancellationToken ct = default)
    {
        if (size > 500) size = 500;
        if (page < 1) page = 1;

        var (total, items) = await inventory.GetPagedAsync(
            name, publisher, hostname, page, size, ct);

        return Results.Ok(new PaginatedResponse<AppInventoryDto>(total, page, size, items));
    }
}
