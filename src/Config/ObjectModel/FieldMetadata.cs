namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// Represents metadata for a field in an entity.
    /// </summary>
    public class FieldMetadata
    {
        /// <summary>
        /// The name of the field (must match a database column).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The alias for the field (must be unique per entity).
        /// </summary>
        public string? Alias { get; set; }

        /// <summary>
        /// The description for the field.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Whether this field is a key (must be unique).
        /// </summary>
        public bool PrimaryKey { get; set; }
    }
}