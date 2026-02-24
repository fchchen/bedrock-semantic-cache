using System;
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

public class ValkeyDocumentStoreTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder("redis/redis-stack-server:latest")
        .Build();

    private IConnectionMultiplexer _redis = null!;
    private ValkeyDocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
        _store = new ValkeyDocumentStore(_redis, Options.Create(new OrchestratorSettings()));
    }

    public async Task DisposeAsync()
    {
        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task StoreAndSearch_ShouldReturnCorrectChunk_WithHighSimilarity()
    {
        // Arrange
        var vector = Enumerable.Repeat(0.1f, 1536).ToArray();
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = "doc1",
            Text = "This is a test chunk.",
            Vector = vector,
            ChunkIndex = 0,
            CharOffset = 0,
            IngestTimestamp = DateTimeOffset.UtcNow
        };

        // Act
        await _store.StoreChunkAsync(chunk);

        var searchVector = Enumerable.Repeat(0.1f, 1536).ToArray();

        await TestHelpers.WaitUntilAsync(async () =>
        {
            var r = await _store.SearchAsync(searchVector, 5);
            return r.Any();
        });

        var results = await _store.SearchAsync(searchVector, 5);

        // Assert
        results.Should().NotBeEmpty();
        results.First().Item.Id.Should().Be(chunk.Id);
        results.First().Score.Should().BeGreaterThan(0.95); // Identical vectors should have score > 0.95
    }

    [Fact]
    public async Task DeleteByDocumentId_ShouldRemoveAllChunksForDocument()
    {
        // Arrange
        var docId = "docToDelete";
        var chunk1 = new DocumentChunk { Id = Guid.NewGuid().ToString(), DocumentId = docId, Vector = Enumerable.Repeat(0.1f, 1536).ToArray() };
        var chunk2 = new DocumentChunk { Id = Guid.NewGuid().ToString(), DocumentId = docId, Vector = Enumerable.Repeat(0.2f, 1536).ToArray() };
        var chunk3 = new DocumentChunk { Id = Guid.NewGuid().ToString(), DocumentId = "otherDoc", Vector = Enumerable.Repeat(0.1f, 1536).ToArray() };

        await _store.StoreChunkAsync(chunk1);
        await _store.StoreChunkAsync(chunk2);
        await _store.StoreChunkAsync(chunk3);

        var db = _redis.GetDatabase();
        await TestHelpers.WaitUntilAsync(async () =>
            await db.KeyExistsAsync($"doc:{chunk1.Id}") &&
            await db.KeyExistsAsync($"doc:{chunk2.Id}") &&
            await db.KeyExistsAsync($"doc:{chunk3.Id}"));

        // Act
        await _store.DeleteByDocumentIdAsync(docId);

        await TestHelpers.WaitUntilAsync(async () =>
            !await db.KeyExistsAsync($"doc:{chunk1.Id}"));

        // Assert
        var exists1 = await db.KeyExistsAsync($"doc:{chunk1.Id}");
        var exists2 = await db.KeyExistsAsync($"doc:{chunk2.Id}");
        var exists3 = await db.KeyExistsAsync($"doc:{chunk3.Id}");

        exists1.Should().BeFalse();
        exists2.Should().BeFalse();
        exists3.Should().BeTrue();
    }
}
