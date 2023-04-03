namespace Azure.DataApiBuilder.Config;

public record RestRuntimeOptions(bool Enabled = true, string Path = RestRuntimeOptions.DEFAULT_REST_PATH)
{
    public const string DEFAULT_REST_PATH = "/api";
};
