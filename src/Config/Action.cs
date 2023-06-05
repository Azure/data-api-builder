// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config
{
    /// <summary>
    /// A detailed version of the action describing what policy to apply
    /// and fields to include and/or exclude.
    /// </summary>
    /// <param name="Name">What kind of action is allowed.</param>
    /// <param name="Policy">Details about item-level security rules.</param>
    /// <param name="Fields">Details what fields to include or exclude</param>
    public record PermissionOperation(
        [property: JsonPropertyName("action"),
        JsonConverter(typeof(OperationEnumJsonConverter))]
        Operation Name,
        [property: JsonPropertyName("policy")]
        Policy? Policy,
        [property: JsonPropertyName("fields")]
        Field? Fields)
    {
        // Set of allowed operations for a request.
        public static readonly HashSet<Operation> ValidPermissionOperations = new() { Operation.Create, Operation.Read, Operation.Update, Operation.Delete };
        public static readonly HashSet<Operation> ValidStoredProcedurePermissionOperations = new() { Operation.Execute };
    }

    /// <summary>
    /// Class to specify custom converter used while deserializing action from json config
    /// to Action.Name.
    /// </summary>
    public class OperationEnumJsonConverter : JsonConverter<Operation>
    {
        // Creating another constant for "*" as we can't use the constant defined in
        // AuthorizationResolver class because of circular dependency.
        public static readonly string WILDCARD = "*";

        /// <inheritdoc/>
        public override Operation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? action = reader.GetString();
            if (WILDCARD.Equals(action))
            {
                return Operation.All;
            }

            return Enum.TryParse<Operation>(action, ignoreCase: true, out Operation operation) ? operation : Operation.None;
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, Operation value, JsonSerializerOptions options)
        {
            string valueToWrite = value is Operation.All ? WILDCARD : value.ToString();
            writer.WriteStringValue(valueToWrite);
        }
    }

    /// <summary>
    /// The operations supported by the service.
    /// </summary>
    public enum Operation
    {
        None,

        // *
        All,

        // Common Operations
        Delete, Read,

        // cosmosdb_nosql operations
        Upsert, Create,

        // Sql operations
        Insert, Update, UpdateGraphQL,

        // Additional
        UpsertIncremental, UpdateIncremental,

        // Only valid operation for stored procedures
        Execute
    }

    /// <summary>
    /// Details about what fields to include or exclude.
    /// Exclusions have precedence over inclusions.
    /// The * can be used as the wildcard character to indicate all fields.
    /// </summary>
    /// <param name="Include">All the fields specified here are included.</param>
    /// <param name="Exclude">All the fields specified here are excluded.</param>
    public class Field
    {
        public Field(HashSet<string>? include, HashSet<string>? exclude)
        {
            // Include being null indicates that it was not specified in the config.
            // This is used later (in authorization resolver) as an indicator that
            // Include resolves to all fields present in the config.
            // And so, unlike Exclude, we don't initialize it with an empty set when null.
            Include = include;

            // Exclude when null, is initialized with an empty set - no field is excluded.
            Exclude = exclude is null ? new() : new(exclude);
        }
        [property: JsonPropertyName("include")]
        public HashSet<string>? Include { get; set; }
        [property: JsonPropertyName("exclude")]
        public HashSet<string> Exclude { get; set; }
    }

    /// <summary>
    /// Details the item-level security rules.
    /// </summary> 
    /// <param name="Request">A rule to be checked before
    /// sending any request to the database.</param>
    /// <param name="Database">An OData style filter rule
    /// (predicate) that will be injected in the query sent to the database.</param>
    public class Policy
    {
        public Policy(string? request, string? database)
        {
            Request = request;
            Database = database;
        }

        [property: JsonPropertyName("request")]
        public string? Request { get; set; }
        [property: JsonPropertyName("database")]
        public string? Database { get; set; }
    }

    public enum RestMethod
    {
        Get,
        Post,
        Put,
        Patch,
        Delete
    };

    public enum GraphQLOperation
    {
        Query,
        Mutation
    };
}
