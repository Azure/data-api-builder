namespace Azure.DataApiBuilder.Config;

public record RestRuntimeOptions(bool Enabled = true, string Path = RestRuntimeOptions.DEFAULT_PATH)
{
    public const string DEFAULT_PATH = "/api";
};
