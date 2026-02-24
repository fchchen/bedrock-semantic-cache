using System;
using System.Collections.Generic;
using Core.Entities;
using FluentAssertions;
using Xunit;

namespace Tests.UnitTests;

public class EntityTests
{
    [Fact]
    public void DocumentChunk_Initialization_SetsFieldsCorrectly()
    {
        var id = Guid.NewGuid().ToString();
        var vector = new[] { 0.1f, 0.2f };
        var timestamp = DateTimeOffset.UtcNow;
        
        var chunk = new DocumentChunk
        {
            Id = id,
            DocumentId = "doc1",
            Text = "Sample text",
            Vector = vector,
            ChunkIndex = 1,
            CharOffset = 10,
            IngestTimestamp = timestamp
        };

        chunk.Id.Should().Be(id);
        chunk.DocumentId.Should().Be("doc1");
        chunk.Text.Should().Be("Sample text");
        chunk.Vector.Should().BeEquivalentTo(vector);
        chunk.ChunkIndex.Should().Be(1);
        chunk.CharOffset.Should().Be(10);
        chunk.IngestTimestamp.Should().Be(timestamp);
    }

    [Fact]
    public void IngestJob_Initialization_SetsFieldsCorrectly()
    {
        var id = Guid.NewGuid().ToString();
        var createdAt = DateTimeOffset.UtcNow;
        
        var job = new IngestJob
        {
            Id = id,
            DocumentId = "doc1",
            FileName = "test.txt",
            Status = "Processing",
            ChunkCount = 5,
            CreatedAt = createdAt
        };

        job.Id.Should().Be(id);
        job.DocumentId.Should().Be("doc1");
        job.FileName.Should().Be("test.txt");
        job.Status.Should().Be("Processing");
        job.ChunkCount.Should().Be(5);
        job.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void CacheEntry_Initialization_SetsFieldsCorrectly()
    {
        var id = Guid.NewGuid().ToString();
        var vector = new[] { 0.5f, 0.6f };
        var createdAt = DateTimeOffset.UtcNow;
        var expiresAt = createdAt.AddDays(1);
        var sourceIds = new List<string> { "chunk1", "chunk2" };

        var entry = new CacheEntry
        {
            Id = id,
            Prompt = "What is RAG?",
            Response = "Retrieval-Augmented Generation",
            Vector = vector,
            SourceChunkIds = sourceIds,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };

        entry.Id.Should().Be(id);
        entry.Prompt.Should().Be("What is RAG?");
        entry.Response.Should().Be("Retrieval-Augmented Generation");
        entry.Vector.Should().BeEquivalentTo(vector);
        entry.SourceChunkIds.Should().BeEquivalentTo(sourceIds);
        entry.CreatedAt.Should().Be(createdAt);
        entry.ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public void SimilarityResult_Initialization_SetsFieldsCorrectly()
    {
        var item = new DocumentChunk { Id = "chunk1" };
        var result = new SimilarityResult<DocumentChunk>(item, 0.95);

        result.Item.Should().Be(item);
        result.Score.Should().Be(0.95);
    }
}
