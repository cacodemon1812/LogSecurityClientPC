using System.Text.Json;
using System.Text.RegularExpressions;
using PolicyCollector.Backend.Api.Models;
using PolicyCollector.Backend.Infrastructure;

namespace PolicyCollector.Backend.Api.Endpoints;

public static partial class IngestEndpoint
{
    public static void MapIngestEndpoint(this WebApplication app)
    {
        app.MapPost("/api/v1/ingest", HandleIngest)
           .RequireRateLimiting("ingest")
           .Produces<IngestResponse>(202)
           .Produces<ErrorResponse>(400)
           .Produces<ErrorResponse>(401)
           .Produces<ErrorResponse>(422)
           .Produces<ErrorResponse>(503)
           .WithName("Ingest");
    }

    private static async Task<IResult> HandleIngest(
        HttpRequest request,
        RedisQueue queue,
        IOptions<BackendOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Ingest");
        if (!request.HasJsonContentType())
            return Results.BadRequest(new ErrorResponse("Content-Type must be application/json"));

        if (request.ContentLength > 10 * 1024 * 1024)
            return Results.BadRequest(new ErrorResponse("Payload exceeds 10MB limit"));

        CollectionPayload? payload;
        try
        {
            payload = await request.ReadFromJsonAsync<CollectionPayload>(
                JsonSerializerOptions.Default, ct);
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(new ErrorResponse($"Invalid JSON: {ex.Message}"));
        }

        if (payload is null)
            return Results.BadRequest(new ErrorResponse("Empty payload"));

        if (!IsSchemaVersionSupported(payload.SchemaVersion, options.Value.SupportedSchemaVersion))
            return Results.UnprocessableEntity(new ErrorResponse(
                "Schema version not supported",
                $"Supported: {options.Value.SupportedSchemaVersion}"));

        var validationError = ValidatePayload(payload);
        if (validationError is not null)
            return Results.BadRequest(new ErrorResponse(validationError));

        var ingestionId = Guid.NewGuid().ToString();
        try
        {
            await queue.EnqueueAsync("ingest:queue", payload, ingestionId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue payload");
            return Results.StatusCode(503);
        }

        logger.LogInformation("Ingested payload from {Hostname}, collection_id={Id}, ingestion_id={IngestionId}",
            payload.Host?.Hostname, payload.CollectionId, ingestionId);

        return Results.Accepted(value: new IngestResponse(ingestionId));
    }

    private static bool IsSchemaVersionSupported(string version, string supported)
    {
        if (!Version.TryParse(version, out var v))
            return false;

        if (!Version.TryParse(supported, out var supportedVer))
            return false;

        return v.Major == supportedVer.Major;
    }

    private static string? ValidatePayload(CollectionPayload payload)
    {
        if (string.IsNullOrEmpty(payload.CollectionId))
            return "collection_id is required";

        if (!Guid.TryParse(payload.CollectionId, out _))
            return "collection_id must be a valid UUID";

        if (payload.CollectedAt == default)
            return "collected_at is required";

        if (payload.CollectedAt > DateTimeOffset.UtcNow.AddHours(24))
            return "collected_at cannot be more than 24h in the future";

        if (payload.CollectedAt < DateTimeOffset.UtcNow.AddDays(-7))
            return "collected_at is too old (> 7 days)";

        if (payload.Host is null)
            return "host is required";

        if (string.IsNullOrWhiteSpace(payload.Host.Hostname))
            return "host.hostname is required";

        if (!HostnameRegex().IsMatch(payload.Host.Hostname))
            return "host.hostname contains invalid characters";

        return null;
    }

    [GeneratedRegex(@"^[a-zA-Z0-9\-\.]{1,255}$")]
    private static partial Regex HostnameRegex();
}
