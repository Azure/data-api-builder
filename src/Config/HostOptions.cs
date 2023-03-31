namespace Azure.DataApiBuilder.Config;

public record HostOptions(CorsOptions? Cors, AuthenticationOptions? Authentication, HostMode Mode = HostMode.Development);
