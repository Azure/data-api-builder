using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.configurations;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Services
{
    public class QueryEngine
    {
        private readonly Dictionary<string, GraphQLQueryResolver> resolvers = new Dictionary<string, GraphQLQueryResolver>();
        private ScriptOptions scriptOptions;

        public void registerResolver(GraphQLQueryResolver resolver)
        {
            if (resolvers.ContainsKey(resolver.GraphQLQueryName))
            {
                resolvers.Remove(resolver.GraphQLQueryName);
            }
            resolvers.Add(resolver.GraphQLQueryName, resolver);
        }

        public async Task<JsonDocument> execute(string graphQLQueryName, Dictionary<string, string> parameters)
        {
            if (!resolvers.TryGetValue(graphQLQueryName, out var resolver))
            {
                throw new NotImplementedException($"{graphQLQueryName} doesn't exist");
            }

            ScriptState<object> scriptState = await runAndInitializedScript();
            scriptState = await scriptState.ContinueWithAsync(resolver.dotNetCodeRequestHandler);
            scriptState = await scriptState.ContinueWithAsync(resolver.dotNetCodeResponseHandler);
            JsonDocument  jsonDocument = JsonDocument.Parse(scriptState.ReturnValue.ToString());


            return jsonDocument;
        }
        
        // private async Task<string> execute()
        // {
        //     CosmosCSharpScriptResponse response = await CosmosCSharpScript.ExecuteAsync(this.scriptState, code, this.scriptOptions);
        //     this.scriptState = response.ScriptState;
        //     this.scriptOptions = response.ScriptOption;
        //
        //     object returnValue = this.scriptState?.ReturnValue;
        //     Dictionary<string, object> mimeBundle = ToRichOutputMIMEBundle(returnValue);
        //
        //     result.Data = mimeBundle;
        // }


        private async void executeInit()
        {
            Assembly netStandardAssembly = Assembly.Load("netstandard");
            this.scriptOptions = ScriptOptions.Default
                .AddReferences(
                    Assembly.GetAssembly(typeof(Microsoft.Azure.Cosmos.CosmosClient)),
                    Assembly.GetAssembly(typeof(JsonObjectAttribute)),
                    Assembly.GetCallingAssembly(),
                    netStandardAssembly)
                .WithImports(
                    "Microsoft.Azure.Cosmos",
                    "Newtonsoft.Json",
                    "Newtonsoft.Json.Linq");
        }
        
        private async Task<ScriptState<object>> runAndInitializedScript()
        {
            executeInit();
            
            Globals.Initialize(ConfigurationProvider.getInstance().cred);
            Globals global = new Globals();

            string code = "CosmosClient client = new CosmosClient(Cosmos.Endpoint, Cosmos.Key);"
                          + "string MyDatabaseName = \"myDB\";"
                          + "string MyContainerName = \"myCol\";"
                          + "Database database = await client.CreateDatabaseIfNotExistsAsync(MyDatabaseName);"
                          + "Container container = await database.CreateContainerIfNotExistsAsync(MyContainerName, \"/id\", 400);";
            //string code = "";
            return await CSharpScript.RunAsync(code, this.scriptOptions, globals: global);
        }
    }
}