# P3 — Agent: Transport + LocalQueue + Scheduler

**Phase:** 1 | **Phụ thuộc:** P1 | **Output:** Pipeline đầy đủ Agent → Backend hoạt động

## Mục tiêu

Implement transport layer (HTTP + HMAC + retry), SQLite offline queue, và scheduler. Sau P3, Agent có thể gửi payload thực tế và tự retry khi mất mạng.

## Files cần tạo

```
src/PolicyCollector.Agent/
  Transport/
    ITransport.cs
    HttpTransport.cs
    LocalQueue.cs
  Scheduler/
    CollectionScheduler.cs
  Jobs/
    RetryJob.cs

tests/PolicyCollector.Agent.Tests/
  Transport/
    HttpTransportTests.cs
    LocalQueueTests.cs
```

---

## Chi tiết từng file

### [FILE] `Transport/ITransport.cs`

```csharp
namespace PolicyCollector.Agent.Transport;

public interface ITransport
{
    // Gửi payload lên Backend
    // Trả về: Success=true nếu server nhận (2xx)
    //         Success=false + ShouldRetry=false nếu 4xx (không retry)
    //         Success=false + ShouldRetry=true nếu 5xx hoặc network error
    Task<TransportResult> SendAsync(CollectionPayload payload, CancellationToken ct);
}

public sealed class TransportResult
{
    public bool Success { get; init; }
    public bool ShouldRetry { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? IngestionId { get; init; }  // UUID từ Backend 202 response
}
```

---

### [FILE] `Transport/HttpTransport.cs`

```csharp
namespace PolicyCollector.Agent.Transport;

public sealed class HttpTransport : ITransport
{
    private readonly HttpClient _http;
    private readonly TransportOptions _options;
    private readonly SecretsProvider _secrets;
    private readonly ILogger<HttpTransport> _logger;

    public async Task<TransportResult> SendAsync(CollectionPayload payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonContext.Default.CollectionPayload);
        var body = new StringContent(json, Encoding.UTF8, "application/json");

        // Tính HMAC trước khi gửi
        var hmac = ComputeHmac(json);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.BackendUrl);
        request.Content = body;
        request.Headers.Add("X-Api-Key", _secrets.GetApiKey() ?? string.Empty);
        request.Headers.Add("X-Agent-Version", GetAgentVersion());
        if (hmac is not null)
            request.Headers.Add("X-Hmac-SHA256", hmac);

        try
        {
            using var response = await _http.SendAsync(request, ct);
            return await HandleResponse(response);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout (không phải cancel từ ngoài)
            _logger.LogWarning("Transport timeout to {Url}", _options.BackendUrl);
            return new TransportResult { Success = false, ShouldRetry = true, ErrorMessage = "Timeout" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Transport network error: {Message}", ex.Message);
            return new TransportResult { Success = false, ShouldRetry = true, ErrorMessage = ex.Message };
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
                return new TransportResult { Success = true, IngestionId = ingestionId,
                    HttpStatusCode = 202 };

            case 400:
            case 422:
                // Schema mismatch hoặc validation error — KHÔNG retry
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Payload rejected ({Status}): {Error}",
                    (int)response.StatusCode, error);
                return new TransportResult { Success = false, ShouldRetry = false,
                    HttpStatusCode = (int)response.StatusCode, ErrorMessage = error };

            case 401:
            case 403:
                _logger.LogError("Authentication failed ({Status})", (int)response.StatusCode);
                return new TransportResult { Success = false, ShouldRetry = false,
                    HttpStatusCode = (int)response.StatusCode, ErrorMessage = "Auth failed" };

            case 429:
                // Rate limited — ShouldRetry=true, caller sẽ đọc Retry-After
                var retryAfter = response.Headers.RetryAfter?.Delta?.Seconds ?? 60;
                _logger.LogWarning("Rate limited, retry after {Seconds}s", retryAfter);
                return new TransportResult { Success = false, ShouldRetry = true,
                    HttpStatusCode = 429, ErrorMessage = $"RateLimited:{retryAfter}" };

            default:
                // 5xx hoặc unexpected
                _logger.LogWarning("Backend returned {Status}", (int)response.StatusCode);
                return new TransportResult { Success = false, ShouldRetry = true,
                    HttpStatusCode = (int)response.StatusCode };
        }
    }

    // HMAC-SHA256(body_utf8_bytes, hmac_secret)
    // Trả về null nếu chưa có HMAC secret
    private string? ComputeHmac(string json)
    {
        var secret = _secrets.GetHmacSecret();
        if (string.IsNullOrEmpty(secret)) return null;

        var keyBytes = Convert.FromBase64String(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(json);
        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToBase64String(hmac.ComputeHash(bodyBytes));
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
        catch { return null; }
    }
}
```

#### Polly Retry Policy (đăng ký trong Program.cs)

```csharp
// Polly policy cho HttpTransport:
builder.Services.AddHttpClient<ITransport, HttpTransport>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    // TLS config
    var handler = new SocketsHttpHandler
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.Online
        }
    };
})
.ConfigurePrimaryHttpMessageHandler(() => handler)
.AddTransientHttpErrorPolicy(p =>
    p.WaitAndRetryAsync(
        retryCount: options.MaxRetries,
        sleepDurationProvider: attempt =>
            TimeSpan.FromSeconds(Math.Pow(2, attempt) * options.InitialRetryDelaySeconds),
        onRetry: (outcome, delay, attempt, _) =>
            logger.LogWarning("Retry {Attempt} after {Delay:g}: {Error}",
                attempt, delay, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString())));
// Lưu ý: Polly retry CHỈ cho network error / 5xx
// 4xx được handle thủ công trong HandleResponse (ShouldRetry=false)
```

---

### [FILE] `Transport/LocalQueue.cs`

```csharp
namespace PolicyCollector.Agent.Transport;

// SQLite outbox pattern — thread-safe, WAL mode
public sealed class LocalQueue : IDisposable
{
    private readonly string _dbPath;
    private readonly LocalQueueOptions _options;
    private readonly ILogger<LocalQueue> _logger;
    private SqliteConnection? _connection;

    // DB path: C:\ProgramData\PolicyCollector\queue.db
    public LocalQueue(IOptions<LocalQueueOptions> options, ILogger<LocalQueue> logger)
    {
        _dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PolicyCollector", "queue.db");
        _options = options.Value;
        _logger = logger;
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();

        // WAL mode: tránh corruption khi power loss
        _connection.Execute("PRAGMA journal_mode=WAL;");
        _connection.Execute("PRAGMA synchronous=NORMAL;");

        _connection.Execute("""
            CREATE TABLE IF NOT EXISTS outbox (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                payload     TEXT NOT NULL,
                created_at  TEXT NOT NULL DEFAULT (datetime('now')),
                retry_count INTEGER NOT NULL DEFAULT 0,
                last_error  TEXT
            )
            """);
    }

    // Enqueue payload — gọi khi HttpTransport thất bại + ShouldRetry=true
    public void Enqueue(CollectionPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonContext.Default.CollectionPayload);

        // Enforce max entries — drop oldest nếu đầy
        var count = _connection!.ExecuteScalar<int>("SELECT COUNT(*) FROM outbox");
        if (count >= _options.MaxEntries)
        {
            var toDrop = count - _options.MaxEntries + 1;
            _connection.Execute(
                $"DELETE FROM outbox WHERE id IN (SELECT id FROM outbox ORDER BY id ASC LIMIT {toDrop})");
            _logger.LogWarning("LocalQueue full, dropped {Count} oldest entries", toDrop);
        }

        _connection.Execute(
            "INSERT INTO outbox (payload) VALUES (@payload)",
            new { payload = json });
    }

    // Lấy batch entries cũ nhất để retry
    public IReadOnlyList<(int Id, string PayloadJson)> Dequeue(int batchSize = 10)
    {
        return _connection!
            .Query<(int Id, string PayloadJson)>(
                "SELECT id, payload FROM outbox ORDER BY id ASC LIMIT @batchSize",
                new { batchSize })
            .ToList();
    }

    // Xóa sau khi gửi thành công
    public void Remove(int id) =>
        _connection!.Execute("DELETE FROM outbox WHERE id = @id", new { id });

    // Update retry count + error khi retry thất bại
    public void UpdateRetry(int id, string error) =>
        _connection!.Execute(
            "UPDATE outbox SET retry_count = retry_count + 1, last_error = @error WHERE id = @id",
            new { id, error });

    // Cleanup entries quá cũ
    public int PurgeExpired()
    {
        var cutoff = DateTime.UtcNow.AddHours(-_options.MaxAgeHours).ToString("yyyy-MM-ddTHH:mm:ssZ");
        return _connection!.Execute(
            "DELETE FROM outbox WHERE created_at < @cutoff", new { cutoff });
    }

    // Đếm pending entries (cho health check / logging)
    public int PendingCount() =>
        _connection!.ExecuteScalar<int>("SELECT COUNT(*) FROM outbox");

    public void Dispose() => _connection?.Dispose();
}
```

---

### [FILE] `Scheduler/CollectionScheduler.cs`

```csharp
namespace PolicyCollector.Agent.Scheduler;

public sealed class CollectionScheduler : BackgroundService
{
    private readonly CollectionJob _job;
    private readonly ITransport _transport;
    private readonly LocalQueue _queue;
    private readonly AgentOptions _options;
    private readonly ILogger<CollectionScheduler> _logger;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("PolicyCollector scheduler started, interval={Interval}min",
            _options.IntervalMinutes);

        if (_options.CollectOnStartup)
        {
            _logger.LogInformation("Running initial collection on startup");
            await RunCollectionCycle(ct);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));

        while (await timer.WaitForNextTickAsync(ct))
            await RunCollectionCycle(ct);
    }

    private async Task RunCollectionCycle(CancellationToken ct)
    {
        try
        {
            var payload = await _job.RunAsync(ct);
            var result = await _transport.SendAsync(payload, ct);

            if (!result.Success && result.ShouldRetry)
            {
                _logger.LogWarning("Send failed (retry), queuing payload locally");
                _queue.Enqueue(payload);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Collection cycle failed unexpectedly");
        }
    }
}
```

---

### [FILE] `Jobs/RetryJob.cs`

```csharp
namespace PolicyCollector.Agent.Jobs;

// Background job: poll LocalQueue, retry sending failed payloads
public sealed class RetryJob : BackgroundService
{
    private readonly LocalQueue _queue;
    private readonly ITransport _transport;
    private readonly LocalQueueOptions _options;
    private readonly ILogger<RetryJob> _logger;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Đợi 30 giây trước khi start (cho CollectionScheduler warm up trước)
        await Task.Delay(TimeSpan.FromSeconds(30), ct);

        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(_options.RetryIntervalMinutes));

        while (await timer.WaitForNextTickAsync(ct))
        {
            // Cleanup expired first
            var purged = _queue.PurgeExpired();
            if (purged > 0)
                _logger.LogInformation("Purged {Count} expired queue entries", purged);

            var pending = _queue.PendingCount();
            if (pending == 0) continue;

            _logger.LogInformation("Retrying {Count} pending payloads", pending);
            await RetryBatch(ct);
        }
    }

    private async Task RetryBatch(CancellationToken ct)
    {
        var batch = _queue.Dequeue(batchSize: 10);

        foreach (var (id, json) in batch)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var payload = JsonSerializer.Deserialize(json, JsonContext.Default.CollectionPayload);
                if (payload is null)
                {
                    _queue.Remove(id);
                    continue;
                }

                var result = await _transport.SendAsync(payload, ct);
                if (result.Success)
                {
                    _queue.Remove(id);
                    _logger.LogInformation("Retry succeeded for queued payload id={Id}", id);
                }
                else if (!result.ShouldRetry)
                {
                    // 4xx — không bao giờ succeed, drop
                    _queue.Remove(id);
                    _logger.LogWarning("Dropping queued payload id={Id}, non-retryable: {Error}",
                        id, result.ErrorMessage);
                }
                else
                {
                    _queue.UpdateRetry(id, result.ErrorMessage ?? "Unknown");
                }
            }
            catch (Exception ex)
            {
                _queue.UpdateRetry(id, ex.Message);
                _logger.LogWarning(ex, "Retry failed for id={Id}", id);
            }
        }
    }
}
```

---

## Unit Tests

### [TEST] `Transport/HttpTransportTests.cs`

```csharp
// Dùng HttpMessageHandler mock (MockHttpMessageHandler)
// Test cases:
//   - 202 response → TransportResult.Success=true, IngestionId parsed
//   - 400 response → Success=false, ShouldRetry=false
//   - 422 response → Success=false, ShouldRetry=false
//   - 401 response → Success=false, ShouldRetry=false
//   - 500 response → Success=false, ShouldRetry=true
//   - HttpRequestException → Success=false, ShouldRetry=true
//   - HMAC header present khi secret có
//   - HMAC header absent khi không có secret
```

### [TEST] `Transport/LocalQueueTests.cs`

```csharp
// Dùng in-memory SQLite hoặc temp file
// Test cases:
//   - Enqueue + Dequeue → FIFO order
//   - Enqueue khi đầy (1000) → oldest dropped
//   - PurgeExpired → entries cũ hơn MaxAgeHours bị xóa
//   - Remove → entry không còn trong queue
//   - UpdateRetry → retry_count tăng, last_error được set
//   - Thread-safe: nhiều Enqueue đồng thời không corrupt
```

---

## Acceptance Criteria

- [ ] `HttpTransport` không retry khi nhận 400/401/422/403
- [ ] `HttpTransport` retry khi network error hoặc 5xx
- [ ] `LocalQueue` FIFO — entry cũ nhất được gửi lại trước
- [ ] `LocalQueue` không bao giờ vượt `MaxEntries` (1000)
- [ ] `LocalQueue` tự purge entries quá `MaxAgeHours` (168h = 7 ngày)
- [ ] `CollectionScheduler` không crash khi `CollectionJob` throw
- [ ] `RetryJob` drop payload sau khi nhận 4xx (không loop forever)
- [ ] `CollectionScheduler` không drift: tick interval chính xác với `PeriodicTimer`
- [ ] HMAC header gửi đúng format `base64(HMAC-SHA256(body_bytes, secret))`
