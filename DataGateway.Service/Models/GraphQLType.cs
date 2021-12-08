using System.Collections.Generic;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// Metadata required to resolve a specific GraphQL type.
    /// </summary>
    public class GraphqlType
    {
        /// <summary>
        /// The name of the table that this GraphQL type corresponds to.
        /// </summary>
        public string Table { get; set; }
        /// <summary>
        /// Metadata required to resolve specific fields of the GraphQL type.
        /// </summary>
        public Dictionary<string, GraphqlField> Fields { get; set; } = new();
    }

    public enum GraphqlRelationshipType
    {
        None,
        OneToOne,
        OneToMany,
        ManyToOne,
        ManyToMany,
    }

    /// <summary>
    /// Metadata required to resolve a specific field of a GraphQL type.
    /// </summary>
    public class GraphqlField
    {
        /// <summary>
        /// The kind of relationship that links the type that this field is
        /// part of and the type that this field has.
        /// </summary>
        public GraphqlRelationshipType RelationshipType { get; set; } = GraphqlRelationshipType.None;
        /// <summary>
        /// The name of the foreign key that should be used to do the join.
        /// </summary>
        public string ForeignKey { get; set; }
    }
}
