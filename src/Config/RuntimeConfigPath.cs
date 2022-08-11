using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Config
{
    /// <summary>
    /// This class encapsulates the path related properties of the RuntimeConfig.
    /// The config file name property is provided by either
    /// the in memory configuration provider, command line configuration provider
    /// or from the in memory updateable configuration controller.
    /// </summary>
    public class RuntimeConfigPath
    {
        public const string CONFIGFILE_NAME = "dab-config";
        public const string CONFIG_EXTENSION = ".json";

        public const string RUNTIME_ENVIRONMENT_VAR_NAME = "DAB_ENVIRONMENT";
        public const string ENVIRONMENT_PREFIX = "DAB_";

        public string? ConfigFileName { get; set; }

        public string? CONNSTRING { get; set; }

        /// <summary>
        /// Parse Json and replace @env('ENVIRONMENT_VARIABLE_NAME') with
        /// the environment variable's value that corresponds to ENVIRONMENT_VARIABLE_NAME.
        /// If no environment variable is found with that name, throw exception.
        /// </summary>
        /// <param name="json">Json string representing the runtime config file.</param>
        /// <returns>Parsed json string.</returns>
        public static string? ParseConfigJsonAndReplaceEnvVariables(string json)
        {
            Utf8JsonReader reader = new(jsonData: Encoding.UTF8.GetBytes(json),
                                        isFinalBlock: true,
                                        state: new());
            MemoryStream stream = new();
            Utf8JsonWriter writer = new(stream, options: new() { Indented = true });

            // @env\('  : match @env('
            // .*?      : lazy match any character except newline 0 or more times
            // (?='\))  : look ahead for ') which will combine with our lazy match
            //            ie: in @env('hello')goodbye') we match @env('hello')
            // '\)      : consume the ') into the match (look ahead doesn't capture)
            // This pattern lazy matches any string that starts with @env(' and ends with ')
            // ie: fooBAR@env('hello-world')bash)FOO')  match: @env('hello-world')
            // This matching pattern allows for the @env('<match>') to be safely nested
            // within strings that contain ') after our match.
            // ie: if the environment variable "Baz" has the value of "Bar"
            // fooBarBaz: "('foo@env('Baz')Baz')" would parse into
            // fooBarBaz: "('fooBarBaz')"
            // Note that there is no escape character currently for ') to exist
            // within the name of the environment variable, but that ') is not
            // a valid environment variable name in certain shells.
            string envPattern = @"@env\('.*?(?='\))'\)";

            // The approach for parsing is to re-write the Json to a new string
            // as we read, using regex.replace for the matches we get from our
            // pattern. We call a helper function for each match that handles
            // getting the environment variable for replacement.
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        writer.WritePropertyName(reader.GetString()!);
                        break;
                    case JsonTokenType.String:
                        string valueToWrite = Regex.Replace(reader.GetString()!, envPattern, new MatchEvaluator(ReplaceMatchWithEnvVariable));
                        writer.WriteStringValue(valueToWrite);
                        break;
                    case JsonTokenType.Number:
                        writer.WriteNumberValue(reader.GetDecimal());
                        break;
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                        writer.WriteBooleanValue(reader.GetBoolean());
                        break;
                    case JsonTokenType.StartObject:
                        writer.WriteStartObject();
                        break;
                    case JsonTokenType.StartArray:
                        writer.WriteStartArray();
                        break;
                    case JsonTokenType.EndArray:
                        writer.WriteEndArray();
                        break;
                    case JsonTokenType.EndObject:
                        writer.WriteEndObject();
                        break;
                    // ie: "path" : null
                    case JsonTokenType.Null:
                        writer.WriteNullValue();
                        break;
                    default:
                        writer.WriteRawValue(reader.GetString()!);
                        break;
                }
            }

            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// Retrieves the name of the environment variable
        /// and then returns the environment variable value associated
        /// with that name, throwing an exception if none is found.
        /// </summary>
        /// <param name="match">The match holding the environment variable name.</param>
        /// <returns>The environment variable value associated with the provided name.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        private static string ReplaceMatchWithEnvVariable(Match match)
        {
            // [^@env\(]   :  any substring that is not @env(
            // .*          :  any char except newline any number of times
            // (?=\))      :  look ahead for end char of )
            // This pattern greedy matches all characters that are not a part of @env()
            // ie: @env('hello@env('goodbye')world') match: 'hello@env('goodbye')world'
            string innerPattern = @"[^@env\(].*(?=\))";

            // strip's first and last characters, ie: '''hello'' --> ''hello'
            string envName = Regex.Match(match.Value, innerPattern).Value[1..^1];
            string? envValue = Environment.GetEnvironmentVariable(envName);
            return envValue is not null ? envValue :
                throw new DataApiBuilderException(message: $"Environmental Variable, {envName}, not found.",
                                               statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                               subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        /// <summary>
        /// Precedence of environments is
        /// 1) Value of DAB_ENVIRONMENT.
        /// 2) Value of ASPNETCORE_ENVIRONMENT.
        /// 3) Default config file name.
        /// In each case, overidden file name takes precedence.
        /// The first file name that exists in current directory is returned.
        /// The fall back options are dab-config.overrides.json/dab-config.json
        /// If no file exists, this will return an empty string.
        /// </summary>
        /// <param name="hostingEnvironmentName">Value of ASPNETCORE_ENVIRONMENT variable</param>
        /// <returns></returns>
        public static string GetFileNameForEnvironment(string? hostingEnvironmentName)
        {
            string configFileNameWithExtension = string.Empty;
            string?[] environmentPrecedence = new[]
            {
                Environment.GetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME),
                hostingEnvironmentName,
                string.Empty
            };

            for (short index = 0;
                index < environmentPrecedence.Length
                && string.IsNullOrEmpty(configFileNameWithExtension);
                index++)
            {
                if (!string.IsNullOrWhiteSpace(environmentPrecedence[index])
                // The last index is for the default case - the last fallback option
                // where environmentPrecedence[index] is string.Empty
                // for that case, we still need to get the file name considering overrides
                // so need to do an OR on the last index here
                    || index == environmentPrecedence.Length - 1)
                {
                    configFileNameWithExtension =
                        GetFileNameConsideringOverrides(environmentPrecedence[index]);
                }
            }

            return configFileNameWithExtension;
        }

        // Used for testing
        public static string DefaultName
        {
            get
            {
                return $"{CONFIGFILE_NAME}{CONFIG_EXTENSION}";
            }
        }

        /// <summary>
        /// Generates the config file name and a corresponding overridden file name,
        /// With precedence given to overridden file name, returns that name
        /// if the file exists in the current directory, else an empty string.
        /// </summary>
        /// <param name="environmentValue">Name of the environment to
        /// generate the config file name for.</param>
        /// <returns></returns>
        private static string GetFileNameConsideringOverrides(string? environmentValue)
        {
            string configFileName =
                !string.IsNullOrEmpty(environmentValue)
                ? $"{CONFIGFILE_NAME}.{environmentValue}"
                : $"{CONFIGFILE_NAME}";
            string overriddenConfigFileNameWithExtension = GetOverriddenName(configFileName);
            if (DoesFileExistInCurrentDirectory(overriddenConfigFileNameWithExtension))
            {
                return overriddenConfigFileNameWithExtension;
            }
            else if (DoesFileExistInCurrentDirectory($"{configFileName}{CONFIG_EXTENSION}"))
            {
                return $"{configFileName}{CONFIG_EXTENSION}";
            }
            else
            {
                return string.Empty;
            }
        }

        private static string GetOverriddenName(string fileName)
        {
            return $"{fileName}.overrides{CONFIG_EXTENSION}";
        }

        private static bool DoesFileExistInCurrentDirectory(string fileName)
        {
            string currentDir = Directory.GetCurrentDirectory();
            return File.Exists(Path.Combine(currentDir, fileName));
        }
    }
}
