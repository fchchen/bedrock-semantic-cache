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

1. **Embed** query → Titan embeddings (1536-dim vectors)
2. **Cache Search** → RediSearch HNSW cosine similarity on Valkey
3. **HIT** if similarity > 0.85 threshold → return cached answer
4. **Retrieve** top-K document chunks from document store (min score 0.70)
5. **Generate** via Claude 3.5 Sonnet with retrieved context
6. **Store** Q&A pair to cache as fire-and-forget background task
7. **Return** response with `X-Cache-Status` header (HIT/MISS)

### Key Infrastructure Details

- **ValkeyDocumentStore / ValkeySemanticCache**: Both use RediSearch FT.CREATE with HNSW indexes, 1536-dim FLOAT32 vectors, COSINE distance. Cache entries link to source chunk IDs for invalidation on re-ingestion.
- **TitanEmbeddingService / ClaudeLlmService**: AWS Bedrock calls wrapped with Polly retry (3 attempts, exponential backoff).
- **IngestPipeline**: Accepts documents, chunks via SentenceAwareChunker (default), embeds, stores. Returns immediately (202 Accepted) with job ID tracked in-memory via JobStore.
- **Cache invalidation**: When a document is re-ingested, old chunks are deleted and cache entries referencing those chunk IDs are invalidated.

## Coding Conventions

- C# style: 4-space indentation, file-scoped namespaces, `PascalCase` for types/methods/properties, `camelCase` for locals/parameters, `I` prefix for interfaces.
- `Nullable` and `ImplicitUsings` are enabled across all projects.
- Test naming: `MethodName_Scenario_ExpectedResult` (e.g., `ProcessChatAsync_CacheHit_ReturnsCachedAnswer_DoesNotCallLlm`).
- Testing stack: xUnit, FluentAssertions, Moq, Testcontainers.Redis, WebApplicationFactory.

## Environment Variables

```
AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_REGION
ConnectionStrings__Redis=localhost:6379,abortConnect=false
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
```
