using System.Net.Http.Json;
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
                 .ReturnsAsync((Core.Entities.SimilarityResult<Core.Entities.CacheEntry>?)null);

        mockDocStore.Setup(d => d.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>()))
                    .ReturnsAsync(new List<Core.Entities.SimilarityResult<Core.Entities.DocumentChunk>>());

        mockLlm.Setup(l => l.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<Core.Entities.DocumentChunk>>()))
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
}