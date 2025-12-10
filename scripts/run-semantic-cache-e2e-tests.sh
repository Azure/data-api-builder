#!/bin/bash
# Test runner for Semantic Cache E2E Tests
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

set -e

echo "🧪 Running Semantic Cache E2E Tests"
echo "===================================="

# Check if environment is setup
if [ "$ENABLE_SEMANTIC_CACHE_E2E_TESTS" != "true" ]; then
    echo "⚠️  ENABLE_SEMANTIC_CACHE_E2E_TESTS is not set to 'true'"
    echo "   Run: export ENABLE_SEMANTIC_CACHE_E2E_TESTS=true"
    echo "   Or use: ENABLE_SEMANTIC_CACHE_E2E_TESTS=true ./scripts/run-semantic-cache-e2e-tests.sh"
    echo ""
fi

# Check if containers are running
echo "🔍 Checking prerequisites..."
if ! docker ps --format "{{.Names}}" | grep -q "redis-test"; then
    echo "❌ Redis container not found. Run ./scripts/setup-semantic-cache-e2e.sh first"
    exit 1
fi

if ! docker ps --format "{{.Names}}" | grep -q "mssql-test"; then
    echo "❌ SQL Server container not found. Run ./scripts/setup-semantic-cache-e2e.sh first"
    exit 1
fi

echo "✅ Prerequisites check passed"
echo ""

# Navigate to test directory
cd "$(dirname "$0")/../src/Service.Tests"

# Run different test categories
echo "🔬 Running SQL Server semantic cache tests..."
ENABLE_SEMANTIC_CACHE_E2E_TESTS=true dotnet test \
    --filter "TestCategory=MSSQL&FullyQualifiedName~SemanticCache" \
    --logger:console \
    --verbosity:normal \
    --collect:"XPlat Code Coverage"

echo ""
echo "🔬 Running MySQL semantic cache tests (if MySQL is available)..."
if docker ps --format "{{.Names}}" | grep -q "mysql-test"; then
    ENABLE_SEMANTIC_CACHE_E2E_TESTS=true dotnet test \
        --filter "TestCategory=MySQL&FullyQualifiedName~SemanticCache" \
        --logger:console \
        --verbosity:normal \
        --no-build
else
    echo "⏭️  Skipping MySQL tests (container not running)"
fi

echo ""
echo "🔬 Running PostgreSQL semantic cache tests (if PostgreSQL is available)..."
if docker ps --format "{{.Names}}" | grep -q "postgres-test"; then
    ENABLE_SEMANTIC_CACHE_E2E_TESTS=true dotnet test \
        --filter "TestCategory=PostgreSQL&FullyQualifiedName~SemanticCache" \
        --logger:console \
        --verbosity:normal \
        --no-build
else
    echo "⏭️  Skipping PostgreSQL tests (container not running)"
fi

echo ""
echo "🎉 E2E Test run complete!"
echo ""
echo "📊 To view Redis cache contents:"
echo "   docker exec -it redis-test redis-cli -a TestRedisPassword123"
echo "   redis> KEYS dab:test:sc:*"
echo ""
echo "🔧 To run individual tests:"
echo "   dotnet test --filter 'FullyQualifiedName~TestSemanticCache_MSSQLDatabase_CacheHitAndMiss'"