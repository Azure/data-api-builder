using HotChocolate.Types;

namespace Azure.DataGateway.Service.GraphQLBuilder.GraphQLTypes
{
    public class DefaultValueType : InputObjectType
    {
        protected override void Configure(IInputObjectTypeDescriptor descriptor)
        {
            descriptor.Name("DefaultValue");
            descriptor.Directive<OneOfDirectiveType>();
            descriptor.Field("byte").Type<ByteType>();
            descriptor.Field("short").Type<ShortType>();
            descriptor.Field("int").Type<IntType>();
            descriptor.Field("long").Type<LongType>();
            descriptor.Field("string").Type<StringType>();
            descriptor.Field("boolean").Type<BooleanType>();
            descriptor.Field("float").Type<FloatType>();
        }
    }
}
