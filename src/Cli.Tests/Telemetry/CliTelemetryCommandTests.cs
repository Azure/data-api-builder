// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Cli.Telemetry;
using CommandLine;

namespace Cli.Tests.Telemetry
{
    [TestClass]
    public class CliTelemetryCommandTests
    {
        private const string SENTINEL = "NEVER_COLLECT_c7d032_entity_path_connection_header_description";

        // Independent of the inspector's registry; these are Program.Execute's registered alternatives.
        private static readonly Type[] _registeredTypes =
        [
            typeof(InitOptions), typeof(AddOptions), typeof(UpdateOptions), typeof(StartOptions),
            typeof(ValidateOptions), typeof(ExportOptions), typeof(AddTelemetryOptions), typeof(ConfigureOptions),
            typeof(AutoConfigOptions), typeof(AutoConfigSimulateOptions), typeof(AppNameOptions)
        ];

        [TestMethod]
        public void ApprovedRegistryMatchesCurrentReflectionMetadata()
        {
            Type[] declaredVerbs = typeof(InitOptions).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<VerbAttribute>() is not null).ToArray();
            CollectionAssert.AreEquivalent(_registeredTypes, declaredVerbs);
            CollectionAssert.AreEquivalent(_registeredTypes, CliTelemetryCommand.RegisteredCommands.Values.ToArray());

            List<string> longNames = new();
            foreach (Type type in _registeredTypes)
            {
                VerbAttribute verb = type.GetCustomAttribute<VerbAttribute>()!;
                Assert.AreEqual(type, CliTelemetryCommand.RegisteredCommands[verb.Name]);
                Assert.IsFalse(verb.IsDefault, "A default verb requires a grammar review.");
                Assert.IsFalse(verb.Aliases.Any(), "A verb alias requires a grammar review.");

                OptionAttribute[] options = OptionProperties(type).Select(property => property.GetCustomAttribute<OptionAttribute>()!).ToArray();
                Assert.IsTrue(options.All(option => !string.IsNullOrEmpty(option.LongName)), "Implicit option names require review.");
                longNames.AddRange(options.Select(option => option.LongName));

                string[] keys = options.Select(option => TelemetryKey(option.LongName)).Distinct(StringComparer.Ordinal).ToArray();
                Assert.IsTrue(keys.Length <= CliTelemetryCommand.MAX_OPTION_COUNT, "The bound must cover every approved option for one verb.");
                Assert.IsTrue(keys.All(key => key.Length <= 128));
            }

            // This MUST fail when an option is added or renamed, including hidden and inherited options.
            // Reflection is not permission to silently add a telemetry dimension.
            CollectionAssert.AreEquivalent(longNames.Distinct(StringComparer.Ordinal).ToArray(), CliTelemetryCommand.ApprovedLongOptions.ToArray());
            string[] collisions = CliTelemetryCommand.ApprovedLongOptions.GroupBy(TelemetryKey)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            CollectionAssert.AreEqual(new[] { "option_log_level" }, collisions);
        }

        [TestMethod]
        public void CurrentShortAliasesAreReviewedAndAllConsumeValues()
        {
            List<string> actual = new();
            foreach (Type type in _registeredTypes)
            {
                string verb = type.GetCustomAttribute<VerbAttribute>()!.Name;
                foreach (PropertyInfo property in OptionProperties(type))
                {
                    OptionAttribute option = property.GetCustomAttribute<OptionAttribute>()!;
                    if (option.ShortName.Length == 0)
                    {
                        continue;
                    }

                    actual.Add($"{verb}:{option.ShortName}={option.LongName}");
                    // v2.9.1 supports boolean short groups, but DAB currently registers NO short
                    // boolean switches. In particular, -g means graphql-schema-file, not graphql.
                    Assert.AreNotEqual(typeof(bool), property.PropertyType);
                    Assert.IsFalse(option.FlagCounter);
                }
            }

            string[] expected =
            [
                "init:c=config", "add:c=config", "update:c=config", "start:c=config", "validate:c=config",
                "export:c=config", "add-telemetry:c=config", "configure:c=config", "auto-config:c=config",
                "auto-config-simulate:c=config", "appname:c=config", "add:s=source", "update:s=source",
                "update:m=map", "export:o=output", "export:g=graphql-schema-file", "export:m=sampling-mode",
                "export:n=sampling-count", "export:d=sampling-days", "auto-config-simulate:o=output", "appname:o=output"
            ];
            CollectionAssert.AreEquivalent(expected, actual.ToArray());
        }

        [TestMethod]
        public void ParserSettingsAreThePinnedProductionGrammar()
        {
            using Parser parser = CreateParser();
            Assert.AreEqual("2.9.1", typeof(Parser).Assembly.GetName().Version!.ToString(3));
            Assert.IsTrue(parser.Settings.CaseInsensitiveEnumValues);
            Assert.IsTrue(parser.Settings.CaseSensitive);
            Assert.IsTrue(parser.Settings.AutoHelp);
            Assert.IsTrue(parser.Settings.AutoVersion);
            Assert.IsFalse(parser.Settings.IgnoreUnknownArguments);
            Assert.IsFalse(parser.Settings.GetoptMode);
            Assert.IsFalse(parser.Settings.EnableDashDash);
            Assert.IsFalse(parser.Settings.AllowMultiInstance);
            Assert.AreEqual(CultureInfo.InvariantCulture, parser.Settings.ParsingCulture);
        }

        [DataTestMethod]
        [DynamicData(nameof(ValidCommandCases), DynamicDataSourceType.Method)]
        public void RecognizesEveryRegisteredVerbWithoutCollectingDefaults(string[] args, Type expectedType, string[] suppliedNames)
        {
            ParserResult<object> parsed = ParseCommands(args);
            Assert.IsTrue(parsed is Parsed<object>, ErrorTags(parsed));
            Assert.AreEqual(expectedType, ((Parsed<object>)parsed).Value.GetType());

            CliTelemetryCommand command = CliTelemetryCommand.Inspect(args);
            Assert.AreEqual(args[0], command.Name);
            Assert.AreEqual("none", command.Control);
            AssertOptions(command, suppliedNames);
        }

        public static IEnumerable<object[]> ValidCommandCases()
        {
            yield return new object[] { new[] { "init", "--database-type", "mssql" }, typeof(InitOptions), new[] { "database-type" } };
            yield return new object[] { new[] { "add", SENTINEL, "--source", SENTINEL, "--permissions", "anonymous:read" }, typeof(AddOptions), new[] { "source", "permissions" } };
            yield return new object[] { new[] { "update", SENTINEL }, typeof(UpdateOptions), Array.Empty<string>() };
            yield return new object[] { new[] { "start" }, typeof(StartOptions), Array.Empty<string>() };
            yield return new object[] { new[] { "validate" }, typeof(ValidateOptions), Array.Empty<string>() };
            yield return new object[] { new[] { "export", "-o", SENTINEL }, typeof(ExportOptions), new[] { "output" } };
            yield return new object[] { new[] { "add-telemetry" }, typeof(AddTelemetryOptions), Array.Empty<string>() };
            yield return new object[] { new[] { "configure" }, typeof(ConfigureOptions), Array.Empty<string>() };
            yield return new object[] { new[] { "auto-config", SENTINEL }, typeof(AutoConfigOptions), Array.Empty<string>() };
            yield return new object[] { new[] { "auto-config-simulate" }, typeof(AutoConfigSimulateOptions), Array.Empty<string>() };
            yield return new object[] { new[] { "appname" }, typeof(AppNameOptions), Array.Empty<string>() };
        }

        [TestMethod]
        public void ExplicitDefaultAndFalseValuesStillHavePresence()
        {
            string[] implicitArgs = ["init", "--database-type", "mssql"];
            string[] explicitArgs = ["init", "--database-type", "mssql", "--host-mode", "production", "--rest.enabled", "false"];
            InitOptions implicitOptions = ParseSuccess<InitOptions>(implicitArgs);
            InitOptions explicitOptions = ParseSuccess<InitOptions>(explicitArgs);
            Assert.AreEqual(implicitOptions.HostMode, explicitOptions.HostMode);
            Assert.AreEqual(CliBool.False, explicitOptions.RestEnabled);
            AssertOptions(CliTelemetryCommand.Inspect(implicitArgs), "database-type");
            AssertOptions(CliTelemetryCommand.Inspect(explicitArgs), "database-type", "host-mode", "rest.enabled");

            string[] telemetryArgs = ["add-telemetry", "--otel-enabled", "false", "--otel-protocol", "grpc", "--otel-service-name", "dab"];
            AddTelemetryOptions implicitTelemetry = ParseSuccess<AddTelemetryOptions>(["add-telemetry"]);
            AddTelemetryOptions explicitTelemetry = ParseSuccess<AddTelemetryOptions>(telemetryArgs);
            Assert.AreEqual(implicitTelemetry.OpenTelemetryEnabled, explicitTelemetry.OpenTelemetryEnabled);
            Assert.AreEqual(implicitTelemetry.OpenTelemetryExportProtocol, explicitTelemetry.OpenTelemetryExportProtocol);
            Assert.AreEqual(implicitTelemetry.OpenTelemetryServiceName, explicitTelemetry.OpenTelemetryServiceName);
            AssertOptions(CliTelemetryCommand.Inspect(telemetryArgs), "otel-enabled", "otel-protocol", "otel-service-name");
        }

        [DataTestMethod]
        [DynamicData(nameof(OptionTokenCases), DynamicDataSourceType.Method)]
        public void MatchesActualOptionTokenGrammar(string[] args, bool expectedParsed, string[] suppliedNames)
        {
            ParserResult<object> parsed = ParseCommands(args);
            Assert.AreEqual(expectedParsed, parsed is Parsed<object>, ErrorTags(parsed));
            CliTelemetryCommand command = CliTelemetryCommand.Inspect(args);
            Assert.AreEqual(args[0], command.Name);
            Assert.AreEqual(ControlFrom(parsed), command.Control);
            AssertOptions(command, suppliedNames);
        }

        public static IEnumerable<object[]> OptionTokenCases()
        {
            // Flag-looking values are never recursively inspected, split, or treated as controls.
            yield return OptionCase(["init", "--database-type", "mssql", "--connection-string=--config"], true, "database-type", "connection-string");
            yield return OptionCase(["start", "--config=--verbose"], true, "config");
            yield return OptionCase(["start", "-c--verbose"], true, "config");
            yield return OptionCase(["start", "-c=--verbose"], true, "config");
            yield return OptionCase(["start", "--config=--help"], true, "config");
            yield return OptionCase(["start", "-c--version"], true, "config");
            yield return OptionCase(["start", "--config", SENTINEL + " --verbose --help"], true, "config");
            yield return OptionCase(["start", "--verbose=--config"], true, "verbose");
            yield return OptionCase(["start", "--verbose=false"], true, "verbose");

            // The next argv element is independently tokenized; do not unconditionally skip it.
            yield return OptionCase(["start", "--config", "--verbose"], true, "config", "verbose");
            yield return OptionCase(["start", "-c", "--mcp-stdio"], true, "config", "mcp-stdio");
            yield return OptionCase(["start", "--config", "--help"], false, "config");
            yield return OptionCase(["start", "--config", "--version"], false, "config");
            yield return OptionCase(["start", "--config", ""], true, "config");
            yield return OptionCase(["start", "--config"], true, "config");
            yield return OptionCase(["init", "--database-type"], false, "database-type");
            yield return OptionCase(["init", "--database-type", SENTINEL], false, "database-type");
            yield return OptionCase(["update", SENTINEL, "--fields.include"], false, "fields.include");
            yield return OptionCase(["configure", "--runtime.health.enabled"], true, "runtime.health.enabled");

            // Lone dash and digit-leading negatives are values, not short-option groups.
            yield return OptionCase(["start", "--config", "-"], true, "config");
            yield return OptionCase(["start", "--config", "-42"], true, "config");
            yield return OptionCase(["start", "--config", "-1--verbose"], true, "config");
            yield return OptionCase(["start", "--config", "-\u0662"], true, "config");
            yield return OptionCase(["start", "--config", "-.5"], false, "config");
            yield return OptionCase(["configure", "--runtime.graphql.depth-limit", "-1"], true, "runtime.graphql.depth-limit");
            yield return OptionCase(["configure", "--runtime.health.enabled", "false", "--show-effective-permissions"], true, "runtime.health.enabled", "show-effective-permissions");

            // DAB leaves EnableDashDash=false. A conventional terminator would be incorrect here.
            yield return OptionCase(["start", "--", "--verbose"], true, "verbose");
            yield return OptionCase(["start", "--config", "--", SENTINEL], true, "config");
            yield return OptionCase(["start", "--", "--help"], false);

            // Both separator and space-delimited sequence values stay values, even after splitting.
            yield return OptionCase(["update", SENTINEL, "--fields.include", "first,--config", "--description", SENTINEL], true, "fields.include", "description");
            yield return OptionCase(["update", SENTINEL, "--fields.include=--config,--description"], true, "fields.include");
            yield return OptionCase(["update", SENTINEL, "--permissions=--config:--description"], true, "permissions");
            yield return OptionCase(["update", SENTINEL, "-mfirst:--config,second:--description"], true, "map");
            yield return OptionCase(["update", SENTINEL, "--fields.include", "first", "second", "--config=" + SENTINEL], true, "fields.include", "config");
            yield return OptionCase(["configure", "--runtime.host.cors.origins", SENTINEL + ",--config", "second", "--show-effective-permissions"], true, "runtime.host.cors.origins", "show-effective-permissions");
            yield return OptionCase(["auto-config", SENTINEL, "--patterns.include", SENTINEL + ",--config", "second"], true, "patterns.include");

            // Short aliases are verb-scoped; -m has different meanings and -ogmn is NOT a group.
            yield return OptionCase(["export", "-o" + SENTINEL, "-g--config", "-m--LogLevel", "-n-1", "-d-2"], true, "output", "graphql-schema-file", "sampling-mode", "sampling-count", "sampling-days");
            yield return OptionCase(["export", "-ogmn", "-c--graphql"], true, "output", "config");
            yield return OptionCase(["export", "-o", SENTINEL, "--graphql", "--generate"], true, "output", "graphql", "generate");
            yield return OptionCase(["update", SENTINEL, "-m--config", "-s--description"], true, "map", "source");
            yield return OptionCase(["appname", "--decode=" + SENTINEL, "-o--config"], true, "decode", "output");
            yield return OptionCase(["auto-config-simulate", "-o--config"], true, "output");
            yield return OptionCase(["start", "-cv"], true, "config");
            yield return OptionCase(["start", "-v"], false);
            yield return OptionCase(["start", "-vm"], false);
            yield return OptionCase(["start", "-xc--verbose"], false, "config");

            // Case sensitivity, the legacy long alias, and v2.9.1's long/short name lookup.
            yield return OptionCase(["start", "--LogLevel", "debug"], true, "log-level");
            yield return OptionCase(["start", "--log-level", "debug"], true, "log-level");
            yield return OptionCase(["start", "--loglevel", "debug"], false);
            yield return OptionCase(["start", "--CONFIG", SENTINEL], false);
            yield return OptionCase(["start", "--c", SENTINEL], true, "config");
            yield return OptionCase(["start", "--c=" + SENTINEL], false);
            yield return OptionCase(["validate", "--verbose"], false);

            // Invalid assignments emit no name token. Preserve the pinned regex's space/newline rules.
            yield return OptionCase(["start", "--config="], false);
            yield return OptionCase(["start", "--config= " + SENTINEL], false);
            yield return OptionCase(["start", "--config=", "--verbose"], false, "verbose");
            yield return OptionCase(["start", "--config=\t--verbose"], true, "config");
            yield return OptionCase(["start", "--config=first\n--verbose"], false);
            yield return OptionCase(["start", "--config=first\n"], true, "config");
            yield return OptionCase(["start", "--=--config"], false);
            yield return OptionCase(["start", "---config"], false);
            yield return OptionCase(["start", "--" + SENTINEL + "=--verbose"], false);
            yield return OptionCase(["start", "--config", SENTINEL, "-c" + SENTINEL], false, "config");
            yield return OptionCase(["start", "--verbose", "--log-level", "debug"], false, "verbose", "log-level");
        }

        [TestMethod]
        public void FlagLookingValuesReachTheActualParserAsValues()
        {
            string[] args = ["init", "--database-type", "mssql", "--connection-string=--config"];
            InitOptions init = ParseSuccess<InitOptions>(args);
            Assert.AreEqual("--config", init.ConnectionString);
            Assert.IsNull(init.Config);
            AssertOptions(CliTelemetryCommand.Inspect(args), "database-type", "connection-string");

            StartOptions attached = ParseSuccess<StartOptions>(["start", "-c--verbose"]);
            Assert.AreEqual("--verbose", attached.Config);
            Assert.IsNull(attached.LogLevel);

            StartOptions booleanAssignment = ParseSuccess<StartOptions>(["start", "--verbose=--config"]);
            Assert.AreEqual(LogLevel.Information, booleanAssignment.LogLevel);
            Assert.AreEqual("--config", booleanAssignment.McpRole);
            Assert.IsNull(booleanAssignment.Config);

            UpdateOptions sequence = ParseSuccess<UpdateOptions>(["update", SENTINEL, "--fields.include=--config,--description"]);
            CollectionAssert.AreEqual(new[] { "--config", "--description" }, sequence.FieldsToInclude!.ToArray());
            Assert.IsNull(sequence.Config);
            Assert.IsNull(sequence.Description);
        }

        [TestMethod]
        public void PresenceDoesNotInventMissingValueValidation()
        {
            // v2.9.1 ForScalar uses Group(2), which drops an incomplete trailing pair. An optional
            // scalar with no value can therefore parse successfully. Do NOT infer presence from
            // the resulting property or pretend the inspector can supply the parser's outcome.
            string[] args = ["start", "--config", "--verbose"];
            StartOptions parsed = ParseSuccess<StartOptions>(args);
            Assert.IsNull(parsed.Config);
            Assert.AreEqual(LogLevel.Information, parsed.LogLevel);
            AssertOptions(CliTelemetryCommand.Inspect(args), "config", "verbose");

            string[] nullableArgs = ["configure", "--runtime.health.enabled"];
            Assert.IsNull(ParseSuccess<ConfigureOptions>(nullableArgs).RuntimeHealthEnabled);
            AssertOptions(CliTelemetryCommand.Inspect(nullableArgs), "runtime.health.enabled");

            string[] sequenceArgs = ["update", SENTINEL, "--fields.include"];
            ParserResult<object> sequence = ParseCommands(sequenceArgs);
            Assert.IsTrue(sequence is NotParsed<object>);
            Assert.IsTrue(((NotParsed<object>)sequence).Errors.Any(error => error.Tag == ErrorType.MissingValueOptionError));
            AssertOptions(CliTelemetryCommand.Inspect(sequenceArgs), "fields.include");
        }

        [DataTestMethod]
        [DynamicData(nameof(ControlCases), DynamicDataSourceType.Method)]
        public void ControlsAgreeWithActualParserErrorTypes(string[] args, string expectedName, string expectedControl)
        {
            ParserResult<object> parsed = ParseCommands(args);
            Assert.AreEqual(expectedControl, ControlFrom(parsed), ErrorTags(parsed));
            CliTelemetryCommand command = CliTelemetryCommand.Inspect(args);
            Assert.AreEqual(expectedName, command.Name);
            Assert.AreEqual(expectedControl, command.Control);
            AssertOptions(command);
        }

        public static IEnumerable<object[]> ControlCases()
        {
            yield return ControlCase([], "unknown", "none");
            yield return ControlCase(["help"], "unknown", "help");
            yield return ControlCase(["--help"], "unknown", "help");
            yield return ControlCase(["help", "start", "--config", SENTINEL], "start", "help");
            yield return ControlCase(["--help", "init", "--database-type", SENTINEL], "init", "help");
            yield return ControlCase(["help", SENTINEL], "unknown", "help");
            yield return ControlCase(["help", "START"], "unknown", "help");
            yield return ControlCase(["help", "--version"], "unknown", "help");
            yield return ControlCase(["version"], "unknown", "version");
            yield return ControlCase(["--version", "start", "--config", SENTINEL], "unknown", "version");
            yield return ControlCase(["start", "--help", "--config", SENTINEL], "start", "help");
            yield return ControlCase(["start", "--version", "--help"], "start", "version");
            yield return ControlCase(["start", "--help", "--version"], "start", "help");
            yield return ControlCase(["init", "--help"], "init", "help");
            yield return ControlCase(["start", "help"], "start", "none");
            yield return ControlCase(["start", "version"], "start", "none");
            yield return ControlCase(["start", SENTINEL, "--help"], "start", "none");
            yield return ControlCase(["start", SENTINEL, "--version"], "start", "none");
            yield return ControlCase(["start", "--help=true"], "start", "none");
            yield return ControlCase(["start", "-h"], "start", "none");
            yield return ControlCase(["start", "-?"], "start", "none");
            yield return ControlCase(["start", "--HELP"], "start", "none");
            yield return ControlCase(["start", "--", "--help"], "start", "none");
            yield return ControlCase(["START", "--help"], "unknown", "none");
            yield return ControlCase(["--help=start"], "unknown", "none");
            yield return ControlCase([SENTINEL, "--config", SENTINEL, "--help"], "unknown", "none");
            yield return ControlCase(["--config", SENTINEL, "start", "--version"], "unknown", "none");
        }

        [TestMethod]
        public void FreeFormSentinelNeverEscapesThroughAnyRecordField()
        {
            string[][] cases =
            [
                ["init", "--database-type", "mssql", "--connection-string", SENTINEL, "--auth.audience", SENTINEL, "-c" + SENTINEL],
                ["update", SENTINEL, "--source", SENTINEL, "--description", SENTINEL, "--policy-database", SENTINEL, "--fields.description=" + SENTINEL],
                ["add-telemetry", "--app-insights-conn-string", SENTINEL, "--otel-headers", SENTINEL, "--otel-endpoint", SENTINEL],
                ["configure", "--runtime.embeddings.api-key", SENTINEL, "--runtime.embeddings.health.test-text", SENTINEL, "--data-source.connection-string", SENTINEL],
                ["start", "role:" + SENTINEL, "--config", SENTINEL],
                ["appname", "--decode", SENTINEL, "--output", SENTINEL],
                ["start", "--" + SENTINEL + "=--config"],
                ["help", SENTINEL],
                [SENTINEL, "--config=" + SENTINEL]
            ];

            foreach (string[] args in cases)
            {
                ParserResult<object> parsed = ParseCommands(args);
                CliTelemetryCommand command = CliTelemetryCommand.Inspect(args);
                Assert.AreEqual(ControlFrom(parsed), command.Control);
                AssertSafeOutput(command);
                Assert.IsFalse(JsonSerializer.Serialize(command).Contains(SENTINEL, StringComparison.Ordinal));
            }

            // No hidden instance state holding args, parsed options, values, or positional data.
            Type[] fields = typeof(CliTelemetryCommand).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Select(field => field.FieldType).ToArray();
            CollectionAssert.AreEquivalent(new[] { typeof(string), typeof(string), typeof(ImmutableDictionary<string, string>) }, fields);
        }

        [TestMethod]
        public void OutputIsImmutableBoundedAndDoesNotRetainTheArgumentArray()
        {
            string[] args = [new string("start".ToCharArray()), "--config=" + SENTINEL];
            _ = ParseSuccess<StartOptions>(args);
            CliTelemetryCommand command = CliTelemetryCommand.Inspect(args);
            Assert.AreNotSame(args[0], command.Name, "Use the fixed registered name, not the supplied token.");
            Array.Fill(args, SENTINEL);
            Assert.AreEqual("start", command.Name);
            AssertOptions(command, "config");
            ImmutableDictionary<string, string> changed = command.Options.SetItem("option_config", "false");
            Assert.AreEqual("false", changed["option_config"]);
            Assert.AreEqual("true", command.Options["option_config"]);
            AssertOptions(CliTelemetryCommand.Inspect(["start"]));

            string[] repeated = Enumerable.Repeat("--config=" + SENTINEL, CliTelemetryCommand.MAX_OPTION_COUNT * 2).Prepend("start").ToArray();
            Assert.IsTrue(ParseCommands(repeated) is NotParsed<object>);
            AssertOptions(CliTelemetryCommand.Inspect(repeated), "config");

            // Exercise the largest current per-verb vocabulary, without constructing values or
            // bypassing the fixed approval list. Bare option names still represent supplied presence.
            foreach (Type type in _registeredTypes)
            {
                string verb = type.GetCustomAttribute<VerbAttribute>()!.Name;
                string[] names = OptionProperties(type).Select(property => property.GetCustomAttribute<OptionAttribute>()!.LongName).ToArray();
                CliTelemetryCommand allOptions = CliTelemetryCommand.Inspect(names.Select(name => "--" + name).Prepend(verb).ToArray());
                AssertOptions(allOptions, names);
                Assert.IsTrue(allOptions.Options.Count <= CliTelemetryCommand.MAX_OPTION_COUNT);
            }
        }

        private static IEnumerable<PropertyInfo> OptionProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetCustomAttribute<OptionAttribute>() is not null);
        }

        private static Parser CreateParser()
        {
            return new(settings =>
            {
                settings.CaseInsensitiveEnumValues = true;
                // Same non-null HelpWriter behavior as Program.Execute, but no console output in tests.
                settings.HelpWriter = TextWriter.Null;
            });
        }

        private static ParserResult<object> ParseCommands(string[] args)
        {
            using Parser parser = CreateParser();
            // Deliberately the real registrations, not a lookalike option model. Never call MapResult
            // or Handler: parsing must not read/write configs, start the engine, or contact a database.
            return parser.ParseArguments<InitOptions, AddOptions, UpdateOptions, StartOptions, ValidateOptions, ExportOptions,
                AddTelemetryOptions, ConfigureOptions, AutoConfigOptions, AutoConfigSimulateOptions, AppNameOptions>(args);
        }

        private static T ParseSuccess<T>(string[] args)
        {
            ParserResult<object> result = ParseCommands(args);
            Assert.IsTrue(result is Parsed<object>, ErrorTags(result));
            Assert.AreEqual(typeof(T), ((Parsed<object>)result).Value.GetType());
            return (T)((Parsed<object>)result).Value;
        }

        private static string ControlFrom(ParserResult<object> result)
        {
            if (result is NotParsed<object> notParsed)
            {
                foreach (Error error in notParsed.Errors)
                {
                    if (error.Tag is ErrorType.HelpRequestedError or ErrorType.HelpVerbRequestedError)
                    {
                        return "help";
                    }

                    if (error.Tag == ErrorType.VersionRequestedError)
                    {
                        return "version";
                    }
                }
            }

            return "none";
        }

        private static string ErrorTags(ParserResult<object> result)
        {
            return result is NotParsed<object> notParsed
                ? string.Join(", ", notParsed.Errors.Select(error => error.Tag))
                : "Parsed";
        }

        private static object[] OptionCase(string[] args, bool parsed, params string[] names)
        {
            return [args, parsed, names];
        }

        private static object[] ControlCase(string[] args, string name, string control)
        {
            return [args, name, control];
        }

        private static string TelemetryKey(string longName)
        {
            string canonical = longName == "LogLevel" ? "log-level" : longName;
            return "option_" + canonical.Replace('-', '_').Replace('.', '_');
        }

        private static void AssertOptions(CliTelemetryCommand command, params string[] names)
        {
            CollectionAssert.AreEquivalent(names.Select(TelemetryKey).Distinct(StringComparer.Ordinal).ToArray(), command.Options.Keys.ToArray());
            AssertSafeOutput(command);
        }

        private static void AssertSafeOutput(CliTelemetryCommand command)
        {
            Assert.IsTrue(command.Name == "unknown" || CliTelemetryCommand.RegisteredCommands.ContainsKey(command.Name));
            Assert.IsTrue(command.Name.Length <= 20);
            Assert.IsTrue(command.Control is "none" or "help" or "version");
            Assert.IsTrue(command.Options.Count <= CliTelemetryCommand.MAX_OPTION_COUNT);
            HashSet<string> approvedKeys = CliTelemetryCommand.ApprovedLongOptions.Select(TelemetryKey).ToHashSet(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> option in command.Options)
            {
                Assert.IsTrue(approvedKeys.Contains(option.Key));
                Assert.AreEqual("true", option.Value);
            }
        }
    }
}
