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
        public bool? Required { get; set; }

        /// <summary>
        /// Gets or sets the default value of the parameter, if any.
        /// </summary>
        public string? Default { get; set; }

        /// <summary>
        /// When true, the value supplied for this parameter (a text string) is automatically
        /// converted to an embedding vector via the configured embedding service before being
        /// passed to the stored procedure. The target stored-procedure parameter must be declared
        /// as VECTOR(N); DAB validates this at startup and rejects misconfigurations with a
        /// clear error.
        ///
        /// Only valid on stored-procedure entities when runtime.embeddings is configured and
        /// enabled. Currently supported on Azure SQL / SQL Server data sources only.
        /// </summary>
        public bool AutoEmbed { get; set; }
    }
}
