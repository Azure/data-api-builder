namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// Represents metadata for a parameter, including its name, description, requirement status, and default value.
    /// </summary>
    public class ParameterMetadata
    {
        /// <summary>
        /// Gets or sets the name of the parameter.
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// Gets or sets the description of the parameter.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the parameter is required.
        /// </summary>
        public bool Required { get; set; }

        /// <summary>
        /// Gets or sets the default value of the parameter, if any.
        /// </summary>
        public string? Default { get; set; }
    }
}
