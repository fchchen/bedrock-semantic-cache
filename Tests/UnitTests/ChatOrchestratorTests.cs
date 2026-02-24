using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Entities;
using Core.Interfaces;
using Core.Services;
using Core.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.UnitTests;

public class ChatOrchestratorTests
{
    private readonly Mock<IEmbeddingService> _mockEmbedding;
    private readonly Mock<ISemanticCache> _mockCache;
    private readonly Mock<IDocumentStore> _mockDocStore;
    private readonly RetrieverService _retriever;
    private readonly Mock<ILlmService> _mockLlm;
    private readonly ChatOrchestrator _orchestrator;

    public ChatOrchestratorTests()
    {
        _mockEmbedding = new Mock<IEmbeddingService>();
        _mockCache = new Mock<ISemanticCache>();
        _mockDocStore = new Mock<IDocumentStore>();
        _mockLlm = new Mock<ILlmService>();
        
        _retriever = new RetrieverService(_mockDocStore.Object)
        {
            TopK = 5,
            MinScoreThreshold = 0.75
        };

        var options = Options.Create(new OrchestratorSettings { SimilarityThreshold = 0.85, TtlHours = 24 });
        var mockLogger = new Mock<ILogger<ChatOrchestrator>>();

        _orchestrator = new ChatOrchestrator(
            _mockEmbedding.Object,
            _mockCache.Object,
            _retriever,
            _mockLlm.Object,
            options,
            mockLogger.Object);
    }

    [Fact]
    public async Task ProcessChatAsync_CacheHit_ReturnsCachedAnswer_DoesNotCallLlm()
    {
        // Arrange
        var prompt = "What is RAG?";
        var vector = new[] { 0.1f, 0.2f };
        _mockEmbedding.Setup(e => e.GetEmbeddingAsync(prompt)).ReturnsAsync(vector);

        var cachedEntry = new CacheEntry 
        { 
            Response = "Cached Answer",
            SourceChunkIds = new List<string> { "chunk1" }
        };
        _mockCache.Setup(c => c.SearchAsync(vector, 0.85))
                  .ReturnsAsync(new SimilarityResult<CacheEntry>(cachedEntry, 0.90));

        // Act
        var response = await _orchestrator.ProcessChatAsync(prompt);

        // Assert
        response.Answer.Should().Be("Cached Answer");
        response.CacheStatus.Should().Be("HIT");
        response.SourceChunkIds.Should().BeEquivalentTo(new[] { "chunk1" });

        _mockDocStore.Verify(d => d.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>()), Times.Never);
        _mockLlm.Verify(l => l.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<DocumentChunk>>()), Times.Never);
    }

    [Fact]
    public async Task ProcessChatAsync_CacheMiss_CallsLlm_StoresInCache()
    {
        // Arrange
        var prompt = "What is RAG?";
        var vector = new[] { 0.1f, 0.2f };
        _mockEmbedding.Setup(e => e.GetEmbeddingAsync(prompt)).ReturnsAsync(vector);

        // Cache returns null (MISS)
        _mockCache.Setup(c => c.SearchAsync(vector, 0.85))
                  .ReturnsAsync((SimilarityResult<CacheEntry>?)null);

        var retrievedChunks = new List<SimilarityResult<DocumentChunk>>
        {
            new(new DocumentChunk { Id = "chunk1", Text = "context1" }, 0.90)
        };
        _mockDocStore.Setup(d => d.SearchAsync(vector, 5)).ReturnsAsync(retrievedChunks);

        _mockLlm.Setup(l => l.GenerateResponseAsync(prompt, It.IsAny<List<DocumentChunk>>()))
                .ReturnsAsync("LLM Answer");

        // Act
        var response = await _orchestrator.ProcessChatAsync(prompt);
        
        // Wait briefly for fire-and-forget background task to finish
        await Task.Delay(50);

        // Assert
        response.Answer.Should().Be("LLM Answer");
        response.CacheStatus.Should().Be("MISS");
        response.SourceChunkIds.Should().BeEquivalentTo(new[] { "chunk1" });

        _mockDocStore.Verify(d => d.SearchAsync(vector, 5), Times.Once);
        _mockLlm.Verify(l => l.GenerateResponseAsync(prompt, It.Is<List<DocumentChunk>>(c => c.Count == 1)), Times.Once);
        _mockCache.Verify(c => c.StoreAsync(It.Is<CacheEntry>(e => 
            e.Prompt == prompt && 
            e.Response == "LLM Answer" && 
            e.SourceChunkIds.Contains("chunk1"))), Times.Once);
    }
}