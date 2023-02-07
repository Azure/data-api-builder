// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.CustomScalars
{
    /// <summary>
    /// 32 bit float
    /// based on: https://github.com/ChilliCream/hotchocolate/blob/c162ca29c23acc69cf81a33155d7384ad4dc9d0f/src/HotChocolate/Core/src/Types/Types/Scalars/FloatType.cs
    /// </summary>
    public class SingleType : FloatTypeBase<float>
    {
        public static readonly NameString TypeName = new("Single");
        public static readonly string SingleDescription = "IEEE 754 32 bit float";

        public SingleType()
            : this(float.MinValue, float.MaxValue)
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleType"/> class.
        /// </summary>
        public SingleType(float min, float max)
            : this(
                TypeName.Value,
                SingleDescription,
                min,
                max,
                BindingBehavior.Implicit)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleType"/> class.
        /// </summary>
        public SingleType(
            NameString name,
            string? description = null,
            float min = float.MinValue,
            float max = float.MaxValue,
            BindingBehavior bind = BindingBehavior.Explicit)
            : base(name, min, max, bind)
        {
            Description = description;
        }

        protected override float ParseLiteral(IFloatValueLiteral valueSyntax) =>
            valueSyntax.ToSingle();

        protected override FloatValueNode ParseValue(float runtimeValue) =>
            new(runtimeValue);
    }
}
