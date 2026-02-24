# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
dotnet restore                                        # Restore NuGet packages
dotnet build BedrockSemanticCache.slnx                # Build all projects
dotnet run --project Web/Web.csproj                   # Run API locally (port 5000)
docker compose up -d                                  # Start Valkey, Jaeger, Prometheus
dotnet test                                           # Run full test suite (unit + integration)
dotnet test --filter "FullyQualifiedName~UnitTests"   # Unit tests only (no Docker needed)
dotnet test --filter "FullyQualifiedName~IntegrationTests"  # Integration tests only (needs Docker)
```

Integration tests use Testcontainers and require Docker running locally.

## Architecture

This is a RAG (Retrieval-Augmented Generation) system with a semantic caching layer, built with .NET 10 Clean Architecture.

### Layer Dependencies

```
Web → Core, Infrastructure
Infrastructure → Core
Core → (no project dependencies)
Tests → Web, Infrastructure, Core
```

**Core must never depend on Infrastructure or Web.** Core contains domain entities, service interfaces, and orchestration logic. Infrastructure contains adapters for external systems (AWS Bedrock, Valkey). Web is the ASP.NET Core Minimal API host.

### 7-Step Chat Orchestration (ChatOrchestrator)

1. **Embed** query → Titan embeddings (configurable dimension, default 1536)
2. **Cache Search** → RediSearch HNSW cosine similarity on Valkey
3. **HIT** if similarity > 0.85 threshold → return cached answer
4. **Retrieve** top-K document chunks from document store (min score 0.70)
5. **Generate** via Claude 3.5 Sonnet with retrieved context
6. **Store** Q&A pair to cache via background task queue
7. **Return** response with `X-Cache-Status` header (HIT/MISS)

### Background Task Processing

Background work (cache storage after chat, document ingestion) is processed through `IBackgroundTaskQueue` — a `Channel<T>`-backed bounded queue (capacity 100) drained by `BackgroundTaskProcessor` (a hosted service). This provides backpressure and graceful shutdown instead of fire-and-forget `Task.Run`.

### Key Infrastructure Details

- **ValkeyDocumentStore / ValkeySemanticCache**: Both use RediSearch FT.CREATE with HNSW indexes, FLOAT32 vectors, COSINE distance. Vector dimension is configured via `OrchestratorSettings.VectorDimension`. Cache entries link to source chunk IDs for invalidation on re-ingestion. Cleanup queries paginate (no hardcoded LIMIT cap) and escape RediSearch special characters in TAG queries.
- **TitanEmbeddingService / ClaudeLlmService**: AWS Bedrock calls wrapped with Polly retry (3 attempts, exponential backoff).
- **IngestPipeline**: Accepts documents, chunks via SentenceAwareChunker (default), embeds chunks in parallel (SemaphoreSlim, max 5 concurrent), stores. Returns immediately (202 Accepted) with job ID tracked in-memory via JobStore.
- **Cache invalidation**: When a document is re-ingested, old chunks are deleted and cache entries referencing those chunk IDs are invalidated.

### API Validation & Error Handling

All endpoints validate input and return RFC 7807 Problem Details on errors. `POST /ingest` enforces a 10 MB content size limit. Unhandled exceptions are caught by the global `UseExceptionHandler` middleware.

### Health Check

`ValkeyHealthCheck` issues an actual `PING` to Redis (not just checking `IsConnected`).

## Coding Conventions

- C# style: 4-space indentation, file-scoped namespaces, `PascalCase` for types/methods/properties, `camelCase` for locals/parameters, `I` prefix for interfaces.
- `Nullable` and `ImplicitUsings` are enabled across all projects.
- Use `[GeneratedRegex]` source generators instead of `Regex.Split`/`Regex.Match` per call.
- Test naming: `MethodName_Scenario_ExpectedResult` (e.g., `ProcessChatAsync_CacheHit_ReturnsCachedAnswer_DoesNotCallLlm`).
- Testing stack: xUnit, FluentAssertions, Moq, Testcontainers.Redis, WebApplicationFactory.
- Use `TestHelpers.WaitUntilAsync` for polling instead of `Task.Delay` in tests to avoid flakiness.
- API integration tests must mock all DI dependencies that Minimal API endpoints resolve as parameters (DI resolution happens before handler body).

## Environment Variables

```
AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_REGION
ConnectionStrings__Redis=localhost:6379,abortConnect=false
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
```
