using System.Text.Json;
using StackExchange.Redis;

namespace PolicyCollector.Backend.Infrastructure;

public sealed class RedisQueue
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisQueue> _logger;

    public RedisQueue(IConnectionMultiplexer redis, ILogger<RedisQueue> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task EnqueueAsync(
        string streamKey,
        CollectionPayload payload,
        string ingestionId,
        CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(payload);

            await db.StreamAddAsync(streamKey,
            [
                new NameValueEntry("ingestion_id", ingestionId),
                new NameValueEntry("payload", json),
                new NameValueEntry("hostname", payload.Host?.Hostname ?? "unknown"),
                new NameValueEntry("enqueued_at", DateTimeOffset.UtcNow.ToString("O"))
            ]);

            _logger.LogDebug("Payload enqueued: ingestion_id={IngestionId}", ingestionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue to Redis");
            throw;
        }
    }

    public async Task<IReadOnlyList<StreamEntry>> ReadGroupAsync(
        string streamKey,
        string groupName,
        string consumerName,
        int count = 10)
    {
        try
        {
            var db = _redis.GetDatabase();

            try
            {
                await db.StreamCreateConsumerGroupAsync(streamKey, groupName, StreamPosition.NewMessages);
            }
            catch (RedisException)
            {
                // Group already exists
            }

            var entries = await db.StreamReadGroupAsync(
                streamKey, groupName, consumerName,
                StreamPosition.NewMessages, count);

            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read from Redis stream");
            return new List<StreamEntry>();
        }
    }

    public async Task AcknowledgeAsync(string streamKey, string groupName, RedisValue messageId)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.StreamAcknowledgeAsync(streamKey, groupName, messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acknowledge Redis message");
        }
    }

    public async Task DeadLetterAsync(string streamKey, RedisValue messageId, string reason)
    {
        try
        {
            var db = _redis.GetDatabase();
            var deadLetterKey = $"{streamKey}:dead-letter";

            await db.ListRightPushAsync(deadLetterKey, new RedisValue[]
            {
                messageId.ToString(),
                reason,
                DateTimeOffset.UtcNow.ToString("O")
            });

            _logger.LogWarning("Message {Id} moved to dead letter: {Reason}", messageId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move message to dead letter");
        }
    }
}
