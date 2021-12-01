using System.Collections.Generic;

namespace Azure.DataGateway.Service.Models
{
    public class GraphqlType
    {
        public string Table { get; set; }
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

    public class GraphqlField
    {
        public GraphqlRelationshipType RelationshipType { get; set; } = GraphqlRelationshipType.None;
        /// <summary>
        /// The name of the foreign key that should be used to do the join.
        /// </summary>
        public string ForeignKey { get; set; }
    }
}
