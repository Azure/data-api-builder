// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Config.Telemetry.TelemetryConfigurationPresence;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Bounded, value-free command provenance and nested AsyncLocal policy scopes. All models
    /// and parser input are synthetic. Only the opt-out variable is temporarily changed;
    /// its previous value is restored without logging it. No files or services are used.
    /// </summary>
    [TestClass]
    [TestCategory("CliTelemetry")]
    [TestCategory("EngineTelemetry")]
    [DoNotParallelize]
    public class CliTelemetryCommandPresenceTests
    {
        private const int CHARACTER_LIMIT = 1024 * 1024;
        private const int ENTITY_LIMIT = 4096;
        private const string SENTINEL = "PRIVATE_COMMAND_PRESENCE_780ac";
        private const string SMALL_JSON = "{\"runtime\":{\"rest\":false},\"entities\":{}}";
        private const string ENTITY_JSON = "{\"entities\":{\"0\":{\"source\":\"synthetic\",\"rest\":null}}}";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
        private string? _previousOptOut;

        [TestInitialize]
        public void ClearOnlyProductOptOutForScopedCapture()
        {
            _previousOptOut = Environment.GetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR);
            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, null);
        }

        [TestCleanup]
        public void RestoreProductOptOut()
            => Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, _previousOptOut);

        [TestMethod]
        public void DirectDisabledCaptureDoesNotInspectInputOrInvokeAmbientPermission()
        {
            int permissions = 0;
            int reads = 0;
            using IDisposable? scope = BeginCommandCapture(() =>
            {
                permissions++;
                throw new InvalidOperationException("The direct disabled API must not evaluate ambient policy.");
            });
            RuntimeConfig hostile = new ObservedRuntimeConfig(CreateConfig(), () =>
            {
                reads++;
                throw new InvalidOperationException("Disabled capture must not read entities.");
            });
            Assert.IsNull(TryCaptureBounded(null!, null!, enabled: false));
            Assert.IsNull(TryCaptureBounded("invalid JSON", hostile, enabled: false));
            Assert.AreEqual(0, permissions);
            Assert.AreEqual(0, reads);
        }

        [TestMethod]
        public void NoAmbientPermissionDoesNotCaptureOrReadTheModel()
        {
            int reads = 0;
            RuntimeConfig hostile = new ObservedRuntimeConfig(CreateConfig(), () =>
            {
                reads++;
                throw new InvalidOperationException();
            });
            using IDisposable? scope = BeginCommandCapture(permission: null);
            Assert.IsNull(scope, "The ordinary no-permission path must not allocate a capture scope.");
            Assert.IsFalse(IsCaptureEnabled());
            Assert.IsNull(TryCaptureForCurrentScope(ENTITY_JSON, hostile));
            Assert.IsNull(TryCaptureForCurrentScope(null!, null!));
            Assert.IsNull(ParseSuccessfully(SMALL_JSON).TelemetryPresence);
            Assert.AreEqual(0, reads);
        }

        [TestMethod]
        public void RevokedCommandPermissionStopsCaptureBeforeInspectingInput()
        {
            bool permitted = true;
            int permissions = 0;
            int reads = 0;
            using IDisposable? scope = BeginCommandCapture(() => { permissions++; return permitted; });
            Assert.IsNotNull(ParseSuccessfully(SMALL_JSON).TelemetryPresence);
            permitted = false;
            RuntimeConfig hostile = new ObservedRuntimeConfig(CreateConfig(), () =>
            {
                reads++;
                throw new InvalidOperationException();
            });
            Assert.IsNull(TryCaptureForCurrentScope(null!, hostile));
            Assert.IsNull(ParseSuccessfully(SMALL_JSON).TelemetryPresence);
            Assert.AreEqual(3, permissions);
            Assert.AreEqual(0, reads);
        }

        [TestMethod]
        public void ProcessOptOutVetoesScopedPermissionButDirectCaptureUsesItsExplicitPolicy()
        {
            RuntimeConfig config = CreateConfig();
            using IDisposable? scope = BeginCommandCapture(() => true);
            Assert.IsNotNull(TryCaptureForCurrentScope(SMALL_JSON, config));
            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, "true");
            Assert.IsNull(TryCaptureForCurrentScope(SMALL_JSON, config));
            Assert.IsNull(ParseSuccessfully(SMALL_JSON).TelemetryPresence);
            Assert.IsNotNull(TryCaptureBounded(SMALL_JSON, config, enabled: true),
                "The pure direct API uses caller-evaluated policy, not process or ambient policy.");
            Assert.IsNull(TryCaptureBounded(null!, null!, enabled: false));
        }

        [DataTestMethod]
        [DataRow(-1, "x", true)]
        [DataRow(0, "x", true)]
        [DataRow(1, "x", false)]
        [DataRow(-1, "é", true)]
        [DataRow(0, "é", true)]
        [DataRow(1, "é", false)]
        public void RawInputLimitCountsCharactersNotUtf8BytesAndPrecedesModelInspection(int offset, string filler, bool captured)
        {
            Assert.AreEqual(CHARACTER_LIMIT, MAX_COMMAND_JSON_CHARACTERS);
            string json = JsonAtLength(CHARACTER_LIMIT + offset, filler[0]);
            Assert.AreEqual(CHARACTER_LIMIT + offset, json.Length);
            if (filler != "x")
            {
                Assert.IsTrue(Encoding.UTF8.GetByteCount(json) > CHARACTER_LIMIT,
                    "The accepted non-ASCII boundary must really exceed the byte limit.");
            }

            RuntimeConfig ordinary = CreateConfig();
            int reads = 0;
            RuntimeConfig observed = new ObservedRuntimeConfig(ordinary, () => { reads++; return ordinary.Entities; });
            TelemetryConfigurationPresence? presence = TryCaptureBounded(json, observed, enabled: true);
            Assert.AreEqual(captured, presence is not null);
            Assert.AreEqual(captured ? 1 : 0, reads);
            if (presence is not null)
            {
                AssertFixedShape(presence);
            }
        }

        [DataTestMethod]
        [DataRow(4095, true)]
        [DataRow(4096, true)]
        [DataRow(4097, false)]
        [DataRow(int.MaxValue, false)]
        public void EntityBudgetDoesNotTrustDictionaryCountOrEnumeratePastOneLookahead(int count, bool captured)
        {
            Assert.AreEqual(ENTITY_LIMIT, MAX_COMMAND_ENTITIES);
            CountingEntities entries = new(count, maximumMoves: ENTITY_LIMIT + 1);
            RuntimeConfig config = CreateConfig(new SequenceDictionary(entries));
            TelemetryConfigurationPresence? presence = TryCaptureBounded(ENTITY_JSON, config, enabled: true);
            Assert.AreEqual(captured, presence is not null);
            Assert.AreEqual(Math.Min(count, ENTITY_LIMIT + 1), entries.Moves);
            Assert.AreEqual(1, entries.Disposals);
            if (presence is not null)
            {
                AssertFixedShape(presence);
                Assert.AreEqual(new EntityPresence(0, 1, 0, 0), presence.Entities[EntityFeature.Rest]);
            }
        }

        [TestMethod]
        public void BoundedSettingsKeepMissingAndExplicitNullDistinct()
        {
            RuntimeConfig config = CreateConfig();
            TelemetryConfigurationPresence? missing = TryCaptureBounded("{}", config, enabled: true);
            TelemetryConfigurationPresence? nulls = TryCaptureBounded("""
                { "runtime": null, "data-source": null, "azure-key-vault": null, "autoentities": null, "data-source-files": null }
                """, config, enabled: true);
            Assert.IsNotNull(missing);
            Assert.IsNotNull(nulls);
            AssertFixedShape(missing);
            AssertFixedShape(nulls);
            Assert.IsTrue(missing.Settings.Values.All(value => value == Presence.Missing));
            Assert.IsTrue(nulls.Settings.Values.All(value => value == Presence.ExplicitNull));
            CollectionAssert.AreEquivalent(missing.Settings.Keys.ToArray(), nulls.Settings.Keys.ToArray());
            Assert.IsTrue(missing.Entities.Values.All(value => value.Total == 0));
            Assert.IsTrue(nulls.Entities.Values.All(value => value.Total == 0));
        }

        [TestMethod]
        public void BoundedEntityFeaturesPreserveMissingNullAndPresentWithoutTheirValues()
        {
            const string json = """
                { "entities": {
                  "Missing": { "source": { "object": "synthetic", "type": "stored-procedure" } },
                  "Null": { "source": { "object": "synthetic", "type": "stored-procedure" },
                    "rest": null, "graphql": null, "cache": null, "mcp": null },
                  "Present": { "source": { "object": "synthetic", "type": "stored-procedure" },
                    "rest": false, "graphql": "PRIVATE_COMMAND_PRESENCE_780ac", "cache": { "enabled": false },
                    "mcp": { "dml-tools": true, "custom-tool": false } }
                } }
                """;
            RuntimeConfig config = CreateConfig(new Dictionary<string, Entity>
            {
                ["Missing"] = NewEntity(EntitySourceType.StoredProcedure),
                ["Null"] = NewEntity(EntitySourceType.StoredProcedure),
                ["Present"] = NewEntity(EntitySourceType.StoredProcedure)
            });
            TelemetryConfigurationPresence? bounded = TryCaptureBounded(json, config, enabled: true);
            TelemetryConfigurationPresence? engine = TryCapture(json, config, enabled: true);
            Assert.IsNotNull(bounded);
            Assert.IsNotNull(engine);
            AssertFixedShape(bounded);
            foreach (EntityFeature feature in Enum.GetValues<EntityFeature>())
            {
                Assert.AreEqual(new EntityPresence(1, 1, 1, 0), bounded.Entities[feature], feature.ToString());
            }

            CollectionAssert.AreEquivalent(engine.Settings.ToArray(), bounded.Settings.ToArray());
            CollectionAssert.AreEquivalent(engine.Entities.ToArray(), bounded.Entities.ToArray());
        }

        [TestMethod]
        public void NullChildEnginePermissionInheritsCommandPolicyAndBounds()
        {
            string oversized = JsonAtLength(CHARACTER_LIMIT + 1);
            int permissions = 0;
            using (BeginCommandCapture(() => { permissions++; return true; }))
            {
                using (IDisposable? child = BeginCapture(permission: null))
                {
                    Assert.IsNull(child);
                    Assert.IsNotNull(ParseSuccessfully(SMALL_JSON).TelemetryPresence);
                    Assert.IsNull(ParseSuccessfully(oversized).TelemetryPresence);
                }

                Assert.IsNull(ParseSuccessfully(oversized).TelemetryPresence);
                Assert.AreEqual(3, permissions);
            }

            Assert.IsFalse(IsCaptureEnabled());
        }

        [TestMethod]
        public void ExplicitDisabledEnginePolicyOverridesEnabledCommandAndThenRestoresIt()
        {
            int commandReads = 0;
            int engineReads = 0;
            using (BeginCommandCapture(() => { commandReads++; return true; }))
            {
                Assert.IsNotNull(ParseSuccessfully(SMALL_JSON).TelemetryPresence);
                using (BeginCapture(() => { engineReads++; return false; }))
                {
                    using (BeginCapture(permission: null))
                    {
                        Assert.IsNull(ParseSuccessfully(SMALL_JSON).TelemetryPresence);
                        Assert.IsNull(TryCaptureForCurrentScope(null!, null!));
                    }

                    Assert.AreEqual(1, commandReads, "Explicit engine policy must not consult its enclosing command.");
                    Assert.AreEqual(2, engineReads);
                }

                Assert.IsNotNull(ParseSuccessfully(SMALL_JSON).TelemetryPresence);
                Assert.AreEqual(2, commandReads);
            }

            Assert.IsFalse(IsCaptureEnabled());
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void DisabledCommandInsideEnabledEngineSuppressesInheritedCallbackAndThenRestoresIt(bool nullPermission)
        {
            int engineReads = 0;
            int commandReads = 0;
            using (BeginCapture(() => { engineReads++; return true; }))
            {
                Func<bool>? permission = nullPermission ? null : () => { commandReads++; return false; };
                using (BeginCommandCapture(permission))
                {
                    Assert.IsNull(ParseSuccessfully(SMALL_JSON).TelemetryPresence);
                    Assert.IsNull(TryCaptureForCurrentScope(null!, null!));
                    Assert.AreEqual(0, engineReads, "A disabled nested CLI must not invoke the engine's callback.");
                    Assert.AreEqual(nullPermission ? 0 : 2, commandReads);
                }

                Assert.IsNotNull(ParseSuccessfully(SMALL_JSON).TelemetryPresence);
                Assert.AreEqual(1, engineReads);
            }

            Assert.IsFalse(IsCaptureEnabled());
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ExplicitEnabledEngineRestoresUnboundedParsingInsideEitherCommandPolicy(bool commandEnabled)
        {
            string oversized = JsonAtLength(CHARACTER_LIMIT + 1);
            using (BeginCommandCapture(() => commandEnabled))
            {
                RuntimeConfig limited = ParseSuccessfully(oversized);
                Assert.IsFalse(limited.IsRestEnabled);
                Assert.IsNull(limited.TelemetryPresence, "Dropping optional provenance must not reject valid large input.");
                using (BeginCapture(() => true))
                {
                    using (BeginCapture(permission: null))
                    {
                        RuntimeConfig engine = ParseSuccessfully(oversized);
                        Assert.IsFalse(engine.IsRestEnabled);
                        Assert.IsNotNull(engine.TelemetryPresence, "An explicit engine scope must not inherit the CLI size cap.");
                    }
                }

                Assert.AreEqual(commandEnabled, ParseSuccessfully(SMALL_JSON).TelemetryPresence is not null);
                Assert.IsNull(ParseSuccessfully(oversized).TelemetryPresence);
            }

            Assert.IsFalse(IsCaptureEnabled());
        }

        [TestMethod]
        public void ExplicitEngineScopeAlsoLiftsTheCommandEntityBudgetAndRestoresIt()
        {
            CountingEntities entries = new(ENTITY_LIMIT + 1, maximumMoves: ENTITY_LIMIT + 1);
            RuntimeConfig config = CreateConfig(new SequenceDictionary(entries));
            using (BeginCommandCapture(() => true))
            {
                Assert.IsNull(TryCaptureForCurrentScope(ENTITY_JSON, config));
                using (BeginCapture(() => true))
                {
                    TelemetryConfigurationPresence? presence = TryCaptureForCurrentScope(ENTITY_JSON, config);
                    Assert.IsNotNull(presence);
                    Assert.AreEqual(new EntityPresence(0, 1, 0, 0), presence.Entities[EntityFeature.Rest]);
                }

                Assert.IsNull(TryCaptureForCurrentScope(ENTITY_JSON, config));
            }

            Assert.AreEqual(3 * (ENTITY_LIMIT + 1), entries.Moves);
            Assert.AreEqual(3, entries.Disposals);
        }

        [TestMethod]
        public void ExceptionsRestoreBothThePreviousPermissionAndItsBoundedness()
        {
            RuntimeConfig config = CreateConfig();
            string oversized = JsonAtLength(CHARACTER_LIMIT + 1);
            InvalidOperationException failure = new("Synthetic scope exit.");
            using (BeginCapture(() => true))
            {
                Assert.AreSame(failure, Assert.ThrowsException<InvalidOperationException>(() =>
                {
                    using IDisposable? command = BeginCommandCapture(() => true);
                    Assert.IsNotNull(TryCaptureForCurrentScope(SMALL_JSON, config));
                    Assert.IsNull(TryCaptureForCurrentScope(oversized, config));
                    using IDisposable? denied = BeginCapture(() => false);
                    Assert.IsNull(TryCaptureForCurrentScope(SMALL_JSON, config));
                    throw failure;
                }));
                Assert.IsNotNull(TryCaptureForCurrentScope(oversized, config), "Unwinding must restore the unbounded engine scope.");
            }

            using (BeginCommandCapture(() => true))
            {
                Assert.AreSame(failure, Assert.ThrowsException<InvalidOperationException>(() =>
                {
                    using IDisposable? engine = BeginCapture(() => true);
                    Assert.IsNotNull(TryCaptureForCurrentScope(oversized, config));
                    throw failure;
                }));
                Assert.IsNotNull(TryCaptureForCurrentScope(SMALL_JSON, config));
                Assert.IsNull(TryCaptureForCurrentScope(oversized, config), "Unwinding must restore the command cap as well as permission.");
            }

            Assert.IsFalse(IsCaptureEnabled());
        }

        [TestMethod]
        public async Task ParallelAsyncCommandScopesDoNotLeakPolicyOrBoundsIntoSiblingsOrParent()
        {
            RuntimeConfig config = CreateConfig();
            string oversized = JsonAtLength(CHARACTER_LIMIT + 1);
            TaskCompletionSource enabledEntered = Signal();
            TaskCompletionSource disabledEntered = Signal();
            TaskCompletionSource release = Signal();

            async Task ExerciseAsync(bool enabled, TaskCompletionSource entered)
            {
                using (BeginCommandCapture(enabled ? () => true : null))
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(_timeout);
                    Assert.AreEqual(enabled, TryCaptureForCurrentScope(SMALL_JSON, config) is not null);
                    Assert.IsNull(TryCaptureForCurrentScope(oversized, config));
                }

                Assert.IsNotNull(TryCaptureForCurrentScope(oversized, config), "Each child restores its inherited engine scope.");
            }

            using (BeginCapture(() => true))
            {
                Task enabled = Task.Run(() => ExerciseAsync(true, enabledEntered));
                Task disabled = Task.Run(() => ExerciseAsync(false, disabledEntered));
                try
                {
                    await Task.WhenAll(enabledEntered.Task, disabledEntered.Task).WaitAsync(_timeout);
                    Assert.IsNotNull(TryCaptureForCurrentScope(oversized, config), "Both child scopes are active while the parent remains unbounded.");
                }
                finally
                {
                    release.TrySetResult();
                    await Task.WhenAll(enabled, disabled).WaitAsync(_timeout);
                }

                Assert.IsNotNull(TryCaptureForCurrentScope(oversized, config));
            }

            Assert.IsFalse(IsCaptureEnabled());
            Assert.IsNull(TryCaptureForCurrentScope(SMALL_JSON, config));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void FaultingPermissionCannotMakeNormalProductParsingFail(bool commandScope)
        {
            int calls = 0;
            Func<bool> permission = () => { calls++; throw new InvalidOperationException(SENTINEL); };
            using (commandScope ? BeginCommandCapture(permission) : BeginCapture(permission))
            {
                RuntimeConfig config = ParseSuccessfully(SMALL_JSON);
                Assert.IsFalse(config.IsRestEnabled);
                Assert.IsNull(config.TelemetryPresence);
                Assert.AreEqual(1, calls);
                Assert.IsFalse(RuntimeConfigLoader.TryParseConfig("{", out RuntimeConfig? invalid, out string? error));
                Assert.IsNull(invalid);
                Assert.IsFalse(string.IsNullOrEmpty(error), "Actual product JSON failures must retain normal parser behavior.");
                Assert.AreEqual(1, calls, "Invalid product JSON must fail before optional capture.");
            }

            using (BeginCommandCapture(() => true))
            {
                Assert.IsNotNull(ParseSuccessfully(SMALL_JSON).TelemetryPresence);
            }
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("{")]
        [DataRow("[]")]
        [DataRow("null")]
        [DataRow("true")]
        [DataRow("{\"entities\":{},}")]
        public void InvalidCaptureInputReturnsNullWithoutPoisoningTheScopeOrNormalParse(string? json)
        {
            RuntimeConfig config = CreateConfig();
            using IDisposable? scope = BeginCommandCapture(() => true);
            Assert.IsNull(TryCaptureBounded(json!, config, enabled: true));
            Assert.IsNull(TryCaptureForCurrentScope(json!, config));
            Assert.IsNotNull(ParseSuccessfully(SMALL_JSON).TelemetryPresence);
        }

        [DataTestMethod]
        [DataRow("get_enumerator", false)]
        [DataRow("move_next", false)]
        [DataRow("current", false)]
        [DataRow("dispose", false)]
        [DataRow("get_enumerator", true)]
        [DataRow("move_next", true)]
        [DataRow("current", true)]
        [DataRow("dispose", true)]
        public void OptionalEnumerationFailuresReturnNullAndDoNotPoisonSubsequentProductParse(string failure, bool commandScope)
        {
            FaultingEntities entries = new(failure);
            RuntimeConfig config = CreateConfig(new SequenceDictionary(entries));
            using IDisposable? scope = commandScope ? BeginCommandCapture(() => true) : BeginCapture(() => true);
            Assert.IsNull(TryCaptureBounded(ENTITY_JSON, config, enabled: true));
            Assert.IsNull(TryCaptureForCurrentScope(ENTITY_JSON, config));
            Assert.AreEqual(2, entries.Failures, "Both paths must actually exercise the requested enumeration failure.");
            Assert.IsNotNull(ParseSuccessfully(SMALL_JSON).TelemetryPresence);
        }

        private static string JsonAtLength(int length, char filler = 'x')
        {
            const string prefix = "{\"runtime\":{\"rest\":false},\"padding\":\"";
            const string suffix = "\",\"entities\":{}}";
            return prefix + new string(filler, length - prefix.Length - suffix.Length) + suffix;
        }

        private static RuntimeConfig CreateConfig(IReadOnlyDictionary<string, Entity>? entities = null)
        {
            RuntimeEntities raw = new(new Dictionary<string, Entity>()) { Entities = entities ?? new Dictionary<string, Entity>() };
            // No source, declarations or constructor-time enumeration of the custom dictionary.
            return new(Schema: SENTINEL, DataSource: null, Entities: raw);
        }

        private static Entity NewEntity(EntitySourceType type = EntitySourceType.Table)
            => new(new(SENTINEL, type, null, null), new(SENTINEL, SENTINEL), null, new(), [], null, null);

        private static RuntimeConfig ParseSuccessfully(string json)
        {
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(json, out RuntimeConfig? config, out string? error,
                replacementSettings: new DeserializationVariableReplacementSettings(doReplaceEnvVar: false, doReplaceAkvVar: false)
                { SkipApplicationNameInjection = true }));
            Assert.IsNull(error);
            Assert.IsNotNull(config);
            return config;
        }

        private static void AssertFixedShape(TelemetryConfigurationPresence presence)
        {
            Assert.AreEqual(26, presence.Settings.Count);
            Assert.AreEqual(5, presence.Entities.Count);
            string json = JsonSerializer.Serialize(presence);
            Assert.IsTrue(json.Length < 8192);
            Assert.IsFalse(json.Contains(SENTINEL, StringComparison.Ordinal));
        }

        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed record ObservedRuntimeConfig(RuntimeConfig Original, Func<RuntimeEntities> ReadEntities) : RuntimeConfig(Original)
        {
            public override RuntimeEntities Entities
            {
                get => ReadEntities();
                init => base.Entities = value;
            }
        }

        private sealed class SequenceDictionary(IEnumerable<KeyValuePair<string, Entity>> entries) : IReadOnlyDictionary<string, Entity>
        {
            public int Count => throw new InvalidOperationException("Capture must enumerate with a bound, not trust Count.");
            public IEnumerable<string> Keys => throw new InvalidOperationException();
            public IEnumerable<Entity> Values => throw new InvalidOperationException();
            public Entity this[string key] => throw new InvalidOperationException();
            public bool ContainsKey(string key) => throw new InvalidOperationException();
            public bool TryGetValue(string key, out Entity value) => throw new InvalidOperationException();
            public IEnumerator<KeyValuePair<string, Entity>> GetEnumerator() => entries.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class CountingEntities(int count, int maximumMoves) : IEnumerable<KeyValuePair<string, Entity>>
        {
            private readonly Entity _entity = NewEntity();
            internal int Moves { get; private set; }
            internal int Disposals { get; private set; }

            public IEnumerator<KeyValuePair<string, Entity>> GetEnumerator()
            {
                try
                {
                    for (int index = 0; index < count; index++)
                    {
                        Moves++;
                        if (index >= maximumMoves)
                        {
                            // A missing product cap fails quickly instead of hanging the test.
                            throw new InvalidOperationException("Entity enumeration exceeded its lookahead budget.");
                        }

                        yield return new(index.ToString(CultureInfo.InvariantCulture), _entity);
                    }
                }
                finally
                {
                    Disposals++;
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class FaultingEntities(string failure) : IEnumerable<KeyValuePair<string, Entity>>
        {
            internal int Failures { get; private set; }

            private void Fail(string phase)
            {
                if (phase == failure)
                {
                    Failures++;
                    throw new InvalidOperationException(SENTINEL);
                }
            }

            public IEnumerator<KeyValuePair<string, Entity>> GetEnumerator()
            {
                Fail("get_enumerator");
                return new Enumerator(this);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class Enumerator(FaultingEntities owner) : IEnumerator<KeyValuePair<string, Entity>>
            {
                private bool _visited;
                public KeyValuePair<string, Entity> Current
                {
                    get
                    {
                        owner.Fail("current");
                        return new("0", NewEntity());
                    }
                }

                object IEnumerator.Current => Current;

                public bool MoveNext()
                {
                    owner.Fail("move_next");
                    bool next = !_visited;
                    _visited = true;
                    return next;
                }

                public void Reset() => throw new NotSupportedException();
                public void Dispose() => owner.Fail("dispose");
            }
        }
    }
}
