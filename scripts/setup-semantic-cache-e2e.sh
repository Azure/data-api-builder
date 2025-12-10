#!/bin/bash
# Setup script for Semantic Cache E2E Testing
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

set -e

echo "🚀 Setting up Semantic Cache E2E Testing Environment"
echo "=================================================="

# Function to check if a container is running
check_container() {
    local container_name=$1
    if docker ps --format "table {{.Names}}" | grep -q "^$container_name$"; then
        echo "✅ $container_name is already running"
        return 0
    else
        echo "❌ $container_name is not running"
        return 1
    fi
}

# Function to wait for service to be ready
wait_for_service() {
    local service_name=$1
    local check_command=$2
    local max_attempts=30
    local attempt=1
    
    echo "⏳ Waiting for $service_name to be ready..."
    while [ $attempt -le $max_attempts ]; do
        if eval "$check_command" >/dev/null 2>&1; then
            echo "✅ $service_name is ready!"
            return 0
        fi
        echo "   Attempt $attempt/$max_attempts - waiting 2 seconds..."
        sleep 2
        attempt=$((attempt + 1))
    done
    
    echo "❌ $service_name failed to start after $max_attempts attempts"
    return 1
}

echo "📦 Setting up containers..."

# Start Redis container for semantic caching
echo "🔴 Setting up Redis container..."
if ! check_container "redis-test"; then
    echo "   Starting Redis container..."
    docker run -d \
        --name redis-test \
        -p 6379:6379 \
        redis:7-alpine \
        redis-server --requirepass TestRedisPassword123 --appendonly yes
    
    wait_for_service "Redis" "docker exec redis-test redis-cli -a TestRedisPassword123 ping"
else
    echo "   Redis container already running"
fi

# Start SQL Server container
echo "🔵 Setting up SQL Server container..."
if ! check_container "mssql-test"; then
    echo "   Starting SQL Server container..."
    docker run -d \
        --name mssql-test \
        -e "ACCEPT_EULA=Y" \
        -e "SA_PASSWORD=YourStrong@Passw0rd" \
        -e "MSSQL_PID=Developer" \
        -p 1433:1433 \
        mcr.microsoft.com/mssql/server:2022-latest
    
    wait_for_service "SQL Server" "docker exec mssql-test /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P 'YourStrong@Passw0rd' -Q 'SELECT 1'"
else
    echo "   SQL Server container already running"
fi

# Start MySQL container (optional)
echo "🟡 Setting up MySQL container..."
if ! check_container "mysql-test"; then
    echo "   Starting MySQL container..."
    docker run -d \
        --name mysql-test \
        -e MYSQL_ROOT_PASSWORD=test123 \
        -e MYSQL_DATABASE=DabTestDb \
        -p 3306:3306 \
        mysql:8.0
    
    wait_for_service "MySQL" "docker exec mysql-test mysql -uroot -ptest123 -e 'SELECT 1'"
else
    echo "   MySQL container already running"
fi

# Start PostgreSQL container (optional)
echo "🟢 Setting up PostgreSQL container..."
if ! check_container "postgres-test"; then
    echo "   Starting PostgreSQL container..."
    docker run -d \
        --name postgres-test \
        -e POSTGRES_PASSWORD=test123 \
        -e POSTGRES_DB=DabTestDb \
        -p 5432:5432 \
        postgres:15
    
    wait_for_service "PostgreSQL" "docker exec postgres-test pg_isready -U postgres"
else
    echo "   PostgreSQL container already running"
fi

# Setup mock embedding service (simple HTTP mock)
echo "🟠 Setting up Mock Embedding Service..."
if ! check_container "mock-openai"; then
    echo "   Creating mock responses directory..."
    mkdir -p /tmp/mock-openai
    
    cat > /tmp/mock-openai/embeddings.json << 'EOF'
{
  "object": "list",
  "data": [
    {
      "object": "embedding",
      "index": 0,
      "embedding": [0.1, 0.2, 0.3, 0.4, 0.5]
    }
  ],
  "model": "text-embedding-ada-002",
  "usage": {
    "prompt_tokens": 5,
    "total_tokens": 5
  }
}
EOF
    
    echo "   Starting mock OpenAI service..."
    docker run -d \
        --name mock-openai \
        -p 8080:8080 \
        -v /tmp/mock-openai:/usr/share/nginx/html \
        nginx:alpine
    
    wait_for_service "Mock OpenAI" "curl -f http://localhost:8080/embeddings.json"
else
    echo "   Mock OpenAI service already running"
fi

echo ""
echo "🎉 Environment setup complete!"
echo ""
echo "📋 Container Status:"
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}" | grep -E "(redis-test|mssql-test|mysql-test|postgres-test|mock-openai)"

echo ""
echo "🔧 To run the E2E tests:"
echo "   export ENABLE_SEMANTIC_CACHE_E2E_TESTS=true"
echo "   cd src/Service.Tests"
echo "   dotnet test --filter TestCategory=SemanticCacheE2E --logger:console;verbosity=detailed"
echo ""
echo "🧹 To clean up when done:"
echo "   docker stop redis-test mssql-test mysql-test postgres-test mock-openai"
echo "   docker rm redis-test mssql-test mysql-test postgres-test mock-openai"
echo ""
echo "🔍 To inspect Redis cache entries:"
echo "   docker exec -it redis-test redis-cli -a TestRedisPassword123"
echo "   > KEYS dab:test:sc:*"
echo "   > HGETALL <key_name>"