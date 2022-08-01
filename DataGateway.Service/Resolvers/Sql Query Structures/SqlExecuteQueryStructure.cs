using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Services;

namespace Azure.DataGateway.Service.Resolvers
{
    public class SqlExecuteStructure : BaseSqlQueryStructure
    {
        // Holds the final, resolved parameters that will be passed when building the execute stored procedure query
        // Keys are the stored procedure arguments (e.g. @id, @username...)
        // Values are the engine-generated referencing parameters (e.g. @param0, @param1...)
        public Dictionary<string, object> ProcedureParameters { get; set; }

        /// <summary>
        /// Constructs a structure with all needed components to build an EXECUTE stored procedure call
        /// requestParams will be resolved from either the request querystring or body by this point
        /// Construct the ProcedureParameters dictionary through resolving requestParams and defaults from config/metadata
        /// Also performs type checking at this stage instead of in RequestValidator to prevent code duplication 
        /// </summary>
        public SqlExecuteStructure(
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            IDictionary<string, object?> requestParams)
        : base(sqlMetadataProvider, entityName: entityName)
        {
            StoredProcedureDefinition storedProcedureDefinition = GetUnderlyingStoredProcedureDefinition();
            ProcedureParameters = new();
            foreach((string paramKey, ParameterDefinition paramDefinition) in storedProcedureDefinition.Parameters)
            {
                // Populate with request param if able
                if (requestParams.TryGetValue(paramKey, out object? requestParamValue))
                {
                    // Parameterize, then add referencing parameter to ProcedureParameters dictionary
                    try
                    {
                        string param = MakeParamWithValue(GetParamAsProcedureParameterType((string)requestParamValue, paramKey));
                        ProcedureParameters.Add(paramKey, $"@{param}");
                    }
                    catch (ArgumentException ex)
                    {
                        // In the case GetParamAsProcedureParameterType fails to parse as SystemType from database metadata
                        throw new DataGatewayException(
                            message: ex.Message,
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                    }
                }
                else
                { // Fill with default value
                    if (paramDefinition.HasConfigDefault)
                    {
                        string param = MakeParamWithValue(paramDefinition.ConfigDefaultValue!);
                        ProcedureParameters.Add(paramKey, $"@{param}");
                    }
                    else
                    {
                        // This case of not all parameters being explicitly between request and config should already be
                        // handled in the request validation stage.

                        throw new DataGatewayException(message: $"did not provide all req'd params. missing \"{paramKey}\"",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                    }
                } 
            }
        }

        ///// <summary>
        ///// If constructing with collection of query strings, convert to dictionary of key-value pairs 
        ///// </summary>
        //public SqlExecuteStructure(
        //    string entityName,
        //    ISqlMetadataProvider sqlMetadataProvider,
        //    NameValueCollection parsedQueryString)
        //: this(entityName, sqlMetadataProvider, parsedQueryString.Cast<string>().ToDictionary(k => k, k =>(object?)parsedQueryString[k]))
        //{
        //}
        
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

        public override string DispatchBuild(IQueryBuilder _queryBuilder)
        {
            return _queryBuilder.Build(this);
        }

        /// <summary>
        /// Gets the value of the parameter cast as the system type
        /// of the stored procedure parameter this parameter is associated with
        /// </summary>
        /// <param name="param"></param>
        /// <param name="procParamName"></param>
        /// <returns></returns>
        protected object GetParamAsProcedureParameterType(string param, string procParamName)
        {
            Type systemType = GetUnderlyingStoredProcedureDefinition().Parameters[procParamName].SystemType!;
            try
            {
                return ParseParamAsSystemType(param, systemType);
            }
            catch (Exception e)
            {
                if (e is FormatException ||
                    e is ArgumentNullException ||
                    e is OverflowException)
                {
                    throw new ArgumentException($@"Parameter ""{param}"" cannot be resolved as stored procedure parameter ""{procParamName}"" " +
                        $@"with type ""{systemType.Name}"".");
                }

                throw;

            }
        }
    }
}
