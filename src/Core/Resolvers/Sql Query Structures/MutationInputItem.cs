// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Resolvers.Sql_Query_Structures
{
    internal class MutationInputItem
    {
        public bool IsMultipleInputType;

        public IDictionary<string, MutationInputItem?>? Input;

        public List<IDictionary<string, MutationInputItem?>>? InputList;

        public MutationInputItem(bool isMultiplInputType, object input)
        {
            IsMultipleInputType = isMultiplInputType;
            if (isMultiplInputType)
            {
                InputList = (List<IDictionary<string, MutationInputItem?>>)input;
            }
            else
            {
                Input = (IDictionary<string, MutationInputItem?>)input;
            }
        }
    }
}
