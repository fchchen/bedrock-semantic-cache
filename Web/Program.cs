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

// Background Task Queues (separate workers to avoid head-of-line blocking)
builder.Services.AddSingleton<IIngestTaskQueue, IngestTaskQueue>();
builder.Services.AddSingleton<ICacheTaskQueue, CacheTaskQueue>();
builder.Services.AddHostedService<BackgroundTaskProcessor<IIngestTaskQueue>>();
builder.Services.AddHostedService<BackgroundTaskProcessor<ICacheTaskQueue>>();

// Core Services
builder.Services.AddSingleton<IChunkingStrategy, SentenceAwareChunker>();
builder.Services.AddSingleton<RetrieverService>();
builder.Services.AddSingleton<ChatOrchestrator>();
builder.Services.AddSingleton<IIngestPipeline, IngestPipeline>();
builder.Services.AddSingleton<JobStore>();

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

// ProblemDetails for consistent error responses
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseDefaultFiles();
app.UseStaticFiles();

// Middlewares
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SemanticCacheMiddleware>();

// Endpoints
const int MaxContentLength = 10 * 1024 * 1024; // 10 MB

const int MaxPromptLength = 10_000;

app.MapPost("/chat", async (ChatRequest request, ChatOrchestrator orchestrator, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt))
        return Results.Problem("Prompt is required.", statusCode: 400);
    if (request.Prompt.Length > MaxPromptLength)
        return Results.Problem($"Prompt exceeds maximum length of {MaxPromptLength} characters.", statusCode: 400);

    var response = await orchestrator.ProcessChatAsync(request.Prompt);
    context.Response.Headers["X-Cache-Status"] = response.CacheStatus;
    return Results.Ok(response);
});

app.MapPost("/ingest", async (IngestRequest request, IIngestPipeline pipeline) =>
{
    if (string.IsNullOrWhiteSpace(request.DocumentId))
        return Results.Problem("DocumentId is required.", statusCode: 400);
    if (string.IsNullOrWhiteSpace(request.FileName))
        return Results.Problem("FileName is required.", statusCode: 400);
    if (string.IsNullOrWhiteSpace(request.Content))
        return Results.Problem("Content is required.", statusCode: 400);
    if (request.Content.Length > MaxContentLength)
        return Results.Problem($"Content exceeds maximum size of {MaxContentLength / 1024 / 1024} MB.", statusCode: 400);

    var job = await pipeline.IngestAsync(request.DocumentId, request.FileName, request.Content);
    return Results.Accepted($"/ingest/{job.Id}", job);
});

app.MapGet("/ingest/{id}", (string id, JobStore jobStore) =>
{
    var job = jobStore.GetJob(id);
    return job is not null ? Results.Ok(job) : Results.NotFound();
});

app.MapPost("/ingest/{documentId}/reingest", async (string documentId, IngestRequest request, IIngestPipeline pipeline, ISemanticCache cache, IDocumentStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.FileName))
        return Results.Problem("FileName is required.", statusCode: 400);
    if (string.IsNullOrWhiteSpace(request.Content))
        return Results.Problem("Content is required.", statusCode: 400);
    if (request.Content.Length > MaxContentLength)
        return Results.Problem($"Content exceeds maximum size of {MaxContentLength / 1024 / 1024} MB.", statusCode: 400);

    // Retrieve old chunk IDs to invalidate semantic cache
    var oldChunkIds = await store.GetChunkIdsByDocumentIdAsync(documentId);
    if (oldChunkIds.Any())
    {
        await cache.InvalidateByChunkIdsAsync(oldChunkIds);
    }

    await store.DeleteByDocumentIdAsync(documentId);
    var job = await pipeline.IngestAsync(documentId, request.FileName, request.Content);
    return Results.Accepted($"/ingest/{job.Id}", job);
});

app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint(); // /metrics

app.MapFallbackToFile("index.html");

app.Run();

public record ChatRequest(string Prompt);
public record IngestRequest(string DocumentId, string FileName, string Content);