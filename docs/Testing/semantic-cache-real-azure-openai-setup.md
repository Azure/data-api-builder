# Testing Semantic Cache with Real Azure OpenAI

This guide explains how to run the Semantic Cache end-to-end (E2E) tests using a real Azure OpenAI embedding deployment.

## Prerequisites

- An Azure OpenAI resource with an embedding model deployed.
- Docker Desktop (for local Redis + database containers).

## Environment variables

Set these before running the E2E tests:

```bash
# Enable semantic cache E2E tests
export ENABLE_SEMANTIC_CACHE_E2E_TESTS=true

# Required: Azure OpenAI
export AZURE_OPENAI_ENDPOINT="https://<your-resource-name>.openai.azure.com"
export AZURE_OPENAI_API_KEY="<your-api-key>"

# Optional: embedding model name (defaults to text-embedding-3-small)
export AZURE_OPENAI_EMBEDDING_MODEL="text-embedding-3-small"

# Optional: Redis connection string override
# export TEST_REDIS_CONNECTION_STRING="localhost:6379,password=TestRedisPassword123"
```

## Run the E2E tests

1. Ensure you have a reachable database for the provider you want to test (MSSQL/MySQL/PostgreSQL).

1. Run the tests from the test project:

```bash
cd src/Service.Tests
ENABLE_SEMANTIC_CACHE_E2E_TESTS=true dotnet test --filter "TestCategory=SemanticCacheE2E&TestCategory=MSSQL"
```

## Troubleshooting

### Tests are skipped

If you see `Assert.Inconclusive` messages, verify:

- `ENABLE_SEMANTIC_CACHE_E2E_TESTS=true`
- `AZURE_OPENAI_ENDPOINT` and `AZURE_OPENAI_API_KEY` are set

### Redis connection issues

- Ensure the `redis-test` container is running.
- Or set `TEST_REDIS_CONNECTION_STRING` to point at your Redis instance.

### Database prerequisite errors

The E2E tests apply the standard Service.Tests schema + seed scripts (DatabaseSchema-*.sql).
If initialization fails, ensure your database container/instance is reachable and the connection string env vars used by Service.Tests are set.

## Notes

- These tests call Azure OpenAI and may incur cost.
