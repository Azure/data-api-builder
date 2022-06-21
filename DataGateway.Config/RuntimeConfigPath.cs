using System.Text;
using System.Text.RegularExpressions;
using Azure.DataGateway.Service.Exceptions;
using Newtonsoft.Json;

namespace Azure.DataGateway.Config
{
    /// <summary>
    /// This class encapsulates the path related properties of the RuntimeConfig.
    /// The config file name property is provided by either
    /// the in memory configuration provider, command line configuration provider
    /// or from the in memory updateable configuration controller.
    /// </summary>
    public class RuntimeConfigPath
    {
        public const string CONFIGFILE_NAME = "hawaii-config";
        public const string CONFIG_EXTENSION = ".json";

        public const string RUNTIME_ENVIRONMENT_VAR_NAME = "HAWAII_ENVIRONMENT";
        public const string ENVIRONMENT_PREFIX = "HAWAII_";

        public string? ConfigFileName { get; set; }

        public string? CONNSTRING { get; set; }

        /// <summary>
        /// Reads the contents of the json config file if it exists,
        /// and sets the deserialized RuntimeConfig object.
        /// </summary>
        public RuntimeConfig? LoadRuntimeConfigValue()
        {
            string? runtimeConfigJson = null;
            if (!string.IsNullOrEmpty(ConfigFileName))
            {
                if (File.Exists(ConfigFileName))
                {
                    runtimeConfigJson = ParseConfigJsonAndReplaceEnvVariables(File.ReadAllText(ConfigFileName));
                }
                else
                {
                    throw new FileNotFoundException($"Requested configuration file {ConfigFileName} does not exist.");
                }
            }

            if (!string.IsNullOrEmpty(runtimeConfigJson))
            {
                RuntimeConfig configValue = RuntimeConfig.GetDeserializedConfig<RuntimeConfig>(runtimeConfigJson);
                configValue.DetermineGlobalSettings();

                if (!string.IsNullOrWhiteSpace(CONNSTRING))
                {
                    configValue.ConnectionString = CONNSTRING;
                }

                return configValue;
            }

            return null;
        }

        /// <summary>
        /// Parse Json and replace @env('ENVIRONMENT_VARIABLE_NAME') with
        /// the environment variable's value that corresponds to ENVIRONMENT_VARIABLE_NAME.
        /// If no environment variable is found with that name, throw exception.
        /// </summary>
        /// <param name="json">Json string representing the runtime config file.</param>
        /// <returns>Parsed json string.</returns>
        public static string? ParseConfigJsonAndReplaceEnvVariables(string json)
        {
            StringBuilder stringBuilder = new();
            // string writer will modify string builder allowing
            // us to return the string builder toString().
            StringWriter stringWriter = new(stringBuilder);
            using JsonTextReader reader = new(new StringReader(json));
            using JsonTextWriter writer = new(stringWriter)
            {
                Formatting = Formatting.Indented
            };

            // @env\('  : match @env('
            // .*?      : lazy match any character except newline 0 or more times
            // (?='\))  : look ahead for ') which will combine with our lazy match
            //            ie: in @env('hello')goodbye') we match @env('hello')
            // '\)      : consume the ') into the match (look ahead doesn't capture)
            // This pattern lazy matches any string that starts with @env(' and ends with ')
            // ie: fooBAR@env('hello-world')bash)FOO')  match: @env('hello-world')
            string envPattern = @"@env\('.*?(?='\))'\)";

            // The approach for parsing is to re-write the Json to a new string
            // as we read, using regex.replace for the matches we get from our
            // pattern. We call a helper function for each match that handles
            // getting the environment variable for replacement.
            while (reader.Read())
            {
                if (reader.Value is not null)
                {
                    switch (reader.TokenType)
                    {
                        case JsonToken.PropertyName:
                            writer.WritePropertyName(reader.Value.ToString()!);
                            break;
                        case JsonToken.String:
                            string valueToWrite = Regex.Replace(reader.Value.ToString()!, envPattern, new MatchEvaluator(ReplaceMatchWithEnvVariable));
                            writer.WriteValue(valueToWrite);
                            break;
                        default:
                            writer.WriteValue(reader.Value);
                            break;
                    }
                }
                else
                {
                    switch (reader.TokenType)
                    {
                        case JsonToken.StartObject:
                            writer.WriteStartObject();
                            break;
                        case JsonToken.StartArray:
                            writer.WriteStartArray();
                            break;
                        case JsonToken.EndArray:
                            writer.WriteEndArray();
                            break;
                        case JsonToken.EndObject:
                            writer.WriteEndObject();
                            break;
                        // ie: "path" : null
                        case JsonToken.Null:
                            writer.WriteNull();
                            break;
                    }
                }
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        /// Retrieves the name of the environment variable
        /// and then returns the environment variable value associated
        /// with that name, throwing an exception if none is found.
        /// </summary>
        /// <param name="match">The match holding the environment variable name.</param>
        /// <returns>The environment variable value associated with the provided name.</returns>
        /// <exception cref="DataGatewayException"></exception>
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
                throw new DataGatewayException(message: $"Environmental Variable, {envName}, not found.",
                                               statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                               subStatusCode: DataGatewayException.SubStatusCodes.ErrorInInitialization);
        }

        /// <summary>
        /// Precedence of environments is
        /// 1) Value of HAWAII_ENVIRONMENT.
        /// 2) Value of ASPNETCORE_ENVIRONMENT.
        /// 3) Default config file name.
        /// In each case, overidden file name takes precedence.
        /// The first file name that exists in current directory is returned.
        /// The fall back options are hawaii-config.overrides.json/hawaii-config.json
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
