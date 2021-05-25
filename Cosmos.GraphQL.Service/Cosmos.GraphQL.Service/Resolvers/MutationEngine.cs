using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using Microsoft.Azure.Cosmos;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Service.Resolvers
{
   public class MutationEngine
    {
        private readonly Dictionary<string, MutationResolver> resolvers = new Dictionary<string, MutationResolver>();
        private ScriptOptions scriptOptions;

        public void registerResolver(MutationResolver resolver)
        {
            if (resolvers.ContainsKey(resolver.graphQLMutationName))
            {
                resolvers.Remove(resolver.graphQLMutationName);
            }
            resolvers.Add(resolver.graphQLMutationName, resolver);
        }

        private JObject toJObject(Dictionary<string, string> parameters)
        {
            JObject jObject = new JObject();

            foreach (var prop in parameters)
            {
                jObject.Add(prop.Key, prop.Value);
                
            }

            return jObject;
        }
        
        public async Task<string> execute(string graphQLMutationName, Dictionary<string, string> parameters)
        {
            if (!resolvers.TryGetValue(graphQLMutationName, out var resolver))
            {
                throw new NotImplementedException($"{graphQLMutationName} doesn't exist");
            }

            Container container = CosmosClientProvider.getCosmosContainer();
            
            switch (resolver.Operation)
            {
                case Operation.Upsert:
                {
                    JObject jObject = toJObject(parameters);

                    await container.UpsertItemAsync(jObject);

                    break;
                    
                }
                default:
                {
                    throw new NotImplementedException("not implemented");
                }
            }


            return "DONE";

            // ScriptState<object> scriptState = await runAndInitializedScript();
            // scriptState = await scriptState.ContinueWithAsync(resolver.dotNetCodeRequestHandler);
            // scriptState = await scriptState.ContinueWithAsync(resolver.dotNetCodeResponseHandler);
            // return scriptState.ReturnValue.ToString();

            // // assert resolver != null
            // int result = await CSharpScript.EvaluateAsync<int>(resolver.dotNetCodeRequestHandler);
            // return result.ToString();
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
            
            Globals.Initialize();
            Globals global = new Globals();

//            string code = "CosmosClient client = new CosmosClient(Cosmos.Endpoint, Cosmos.Key);";
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