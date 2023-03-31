namespace Azure.DataApiBuilder.Config;

public record RestRuntimeOptions(bool Enabled = true, string? Path = "/api");
