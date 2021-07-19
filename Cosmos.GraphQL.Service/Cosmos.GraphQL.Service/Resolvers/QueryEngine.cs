using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.configurations;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using Microsoft.Azure.Cosmos;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Services
{
    public class QueryEngine
    {
        private readonly CosmosClientProvider _clientProvider;

        private ScriptOptions scriptOptions;
        private MetadataStoreProvider _metadataStoreProvider;

        public QueryEngine(CosmosClientProvider clientProvider, MetadataStoreProvider metadataStoreProvider)
        {
            this._clientProvider = clientProvider;
            this._metadataStoreProvider = metadataStoreProvider;
        }

        public void registerResolver(GraphQLQueryResolver resolver)
        {
            this._metadataStoreProvider.StoreQueryResolver(resolver);  
        }

        public async Task<JsonDocument> execute(string graphQLQueryName, Dictionary<string, string> parameters)
        {
            var resolver = _metadataStoreProvider.GetQueryResolver(graphQLQueryName);

            // ScriptState<object> scriptState = await runAndInitializedScript();
            // scriptState = await scriptState.ContinueWithAsync(resolver.dotNetCodeRequestHandler);
            // scriptState = await scriptState.ContinueWithAsync(resolver.dotNetCodeResponseHandler);

            var container = this._clientProvider.getCosmosClient().GetDatabase(resolver.databaseName).GetContainer(resolver.containerName);

            var querySpec = new QueryDefinition(resolver.parametrizedQuery);
            
            foreach(var parameterEntry in parameters)
            {
                querySpec.WithParameter(parameterEntry.Key, parameterEntry.Value);
            }

            var firstPage = container.GetItemQueryIterator<JObject>(querySpec).ReadNextAsync().Result;

            JObject firstItem = null;

            var iterator = firstPage.GetEnumerator();
            while (iterator.MoveNext()&& firstItem != null)
            {
                firstItem = iterator.Current;
            }
            
            JsonDocument  jsonDocument = JsonDocument.Parse(firstItem.ToString());

            return await Task.FromResult(jsonDocument);
        }
        
        // private async void executeInit()
        // {
        //     Assembly netStandardAssembly = Assembly.Load("netstandard");
        //     this.scriptOptions = ScriptOptions.Default
        //         .AddReferences(
        //             Assembly.GetAssembly(typeof(Microsoft.Azure.Cosmos.CosmosClient)),
        //             Assembly.GetAssembly(typeof(JsonObjectAttribute)),
        //             Assembly.GetCallingAssembly(),
        //             netStandardAssembly)
        //         .WithImports(
        //             "Microsoft.Azure.Cosmos",
        //             "Newtonsoft.Json",
        //             "Newtonsoft.Json.Linq");
        // }
        
        // private async Task<ScriptState<object>> runAndInitializedScript()
        // {
        //     executeInit();
        //     
        //     Globals.Initialize(_clientProvider.getCosmosContainer());
        //     Globals global = new Globals();
        //     return await CSharpScript.RunAsync("Container container = Cosmos.Container;", this.scriptOptions, globals: global);
        // }
    }
}