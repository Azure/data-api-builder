using CommandLine;
using static Cli.Utils;

namespace Cli
{
    /// <summary>
    /// Main class for CLI
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main CLI entry point
        /// </summary>
        /// <param name="args">CLI arguments</param>
        /// <returns>0 on success, -1 on failure.</returns>
        public static int Main(string[] args)
        {
            Parser parser = new(settings =>
                {
                    settings.CaseInsensitiveEnumValues = true;
                    settings.HelpWriter = Console.Out;
                }
            );
            ParserResult<object>? result = parser.ParseArguments<InitOptions, AddOptions, UpdateOptions, StartOptions>(args)
                .WithParsed<InitOptions>(options =>
                {
                    bool isSuccess = ConfigGenerator.TryGenerateConfig(options);
                    if (isSuccess)
                    {
                        Console.WriteLine($"Config file generated.");
                        Console.WriteLine($"SUGGESTION: Use 'dab add <options>' to add new entities in your config.");
                    }
                    else
                    {
                        Console.WriteLine($"ERROR: Could not generate config file.");
                    }
                })
                .WithParsed<AddOptions>(options =>
                {
                    bool isSuccess = ConfigGenerator.TryAddEntityToConfigWithOptions(options);
                    if (isSuccess)
                    {
                        Console.WriteLine($"Added new entity: {options.Entity} with source: {options.Source} to config: {options.Config}" +
                            $" with permissions: {string.Join(SEPARATOR, options.Permissions.ToArray())}.");
                        Console.WriteLine($"SUGGESTION: Use 'dab update <options>' to update any entities in your config.");
                    }
                    else
                    {
                        Console.WriteLine($"ERROR: Could not add entity: {options.Entity} source: {options.Source} to config: {options.Config}" +
                            $" with permissions: {string.Join(SEPARATOR, options.Permissions.ToArray())}.");
                    }
                })
                .WithParsed<UpdateOptions>(options =>
                {
                    bool isSuccess = ConfigGenerator.TryUpdateEntityWithOptions(options);

                    if (isSuccess)
                    {
                        Console.WriteLine($"Updated the entity:{options.Entity} in the config.");
                    }
                    else
                    {
                        Console.WriteLine($"Could not update the entity: {options.Entity}.");
                    }
                })
                .WithParsed<StartOptions>(options =>
                {
                    bool isSuccess = ConfigGenerator.TryStartEngineWithOptions(options);

                    if (!isSuccess)
                    {
                        Console.Error.WriteLine("Failed to start the engine.");
                    }
                });

            return result is Parsed<object> ? 0 : -1;
        }
    }
}
