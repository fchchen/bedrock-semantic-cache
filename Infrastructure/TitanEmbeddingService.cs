using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Core.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Infrastructure;

public class TitanEmbeddingService : IEmbeddingService
{
    private readonly IAmazonBedrockRuntime _bedrockClient;
    private readonly ILogger<TitanEmbeddingService> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private static readonly ActivitySource ActivitySource = new("BedrockSemanticCache");
    private const string ModelId = "amazon.titan-embed-text-v1";

    public TitanEmbeddingService(IAmazonBedrockRuntime bedrockClient, ILogger<TitanEmbeddingService> logger)
    {
        _bedrockClient = bedrockClient;
        _logger = logger;

        _retryPolicy = Policy
            .Handle<AmazonBedrockRuntimeException>()
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(exception, "Error calling Bedrock Titan Embedding. Retrying in {Delay}s. Attempt {RetryCount}", timeSpan.TotalSeconds, retryCount);
                });
    }

    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        using var activity = ActivitySource.StartActivity("EmbeddingService.GetEmbeddingAsync");
        activity?.SetTag("model.id", ModelId);
        activity?.SetTag("input.length", text.Length);

        var payload = new { inputText = text };
        var request = new InvokeModelRequest
        {
            ModelId = ModelId,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await _retryPolicy.ExecuteAsync(() => _bedrockClient.InvokeModelAsync(request));
            
            stopwatch.Stop();
            _logger.LogInformation("Successfully generated embedding. Model: {ModelId}, Latency: {LatencyMs}ms", ModelId, stopwatch.ElapsedMilliseconds);

            using var reader = new StreamReader(response.Body);
            var responseBody = await reader.ReadToEndAsync();
            
            var result = JsonSerializer.Deserialize<TitanEmbeddingResponse>(responseBody);
            if (result?.Embedding == null || result.Embedding.Length == 0)
                throw new InvalidOperationException("Bedrock Titan returned null or empty embedding.");
            return result.Embedding;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to generate embedding after retries. Model: {ModelId}, Latency: {LatencyMs}ms", ModelId, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private class TitanEmbeddingResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }
}