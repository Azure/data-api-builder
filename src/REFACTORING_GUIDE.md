# Data API Builder - Refactoring from Program/Startup Pattern to Minimal Hosting Model

## Overview

This refactoring modernizes the Azure Data API Builder service from the traditional ASP.NET Core Program/Startup pattern to the modern minimal hosting model introduced in .NET 6+. The refactoring maintains backward compatibility with existing tests while providing a more streamlined approach for new development.

## Changes Summary

### 1. **Program.cs** - Entry Point Refactoring
- **Before**: Traditional `Main()` method with `CreateHostBuilder()`
- **After**: Top-level statements with `WebApplication.CreateBuilder()`
- **Key Changes**:
  - Removed `Main()` method in favor of top-level statements
  - Added `CreateWebApplicationBuilder()` method for new minimal hosting pattern
  - Kept `CreateHostBuilder()` for backward compatibility with tests
  - Made `Program` class `partial` to support both patterns

### 2. **StartupExtensions.cs** - NEW FILE
Extracts service configuration and pipeline setup logic from `Startup.cs` into extension methods:
- `ConfigureServices(IServiceCollection, IConfiguration)` - Configures all DI services
- `ConfigurePipeline(WebApplication)` - Configures the HTTP request pipeline
- Helper methods for:
  - Config loader creation (FileSystem vs CosmosDB)
  - Telemetry configuration (AppInsights, OpenTelemetry, Azure Log Analytics, File Sink)
  - Logger configuration
  - Database services
  - Health checks
  - HTTP clients
  - Authentication & Authorization
  - GraphQL
  - Caching

### 3. **StartupConfiguration.cs** - NEW FILE
Static configuration class that holds settings previously in `Startup` class:
- Static properties for telemetry options
- Helper methods for:
  - Logger factory creation
  - Authentication configuration (V1 and V2)
  - CORS policy building
  - UI enablement checks
  - GraphQL schema eviction

### 4. **GraphQLServiceExtensions.cs** - NEW FILE
Extracts GraphQL configuration into dedicated extension methods:
- `AddGraphQLServices(IServiceCollection, GraphQLRuntimeOptions?)` - Configures GraphQL server
- Error filtering and handling
- Type converters for LocalTime/TimeOnly

### 5. **Startup.cs** - Legacy Compatibility
- **Status**: Marked as `[Obsolete]`
- **Purpose**: Maintains backward compatibility with existing tests
- **Behavior**: Delegates to new extension methods
- **Recommendation**: Use `Program.CreateWebApplicationBuilder()` for new code

## Architecture Benefits

### 1. **Separation of Concerns**
- Configuration logic separated into focused extension methods
- Each file has a single responsibility
- Easier to test and maintain

### 2. **Modern .NET Pattern**
- Aligns with current .NET best practices
- Uses minimal hosting model
- Top-level statements for cleaner entry point

### 3. **Backward Compatibility**
- Existing tests continue to work
- `CreateWebHostBuilder()` methods preserved
- Gradual migration path

### 4. **Improved Organization**
```
Program.cs                    → Entry point & builder creation
StartupExtensions.cs          → Service & pipeline configuration
StartupConfiguration.cs       → Static configuration & helpers
GraphQLServiceExtensions.cs   → GraphQL-specific configuration
Startup.cs (Obsolete)         → Legacy compatibility layer
```

## Migration Guide

### For New Development
```csharp
// Old Pattern
var host = Host.CreateDefaultBuilder(args)
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.UseStartup<Startup>();
    })
    .Build();

// New Pattern
var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureServices(builder.Configuration);
var app = builder.Build();
app.ConfigurePipeline();
app.Run();
```

### For Tests
Tests can continue using the legacy pattern:
```csharp
// Still supported for backward compatibility
var webHostBuilder = Program.CreateWebHostBuilder(args);
```

## Configuration Flow

### Minimal Hosting Model Flow
1. **Program.cs** (top-level) → Creates `WebApplicationBuilder`
2. **StartupExtensions.ConfigureServices()** → Registers all services
3. **WebApplicationBuilder.Build()** → Creates `WebApplication`
4. **StartupExtensions.ConfigurePipeline()** → Configures middleware
5. **WebApplication.Run()** → Starts the application

### Legacy Pattern Flow (for tests)
1. **Program.CreateHostBuilder()** → Creates `IHostBuilder`
2. **Startup.ConfigureServices()** → Delegates to extension methods
3. **Startup.Configure()** → Delegates to extension methods

## Key Extension Methods

### Service Configuration
- `ConfigureServices()` - Main entry point for DI setup
- `ConfigureTelemetry()` - Application Insights, OpenTelemetry, Azure Log Analytics
- `ConfigureLoggers()` - Logger factory setup
- `ConfigureAuthenticationAndAuthorization()` - Security setup
- `ConfigureCaching()` - FusionCache L2 setup

### Pipeline Configuration
- `ConfigurePipeline()` - Main entry point for middleware setup
- HTTPS redirection conditional setup
- CORS configuration
- Authentication/Authorization middleware
- GraphQL endpoint mapping
- Health check endpoint mapping

## Breaking Changes

None. The refactoring is fully backward compatible:
- Existing tests continue to work without modification
- Legacy `Startup` class still available (marked obsolete)
- All functionality preserved

## Future Improvements

1. **Remove Legacy Startup**: Once all tests are migrated to the new pattern
2. **Further Modularization**: Break down large extension methods into smaller focused methods
3. **Configuration Builders**: Consider fluent builder pattern for complex configurations
4. **Source Generators**: Potential use of source generators for DI registration

## Testing Considerations

### Unit Tests
- Test extension methods directly
- Mock `IServiceCollection` and `WebApplication`
- Verify service registrations

### Integration Tests
- Continue using existing test infrastructure
- Gradually migrate to use `CreateWebApplicationBuilder()`
- Both patterns supported during transition

## Performance Impact

**No Performance Impact**: The refactoring is purely organizational and does not change the runtime behavior or performance characteristics of the application.

## Rollback Strategy

If issues arise:
1. The legacy `Startup` class remains functional
2. Simply revert to calling `CreateHostBuilder()` instead of `CreateWebApplicationBuilder()`
3. All functionality preserved in extension methods

## Conclusion

This refactoring modernizes the codebase while maintaining full backward compatibility. The new structure is more aligned with current .NET best practices, easier to maintain, and provides a clearer separation of concerns. The gradual migration path ensures that existing tests and deployment processes continue to work without modification.
