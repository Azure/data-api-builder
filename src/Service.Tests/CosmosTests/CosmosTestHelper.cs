// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    public static class CosmosTestHelper
    {
        public static readonly string DB_NAME = "graphqlTestDb";

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
                },
                stars = new[]
                {
                    new
                    {
                        id = id,
                        name = name + "_star",
                        tag =
                            new {
                                id = id,
                                name = "tag1"
                            }
                    },
                    new {
                        id = id,
                        name = name + "_star",
                        tag =
                            new {
                                id = id,
                                name = "tag2"
                            }
                    }
                },
                suns = new[]
                {
                    new
                    {
                        id = id,
                        name = name + "_sun"
                    },
                    new {
                        id = id,
                        name = name + "_sun"
                    }
                },
                moons = new[]
                {
                    new
                    {
                        id = id,
                        name = numericVal + " moon",
                        details = "12 Craters",
                        moonAdditionalAttributes = new []
                        {
                            new
                            {
                                id = id + "v1",
                                name = "moonattr" + numericVal,
                                moreAttributes = new[]
                                {
                                    new
                                    {
                                        id = id + "v1v1",
                                        name = "moonattr" + numericVal
                                    },
                                    new
                                    {
                                        id = id + "v1v2",
                                        name = "moonattr" + numericVal
                                    }
                                }
                            },
                            new
                            {
                                id = id + "v2",
                                name = "moonattr" +  (numericVal + 1),
                                moreAttributes = new[]
                                {
                                    new
                                    {
                                        id = id + "v1v1",
                                        name = "moonattr" + numericVal
                                    },
                                    new
                                    {
                                        id = id + "v1v2",
                                        name = "moonattr" + numericVal
                                    }
                                }
                            }
                        }
                    },
                    new
                    {
                        id = id,
                        name = (numericVal + 1) + " moon",
                        details = "11 Craters",
                        moonAdditionalAttributes = new []
                        {
                            new
                            {
                                id = id + "v1",
                                name = "moonattr" + numericVal,
                                moreAttributes = new[]
                                {
                                    new
                                    {
                                        id = id + "v1v1",
                                        name = "moonattr" + numericVal
                                    },
                                    new
                                    {
                                        id = id + "v1v2",
                                        name = "moonattr" + numericVal
                                    }
                                }
                            },
                            new
                            {
                                id = id + "v2",
                                name = "moonattr" +  (numericVal + 1),
                                moreAttributes = new[]
                                {
                                    new
                                    {
                                        id = id + "v1v1",
                                        name = "moonattr" + numericVal
                                    },
                                    new
                                    {
                                        id = id + "v1v2",
                                        name = "moonattr" + numericVal
                                    }
                                }
                            }
                        }
                    }
                },
                earth = new
                {
                    id = id,
                    name = "blue earth" + numericVal,
                    type = "earth" + numericVal
                },
                additionalAttributes = new[]
                {
                    new
                    {
                        id = id + "v1",
                        name = "volcano" + numericVal
                    },
                    new
                    {
                        id = id + "v2",
                        name = "volcano" +  (numericVal + 1)
                    }
                },

                tags = new[] { "tag1", "tag2" }
            };
        }
    }
}
