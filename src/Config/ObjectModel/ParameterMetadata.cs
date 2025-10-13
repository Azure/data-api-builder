namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// Represents metadata for a parameter, including its name, description, requirement status, and default value.
    /// </summary>
    public class ParameterMetadata
    {
        public required string Name { get; set; }
        public string? Description { get; set; }
        public bool Required { get; set; }
        public string? Default { get; set; }
    }
}
