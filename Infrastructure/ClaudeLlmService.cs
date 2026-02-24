using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Core.Entities;
using Core.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Infrastructure;

public class ClaudeLlmService : ILlmService
{
    private readonly IAmazonBedrockRuntime _bedrockClient;
    private readonly ILogger<ClaudeLlmService> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private static readonly ActivitySource ActivitySource = new("BedrockSemanticCache");
    private const string ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0";

    public ClaudeLlmService(IAmazonBedrockRuntime bedrockClient, ILogger<ClaudeLlmService> logger)
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
                    _logger.LogWarning(exception, "Error calling Bedrock Claude LLM. Retrying in {Delay}s. Attempt {RetryCount}", timeSpan.TotalSeconds, retryCount);
                });
    }

    public async Task<string> GenerateResponseAsync(string prompt, List<DocumentChunk> context)
    {
        using var activity = ActivitySource.StartActivity("ClaudeLlmService.GenerateResponseAsync");
        activity?.SetTag("model.id", ModelId);
        activity?.SetTag("context.chunks.count", context.Count);

        var contextBuilder = new StringBuilder();
        foreach (var chunk in context)
        {
            contextBuilder.AppendLine(chunk.Text);
        }

        var systemPrompt = @"You are a helpful assistant. Answer the user's question using ONLY the context provided. If the context does not contain enough information, say so. Do not hallucinate.";
        
        var userMessage = $@"Context:
{contextBuilder}

Question: {prompt}";

        var payload = new
        {
            anthropic_version = "bedrock-2023-05-31",
            max_tokens = 1000,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = new[] { new { type = "text", text = userMessage } } }
            }
        };

        var bodyBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await _retryPolicy.ExecuteAsync(() => _bedrockClient.InvokeModelAsync(new InvokeModelRequest
            {
                ModelId = ModelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(bodyBytes)
            }));
            
            stopwatch.Stop();
            _logger.LogInformation("Successfully generated LLM response. Model: {ModelId}, Latency: {LatencyMs}ms", ModelId, stopwatch.ElapsedMilliseconds);

            using var reader = new StreamReader(response.Body);
            var responseBody = await reader.ReadToEndAsync();
            
            var result = JsonSerializer.Deserialize<ClaudeResponse>(responseBody);
            return result?.Content?.FirstOrDefault()?.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to generate LLM response after retries. Model: {ModelId}, Latency: {LatencyMs}ms", ModelId, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private class ClaudeResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("content")]
        public List<ClaudeContent>? Content { get; set; }
    }

    private class ClaudeContent
    {
        [System.Text.Json.Serialization.JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }
}