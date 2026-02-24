# Bedrock Semantic Cache

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![AWS Bedrock](https://img.shields.io/badge/AWS-Bedrock-FF9900.svg)](https://aws.amazon.com/bedrock/)
[![Valkey](https://img.shields.io/badge/Valkey-Redis--Stack-DC382D.svg)](https://valkey.io/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A **Retrieval-Augmented Generation (RAG)** API with a **semantic caching** layer that checks for semantically similar previous answers before calling the LLM, reducing latency and AWS Bedrock costs. Built with .NET 10 Clean Architecture, AWS Bedrock (Claude 3.5 Sonnet + Titan Embeddings), and Valkey/Redis Stack for HNSW vector search.

## How It Works

```
User Query
    |
    v
[1. Embed] ──> Amazon Titan (1536-dim vector)
    |
    v
[2. Cache Search] ──> Valkey HNSW index (cosine similarity)
    |
    ├── Score > 0.85 ──> [3a. HIT] ──> Return cached answer
    |
    └── Score < 0.85 ──> [3b. MISS]
                              |
                              v
                    [4. Retrieve] ──> Top-K document chunks (min score 0.70)
                              |
                              v
                    [5. Generate] ──> Claude 3.5 Sonnet via Bedrock
                              |
                              v
                    [6. Store] ──> Cache Q&A pair (background task)
                              |
                              v
                    [7. Return] ──> Response + X-Cache-Status header
```

Cache entries are linked to their source document chunk IDs. When a document is re-ingested, all cache entries referencing its old chunks are automatically invalidated.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| API | ASP.NET Core 10 Minimal APIs |
| Embeddings | Amazon Titan Text Embeddings v1 (1536-dim) |
| LLM | Claude 3.5 Sonnet via AWS Bedrock |
| Vector DB | Valkey / Redis Stack (RediSearch HNSW) |
| Observability | OpenTelemetry, Jaeger (traces), Prometheus (metrics) |
| Testing | xUnit, FluentAssertions, Moq, Testcontainers |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/products/docker-desktop/)
- AWS credentials with `bedrock:InvokeModel` permission

### Setup

```bash
# Start infrastructure (Valkey, Jaeger, Prometheus)
docker compose up -d

# Set AWS credentials
export AWS_ACCESS_KEY_ID=<your-key>
export AWS_SECRET_ACCESS_KEY=<your-secret>
export AWS_REGION=us-east-1

# Run the API
dotnet run --project Web/Web.csproj
```

### API Usage

**Ingest a document:**
```bash
curl -X POST http://localhost:5000/ingest \
  -H "Content-Type: application/json" \
  -d '{"documentId": "doc1", "fileName": "guide.txt", "content": "Your document text here..."}'
```

**Query the RAG system:**
```bash
curl -X POST http://localhost:5000/chat \
  -H "Content-Type: application/json" \
  -d '{"prompt": "What does the document say about..."}'
```

The response includes `X-Cache-Status: HIT` or `MISS` and `X-Latency-Ms` headers.

**Re-ingest a document** (invalidates related cache entries):
```bash
curl -X POST http://localhost:5000/ingest/doc1/reingest \
  -H "Content-Type: application/json" \
  -d '{"documentId": "doc1", "fileName": "guide.txt", "content": "Updated content..."}'
```

### All Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/chat` | Query the RAG system |
| `POST` | `/ingest` | Ingest a document (returns 202 with job ID) |
| `GET` | `/ingest/{id}` | Check ingestion job status |
| `POST` | `/ingest/{documentId}/reingest` | Re-ingest and invalidate stale cache |
| `GET` | `/health` | Valkey connectivity check |
| `GET` | `/metrics` | Prometheus scrape endpoint |

## Architecture

```
Web/                          Core/                        Infrastructure/
  Program.cs (DI, endpoints)    ChatOrchestrator             TitanEmbeddingService (Bedrock)
  CorrelationIdMiddleware       RetrieverService              ClaudeLlmService (Bedrock)
  SemanticCacheMiddleware       IngestPipeline                ValkeyDocumentStore (RediSearch)
  ValkeyHealthCheck             BackgroundTaskQueue           ValkeySemanticCache (RediSearch)
                                JobStore                      SentenceAwareChunker
```

**Clean Architecture layers:** Core has no dependency on Infrastructure or Web. Infrastructure adapts external systems (AWS, Valkey) to Core interfaces. Web is the HTTP host.

Background work (cache storage, document ingestion) is processed through a `Channel<T>`-backed queue with a hosted service, providing backpressure and graceful shutdown.

## Testing

```bash
# Full suite (unit + integration, needs Docker for Testcontainers)
dotnet test

# Unit tests only (no Docker needed)
dotnet test --filter "FullyQualifiedName~UnitTests"
```

## Observability

| Tool | URL | Purpose |
|------|-----|---------|
| Jaeger | http://localhost:16686 | Distributed traces |
| Prometheus | http://localhost:9090 | Metrics |

Request correlation IDs propagate through `X-Correlation-Id` headers and Serilog structured logs.

## License

[MIT](https://opensource.org/licenses/MIT)
