using System.Collections.Generic;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// Metadata required to resolve a specific GraphQL type.
    /// </summary>
    /// <param name="Table">The name of the table that this GraphQL type corresponds to.</param>
    /// <param name="IsPaginationType">Shows if the type is a *Connection pagination result type</param>
    /// <param name="DatabaseName">The name of the database that this GraphQL type corresponds to.</param>
    public record GraphQLType(string Table, bool IsPaginationType, string DatabaseName)
    {
        /// <summary>
        /// Metadata required to resolve specific fields of the GraphQL type.
        /// </summary>
        public Dictionary<string, GraphQLField> Fields { get; init; } = new();
    }

    public enum GraphQLRelationshipType
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
    public record GraphQLField
    {
        /// <summary>
        /// The kind of relationship that links the type that this field is
        /// part of and the type that this field has.
        /// </summary>
        public GraphQLRelationshipType RelationshipType { get; init; } = GraphQLRelationshipType.None;
        /// <summary>
        /// The name of the associative table is used to link the two types in
        /// a ManyToMany relationship.
        /// </summary>
        public string AssociativeTable { get; init; } = null!;

        /// <summary>
        /// The name of the foreign key that should be used to do the join on
        /// the left side of the join. Depending on the RelationshipType this
        /// foreign key has some different requirements:
        ///
        /// 1. For OneToOne and ManyToOne it means that this foreign key should
        ///    be defined on the table of the type that this field is part of.
        /// 2. For ManyToMany this foreign key should be defined on the
        ///    associative table and it should reference the table this field
        ///    is part of.
        /// 3. For OneToMany this field should not be set.
        /// </summary>
        public string LeftForeignKey { get; init; } = null!;

        /// <summary>
        /// The name of the foreign key that should be used to do the join on
        /// the right side of the join. Depending on the RelationshipType this
        /// foreign key has some different requirements:
        ///
        /// 1. For OneToOne and OneToMany it means that this foreign key should
        ///    be defined on the table of the type that this field has.
        /// 2. For ManyToMany this foreign key should be defined on the
        ///    associative table and it should reference the table of the type
        ///    that this field has.
        /// 3. For ManyToOne this field should not be set.
        /// </summary>
        public string RightForeignKey { get; init; } = null!;
    }
}
