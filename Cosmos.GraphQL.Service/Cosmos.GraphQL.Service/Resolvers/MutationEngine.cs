using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.configurations;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using GraphQL.Execution;
using GraphQL.Language.AST;
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
        private readonly CosmosClientProvider _clientProvider;

        private ScriptOptions scriptOptions;

        public MutationEngine(CosmosClientProvider clientProvider)
        {
            this._clientProvider = clientProvider;
        }

        public void registerResolver(MutationResolver resolver)
        {
            if (resolvers.ContainsKey(resolver.graphQLMutationName))
            {
                resolvers.Remove(resolver.graphQLMutationName);
            }
            resolvers.Add(resolver.graphQLMutationName, resolver);
        }

        private JObject toJObject(IDictionary<string, ArgumentValue> parameters)
        {
            JObject jObject = new JObject();

            if (parameters != null)
            {
                foreach (var prop in parameters)
                {
                    jObject.Add(prop.Key, prop.Value.Value.ToString());
                }
            }
            else
            {
                jObject.Add("id", Guid.NewGuid().ToString());
            }

            return jObject;
        }
        
        public async Task<JsonDocument> execute(string graphQLMutationName, IDictionary<string, ArgumentValue> parameters)
        {
            if (!resolvers.TryGetValue(graphQLMutationName, out var resolver))
            {
                throw new NotImplementedException($"{graphQLMutationName} doesn't exist");
            }

            Container container = _clientProvider.getCosmosContainer();
            ScriptState<object> scriptState = await runAndInitializedScript();
            // scriptState = await scriptState.ContinueWithAsync(resolver.dotNetCodeRequestHandler);
            // scriptState = await scriptState.ContinueWithAsync(resolver.dotNetCodeResponseHandler);
            
            JObject jObject = toJObject(parameters);
            string jsonAsString = container.UpsertItemAsync(jObject).Result.Resource.ToString();
            return JsonDocument.Parse(jsonAsString);

            // switch (resolver.Operation)
            // {
            //     case Operation.Upsert.ToString():
            //     {
            //         JObject jObject = toJObject(parameters);
            //
            //         await container.UpsertItemAsync(jObject);
            //
            //         break;
            //         
            //     }
            //     default:
            //     {
            //         throw new NotImplementedException("not implemented");
            //     }
            // }



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
            
            Globals.Initialize(_clientProvider.getCosmosContainer());
            Globals global = new Globals();

            return await CSharpScript.RunAsync("Container container = Cosmos.Container;", this.scriptOptions, globals: global);
        }
    }
}