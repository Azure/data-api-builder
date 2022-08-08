using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass]
    public abstract class RestApiTestBase : SqlTestBase
    {
        protected static RestService _restService;
        protected static RestController _restController;
        protected static readonly string _integrationEntityName = "Book";
        protected static readonly string _integrationTableName = "books";
        protected static readonly string _entityWithCompositePrimaryKey = "Review";
        protected static readonly string _tableWithCompositePrimaryKey = "reviews";
        protected const int STARTING_ID_FOR_TEST_INSERTS = 5001;
        protected static readonly string _integration_NonAutoGenPK_EntityName = "Magazine";
        protected static readonly string _integration_NonAutoGenPK_TableName = "magazines";
        protected static readonly string _integration_AutoGenNonPK_EntityName = "Comic";
        protected static readonly string _integration_AutoGenNonPK_TableName = "comics";
        protected static readonly string _Composite_NonAutoGenPK_EntityName = "Stock";
        protected static readonly string _Composite_NonAutoGenPK_TableName = "stocks";
        protected static readonly string _integrationEntityHasColumnWithSpace = "Broker";
        protected static readonly string _integrationTableHasColumnWithSpace = "brokers";
        protected static readonly string _integrationTieBreakEntity = "Author";
        protected static readonly string _integrationTieBreakTable = "authors";
        protected static readonly string _integrationMappingEntity = "Tree";
        protected static readonly string _integrationMappingTable = "trees";
        protected static readonly string _integrationMappingDifferentEntity = "Shrub";
        protected static readonly string _integrationBrokenMappingEntity = "Fungus";
        protected static readonly string _integrationUniqueCharactersEntity = "ArtOfWar";
        protected static readonly string _integrationUniqueCharactersTable = "aow";
        protected static readonly string _nonExistentEntityName = "!@#$%^&*()_+definitely_nonexistent_entity!@#$%^&*()_+";
        protected static readonly string _emptyTableEntityName = "Empty";
        protected static readonly string _emptyTableTableName = "empty_table";
        protected static readonly string _simple_all_books = "books_view_all";
        protected static readonly string _simple_subset_stocks = "stocks_view_selected";
        protected static readonly string _composite_subset_bookPub = "books_publishers_view_composite";

        public abstract string GetQuery(string key);
    }
}
