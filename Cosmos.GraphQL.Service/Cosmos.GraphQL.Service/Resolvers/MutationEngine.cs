using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.configurations;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using Cosmos.GraphQL.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Service.Resolvers
{
    public class MutationEngine
    {
        private readonly CosmosClientProvider _clientProvider;

        private readonly IMetadataStoreProvider _metadataStoreProvider;

        private ScriptOptions scriptOptions;

        public MutationEngine(CosmosClientProvider clientProvider, IMetadataStoreProvider metadataStoreProvider)
        {
            this._clientProvider = clientProvider;
            this._metadataStoreProvider = metadataStoreProvider;
        }

        public void registerResolver(MutationResolver resolver)
        {
            // TODO: add into system container/rp

            this._metadataStoreProvider.StoreMutationResolver(resolver);
        }

        private JObject execute(IDictionary<string, object> parameters, MutationResolver resolver)
        {
            JObject jObject;

            if (parameters != null)
            {
                var json = JsonConvert.SerializeObject(parameters);
                jObject = JObject.Parse(json);
            }
            else
            {
                jObject = JObject.Parse(String.Format("\"id\": {0}", Guid.NewGuid().ToString()));
            }

            var container = _clientProvider.getCosmosClient().GetDatabase(resolver.databaseName)
                .GetContainer(resolver.containerName);
            // TODO: check insertion type

            JObject res = container.UpsertItemAsync(jObject).Result.Resource;
            return res;
        }

        public async Task<JsonDocument> execute(string graphQLMutationName,
            IDictionary<string, object> parameters)
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