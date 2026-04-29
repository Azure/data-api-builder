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
        /// When true, the parameter value (text) is automatically embedded via the
        /// EmbeddingService and the resulting vector is passed to the stored procedure.
        /// Only valid on stored-procedure entities when runtime.embeddings is configured.
        ///
        /// IMPORTANT: The target stored procedure parameter must be declared as VECTOR(N).
        /// SQL Server's metadata system reports VECTOR(N) and varbinary indistinguishably,
        /// so DAB cannot detect this misconfiguration at startup. If embed:true is applied
        /// to a non-VECTOR parameter (e.g., NVARCHAR or VARBINARY), the request will fail
        /// at runtime with a SQL error or return semantically incorrect results.
        /// It is the developer's responsibility to ensure the sproc parameter is VECTOR(N).
        /// </summary>
        public bool Embed { get; set; }
    }
}
