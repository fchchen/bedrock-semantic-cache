# Bedrock Semantic Cache

A production-grade Retrieval-Augmented Generation (RAG) system with a semantic caching layer, built using .NET Clean Architecture, AWS Bedrock, and Valkey (Redis Stack).

## Project Overview
The system optimizes RAG performance and cost by implementing a semantic cache. It checks for semantically similar previous answers before querying the LLM. If a cache miss occurs, it retrieves relevant document chunks from a vector store, generates a response via AWS Bedrock (Claude), and stores the result back in the cache.

### Core Technologies
- **Runtime:** .NET 10 (C#)
- **Vector Database:** Valkey (Redis Stack) with RediSearch HNSW indexes.
- **AI Services:** AWS Bedrock (Titan for embeddings, Claude 3.5 Sonnet for generation).
- **Observability:** OpenTelemetry (Tracing via Jaeger, Metrics via Prometheus).
- **Logging:** Serilog.

### Architecture
- **Core:** Contains domain entities, interfaces, and the central `ChatOrchestrator`.
- **Infrastructure:** Implementations for Valkey vector stores, Bedrock AI services, and chunking strategies.
- **Web:** ASP.NET Minimal API exposing `/chat`, `/ingest`, and health/metrics endpoints.
- **Tests:** Unit tests (xUnit/Moq) and Integration tests (Testcontainers.Redis).

## Building and Running

### Prerequisites
- .NET 10 SDK
- Docker & Docker Compose
- AWS Credentials (configured via environment or CLI) for Bedrock access.

### Commands
- **Build Solution:**
  ```bash
  dotnet build
  ```
- **Run Application (Local):**
  ```bash
  dotnet run --project Web/Web.csproj
  ```
- **Run Infrastructure (Docker):**
  ```bash
  docker-compose up -d
  ```
- **Run Tests:**
  ```bash
  dotnet test
  ```

## Development Conventions
- **Test-Driven Development:** New infrastructure features should be verified with integration tests using `Testcontainers`.
- **Semantic Versioning:** Follow standard semver for any shared components.
- **Observability First:** Every new service method should be wrapped in an OpenTelemetry `Activity` span.
- **Surgical Updates:** When modifying existing code, adhere strictly to the established Clean Architecture boundaries.

## Key Endpoints
- `POST /chat`: Primary RAG interface `{ "prompt": "string" }`. Returns answers with `X-Cache-Status` header (HIT/MISS).
- `POST /ingest`: Ingest documents for retrieval.
- `GET /health`: System health status.
- `GET /metrics`: Prometheus metrics endpoint.

---
*Note: This file serves as instructional context for Gemini CLI and other AI agents interacting with this repository.*
