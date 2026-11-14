// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>A closed, value-free category, not a database identifier or command-line value.</summary>
    internal enum CliTelemetryDatabaseType
    {
        Unknown = 0,
        MsSql,
        DwSql,
        PostgreSql,
        MySql,
        CosmosDbNoSql,
        CosmosDbPostgreSql,
        Multiple
    }

    /// <summary>
    /// Projects only an already loaded or successfully saved in-memory model. Never loads a
    /// config, resolves a path, serializes the model, or reads connection strings or options.
    /// All traversal state is local and discarded; only the closed enum leaves this boundary.
    /// </summary>
    internal static class CliTelemetryDatabaseTypeFormatter
    {
        // One shared budget: each config and each projected source consumes one node. The
        // default map entry replaced by the actual root DataSource is not counted twice.
        // Thus a single config can project at most 255 sources. Child declarations are checked
        // once in loader order, with at most one lookahead beyond its bounded ChildConfigs.
        private const int MAX_NODES = 256;

        internal static CliTelemetryDatabaseType FromConfiguration(RuntimeConfig? config)
        {
            if (config is null)
            {
                return CliTelemetryDatabaseType.Unknown;
            }

            try
            {
                int remainingNodes = MAX_NODES;
                if (!HasCompleteChildConfigs(config, ref remainingNodes))
                {
                    return CliTelemetryDatabaseType.Unknown;
                }

                CliTelemetryDatabaseType result = CliTelemetryDatabaseType.Unknown;
                DataSource? rootSource = config.DataSource;
                if (rootSource is not null && !IncludeSource(rootSource, ref remainingNodes, ref result))
                {
                    return CliTelemetryDatabaseType.Unknown;
                }

                foreach ((string name, DataSource source) in config.GetDataSourceNamesToDataSourcesIterator())
                {
                    // Configure uses record clones: DataSource changes but the internal map
                    // still contains the previous default. The actual root field wins.
                    if (rootSource is not null && string.Equals(name, config.DefaultDataSourceName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!IncludeSource(source, ref remainingNodes, ref result))
                    {
                        return CliTelemetryDatabaseType.Unknown;
                    }
                }

                return result;
            }
            catch (Exception)
            {
                // Optional projection, including a failed or concurrently changed enumerable,
                // must not fail a command or turn partial evidence into a known provider.
                return CliTelemetryDatabaseType.Unknown;
            }
        }

        internal static CliTelemetryDatabaseType Normalize(CliTelemetryDatabaseType value) => value switch
        {
            CliTelemetryDatabaseType.MsSql or CliTelemetryDatabaseType.DwSql or CliTelemetryDatabaseType.PostgreSql
                or CliTelemetryDatabaseType.MySql or CliTelemetryDatabaseType.CosmosDbNoSql
                or CliTelemetryDatabaseType.CosmosDbPostgreSql or CliTelemetryDatabaseType.Multiple => value,
            _ => CliTelemetryDatabaseType.Unknown
        };

        internal static string Wire(CliTelemetryDatabaseType value) => value switch
        {
            CliTelemetryDatabaseType.MsSql => "mssql",
            CliTelemetryDatabaseType.DwSql => "dwsql",
            CliTelemetryDatabaseType.PostgreSql => "postgresql",
            CliTelemetryDatabaseType.MySql => "mysql",
            CliTelemetryDatabaseType.CosmosDbNoSql => "cosmosdb_nosql",
            CliTelemetryDatabaseType.CosmosDbPostgreSql => "cosmosdb_postgresql",
            CliTelemetryDatabaseType.Multiple => "multiple",
            _ => "unknown"
        };

        private static bool HasCompleteChildConfigs(RuntimeConfig root, ref int remainingNodes)
        {
            Stack<RuntimeConfig> pending = new();
            HashSet<RuntimeConfig> visited = new(ReferenceEqualityComparer.Instance);
            pending.Push(root);
            while (pending.TryPop(out RuntimeConfig? config))
            {
                if (remainingNodes == 0 || !visited.Add(config))
                {
                    return false;
                }

                remainingNodes--;
                int childCount = config.ChildConfigs.Count;
                IEnumerable<string>? sourceFiles = config.DataSourceFiles?.SourceFiles;
                if (sourceFiles is null)
                {
                    // Explicit merged maps need not carry child records when there are no
                    // file declarations. Retained children after removing declarations are
                    // different: that clone no longer describes the model that was loaded.
                    if (childCount != 0)
                    {
                        return false;
                    }

                    continue;
                }

                if (childCount > remainingNodes - pending.Count)
                {
                    return false;
                }

                using IEnumerator<string> declarations = sourceFiles.GetEnumerator();
                for (int index = 0; index < childCount; index++)
                {
                    (string fileName, RuntimeConfig child) = config.ChildConfigs[index];
                    if (!declarations.MoveNext() || child is null || string.IsNullOrWhiteSpace(fileName)
                        || !string.Equals(declarations.Current, fileName, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    pending.Push(child);
                }

                if (declarations.MoveNext() || config.ChildConfigs.Count != childCount)
                {
                    // Includes missing files skipped by normal loading and changed declarations
                    // on a record clone. Do not discover, load, or infer those sources here.
                    return false;
                }
            }

            return true;
        }

        private static bool IncludeSource(DataSource? source, ref int remainingNodes, ref CliTelemetryDatabaseType result)
        {
            if (remainingNodes == 0)
            {
                return false;
            }

            remainingNodes--;
            CliTelemetryDatabaseType provider = source?.DatabaseType switch
            {
                DatabaseType.MSSQL => CliTelemetryDatabaseType.MsSql,
                DatabaseType.DWSQL => CliTelemetryDatabaseType.DwSql,
                DatabaseType.PostgreSQL => CliTelemetryDatabaseType.PostgreSql,
                DatabaseType.MySQL => CliTelemetryDatabaseType.MySql,
                DatabaseType.CosmosDB_NoSQL => CliTelemetryDatabaseType.CosmosDbNoSql,
                DatabaseType.CosmosDB_PostgreSQL => CliTelemetryDatabaseType.CosmosDbPostgreSql,
                _ => CliTelemetryDatabaseType.Unknown
            };
            if (provider == CliTelemetryDatabaseType.Unknown)
            {
                return false;
            }

            result = result == CliTelemetryDatabaseType.Unknown || result == provider ? provider : CliTelemetryDatabaseType.Multiple;
            return true;
        }
    }
}
