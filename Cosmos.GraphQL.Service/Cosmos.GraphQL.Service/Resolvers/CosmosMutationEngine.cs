using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Services;
using GraphQL.Execution;
using Microsoft.Azure.Cosmos;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Service.Resolvers
{
    public class CosmosMutationEngine : IMutationEngine
    {
        private readonly IClientProvider<CosmosClient> _clientProvider;

        private readonly IMetadataStoreProvider _metadataStoreProvider;

        public CosmosMutationEngine(IClientProvider<CosmosClient> clientProvider, IMetadataStoreProvider metadataStoreProvider)
        {
            _clientProvider = clientProvider;
            _metadataStoreProvider = metadataStoreProvider;
        }

        /// <summary>
        /// Persists resolver configuration. When resolver config,
        /// is received from REST endpoint and not configuration file.
        /// </summary>
        /// <param name="resolver">The given mutation resolver.</param>
        public void RegisterResolver(MutationResolver resolver)
        {
            // TODO: add into system container/rp

            this._metadataStoreProvider.StoreMutationResolver(resolver);
        }

        private JObject execute(IDictionary<string, ArgumentValue> parameters, MutationResolver resolver)
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

            var container = _clientProvider.GetClient().GetDatabase(resolver.databaseName)
                .GetContainer(resolver.containerName);
            // TODO: check insertion type

            JObject res = container.UpsertItemAsync(jObject).Result.Resource;
            return res;
        }

        /// <summary>
        /// Executes the mutation query and return result as JSON object asynchronously.
        /// </summary>
        /// <param name="graphQLMutationName">name of the GraphQL mutation query.</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public async Task<JsonDocument> Execute(string graphQLMutationName,
            IDictionary<string, ArgumentValue> parameters)
        {

            var resolver = _metadataStoreProvider.GetMutationResolver(graphQLMutationName);
            
            JObject jObject = execute(parameters, resolver);
            return JsonDocument.Parse(jObject.ToString());

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
    }
}