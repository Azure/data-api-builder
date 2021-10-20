using System;
using System.Text;
using System.IO;
using Cosmos.GraphQL.Service.Controllers;
using Cosmos.GraphQL.Service.Resolvers;
using Cosmos.GraphQL.Services;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;

namespace Cosmos.GraphQL.Service.Tests.MsSql
{
    /// <summary>
    /// Initial setup of Sql related components for running Sql unit/integration tests
    /// </summary>
    public class MsSqlTestBase : IDisposable
    {
        protected IMetadataStoreProvider _metadataStoreProvider;
        protected IQueryExecutor _queryExecutor;
        protected IQueryBuilder _queryBuilder;
        protected IQueryEngine _queryEngine;
        protected GraphQLService _graphQLService;
        protected GraphQLController _graphQLController;
        protected DatabaseInteractor _databaseInteractor;

        public string IntegrationDatabaseName { get; set; } = "IntegrationDB";
        public string IntegrationTableName { get; set; } = "character";

        public MsSqlTestBase()
        {
            Init();
        }

        private void Init()
        {
            // Setup Schema and Resolvers
            //
            _metadataStoreProvider = new MetadataStoreProviderForTest();
            _metadataStoreProvider.StoreGraphQLSchema(MsSqlTestHelper.GraphQLSchema);
            _metadataStoreProvider.StoreQueryResolver(MsSqlTestHelper.GetQueryResolverJson(MsSqlTestHelper.CharacterByIdResolver));
            _metadataStoreProvider.StoreQueryResolver(MsSqlTestHelper.GetQueryResolverJson(MsSqlTestHelper.CharacterListResolver));

            // Setup Database Components
            //
            _queryExecutor = new QueryExecutor<SqlConnection>(MsSqlTestHelper.DataGatewayConfig);
            _queryBuilder = new MsSqlQueryBuilder();
            _queryEngine = new SqlQueryEngine(_metadataStoreProvider, _queryExecutor, _queryBuilder);

            // Setup Integration DB Components
            //
            _databaseInteractor = new DatabaseInteractor(_queryExecutor);

            // We are doing try/catch because the database will be created after the first test class
            // so on the second test class, we dont need to create it again.
            //
            try
            {
                CreateDatabase();
            }
            catch (AggregateException)
            {
            }

            CreateTable();
            InsertData();

            // Setup GraphQL Components
            //
            _graphQLService = new GraphQLService(_queryEngine, mutationEngine: null, _metadataStoreProvider);
            _graphQLController = new GraphQLController(logger: null, _queryEngine, mutationEngine: null, _graphQLService);
        }

        internal static DefaultHttpContext GetHttpContextWithBody(string data)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
            var httpContext = new DefaultHttpContext()
            {
                Request = { Body = stream, ContentLength = stream.Length }
            };
            return httpContext;
        }

        /// <summary>
        /// Creates a default database.
        /// </summary>
        private void CreateDatabase()
        {
            _databaseInteractor.CreateDatabase(IntegrationDatabaseName);
        }

        /// <summary>
        /// Creates a default table
        /// </summary>
        private void CreateTable()
        {
            _databaseInteractor.CreateTable(IntegrationTableName, "id int, name varchar(20), type varchar(20), homePlanet int, primaryFunction varchar(20)");
        }

        /// <summary>
        /// Inserts some default data into the table
        /// </summary>
        public virtual void InsertData()
        {
            _databaseInteractor.InsertData(IntegrationTableName, "'1', 'Mace', 'Jedi','1','Master'");
            _databaseInteractor.InsertData(IntegrationTableName, "'2', 'Plo Koon', 'Jedi','2','Master'");
            _databaseInteractor.InsertData(IntegrationTableName, "'3', 'Yoda', 'Jedi','3','Master'");
        }

        /// <summary>
        /// Drops all tables in the database when tests are complete.
        /// </summary>
        public void Dispose()
        {
            _databaseInteractor.DropTables();
        }
    }
}
