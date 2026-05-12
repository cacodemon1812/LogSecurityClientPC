using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PolicyCollector.Agent.Config;

namespace PolicyCollector.Agent.Transport;

public sealed class HttpTransport : ITransport
{
    private readonly HttpClient _http;
    private readonly TransportOptions _options;
    private readonly SecretsProvider _secrets;
    private readonly ILogger<HttpTransport> _logger;

    public HttpTransport(
        HttpClient http,
        IOptions<TransportOptions> options,
        SecretsProvider secrets,
        ILogger<HttpTransport> logger)
    {
        _http = http;
        _options = options.Value;
        _secrets = secrets;
        _logger = logger;
    }

    public async Task<TransportResult> SendAsync(CollectionPayload payload, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            var body = new StringContent(json, Encoding.UTF8, "application/json");

            var hmac = ComputeHmac(json);

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.BackendUrl)
            {
                Content = body
            };

            request.Headers.Add("X-Api-Key", _secrets.GetApiKey() ?? string.Empty);
            request.Headers.Add("X-Agent-Version", GetAgentVersion());

            if (hmac is not null)
                request.Headers.Add("X-Hmac-SHA256", hmac);

            using var response = await _http.SendAsync(request, ct);
            return await HandleResponse(response);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Transport timeout to {Url}", _options.BackendUrl);
            return TransportResult.NetworkError("Timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Transport network error");
            return TransportResult.NetworkError(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during transport");
            return TransportResult.NetworkError(ex.Message);
        }
    }

    private async Task<TransportResult> HandleResponse(HttpResponseMessage response)
    {
        switch ((int)response.StatusCode)
        {
            case 202:
                var body = await response.Content.ReadAsStringAsync();
                var ingestionId = ParseIngestionId(body);
                _logger.LogInformation("Payload accepted, ingestion_id={Id}", ingestionId);
                return TransportResult.Ok(ingestionId);

            case 400:
            case 422:
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Payload rejected ({Status}): {Error}", (int)response.StatusCode, error);
                return TransportResult.ClientError((int)response.StatusCode, error);

            case 401:
            case 403:
                _logger.LogError("Authentication failed ({Status})", (int)response.StatusCode);
                return TransportResult.ClientError((int)response.StatusCode, "Auth failed");

            case 429:
                var retryAfter = response.Headers.RetryAfter?.Delta?.Seconds ?? 60;
                _logger.LogWarning("Rate limited, retry after {Seconds}s", retryAfter);
                return TransportResult.ServerError(429, $"RateLimited:{retryAfter}");

            default:
                _logger.LogWarning("Backend returned {Status}", (int)response.StatusCode);
                return TransportResult.ServerError((int)response.StatusCode, response.StatusCode.ToString());
        }
    }

    private string? ComputeHmac(string json)
    {
        var secret = _secrets.GetHmacSecret();
        if (string.IsNullOrEmpty(secret))
            return null;

        try
        {
            var keyBytes = Convert.FromBase64String(secret);
            var bodyBytes = Encoding.UTF8.GetBytes(json);
            using var hmac = new HMACSHA256(keyBytes);
            return Convert.ToBase64String(hmac.ComputeHash(bodyBytes));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute HMAC");
            return null;
        }
    }

    private static string GetAgentVersion() =>
        typeof(HttpTransport).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static string? ParseIngestionId(string responseBody)
    {
        try
        {
            var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement.GetProperty("ingestion_id").GetString();
        }
        catch
        {
            return null;
        }
    }
}
