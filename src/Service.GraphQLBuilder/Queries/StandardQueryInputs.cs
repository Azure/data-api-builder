// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Service.GraphQLBuilder.CustomScalars;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.NodaTime;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Queries
{
    public static class StandardQueryInputs
    {
        public static InputObjectTypeDefinitionNode IdInputType() =>
            new(
                location: null,
                new NameNode("IdFilterInput"),
                new StringValueNode("Input type for adding ID filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new(null, new NameNode("eq"), new StringValueNode("Equals"), new IdType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("neq"), new StringValueNode("Not Equals"), new IdType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("isNull"), new StringValueNode("Not null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode BooleanInputType() =>
            new(
                location: null,
                new NameNode("BooleanFilterInput"),
                new StringValueNode("Input type for adding Boolean filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new(null, new NameNode("eq"), new StringValueNode("Equals"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("neq"), new StringValueNode("Not Equals"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("isNull"), new StringValueNode("Not null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode ByteInputType() =>
            new(
                location: null,
                new NameNode("ByteFilterInput"),
                new StringValueNode("Input type for adding Byte filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new(null, new NameNode("eq"), new StringValueNode("Equals"), new ByteType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gt"), new StringValueNode("Greater Than"), new ByteType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gte"), new StringValueNode("Greater Than or Equal To"), new ByteType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lt"), new StringValueNode("Less Than"), new ByteType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lte"), new StringValueNode("Less Than or Equal To"), new ByteType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("neq"), new StringValueNode("Not Equals"), new ByteType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("isNull"), new StringValueNode("Not null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode ShortInputType() =>
            new(
                location: null,
                new NameNode("ShortFilterInput"),
                new StringValueNode("Input type for adding Short filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new(null, new NameNode("eq"), new StringValueNode("Equals"), new ShortType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gt"), new StringValueNode("Greater Than"), new ShortType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gte"), new StringValueNode("Greater Than or Equal To"), new ShortType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lt"), new StringValueNode("Less Than"), new ShortType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lte"), new StringValueNode("Less Than or Equal To"), new ShortType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("neq"), new StringValueNode("Not Equals"), new ShortType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("isNull"), new StringValueNode("Not null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode IntInputType() =>
            new(
                location: null,
                new NameNode("IntFilterInput"),
                new StringValueNode("Input type for adding Int filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new(null, new NameNode("eq"), new StringValueNode("Equals"), new IntType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gt"), new StringValueNode("Greater Than"), new IntType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gte"), new StringValueNode("Greater Than or Equal To"), new IntType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lt"), new StringValueNode("Less Than"), new IntType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lte"), new StringValueNode("Less Than or Equal To"), new IntType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("neq"), new StringValueNode("Not Equals"), new IntType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("isNull"), new StringValueNode("Not null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode LongInputType() =>
            new(
                location: null,
                new NameNode("LongFilterInput"),
                new StringValueNode("Input type for adding Long filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new(null, new NameNode("eq"), new StringValueNode("Equals"), new LongType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gt"), new StringValueNode("Greater Than"), new LongType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gte"), new StringValueNode("Greater Than or Equal To"), new LongType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lt"), new StringValueNode("Less Than"), new LongType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lte"), new StringValueNode("Less Than or Equal To"), new LongType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("neq"), new StringValueNode("Not Equals"), new LongType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("isNull"), new StringValueNode("Not null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode SingleInputType() =>
            new(
                location: null,
                new NameNode("SingleFilterInput"),
                new StringValueNode("Input type for adding Single filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new(null, new NameNode("eq"), new StringValueNode("Equals"), new SingleType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gt"), new StringValueNode("Greater Than"), new SingleType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gte"), new StringValueNode("Greater Than or Equal To"), new SingleType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lt"), new StringValueNode("Less Than"), new SingleType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lte"), new StringValueNode("Less Than or Equal To"), new SingleType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("neq"), new StringValueNode("Not Equals"), new SingleType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("isNull"), new StringValueNode("Not null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode FloatInputType() =>
            new(
                location: null,
                new NameNode("FloatFilterInput"),
                new StringValueNode("Input type for adding Float filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new(null, new NameNode("eq"), new StringValueNode("Equals"), new FloatType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gt"), new StringValueNode("Greater Than"), new FloatType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gte"), new StringValueNode("Greater Than or Equal To"), new FloatType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lt"), new StringValueNode("Less Than"), new FloatType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lte"), new StringValueNode("Less Than or Equal To"), new FloatType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("neq"), new StringValueNode("Not Equals"), new FloatType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("isNull"), new StringValueNode("Not null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode DecimalInputType() =>
            new(
                location: null,
                new NameNode("DecimalFilterInput"),
                new StringValueNode("Input type for adding Decimal filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new(null, new NameNode("eq"), new StringValueNode("Equals"), new DecimalType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gt"), new StringValueNode("Greater Than"), new DecimalType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gte"), new StringValueNode("Greater Than or Equal To"), new DecimalType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lt"), new StringValueNode("Less Than"), new DecimalType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lte"), new StringValueNode("Less Than or Equal To"), new DecimalType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("neq"), new StringValueNode("Not Equals"), new DecimalType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("isNull"), new StringValueNode("Not null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode StringInputType() =>
            new(
                location: null,
                new NameNode("StringFilterInput"),
                new StringValueNode("Input type for adding String filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new(null, new NameNode("eq"), new StringValueNode("Equals"), new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("contains"), new StringValueNode("Contains"), new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("notContains"), new StringValueNode("Not Contains"), new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("startsWith"), new StringValueNode("Starts With"), new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("endsWith"), new StringValueNode("Ends With"), new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("neq"), new StringValueNode("Not Equals"), new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("isNull"), new StringValueNode("Is null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode DateTimeInputType() =>
            new(
                location: null,
                new NameNode("DateTimeFilterInput"),
                new StringValueNode("Input type for adding DateTime filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new(null, new NameNode("eq"), new StringValueNode("Equals"), new DateTimeType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gt"), new StringValueNode("Greater Than"), new DateTimeType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("gte"), new StringValueNode("Greater Than or Equal To"), new DateTimeType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lt"), new StringValueNode("Less Than"), new DateTimeType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("lte"), new StringValueNode("Less Than or Equal To"), new DateTimeType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("neq"), new StringValueNode("Not Equals"), new DateTimeType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("isNull"), new StringValueNode("Not null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode ByteArrayInputType() =>
            new(
                location: null,
                new NameNode("ByteArrayFilterInput"),
                new StringValueNode("Input type for adding ByteArray filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new(null, new NameNode("isNull"), new StringValueNode("Is null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode LocalTimeInputType() =>
            new(
                location: null,
                new NameNode("LocalTimeFilterInput"),
                new StringValueNode("Input type for adding LocalTime filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                            new(null, new NameNode("eq"), new StringValueNode("Equals"), new LocalTimeType().ToTypeNode(), null, new List<DirectiveNode>()),
                            new(null, new NameNode("gt"), new StringValueNode("Greater Than"), new LocalTimeType().ToTypeNode(), null, new List<DirectiveNode>()),
                            new(null, new NameNode("gte"), new StringValueNode("Greater Than or Equal To"), new LocalTimeType().ToTypeNode(), null, new List<DirectiveNode>()),
                            new(null, new NameNode("lt"), new StringValueNode("Less Than"), new LocalTimeType().ToTypeNode(), null, new List<DirectiveNode>()),
                            new(null, new NameNode("lte"), new StringValueNode("Less Than or Equal To"), new LocalTimeType().ToTypeNode(), null, new List<DirectiveNode>()),
                            new(null, new NameNode("neq"), new StringValueNode("Not Equals"), new LocalTimeType().ToTypeNode(), null, new List<DirectiveNode>()),
                            new(null, new NameNode("isNull"), new StringValueNode("Is null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode UuidInputType() =>
            new(
                location: null,
                new NameNode("UuidFilterInput"),
                new StringValueNode("Input type for adding Uuid filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new(null, new NameNode("eq"), new StringValueNode("Equals"), new UuidType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("contains"), new StringValueNode("Contains"), new UuidType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("notContains"), new StringValueNode("Not Contains"), new UuidType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("startsWith"), new StringValueNode("Starts With"), new UuidType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("endsWith"), new StringValueNode("Ends With"), new UuidType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("neq"), new StringValueNode("Not Equals"), new UuidType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new(null, new NameNode("isNull"), new StringValueNode("Is null test"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static Dictionary<string, InputObjectTypeDefinitionNode> InputTypes = new()
        {
            { "ID", IdInputType() },
            { UUID_TYPE, UuidInputType() },
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
            { BYTEARRAY_TYPE, ByteArrayInputType() },
            { LOCALTIME_TYPE, LocalTimeInputType() },
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
