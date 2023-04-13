namespace Azure.DataApiBuilder.Config;

public record GraphQLRuntimeOptions(bool Enabled = true, string Path = GraphQLRuntimeOptions.DEFAULT_PATH, bool AllowIntrospection = true)
{
    public const string DEFAULT_PATH = "/graphql";
}
