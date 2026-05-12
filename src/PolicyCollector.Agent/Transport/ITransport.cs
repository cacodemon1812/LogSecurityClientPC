namespace PolicyCollector.Agent.Transport;

public interface ITransport
{
    Task<TransportResult> SendAsync(CollectionPayload payload, CancellationToken ct);
}

public sealed class TransportResult
{
    public bool Success { get; init; }
    public bool ShouldRetry { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? IngestionId { get; init; }

    public static TransportResult Ok(string? ingestionId) =>
        new() { Success = true, IngestionId = ingestionId, HttpStatusCode = 202 };

    public static TransportResult ClientError(int statusCode, string message) =>
        new() { Success = false, ShouldRetry = false, HttpStatusCode = statusCode, ErrorMessage = message };

    public static TransportResult ServerError(int statusCode, string message) =>
        new() { Success = false, ShouldRetry = true, HttpStatusCode = statusCode, ErrorMessage = message };

    public static TransportResult NetworkError(string message) =>
        new() { Success = false, ShouldRetry = true, ErrorMessage = message };
}
