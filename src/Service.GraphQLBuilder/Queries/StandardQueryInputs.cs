// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Queries
{
    public sealed class StandardQueryInputs
    {
        private static readonly ITypeNode _id = new NamedTypeNode(ScalarNames.ID);
        private static readonly ITypeNode _boolean = new NamedTypeNode(ScalarNames.Boolean);
        private static readonly ITypeNode _byte = new NamedTypeNode(ScalarNames.Byte);
        private static readonly ITypeNode _short = new NamedTypeNode(ScalarNames.Short);
        private static readonly ITypeNode _int = new NamedTypeNode(ScalarNames.Int);
        private static readonly ITypeNode _long = new NamedTypeNode(ScalarNames.Long);
        private static readonly ITypeNode _single = new NamedTypeNode("Single");
        private static readonly ITypeNode _float = new NamedTypeNode(ScalarNames.Float);
        private static readonly ITypeNode _decimal = new NamedTypeNode(ScalarNames.Decimal);
        private static readonly ITypeNode _string = new NamedTypeNode(ScalarNames.String);
        private static readonly ITypeNode _dateTime = new NamedTypeNode(ScalarNames.DateTime);
        private static readonly ITypeNode _localTime = new NamedTypeNode(ScalarNames.LocalTime);
        private static readonly ITypeNode _uuid = new NamedTypeNode(ScalarNames.UUID);

        private static readonly NameNode _eq = new("eq");
        private static readonly StringValueNode _eqDescription = new("Equals");
        private static readonly NameNode _neq = new("neq");
        private static readonly StringValueNode _neqDescription = new("Not Equals");
        private static readonly NameNode _isNull = new("isNull");
        private static readonly StringValueNode _isNullDescription = new("Not null test");
        private static readonly NameNode _gt = new("gt");
        private static readonly StringValueNode _gtDescription = new("Greater Than");
        private static readonly NameNode _gte = new("gte");
        private static readonly StringValueNode _gteDescription = new("Greater Than or Equal To");
        private static readonly NameNode _lt = new("lt");
        private static readonly StringValueNode _ltDescription = new("Less Than");
        private static readonly NameNode _lte = new("lte");
        private static readonly StringValueNode _lteDescription = new("Less Than or Equal To");
        private static readonly NameNode _contains = new("contains");
        private static readonly StringValueNode _containsDescription = new("Contains");
        private static readonly NameNode _notContains = new("notContains");
        private static readonly StringValueNode _notContainsDescription = new("Not Contains");
        private static readonly NameNode _startsWith = new("startsWith");
        private static readonly StringValueNode _startsWithDescription = new("Starts With");
        private static readonly NameNode _endsWith = new("endsWith");
        private static readonly StringValueNode _endsWithDescription = new("Ends With");
        private static readonly NameNode _in = new("in");
        private static readonly StringValueNode _inDescription = new("In");
        
        // List filter operations
        private static readonly NameNode _some = new("some");
        private static readonly StringValueNode _someDescription = new("At least one element matches the filter");
        private static readonly NameNode _none = new("none");
        private static readonly StringValueNode _noneDescription = new("No elements match the filter");
        private static readonly NameNode _all = new("all");
        private static readonly StringValueNode _allDescription = new("All elements match the filter");
        private static readonly NameNode _any = new("any");
        private static readonly StringValueNode _anyDescription = new("List is not empty");

        private static InputObjectTypeDefinitionNode IdInputType() =>
            CreateSimpleEqualsFilter("IdFilterInput", "Input type for adding ID filters", _id);

        private static InputObjectTypeDefinitionNode BooleanInputType() =>
            CreateSimpleEqualsFilter("BooleanFilterInput", "Input type for adding Boolean filters", _boolean);

        private static InputObjectTypeDefinitionNode ByteInputType() =>
            CreateComparableFilter("ByteFilterInput", "Input type for adding Byte filters", _byte);

        private static InputObjectTypeDefinitionNode ShortInputType() =>
            CreateComparableFilter("ShortFilterInput", "Input type for adding Short filters", _short);

        private static InputObjectTypeDefinitionNode IntInputType() =>
            CreateComparableFilter("IntFilterInput", "Input type for adding Int filters", _int);

        private static InputObjectTypeDefinitionNode LongInputType() =>
            CreateComparableFilter("LongFilterInput", "Input type for adding Long filters", _long);

        private static InputObjectTypeDefinitionNode SingleInputType() =>
            CreateComparableFilter("SingleFilterInput", "Input type for adding Single filters", _single);

        private static InputObjectTypeDefinitionNode FloatInputType() =>
            CreateComparableFilter("FloatFilterInput", "Input type for adding Float filters", _float);

        private static InputObjectTypeDefinitionNode DecimalInputType() =>
            CreateComparableFilter("DecimalFilterInput", "Input type for adding Decimal filters", _decimal);

        private static InputObjectTypeDefinitionNode StringInputType() =>
            CreateStringFilter("StringFilterInput", "Input type for adding String filters", _string);

        private static InputObjectTypeDefinitionNode DateTimeInputType() =>
            CreateComparableFilter("DateTimeFilterInput", "Input type for adding DateTime filters", _dateTime);

        public static InputObjectTypeDefinitionNode ByteArrayInputType() =>
            new(
                location: null,
                new NameNode("ByteArrayFilterInput"),
                new StringValueNode("Input type for adding ByteArray filters"),
                [],
                [
                    new(null, _isNull, _isNullDescription, _boolean, null, []),
                ]
            );

        private static InputObjectTypeDefinitionNode LocalTimeInputType() =>
            CreateComparableFilter("LocalTimeFilterInput", "Input type for adding LocalTime filters", _localTime);

        private static InputObjectTypeDefinitionNode UuidInputType() =>
            CreateStringFilter("UuidFilterInput", "Input type for adding Uuid filters", _uuid);

        private static InputObjectTypeDefinitionNode CreateSimpleEqualsFilter(
           string name,
           string description,
           ITypeNode type) =>
           new(
               location: null,
               new NameNode(name),
               new StringValueNode(description),
               [],
               [
                   new(null, _eq, _eqDescription, type, null, []),
                   new(null, _neq, _neqDescription, type, null, []),
                   new(null, _isNull, _isNullDescription, _boolean, null, []),
                   new(null, _in, _inDescription, new ListTypeNode(type), null, [])
               ]
           );

        private static InputObjectTypeDefinitionNode CreateComparableFilter(
            string name,
            string description,
            ITypeNode type) =>
            new(
                location: null,
                new NameNode(name),
                new StringValueNode(description),
                [],
                [
                    new(null, _eq, _eqDescription, type, null, []),
                    new(null, _gt, _gtDescription, type, null, []),
                    new(null, _gte, _gteDescription, type, null, []),
                    new(null, _lt, _ltDescription, type, null, []),
                    new(null, _lte, _lteDescription, type, null, []),
                    new(null, _neq, _neqDescription, type, null, []),
                    new(null, _isNull, _isNullDescription, _boolean, null, []),
                    new(null, _in, _inDescription, new ListTypeNode(type), null, [])
                ]
            );

        private static InputObjectTypeDefinitionNode CreateStringFilter(
            string name,
            string description,
            ITypeNode type) =>
            new(
                location: null,
                new NameNode(name),
                new StringValueNode(description),
                [],
                [
                    new(null, _eq, _eqDescription, type, null, []),
                    new(null, _contains, _containsDescription, type, null, []),
                    new(null, _notContains, _notContainsDescription, type, null, []),
                    new(null, _startsWith, _startsWithDescription, type, null, []),
                    new(null, _endsWith, _endsWithDescription, type, null, []),
                    new(null, _neq, _neqDescription, type, null, []),
                    new(null, _isNull, _isNullDescription, _boolean, null, []),
                    new(null, _in, _inDescription, new ListTypeNode(type), null, [])
                ]
            );

        private static InputObjectTypeDefinitionNode CreateListFilter(
            string name,
            string description,
            string elementFilterTypeName,
            ITypeNode elementScalarType) =>
            new(
                location: null,
                new NameNode(name),
                new StringValueNode(description),
                [],
                [
                    new(null, _some, _someDescription, new NamedTypeNode(elementFilterTypeName), null, []),
                    new(null, _none, _noneDescription, new NamedTypeNode(elementFilterTypeName), null, []),
                    new(null, _all, _allDescription, new NamedTypeNode(elementFilterTypeName), null, []),
                    new(null, _any, _anyDescription, _boolean, null, []),
                    new(null, _isNull, _isNullDescription, _boolean, null, []),
                    // Legacy support - these operations work directly on scalar values
                    new(null, _eq, _eqDescription, elementScalarType, null, []),
                    new(null, _contains, _containsDescription, elementScalarType, null, []),
                    new(null, _notContains, _notContainsDescription, elementScalarType, null, []),
                    new(null, _startsWith, _startsWithDescription, elementScalarType, null, []),
                    new(null, _endsWith, _endsWithDescription, elementScalarType, null, []),
                    new(null, _neq, _neqDescription, elementScalarType, null, []),
                    new(null, _in, _inDescription, new ListTypeNode(elementScalarType), null, [])
                ]
            );

        private static InputObjectTypeDefinitionNode IdListInputType() =>
            CreateListFilter("IdListFilterInput", "Input type for adding list of ID filters", "IdFilterInput", _id);

        private static InputObjectTypeDefinitionNode BooleanListInputType() =>
            CreateListFilter("BooleanListFilterInput", "Input type for adding list of Boolean filters", "BooleanFilterInput", _boolean);

        private static InputObjectTypeDefinitionNode ByteListInputType() =>
            CreateListFilter("ByteListFilterInput", "Input type for adding list of Byte filters", "ByteFilterInput", _byte);

        private static InputObjectTypeDefinitionNode ShortListInputType() =>
            CreateListFilter("ShortListFilterInput", "Input type for adding list of Short filters", "ShortFilterInput", _short);

        private static InputObjectTypeDefinitionNode IntListInputType() =>
            CreateListFilter("IntListFilterInput", "Input type for adding list of Int filters", "IntFilterInput", _int);

        private static InputObjectTypeDefinitionNode LongListInputType() =>
            CreateListFilter("LongListFilterInput", "Input type for adding list of Long filters", "LongFilterInput", _long);

        private static InputObjectTypeDefinitionNode SingleListInputType() =>
            CreateListFilter("SingleListFilterInput", "Input type for adding list of Single filters", "SingleFilterInput", _single);

        private static InputObjectTypeDefinitionNode FloatListInputType() =>
            CreateListFilter("FloatListFilterInput", "Input type for adding list of Float filters", "FloatFilterInput", _float);

        private static InputObjectTypeDefinitionNode DecimalListInputType() =>
            CreateListFilter("DecimalListFilterInput", "Input type for adding list of Decimal filters", "DecimalFilterInput", _decimal);

        private static InputObjectTypeDefinitionNode StringListInputType() =>
            CreateListFilter("StringListFilterInput", "Input type for adding list of String filters", "StringFilterInput", _string);

        private static InputObjectTypeDefinitionNode DateTimeListInputType() =>
            CreateListFilter("DateTimeListFilterInput", "Input type for adding list of DateTime filters", "DateTimeFilterInput", _dateTime);

        private static InputObjectTypeDefinitionNode LocalTimeListInputType() =>
            CreateListFilter("LocalTimeListFilterInput", "Input type for adding list of LocalTime filters", "LocalTimeFilterInput", _localTime);

        private static InputObjectTypeDefinitionNode UuidListInputType() =>
            CreateListFilter("UuidListFilterInput", "Input type for adding list of Uuid filters", "UuidFilterInput", _uuid);

        /// <summary>
        /// Gets a filter input object type by the corresponding scalar type name.
        /// </summary>
        /// <param name="scalarTypeName">
        /// The scalar type name.
        /// </param>
        /// <returns>
        /// The filter input object type.
        /// </returns>
        public static InputObjectTypeDefinitionNode GetFilterTypeByScalar(string scalarTypeName)
            => _instance._inputMap[scalarTypeName];

        /// <summary>
        /// Gets a list filter input object type by the corresponding scalar type name.
        /// </summary>
        /// <param name="scalarTypeName">
        /// The scalar type name (of the list element).
        /// </param>
        /// <returns>
        /// The list filter input object type.
        /// </returns>
        public static InputObjectTypeDefinitionNode GetListFilterTypeByScalar(string scalarTypeName)
            => _instance._listInputMap[scalarTypeName];

        /// <summary>
        /// Specifies if the given type name is a standard filter input object type.
        /// </summary>
        /// <param name="filterTypeName">
        /// The type name to check.
        /// </param>
        /// <returns>
        /// <c>true</c> if the type name is a standard filter input object type; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsFilterType(string filterTypeName)
            => _instance._standardQueryInputNames.Contains(filterTypeName);

        private static readonly StandardQueryInputs _instance = new();
        private readonly Dictionary<string, InputObjectTypeDefinitionNode> _inputMap = [];
        private readonly Dictionary<string, InputObjectTypeDefinitionNode> _listInputMap = [];
        private readonly HashSet<string> _standardQueryInputNames = [];

        private StandardQueryInputs()
        {
            AddInputType(ScalarNames.ID, IdInputType());
            AddInputType(ScalarNames.UUID, UuidInputType());
            AddInputType(ScalarNames.Byte, ByteInputType());
            AddInputType(ScalarNames.Short, ShortInputType());
            AddInputType(ScalarNames.Int, IntInputType());
            AddInputType(ScalarNames.Long, LongInputType());
            AddInputType(SINGLE_TYPE, SingleInputType());
            AddInputType(ScalarNames.Float, FloatInputType());
            AddInputType(ScalarNames.Decimal, DecimalInputType());
            AddInputType(ScalarNames.Boolean, BooleanInputType());
            AddInputType(ScalarNames.String, StringInputType());
            AddInputType(ScalarNames.DateTime, DateTimeInputType());
            AddInputType(ScalarNames.ByteArray, ByteArrayInputType());
            AddInputType(ScalarNames.LocalTime, LocalTimeInputType());
            
            // Add list filter types
            AddListInputType(ScalarNames.ID, IdListInputType());
            AddListInputType(ScalarNames.UUID, UuidListInputType());
            AddListInputType(ScalarNames.Byte, ByteListInputType());
            AddListInputType(ScalarNames.Short, ShortListInputType());
            AddListInputType(ScalarNames.Int, IntListInputType());
            AddListInputType(ScalarNames.Long, LongListInputType());
            AddListInputType(SINGLE_TYPE, SingleListInputType());
            AddListInputType(ScalarNames.Float, FloatListInputType());
            AddListInputType(ScalarNames.Decimal, DecimalListInputType());
            AddListInputType(ScalarNames.Boolean, BooleanListInputType());
            AddListInputType(ScalarNames.String, StringListInputType());
            AddListInputType(ScalarNames.DateTime, DateTimeListInputType());
            AddListInputType(ScalarNames.LocalTime, LocalTimeListInputType());

            void AddInputType(string inputTypeName, InputObjectTypeDefinitionNode inputType)
            {
                _inputMap.Add(inputTypeName, inputType);
                _standardQueryInputNames.Add(inputType.Name.Value);
            }
            
            void AddListInputType(string inputTypeName, InputObjectTypeDefinitionNode inputType)
            {
                _listInputMap.Add(inputTypeName, inputType);
                _standardQueryInputNames.Add(inputType.Name.Value);
            }
        }
    }
}
