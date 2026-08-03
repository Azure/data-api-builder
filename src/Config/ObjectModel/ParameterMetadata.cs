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
        /// converted to an embedding vector via the configured embedding service. The serialized
        /// vector (a JSON array string like "[0.012,-0.045,...]") is then passed as the parameter
        /// value to the stored procedure.
        ///
        /// The stored-procedure parameter must be a string-compatible type
        /// (NVARCHAR/VARCHAR/CHAR/TEXT) — DAB itself is provider-neutral; the per-provider
        /// metadata check lives in the metadata provider for that database (currently only
        /// MSSQL implements it). If the sproc needs vector arithmetic (e.g., Azure SQL's
        /// VECTOR_DISTANCE), it must do the CAST to VECTOR(N) itself.
        ///
        /// Only valid on stored-procedure entities when runtime.embeddings is configured
        /// and enabled.
        /// </summary>
        public bool AutoEmbed { get; set; }
    }
}
