// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Service.GraphQLBuilder.CustomScalars;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes
{
    public class DefaultValueType : InputObjectType
    {
        protected override void Configure(IInputObjectTypeDescriptor descriptor)
        {
            descriptor.Name("DefaultValue");
            descriptor.OneOf();
            descriptor.Field(BYTE_TYPE).Type<ByteType>();
            descriptor.Field(SHORT_TYPE).Type<ShortType>();
            descriptor.Field(INT_TYPE).Type<IntType>();
            descriptor.Field(LONG_TYPE).Type<LongType>();
            descriptor.Field(STRING_TYPE).Type<StringType>();
            descriptor.Field(BOOLEAN_TYPE).Type<BooleanType>();
            descriptor.Field(SINGLE_TYPE).Type<SingleType>();
            descriptor.Field(FLOAT_TYPE).Type<FloatType>();
            descriptor.Field(DECIMAL_TYPE).Type<DecimalType>();
            descriptor.Field(DATETIME_TYPE).Type<DateTimeType>();
            descriptor.Field(BYTEARRAY_TYPE).Type<ByteArrayType>();
            descriptor.Field(LOCALTIME_TYPE).Type<HotChocolate.Types.NodaTime.LocalTimeType>();
        }
    }
}
