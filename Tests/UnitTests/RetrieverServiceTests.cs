using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Entities;
using Core.Interfaces;
using Core.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Tests.UnitTests;

public class RetrieverServiceTests
{
    [Fact]
    public async Task RetrieveAsync_ShouldFilterOutChunks_BelowScoreThreshold()
    {
        // Arrange
        var mockStore = new Mock<IDocumentStore>();
        var queryVector = new float[] { 0.1f, 0.2f };
        
        var chunkHigh = new DocumentChunk { Id = "high" };
        var chunkMed = new DocumentChunk { Id = "med" };
        var chunkLow = new DocumentChunk { Id = "low" };

        var mockResults = new List<SimilarityResult<DocumentChunk>>
        {
            new(chunkHigh, 0.95), // Above 0.75
            new(chunkMed, 0.75),  // Equal to 0.75
            new(chunkLow, 0.50)   // Below 0.75
        };

        mockStore.Setup(s => s.SearchAsync(queryVector, It.IsAny<int>()))
                 .ReturnsAsync(mockResults);

        var service = new RetrieverService(mockStore.Object)
        {
            TopK = 5,
            MinScoreThreshold = 0.75
        };

        // Act
        var results = await service.RetrieveAsync(queryVector);

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain(chunkHigh);
        results.Should().Contain(chunkMed);
        results.Should().NotContain(chunkLow);
        
        // Also verify ordering
        results[0].Id.Should().Be("high"); // higher score first
        results[1].Id.Should().Be("med");
    }
}