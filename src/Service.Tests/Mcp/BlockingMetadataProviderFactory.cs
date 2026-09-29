// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Test metadata provider factory that pauses initialization until explicitly released.
    /// </summary>
    internal sealed class BlockingMetadataProviderFactory : IMetadataProviderFactory
    {
        private readonly TaskCompletionSource _completeInitialization =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _initializationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets a signal that completes when metadata initialization starts.
        /// </summary>
        public Task InitializationStarted => _initializationStarted.Task;

        /// <summary>
        /// Gets the number of metadata initialization calls.
        /// </summary>
        public int InitializeAsyncCallCount { get; private set; }

        /// <inheritdoc />
        public Task InitializeAsync() => InitializeAsync(CancellationToken.None);

        /// <inheritdoc />
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            InitializeAsyncCallCount++;
            _initializationStarted.TrySetResult();
            await _completeInitialization.Task.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Releases the blocked metadata initialization operation.
        /// </summary>
        public void CompleteInitialization()
        {
            _completeInitialization.TrySetResult();
        }

        /// <inheritdoc />
        public ISqlMetadataProvider GetMetadataProvider(string dataSourceName)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public IEnumerable<ISqlMetadataProvider> ListMetadataProviders()
            => Array.Empty<ISqlMetadataProvider>();

        /// <inheritdoc />
        public List<Exception> GetAllMetadataExceptions()
            => new();

        /// <inheritdoc />
        public void InitializeAsync(
            Dictionary<string, Dictionary<string, DatabaseObject>> entityToDatabaseObjectMap,
            Dictionary<string, Dictionary<string, string>> graphQLStoredProcedureExposedNameToEntityNameMap)
        {
            InitializeAsyncCallCount++;
        }
    }
}
