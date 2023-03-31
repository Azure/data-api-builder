namespace Azure.DataApiBuilder.Config;

public record GraphQLRuntimeOptions(bool Enabled = true, string? Path = "/graphql", bool AllowIntrospection = true);
