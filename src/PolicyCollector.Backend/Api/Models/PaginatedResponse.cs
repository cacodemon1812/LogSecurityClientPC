namespace PolicyCollector.Backend.Api.Models;

public sealed record PaginatedResponse<T>(
    int Total,
    int Page,
    int Size,
    IReadOnlyList<T> Items);
