namespace Azure.DataApiBuilder.Config;

public record AuthenticationOptions(string Provider, JwtOptions? Jwt);
