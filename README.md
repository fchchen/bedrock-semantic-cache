# Bedrock Semantic Cache

[![.NET 10](https://img.shields.io/badge/.NET-10-blue.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![AWS Bedrock](https://img.shields.io/badge/AWS-Bedrock-orange.svg)](https://aws.amazon.com/bedrock/)
[![Valkey](https://img.shields.io/badge/Valkey-Redis--Stack-red.svg)](https://valkey.io/)

A production-grade **Retrieval-Augmented Generation (RAG)** system with a **Semantic Caching** layer. Built using .NET Clean Architecture, AWS Bedrock (Claude 3.5 Sonnet + Titan), and Valkey (Redis Stack) for high-performance vector search.

## 🚀 Overview

This system optimizes RAG performance and cost by implementing a semantic cache. It checks for semantically similar previous answers before querying the LLM, significantly reducing latency and AWS Bedrock token costs.

### Key Features
- **Semantic Caching:** Checks the cache index (Valkey) for similar Q&A pairs before calling the LLM.
- **Vector Retrieval:** Uses RediSearch HNSW indexes for high-speed vector similarity search (1536-dim).
- **AWS Bedrock Integration:**
    - **Amazon Titan:** For generating text embeddings.
    - **Claude 3.5 Sonnet:** For high-quality, context-aware response generation.
- **Clean Architecture:** Domain-driven design with Core, Infrastructure, Web, and Test layers.
- **Observability:** Full OpenTelemetry instrumentation for traces (Jaeger) and metrics (Prometheus).
- **Auto-Invalidation:** Cache entries are linked to source document chunk IDs and automatically invalidated when documents are updated.

## 🏗️ Architecture

The system follows a 7-step orchestrator workflow:
1. **Embed:** Convert user query into a vector using Amazon Titan.
2. **Cache Search:** Search the Semantic Cache index in Valkey (Cosine similarity).
3. **HIT/MISS:** If a similar answer exists (threshold > 0.85), return it immediately.
4. **Retrieve:** On a cache miss, retrieve the Top-K relevant chunks from the Document Index.
5. **Generate:** Send the prompt + context to Claude 3.5 Sonnet via Bedrock.
6. **Store:** Save the new Q&A pair and source chunk IDs back to the cache (background task).
7. **Return:** Deliver the final response to the user.

## 🛠️ Tech Stack
- **Framework:** ASP.NET Core 10 (Minimal APIs)
- **Vector DB:** Valkey / Redis Stack (RediSearch)
- **AI/LLM:** AWS Bedrock (Claude 3.5 Sonnet, Titan G1)
- **Observability:** OpenTelemetry, Jaeger, Prometheus
- **Testing:** xUnit, FluentAssertions, Moq, Testcontainers

## 🏁 Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- AWS Credentials with `bedrock:InvokeModel` permissions.

### Setup
1. **Clone the repository:**
   ```bash
   git clone https://github.com/fchchen/bedrock-semantic-cache.git
   cd bedrock-semantic-cache
   ```

2. **Spin up Infrastructure:**
   ```bash
   docker-compose up -d
   ```
   This starts Valkey (port 6379), Jaeger (port 16686), and Prometheus (port 9090).

3. **Configure AWS:**
   Ensure your environment variables (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_REGION`) are set.

4. **Run the API:**
   ```bash
   dotnet run --project Web/Web.csproj
   ```

### API Endpoints
- `POST /ingest`: Upload document content for indexing.
- `POST /chat`: Query the RAG system.
- `GET /health`: Check system and Valkey connectivity.
- `GET /metrics`: Scrape Prometheus metrics.

## 🧪 Testing
Run the full suite of unit and integration tests (uses Testcontainers):
```bash
dotnet test
```

## 📊 Observability
- **Traces:** View detailed request spans at `http://localhost:16686` (Jaeger).
- **Metrics:** Monitor cache hit ratios and latencies via Prometheus at `http://localhost:9090`.

## 📜 License
This project is licensed under the MIT License.
