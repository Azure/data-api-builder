using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Services;

namespace Azure.DataGateway.Service.Resolvers
{
    public class SqlExecuteStructure : BaseSqlQueryStructure
    {
        // Holds the final, resolved parameters that will be passed when building the execute stored procedure query
        public Dictionary<string, object> ProcedureParameters { get; set; }
        /// <summary>
        /// requestParams will be resolved from either the request querystring or body by this point
        /// construct the ProcedureParameters dictionary through resolving requestParams and defaults from config/metadata
        /// </summary>
        public SqlExecuteStructure(
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            IDictionary<string, object?> requestParams)
        : base(sqlMetadataProvider, entityName: entityName)
        {
            StoredProcedureDefinition storedProcedureDefinition = SqlMetadataProvider.GetStoredProcedureDefinition(entityName);
            ProcedureParameters = new();
            foreach((string paramKey, ParameterDefinition paramValue) in storedProcedureDefinition.Parameters)
            {
                // populate with request param if able
                if (requestParams.TryGetValue(paramKey, out object? requestParamValue))
                {
                    ProcedureParameters.Add(paramKey, requestParamValue!);
                }
                else
                { // fill with default value
                    if (paramValue.HasConfigDefault)
                    {
                        ProcedureParameters.Add(paramKey, paramValue.ConfigDefaultValue!);
                    }
                    else
                    {
                        // ideally should check if a default is set in sql, but no easy way to do that
                        // catching the database error will have to suffice then
                    }
                } 
            }
        }

        /// <summary>
        /// If constructing with collection of query strings, convert to dictionary of key-value pairs 
        /// </summary>
        public SqlExecuteStructure(
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            NameValueCollection parsedQueryString)
        : this(entityName, sqlMetadataProvider, parsedQueryString.Cast<string>().ToDictionary(k => k, k =>(object?)parsedQueryString[k]))
        {
        }
        
        public string BuildProcedureParameterList()
        {
            Stopwatch timer = Stopwatch.StartNew();

            IEnumerable<string> paramsList = ProcedureParameters.Select(x => $"{x.Key} = {x.Value}");
            string list = string.Join(", ", paramsList);
            timer.Stop();
            Console.WriteLine(timer.Elapsed.ToString());

            timer = Stopwatch.StartNew();

            string parameterList = string.Empty;
            foreach ((string paramKey, object paramValue) in ProcedureParameters)
            {
                parameterList += $"@{paramKey} = {paramValue}, ";
            }

            parameterList = parameterList.Length > 0 ? parameterList[..^2] : parameterList;
            timer.Stop();
            Console.WriteLine(timer.Elapsed.ToString());

            timer = Stopwatch.StartNew();
            StringBuilder sb = new();
            foreach ((string paramKey, object paramValue) in ProcedureParameters)
            {
                sb.Append($"@{paramKey} = {paramValue}, ");
            }

            parameterList = sb.ToString();
            parameterList = parameterList.Length > 0 ? parameterList[..^2] : parameterList;
            timer.Stop();
            Console.WriteLine(timer.Elapsed.ToString());
            
            // At least one parameter added, remove trailing comma and space, else return empty string
            return parameterList;
        }
    }
}
