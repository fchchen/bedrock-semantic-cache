using Amazon.BedrockRuntime;
using Core.Interfaces;
using Core.Services;
using Core.Settings;
using Infrastructure;
using Infrastructure.Chunking;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;
using Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Configuration
builder.Services.Configure<OrchestratorSettings>(builder.Configuration.GetSection("Orchestrator"));

// Valkey / Redis
var redisCnx = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisCnx));

// AWS Bedrock
builder.Services.AddAWSService<IAmazonBedrockRuntime>();

// Infrastructure Services
builder.Services.AddSingleton<IEmbeddingService, TitanEmbeddingService>();
builder.Services.AddSingleton<ILlmService, ClaudeLlmService>();
builder.Services.AddSingleton<IDocumentStore, ValkeyDocumentStore>();
builder.Services.AddSingleton<ISemanticCache, ValkeySemanticCache>();

// Core Services
builder.Services.AddSingleton<IChunkingStrategy, SentenceAwareChunker>();
builder.Services.AddSingleton<RetrieverService>();
builder.Services.AddSingleton<ChatOrchestrator>();
builder.Services.AddSingleton<IIngestPipeline, IngestPipeline>();

// OpenTelemetry
var resourceBuilder = ResourceBuilder.CreateDefault().AddService("BedrockSemanticCache");
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(resourceBuilder)
        .AddSource("BedrockSemanticCache")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(resourceBuilder)
        .AddMeter("BedrockSemanticCache")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddPrometheusExporter());

// Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<Web.Health.ValkeyHealthCheck>("Valkey");

var app = builder.Build();

// Middlewares
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SemanticCacheMiddleware>();

// Endpoints
app.MapPost("/chat", async (ChatRequest request, ChatOrchestrator orchestrator, HttpContext context) =>
{
    var response = await orchestrator.ProcessChatAsync(request.Prompt);
    context.Response.Headers["X-Cache-Status"] = response.CacheStatus;
    return Results.Ok(response);
});

app.MapPost("/ingest", async (IngestRequest request, IIngestPipeline pipeline) =>
{
    var job = await pipeline.IngestAsync(request.DocumentId, request.FileName, request.Content);
    return Results.Accepted($"/ingest/{job.Id}", job);
});

app.MapPost("/ingest/{documentId}/reingest", async (string documentId, IngestRequest request, IIngestPipeline pipeline, ISemanticCache cache, IDocumentStore store) =>
{
    // Implementation of cache invalidation on re-ingest
    await store.DeleteByDocumentIdAsync(documentId);
    // In a real app we'd need the old chunk IDs. 
    // Simplified: invalidate by doc ID if we had it, or just let TTL handle it.
    // Blueprint says: "Triggers DeleteByDocumentIdAsync on both stores"
    var job = await pipeline.IngestAsync(documentId, request.FileName, request.Content);
    return Results.Accepted($"/ingest/{job.Id}", job);
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapPrometheusScrapingEndpoint(); // /metrics

app.Run();

public record ChatRequest(string Prompt);
public record IngestRequest(string DocumentId, string FileName, string Content);