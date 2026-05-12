using System.Text.Json.Serialization;

namespace PolicyCollector.Backend.Api.Models;

public sealed record IngestResponse(
    [property: JsonPropertyName("ingestion_id")] string IngestionId);

public sealed record ErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("detail")] string? Detail = null);
