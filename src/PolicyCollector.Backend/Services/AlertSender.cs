using System.Text;
using System.Text.Json;
using PolicyCollector.Backend.Config;
using PolicyCollector.Backend.Data.Models;
using Microsoft.Extensions.Options;

namespace PolicyCollector.Backend.Services;

public sealed class AlertSender
{
    private readonly HttpClient _http;
    private readonly BackendOptions _options;
    private readonly ILogger<AlertSender> _logger;

    public AlertSender(HttpClient http, IOptions<BackendOptions> options, ILogger<AlertSender> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string hostname,
        IReadOnlyList<ViolationEntry> violations,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.AlertWebhookUrl))
            return;

        var payload = new
        {
            alerts = violations.Select(v => new
            {
                labels = new
                {
                    alertname = $"PolicyViolation_{v.RuleId}",
                    severity = v.Severity,
                    hostname,
                    rule_id = v.RuleId
                },
                annotations = new
                {
                    summary = v.Message,
                    description = $"Host: {hostname}\nExpected: {v.Expected}\nActual: {v.Actual}"
                },
                startsAt = DateTimeOffset.UtcNow
            }).ToList()
        };

        try
        {
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(_options.AlertWebhookUrl, content, ct);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Alert sent for {Host}: {Count} violations", hostname, violations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send alert for {Host}", hostname);
        }
    }
}
