using System;
using Azure.DataApiBuilder.Config;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    public static class CosmosTestHelper
    {
        public static readonly string DB_NAME = "graphqlTestDb";
        private static Lazy<RuntimeConfigPath>
            _runtimeConfigPath = new(() => TestHelper.GetRuntimeConfigPath(TestCategory.COSMOS));

        public static RuntimeConfigPath ConfigPath
        {
            get
            {
                return _runtimeConfigPath.Value;
            }
        }

        public static object GetItem(string id, string name = null, int numericVal = 4)
        {
            return new
            {
                id = id,
                name = string.IsNullOrEmpty(name) ? "test name" : name,
                dimension = "space",
                age = numericVal,
                myBooleanProp = true,
                anotherPojo = new
                {
                    anotherProp = "myname",
                    anotherIntProp = 55,
                    person = new
                    {
                        firstName = "A Person",
                        lastName = "the last name",
                        zipCode = 784298
                    }
                },
                character = new
                {
                    id = id,
                    name = "planet character",
                    type = "Mars",
                    homePlanet = 1,
                    primaryFunction = "test function",
                    star = new
                    {
                        name = name + "_star"
                    }
                }
            };
        }
    }
}
