# Repository Guidelines

## Project Structure & Module Organization
- `Core/`: domain entities, interfaces, and orchestration services (`ChatOrchestrator`, `IngestPipeline`, `RetrieverService`). Keep this layer framework-agnostic.
- `Infrastructure/`: adapters for external systems (AWS Bedrock, Valkey/Redis, chunking strategies).
- `Web/`: ASP.NET Core Minimal API host (`Program.cs`), middleware, health checks, and runtime config.
- `Tests/`: `UnitTests/` for isolated logic and `IntegrationTests/` for Redis/API flows.
- Root ops files: `docker-compose.yml`, `Dockerfile`, `prometheus.yml`, `BedrockSemanticCache.slnx`.

## Build, Test, and Development Commands
- `dotnet restore`: restore NuGet packages.
- `dotnet build BedrockSemanticCache.slnx`: compile all projects.
- `dotnet run --project Web/Web.csproj`: run API locally.
- `docker compose up -d` (or `docker-compose up -d`): start Valkey, Jaeger, and Prometheus.
- `dotnet test`: run full unit + integration suite.
- `dotnet test --filter "FullyQualifiedName~UnitTests"`: run unit tests only when iterating quickly.

## Coding Style & Naming Conventions
- Follow standard C# style with 4-space indentation and file-scoped namespaces.
- Use `PascalCase` for types/methods/properties, `camelCase` for locals/parameters, and `I` prefix for interfaces.
- Tests and production files should be named by primary type (example: `ValkeySemanticCache.cs`, `ValkeySemanticCacheTests.cs`).
- `Nullable` and `ImplicitUsings` are enabled across projects; keep nullability warnings clean.
- Respect clean architecture boundaries: `Core` must not depend on `Infrastructure` or `Web`.

## Testing Guidelines
- Frameworks: xUnit, FluentAssertions, Moq, Testcontainers.Redis.
- Test names should describe behavior (example: `ProcessChatAsync_CacheHit_ReturnsCachedAnswer_DoesNotCallLlm`).
- Add or update unit tests for logic changes; add integration tests for Valkey, middleware, or API contract changes.
- Integration tests require Docker running locally.

## Commit & Pull Request Guidelines
- Use short, imperative commit subjects (history examples: `Add README.md...`, `Initial commit: ...`).
- Keep commits focused by concern (core logic, infra adapter, tests, docs).
- PRs should include: purpose, key design notes, test evidence (`dotnet test`), and any config/env changes.
- For endpoint behavior changes, include sample request/response and relevant headers (for example `X-Cache-Status`).

## Security & Configuration Tips
- Never commit secrets. Use environment variables for `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, and `AWS_REGION`.
- Keep non-secret defaults in `appsettings*.json`; document new required settings in `README.md`.
