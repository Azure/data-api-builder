// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
// using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config.NamingPolicies;
// using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Contains the information needed to connect to the backend database.
/// </summary>
/// <param name="DatabaseType">Type of database to use.</param>
/// <param name="ConnectionString">Connection string to access the database.</param>
/// <param name="Options">Custom options for the specific database. If there are no options, this could be null.</param>
public record DataSource(DatabaseType DatabaseType, string ConnectionString, Dictionary<string, JsonElement>? Options)
{
    // const string ENV_PATTERN = @"@env\('.*?(?='\))'\)";
    /// <summary>
    /// Converts the <c>Options</c> dictionary into a typed options object.
    /// May return null if the dictionary is null.
    /// </summary>
    /// <typeparam name="TOptionType">The strongly typed object for Options.</typeparam>
    /// <returns>The strongly typed representation of Options.</returns>
    /// <exception cref="NotSupportedException">Thrown when the provided <c>TOptionType</c> is not supported for parsing.</exception>
    public TOptionType? GetTypedOptions<TOptionType>() where TOptionType : IDataSourceOptions
    {
        HyphenatedNamingPolicy namingPolicy = new();

        if (typeof(TOptionType).IsAssignableFrom(typeof(CosmosDbNoSQLDataSourceOptions)))
        {
            return Options is not null ?
                (TOptionType)(object)new CosmosDbNoSQLDataSourceOptions(
                Database: ReadStringOption(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Database))),
                Container: ReadStringOption(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Container))),
                Schema: ReadStringOption(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Schema))),
                // The "raw" schema will be provided via the controller to setup config, rather than parsed from the JSON file.
                GraphQLSchema: ReadStringOption(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.GraphQLSchema))))
                : default;
        }

        if (typeof(TOptionType).IsAssignableFrom(typeof(MsSqlOptions)))
        {
            return (TOptionType)(object)new MsSqlOptions(
                SetSessionContext: ReadBoolOption(namingPolicy.ConvertName(nameof(MsSqlOptions.SetSessionContext))));
        }

        throw new NotSupportedException($"The type {typeof(TOptionType).FullName} is not a supported strongly typed options object");
    }

    private string? ReadStringOption(string option)
    {
        if (Options is not null && Options.TryGetValue(option, out JsonElement value))
        {
            return value.GetString();
            // string? val= value.GetString();
            // return Regex.Replace(val!, ENV_PATTERN, new MatchEvaluator(ReplaceMatchWithEnvVariable));
        }

        return null;
    }

    private bool ReadBoolOption(string option)
    {
        if (Options is not null && Options.TryGetValue(option, out JsonElement value))
        {
            return value.GetBoolean();
        }

        return false;
    }

    // private string ReplaceMatchWithEnvVariable(Match match)
    //     {
    //         // [^@env\(]   :  any substring that is not @env(
    //         // .*          :  any char except newline any number of times
    //         // (?=\))      :  look ahead for end char of )
    //         // This pattern greedy matches all characters that are not a part of @env()
    //         // ie: @env('hello@env('goodbye')world') match: 'hello@env('goodbye')world'
    //         string innerPattern = @"[^@env\(].*(?=\))";

    //         // strips first and last characters, ie: '''hello'' --> ''hello'
    //         string envName = Regex.Match(match.Value, innerPattern).Value[1..^1];
    //         string? envValue = Environment.GetEnvironmentVariable(envName);
    //         // return envValue ?? match.Value;
    //         // if (_replacementFailureMode == EnvironmentVariableReplacementFailureMode.Throw)
    //         // {
    //             return envValue is not null ? envValue :
    //                 throw new DataApiBuilderException(message: $"Environmental Variable, {envName}, not found.",
    //                                                statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
    //                                                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
    //         // }
    //         // else
    //         // {
    //         //     return envValue ?? match.Value;
    //         // }
    //     }

    [JsonIgnore]
    public string DatabaseTypeNotSupportedMessage => $"The provided database-type value: {DatabaseType} is currently not supported. Please check the configuration file.";
}

public interface IDataSourceOptions { }

/// <summary>
/// The CosmosDB NoSQL connection options.
/// </summary>
/// <param name="Database">Name of the default CosmosDB database.</param>
/// <param name="Container">Name of the default CosmosDB container.</param>
/// <param name="Schema">Path to the GraphQL schema file.</param>
/// <param name="GraphQLSchema">Raw contents of the GraphQL schema.</param>
public record CosmosDbNoSQLDataSourceOptions(string? Database, string? Container, string? Schema, string? GraphQLSchema) : IDataSourceOptions;

/// <summary>
/// Options for MsSql database.
/// </summary>
public record MsSqlOptions(bool SetSessionContext = true) : IDataSourceOptions;
