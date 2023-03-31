namespace Azure.DataApiBuilder.Config;

public record RuntimeOptions(RestRuntimeOptions Rest, GraphQLRuntimeOptions GraphQL, HostOptions Host);
