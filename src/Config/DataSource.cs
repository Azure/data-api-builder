namespace Azure.DataApiBuilder.Config;

public record DataSource(DataSourceType DatabaseType, string ConnectionString, Dictionary<string, object> Options);
