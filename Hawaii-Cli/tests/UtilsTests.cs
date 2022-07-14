namespace Hawaii.Cli.Tests
{
    /// <summary>
    /// Tests for Utils methods.
    /// </summary>
    [TestClass]
    public class UtilsTests
    {
        /// <summary>
        /// Test to check if it successfully creates the rest object
        /// which can be either a boolean value
        /// or a RestEntitySettings object
        /// </summary>
        [TestMethod]
        public void TestGetRestDetails()
        {
            // when the rest is a boolean object
            object? restDetails = GetRestDetails("true");
            Assert.IsNotNull(restDetails);
            Assert.IsInstanceOfType(restDetails, typeof(bool));
            Assert.IsTrue((bool)restDetails);

            restDetails = GetRestDetails("True");
            Assert.IsNotNull(restDetails);
            Assert.IsInstanceOfType(restDetails, typeof(bool));
            Assert.IsTrue((bool)restDetails);

            restDetails = GetRestDetails("false");
            Assert.IsNotNull(restDetails);
            Assert.IsInstanceOfType(restDetails, typeof(bool));
            Assert.IsFalse((bool)restDetails);

            restDetails = GetRestDetails("False");
            Assert.IsNotNull(restDetails);
            Assert.IsInstanceOfType(restDetails, typeof(bool));
            Assert.IsFalse((bool)restDetails);

            // when rest is non-boolean string
            restDetails = GetRestDetails("book");
            Assert.AreEqual(new RestEntitySettings(Route: "/book"), restDetails);
        }

        /// <summary>
        /// Test to check if it successfully creates the graphql object which can be either a boolean value
        /// or a GraphQLEntitySettings object containing graphql type {singular, plural} based on the input
        /// </summary>
        [TestMethod]
        public void TestGetGraphQLDetails()
        {
            object? graphQlDetails = GetGraphQLDetails("true");
            Assert.IsNotNull(graphQlDetails);
            Assert.IsInstanceOfType(graphQlDetails, typeof(bool));
            Assert.IsTrue((bool)graphQlDetails);

            graphQlDetails = GetGraphQLDetails("True");
            Assert.IsNotNull(graphQlDetails);
            Assert.IsInstanceOfType(graphQlDetails, typeof(bool));
            Assert.IsTrue((bool)graphQlDetails);

            graphQlDetails = GetGraphQLDetails("false");
            Assert.IsNotNull(graphQlDetails);
            Assert.IsInstanceOfType(graphQlDetails, typeof(bool));
            Assert.IsFalse((bool)graphQlDetails);

            graphQlDetails = GetGraphQLDetails("False");
            Assert.IsNotNull(graphQlDetails);
            Assert.IsInstanceOfType(graphQlDetails, typeof(bool));
            Assert.IsFalse((bool)graphQlDetails);

            //when graphql is null
            Assert.IsNull(GetGraphQLDetails(null));

            // when graphql is non-boolean string
            graphQlDetails = GetGraphQLDetails("book");
            Assert.AreEqual(new GraphQLEntitySettings(Type: new SingularPlural(Singular: "book", Plural: "books")), graphQlDetails);

            // when graphql is a pair of string for custom singular, plural string.
            graphQlDetails = GetGraphQLDetails("book:plural_books");
            Assert.AreEqual(new GraphQLEntitySettings(Type: new SingularPlural(Singular: "book", Plural: "plural_books")), graphQlDetails);

            // Invalid graphql string
            graphQlDetails = GetGraphQLDetails("book:plural_books:ads");
            Assert.IsNull(graphQlDetails);
        }
    }
}
