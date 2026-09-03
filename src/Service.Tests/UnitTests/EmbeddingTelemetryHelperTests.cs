// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Azure.DataApiBuilder.Core.Services.Embeddings;
using Azure.DataApiBuilder.Core.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class EmbeddingTelemetryHelperTests
    {
        /// <summary>
        /// Verifies request, API, cache, error, duration, token, text-count, and dimension helpers publish their expected instruments.
        /// </summary>
        [TestMethod]
        public void MetricHelpers_RecordAllEmbeddingMeasurements()
        {
            List<string> measurements = new();
            using MeterListener listener = new();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == EmbeddingTelemetryHelper.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => measurements.Add(instrument.Name));
            listener.SetMeasurementEventCallback<int>((instrument, _, _, _) => measurements.Add(instrument.Name));
            listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => measurements.Add(instrument.Name));
            listener.Start();

            EmbeddingTelemetryHelper.TrackEmbeddingRequest("provider", 2);
            EmbeddingTelemetryHelper.TrackApiCall("provider", 2);
            EmbeddingTelemetryHelper.TrackCacheHit("provider");
            EmbeddingTelemetryHelper.TrackCacheMiss("provider");
            EmbeddingTelemetryHelper.TrackError("provider", "failure");
            EmbeddingTelemetryHelper.TrackApiDuration("provider", TimeSpan.FromMilliseconds(12), 2);
            EmbeddingTelemetryHelper.TrackTotalDuration("provider", TimeSpan.FromMilliseconds(20), fromCache: true);
            EmbeddingTelemetryHelper.TrackTokenUsage("provider", 30);
            EmbeddingTelemetryHelper.TrackDimensions("provider", 1536);

            CollectionAssert.Contains(measurements, "embedding_requests_total");
            CollectionAssert.Contains(measurements, "embedding_api_calls_total");
            CollectionAssert.Contains(measurements, "embedding_cache_hits_total");
            CollectionAssert.Contains(measurements, "embedding_cache_misses_total");
            CollectionAssert.Contains(measurements, "embedding_errors_total");
            CollectionAssert.Contains(measurements, "embedding_texts_processed_total");
            CollectionAssert.Contains(measurements, "embedding_api_duration_ms");
            CollectionAssert.Contains(measurements, "embedding_total_duration_ms");
            CollectionAssert.Contains(measurements, "embedding_tokens_total");
            CollectionAssert.Contains(measurements, "embedding_dimensions");
        }

        /// <summary>
        /// Verifies embedding, cache, duration, and dimension helpers compose their tags and mark the activity successful.
        /// </summary>
        [TestMethod]
        public void ActivityHelpers_SetSuccessAndCacheTags()
        {
            using ActivityListener listener = CreateListener();
            using Activity? activity = EmbeddingTelemetryHelper.StartEmbeddingActivity("EmbedAsync");
            Assert.IsNotNull(activity);

            activity.SetEmbeddingActivityTags("azure-openai", "model", 3);
            activity.SetCacheActivityTags(2, 1);
            activity.SetEmbeddingActivitySuccess(12.5, 1536);

            Assert.AreEqual("azure-openai", activity.GetTagItem("embedding.provider"));
            Assert.AreEqual("model", activity.GetTagItem("embedding.model"));
            Assert.AreEqual(3, activity.GetTagItem("embedding.text_count"));
            Assert.AreEqual(2, activity.GetTagItem("embedding.cache_hits"));
            Assert.AreEqual(1, activity.GetTagItem("embedding.cache_misses"));
            Assert.AreEqual(12.5, activity.GetTagItem("embedding.duration_ms"));
            Assert.AreEqual(1536, activity.GetTagItem("embedding.dimensions"));
            Assert.AreEqual(ActivityStatusCode.Ok, activity.Status);
        }

        [TestMethod]
        public void ActivityHelpers_OmitOptionalModelAndDimensions()
        {
            using ActivityListener listener = CreateListener();
            using Activity? activity = EmbeddingTelemetryHelper.StartEmbeddingActivity("EmbedBatchAsync");
            Assert.IsNotNull(activity);

            activity.SetEmbeddingActivityTags("openai", null, 1);
            activity.SetEmbeddingActivitySuccess(1.5);

            Assert.IsNull(activity.GetTagItem("embedding.model"));
            Assert.IsNull(activity.GetTagItem("embedding.dimensions"));
        }

        [TestMethod]
        public void SetEmbeddingActivityError_RecordsExceptionDetails()
        {
            using ActivityListener listener = CreateListener();
            using Activity? activity = EmbeddingTelemetryHelper.StartEmbeddingActivity("EmbedAsync");
            Assert.IsNotNull(activity);
            InvalidOperationException error = new("boom");

            activity.SetEmbeddingActivityError(error);

            Assert.AreEqual(ActivityStatusCode.Error, activity.Status);
            Assert.AreEqual("boom", activity.StatusDescription);
            Assert.AreEqual(nameof(InvalidOperationException), activity.GetTagItem("error.type"));
            Assert.AreEqual("boom", activity.GetTagItem("error.message"));
        }

        private static ActivityListener CreateListener()
        {
            string sourceName = TelemetryTracesHelper.DABActivitySource.Name;
            ActivityListener listener = new()
            {
                ShouldListenTo = source => source.Name == sourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
            };
            ActivitySource.AddActivityListener(listener);
            return listener;
        }
    }
}
