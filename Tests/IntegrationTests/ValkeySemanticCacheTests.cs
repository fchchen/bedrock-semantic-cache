using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Entities;
using Core.Settings;
using FluentAssertions;
using Infrastructure;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Tests.IntegrationTests;

public class ValkeySemanticCacheTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder("redis/redis-stack-server:latest")
        .Build();

    private IConnectionMultiplexer _redis = null!;
    private ValkeySemanticCache _cache = null!;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
        _cache = new ValkeySemanticCache(_redis, Options.Create(new OrchestratorSettings()));
    }

    public async Task DisposeAsync()
    {
        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task StoreAndSearch_ShouldReturnCacheEntry_WithHighSimilarity()
    {
        // Arrange
        var vector = Enumerable.Repeat(0.5f, 1536).ToArray();
        var entry = new CacheEntry
        {
            Id = Guid.NewGuid().ToString(),
            Prompt = "What is the capital of France?",
            Response = "Paris",
            Vector = vector,
            SourceChunkIds = new List<string> { "chunk-1", "chunk-2" },
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };

        // Act
        await _cache.StoreAsync(entry);

        var searchVector = Enumerable.Repeat(0.5f, 1536).ToArray();

        await TestHelpers.WaitUntilAsync(async () =>
        {
            var r = await _cache.SearchAsync(searchVector, 0.95);
            return r != null;
        });

        var result = await _cache.SearchAsync(searchVector, 0.95);

        // Assert
        result.Should().NotBeNull();
        result!.Item.Id.Should().Be(entry.Id);
        result.Item.Prompt.Should().Be(entry.Prompt);
        result.Item.SourceChunkIds.Should().BeEquivalentTo(entry.SourceChunkIds);
        result.Score.Should().BeGreaterThan(0.95);
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnNull_WhenTTL_Expires()
    {
        // Arrange
        var vector = Enumerable.Repeat(0.3f, 1536).ToArray();
        var entry = new CacheEntry
        {
            Id = Guid.NewGuid().ToString(),
            Prompt = "Will expire soon",
            Response = "Short lived",
            Vector = vector,
            SourceChunkIds = new List<string> { "chunk-a" },
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(500) // Expires in 500ms
        };

        await _cache.StoreAsync(entry);

        // Wait for TTL to expire via polling
        await TestHelpers.WaitUntilAsync(async () =>
        {
            var r = await _cache.SearchAsync(vector, 0.0);
            return r == null;
        }, timeout: TimeSpan.FromSeconds(5));

        // Act
        var result = await _cache.SearchAsync(vector, 0.95);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task InvalidateByChunkIds_ShouldRemoveReferencingEntries()
    {
        // Arrange
        var entry1 = new CacheEntry { Id = Guid.NewGuid().ToString(), SourceChunkIds = new List<string> { "chunk-1" }, Vector = Enumerable.Repeat(0.1f, 1536).ToArray(), ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        var entry2 = new CacheEntry { Id = Guid.NewGuid().ToString(), SourceChunkIds = new List<string> { "chunk-1", "chunk-2" }, Vector = Enumerable.Repeat(0.2f, 1536).ToArray(), ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        var entry3 = new CacheEntry { Id = Guid.NewGuid().ToString(), SourceChunkIds = new List<string> { "chunk-3" }, Vector = Enumerable.Repeat(0.3f, 1536).ToArray(), ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };

        await _cache.StoreAsync(entry1);
        await _cache.StoreAsync(entry2);
        await _cache.StoreAsync(entry3);

        var db = _redis.GetDatabase();
        await TestHelpers.WaitUntilAsync(async () =>
            await db.KeyExistsAsync($"cache:{entry1.Id}") &&
            await db.KeyExistsAsync($"cache:{entry2.Id}") &&
            await db.KeyExistsAsync($"cache:{entry3.Id}"));

        // Act
        await _cache.InvalidateByChunkIdsAsync(new List<string> { "chunk-1" });

        await TestHelpers.WaitUntilAsync(async () =>
            !await db.KeyExistsAsync($"cache:{entry1.Id}"));

        // Assert
        var exists1 = await db.KeyExistsAsync($"cache:{entry1.Id}");
        var exists2 = await db.KeyExistsAsync($"cache:{entry2.Id}");
        var exists3 = await db.KeyExistsAsync($"cache:{entry3.Id}");

        exists1.Should().BeFalse("entry1 references chunk-1");
        exists2.Should().BeFalse("entry2 references chunk-1");
        exists3.Should().BeTrue("entry3 references chunk-3, not chunk-1");
    }
}
