using System.Net;
using System.Net.Http.Json;
using Core.Entities;
using Core.Interfaces;
using Core.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Tests.IntegrationTests;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostChat_ReturnsCorrectHeaders_AndResponse()
    {
        // Arrange
        var mockEmbedding = new Mock<IEmbeddingService>();
        var mockCache = new Mock<ISemanticCache>();
        var mockLlm = new Mock<ILlmService>();
        var mockDocStore = new Mock<IDocumentStore>();
        var mockRedis = new Mock<IConnectionMultiplexer>();

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockEmbedding.Object);
                services.AddSingleton(mockCache.Object);
                services.AddSingleton(mockLlm.Object);
                services.AddSingleton(mockDocStore.Object);
                services.AddSingleton(mockRedis.Object);
            });
        }).CreateClient();

        mockEmbedding.Setup(e => e.GetEmbeddingAsync(It.IsAny<string>()))
                     .ReturnsAsync(new float[1536]);

        mockCache.Setup(c => c.SearchAsync(It.IsAny<float[]>(), It.IsAny<double>()))
                 .ReturnsAsync((SimilarityResult<CacheEntry>?)null);

        mockDocStore.Setup(d => d.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>()))
                    .ReturnsAsync(new List<SimilarityResult<DocumentChunk>>());

        mockLlm.Setup(l => l.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<DocumentChunk>>()))
               .ReturnsAsync("Test Response");

        // Act
        var response = await client.PostAsJsonAsync("/chat", new { Prompt = "Hello" });

        // Assert
        response.EnsureSuccessStatusCode();
        response.Headers.Should().ContainKey("X-Cache-Status");
        response.Headers.Should().ContainKey("X-Latency-Ms");
        response.Headers.GetValues("X-Cache-Status").Should().Contain("MISS");

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>();
        result.Should().NotBeNull();
        result!.Answer.Should().Be("Test Response");
        result.CacheStatus.Should().Be("MISS");
    }

    [Fact]
    public async Task PostChat_WithEmptyPrompt_ReturnsBadRequest()
    {
        // Arrange — must mock all dependencies so DI can resolve the endpoint parameters
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockEmbedding = new Mock<IEmbeddingService>();
        var mockCache = new Mock<ISemanticCache>();
        var mockLlm = new Mock<ILlmService>();
        var mockDocStore = new Mock<IDocumentStore>();

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockRedis.Object);
                services.AddSingleton(mockEmbedding.Object);
                services.AddSingleton(mockCache.Object);
                services.AddSingleton(mockLlm.Object);
                services.AddSingleton(mockDocStore.Object);
            });
        }).CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/chat", new { Prompt = "" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostChat_WithOversizedPrompt_ReturnsBadRequest()
    {
        // Arrange
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockEmbedding = new Mock<IEmbeddingService>();
        var mockCache = new Mock<ISemanticCache>();
        var mockLlm = new Mock<ILlmService>();
        var mockDocStore = new Mock<IDocumentStore>();

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockRedis.Object);
                services.AddSingleton(mockEmbedding.Object);
                services.AddSingleton(mockCache.Object);
                services.AddSingleton(mockLlm.Object);
                services.AddSingleton(mockDocStore.Object);
            });
        }).CreateClient();

        var oversizedPrompt = new string('a', 10_001);

        // Act
        var response = await client.PostAsJsonAsync("/chat", new { Prompt = oversizedPrompt });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostIngest_WithEmptyContent_ReturnsBadRequest()
    {
        // Arrange — must mock all dependencies so DI can resolve IIngestPipeline
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockEmbedding = new Mock<IEmbeddingService>();
        var mockDocStore = new Mock<IDocumentStore>();

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockRedis.Object);
                services.AddSingleton(mockEmbedding.Object);
                services.AddSingleton(mockDocStore.Object);
            });
        }).CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/ingest", new IngestRequest("doc1", "file.txt", ""));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ingest_ReturnsAccepted_AndJobCanBeRetrieved()
    {
        // Arrange
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockRedis.Object);
            });
        }).CreateClient();

        var request = new IngestRequest("doc123", "test.txt", "Some content to ingest.");

        // Act
        var response = await client.PostAsJsonAsync("/ingest", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var job = await response.Content.ReadFromJsonAsync<IngestJob>();
        job.Should().NotBeNull();
        job!.DocumentId.Should().Be("doc123");

        // Retrieve the job status
        var statusResponse = await client.GetAsync($"/ingest/{job.Id}");
        statusResponse.EnsureSuccessStatusCode();
        var retrievedJob = await statusResponse.Content.ReadFromJsonAsync<IngestJob>();
        retrievedJob!.Id.Should().Be(job.Id);
    }

    [Fact]
    public async Task Reingest_InvalidatesCache_AndReturnsAccepted()
    {
        // Arrange
        var mockCache = new Mock<ISemanticCache>();
        var mockDocStore = new Mock<IDocumentStore>();
        var mockEmbedding = new Mock<IEmbeddingService>();
        var mockRedis = new Mock<IConnectionMultiplexer>();

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockCache.Object);
                services.AddSingleton(mockDocStore.Object);
                services.AddSingleton(mockEmbedding.Object);
                services.AddSingleton(mockRedis.Object);
            });
        }).CreateClient();

        var docId = "doc1";
        var oldChunkIds = new List<string> { "chunk1", "chunk2" };

        mockDocStore.Setup(s => s.GetChunkIdsByDocumentIdAsync(docId))
                    .ReturnsAsync(oldChunkIds);

        var request = new IngestRequest(docId, "updated.txt", "Updated content");

        // Act
        var response = await client.PostAsJsonAsync($"/ingest/{docId}/reingest", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Verify invalidation was called
        mockCache.Verify(c => c.InvalidateByChunkIdsAsync(oldChunkIds), Times.Once);
        // Verify old chunks were deleted
        mockDocStore.Verify(s => s.DeleteByDocumentIdAsync(docId), Times.Once);
    }
}

public record IngestRequest(string DocumentId, string FileName, string Content);
