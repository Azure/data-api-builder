// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Service.GraphQLBuilder.CustomScalars;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedTypes;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Queries
{
    public static class StandardQueryInputs
    {
        public static InputObjectTypeDefinitionNode IdInputType() =>
            new(location: null,
                name: new("IdFilterInput"),
                description: new("Input type for adding ID filters"),
                directives: new List<DirectiveNode>(),
                fields: new List<InputValueDefinitionNode> {
                    new (location: null,
                        name: new ("eq"),
                        description: new ("Equals"),
                        type: new IdType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("neq"),
                        description: new ("Not Equals"),
                        type: new IdType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("isNull"),
                        description: new ("Not null test"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode BooleanInputType() =>
            new(location: null,
                name: new("BooleanFilterInput"),
                description: new("Input type for adding Boolean filters"),
                directives: new List<DirectiveNode>(),
                fields: new List<InputValueDefinitionNode> {
                    new (location: null,
                        name: new ("eq"),
                        description: new ("Equals"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("neq"),
                        description: new ("Not Equals"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("isNull"),
                        description: new ("Not null test"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>())
                });

        public static InputObjectTypeDefinitionNode ByteInputType() =>
            new(location: null,
                name: new("ByteFilterInput"),
                description: new("Input type for adding Byte filters"),
                directives: new List<DirectiveNode>(),
                fields: new List<InputValueDefinitionNode> {
                    new (location: null,
                        name: new ("eq"),
                        description: new ("Equals"),
                        type: new ByteType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gt"),
                        description: new ("Greater Than"),
                        type: new ByteType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gte"),
                        description: new ("Greater Than or Equal To"),
                        type: new ByteType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lt"),
                        description: new ("Less Than"),
                        type: new ByteType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lte"),
                        description: new ("Less Than or Equal To"),
                        type: new ByteType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("neq"),
                        description: new ("Not Equals"),
                        type: new ByteType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("isNull"),
                        description: new ("Not null test"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode ShortInputType() =>
            new(location: null,
                name: new("ShortFilterInput"),
                description: new("Input type for adding Short filters"),
                directives: new List<DirectiveNode>(),
                fields: new List<InputValueDefinitionNode> {
                    new (location: null,
                        name: new ("eq"),
                        description: new ("Equals"),
                        type: new ShortType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gt"),
                        description: new ("Greater Than"),
                        type: new ShortType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gte"),
                        description: new ("Greater Than or Equal To"),
                        type: new ShortType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lt"),
                        description: new ("Less Than"),
                        type: new ShortType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lte"),
                        description: new ("Less Than or Equal To"),
                        type: new ShortType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("neq"),
                        description: new ("Not Equals"),
                        type: new ShortType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("isNull"),
                        description: new ("Not null test"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode IntInputType() =>
            new(location: null,
                name: new("IntFilterInput"),
                description: new("Input type for adding Int filters"),
                directives: new List<DirectiveNode>(),
                fields: new List<InputValueDefinitionNode> {
                    new (location: null,
                        name: new ("eq"),
                        description: new ("Equals"),
                        type: new IntType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gt"),
                        description: new ("Greater Than"),
                        type: new IntType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gte"),
                        description: new ("Greater Than or Equal To"),
                        type: new IntType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lt"),
                        description: new ("Less Than"),
                        type: new IntType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lte"),
                        description: new ("Less Than or Equal To"),
                        type: new IntType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("neq"),
                        description: new ("Not Equals"),
                        type: new IntType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("isNull"),
                        description: new ("Not null test"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode LongInputType() =>
            new(location: null,
                name: new("LongFilterInput"),
                description: new("Input type for adding Long filters"),
                directives: new List<DirectiveNode>(),
                fields: new List<InputValueDefinitionNode> {
                    new (location: null,
                        name: new ("eq"),
                        description: new ("Equals"),
                        type: new LongType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gt"),
                        description: new ("Greater Than"),
                        type: new LongType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gte"),
                        description: new ("Greater Than or Equal To"),
                        type: new LongType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lt"),
                        description: new ("Less Than"),
                        type: new LongType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lte"),
                        description: new ("Less Than or Equal To"),
                        type: new LongType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("neq"),
                        description: new ("Not Equals"),
                        type: new LongType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("isNull"),
                        description: new ("Not null test"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode SingleInputType() =>
            new(location: null,
                name: new("SingleFilterInput"),
                description: new("Input type for adding Single filters"),
                directives: new List<DirectiveNode>(),
                fields: new List<InputValueDefinitionNode> {
                    new (location: null,
                        name: new ("eq"),
                        description: new ("Equals"),
                        type: new SingleType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gt"),
                        description: new ("Greater Than"),
                        type: new SingleType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gte"),
                        description: new ("Greater Than or Equal To"),
                        type: new SingleType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lt"),
                        description: new ("Less Than"),
                        type: new SingleType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lte"),
                        description: new ("Less Than or Equal To"),
                        type: new SingleType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("neq"),
                        description: new ("Not Equals"),
                        type: new SingleType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("isNull"),
                        description: new ("Not null test"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode FloatInputType() =>
            new(location: null,
                name: new("FloatFilterInput"),
                description: new("Input type for adding Float filters"),
                directives: new List<DirectiveNode>(),
                fields: new List<InputValueDefinitionNode> {
                    new (location: null,
                        name: new ("eq"),
                        description: new ("Equals"),
                        type: new FloatType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gt"),
                        description: new ("Greater Than"),
                        type: new FloatType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gte"),
                        description: new ("Greater Than or Equal To"),
                        type: new FloatType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lt"),
                        description: new ("Less Than"),
                        type: new FloatType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lte"),
                        description: new ("Less Than or Equal To"),
                        type: new FloatType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("neq"),
                        description: new ("Not Equals"),
                        type: new FloatType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("isNull"),
                        description: new ("Not null test"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode DecimalInputType() =>
            new(location: null,
                name: new("DecimalFilterInput"),
                description: new("Input type for adding Decimal filters"),
                directives: new List<DirectiveNode>(),
                fields: new List<InputValueDefinitionNode> {
                    new (location: null,
                        name: new ("eq"),
                        description: new ("Equals"),
                        type: new DecimalType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gt"),
                        description: new ("Greater Than"),
                        type: new DecimalType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gte"),
                        description: new ("Greater Than or Equal To"),
                        type: new DecimalType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lt"),
                        description: new ("Less Than"),
                        type: new DecimalType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lte"),
                        description: new ("Less Than or Equal To"),
                        type: new DecimalType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("neq"),
                        description: new ("Not Equals"),
                        type: new DecimalType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("isNull"),
                        description: new ("Not null test"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode StringInputType() =>
            new(location: null,
                name: new("StringFilterInput"),
                description: new("Input type for adding String filters"),
                directives: new List<DirectiveNode>(),
                fields: new List<InputValueDefinitionNode> {
                    new (location: null,
                        name: new ("eq"),
                        description: new ("Equals"),
                        type: new StringType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("contains"),
                        description: new ("Contains"),
                        type: new StringType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("notContains"),
                        description: new ("Not Contains"),
                        type: new StringType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("startsWith"),
                        description: new ("Starts With"),
                        type: new StringType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("endsWith"),
                        description: new ("Ends With"),
                        type: new StringType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("neq"),
                        description: new ("Not Equals"),
                        type: new StringType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("caseInsensitive"),
                        description: new ("Case Insensitive"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: new BooleanValueNode(false),
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("isNull"),
                        description: new ("Not null test"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode DateTimeInputType() =>
            new(location: null,
                name: new("DateTimeFilterInput"),
                description: new("Input type for adding DateTime filters"),
                directives: new List<DirectiveNode>(),
                fields: new List<InputValueDefinitionNode> {
                    new (location: null,
                        name: new ("eq"),
                        description: new ("Equals"),
                        type: new DateTimeType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gt"),
                        description: new ("Greater Than"),
                        type: new DateTimeType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("gte"),
                        description: new ("Greater Than or Equal To"),
                        type: new DateTimeType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lt"),
                        description: new ("Less Than"),
                        type: new DateTimeType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("lte"),
                        description: new ("Less Than or Equal To"),
                        type: new DateTimeType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("neq"),
                        description: new ("Not Equals"),
                        type: new DateTimeType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>()),
                    new (location: null,
                        name: new ("isNull"),
                        description: new ("Not null test"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode ByteArrayInputType() =>
            new(location: null,
                name: new("ByteArrayFilterInput"),
                description: new("Input type for adding ByteArray filters"),
                directives: new List<DirectiveNode>(),
                fields: new List<InputValueDefinitionNode> {
                    new (location: null,
                        name: new ("isNull"),
                        description: new ("Not null test"),
                        type: new BooleanType().ToTypeNode(),
                        defaultValue: null,
                        directives: new List<DirectiveNode>())
                }
            );

        public static Dictionary<string, InputObjectTypeDefinitionNode> InputTypes = new()
        {
            { "ID", IdInputType() },
            { BYTE_TYPE, ByteInputType() },
            { SHORT_TYPE, ShortInputType() },
            { INT_TYPE, IntInputType() },
            { LONG_TYPE, LongInputType() },
            { SINGLE_TYPE, SingleInputType() },
            { FLOAT_TYPE, FloatInputType() },
            { DECIMAL_TYPE, DecimalInputType() },
            { BOOLEAN_TYPE, BooleanInputType() },
            { STRING_TYPE, StringInputType() },
            { DATETIME_TYPE, DateTimeInputType() },
            { BYTEARRAY_TYPE, ByteArrayInputType() }
        };

        /// <summary>
        /// Returns true if the given inputObjectTypeName is one
        /// of the values in the InputTypes dictionary i.e.
        /// any of the scalar inputs like String, Boolean, Integer, Id etc.
        /// </summary>
        public static bool IsStandardInputType(string inputObjectTypeName)
        {
            HashSet<string> standardQueryInputNames =
                InputTypes.Values.ToList().Select(x => x.Name.Value).ToHashSet();
            return standardQueryInputNames.Contains(inputObjectTypeName);
        }
    }
}
