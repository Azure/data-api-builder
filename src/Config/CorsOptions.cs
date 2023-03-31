namespace Azure.DataApiBuilder.Config;

public record CorsOptions(string[] Origins, bool AllowCredentials = false);
