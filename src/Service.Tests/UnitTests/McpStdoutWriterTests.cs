// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="McpStdoutWriter"/> — the process-wide,
    /// lock-protected owner of the MCP stdio JSON-RPC channel.
    /// Validates concurrency safety (no torn lines), disposal idempotency,
    /// and post-dispose write semantics.
    /// </summary>
    [TestClass]
    public class McpStdoutWriterTests
    {
        /// <summary>
        /// Calling <see cref="McpStdoutWriter.Dispose"/> twice must not throw.
        /// This guards against double-shutdown (e.g. ProcessExit hook running
        /// alongside DI container disposal).
        /// </summary>
        [TestMethod]
        public void Dispose_IsIdempotent()
        {
            // Arrange — back the writer with an in-memory stream so we never
            // touch the real stdout from a unit test. leaveOpen:true so the
            // 'using' on MemoryStream is the sole owner.
            using MemoryStream ms = new();
            StreamWriter inner = new(
                ms,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: -1,
                leaveOpen: true);
            McpStdoutWriter writer = new(inner);

            // Act
            writer.Dispose();
            writer.Dispose(); // Second call must be a no-op.

            // Assert — no exception thrown is the success criterion.
        }

        /// <summary>
        /// After <see cref="McpStdoutWriter.Dispose"/>, further
        /// <see cref="McpStdoutWriter.WriteLine"/> calls must silently no-op.
        /// Late writes can occur from queued logger callbacks during shutdown,
        /// and they must not throw <c>ObjectDisposedException</c> through the
        /// logging pipeline.
        /// </summary>
        [TestMethod]
        public void WriteLine_AfterDispose_IsNoOp()
        {
            // Arrange — leaveOpen:true so disposing the writer doesn't close
            // the MemoryStream we still need to inspect afterwards.
            using MemoryStream ms = new();
            StreamWriter inner = new(
                ms,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: -1,
                leaveOpen: true)
            {
                AutoFlush = true
            };
            McpStdoutWriter writer = new(inner);

            // Write one line so we have a known baseline length.
            writer.WriteLine("before-dispose");
            long lengthBeforeDispose = ms.Length;

            // Act — dispose then attempt to write.
            writer.Dispose();
            writer.WriteLine("after-dispose"); // Must not throw.

            // Assert — stream length must not have grown after dispose.
            Assert.AreEqual(
                expected: lengthBeforeDispose,
                actual: ms.Length,
                message: "WriteLine after Dispose must be a silent no-op.");
        }

        /// <summary>
        /// Heavy concurrency test that exercises the lock contract:
        /// many threads calling <see cref="McpStdoutWriter.WriteLine"/> in
        /// parallel must produce intact, non-interleaved lines on the stream.
        /// This is the core invariant that protects the MCP JSON-RPC channel
        /// from byte-level corruption when notifications and responses race.
        /// </summary>
        [TestMethod]
        public void WriteLine_FromManyThreads_ProducesIntactNonInterleavedLines()
        {
            // Arrange
            const int threadCount = 16;
            const int writesPerThread = 500;
            const int totalWrites = threadCount * writesPerThread;

            using MemoryStream ms = new();
            StreamWriter inner = new(
                ms,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: -1,
                leaveOpen: true)
            {
                AutoFlush = true
            };
            using McpStdoutWriter writer = new(inner);

            // Each thread emits a unique, recognizable payload.
            // Format: "thread-{id}-write-{sequence}" — easy to parse and tally.
            ConcurrentBag<string> expected = new();

            // Act — fan out N parallel producers.
            Parallel.For(0, threadCount, threadId =>
            {
                for (int i = 0; i < writesPerThread; i++)
                {
                    string line = $"thread-{threadId:D2}-write-{i:D4}";
                    expected.Add(line);
                    writer.WriteLine(line);
                }
            });

            // Flush by disposing the underlying writer reference (the
            // McpStdoutWriter wraps it; dispose forwards to inner).
            writer.Dispose();

            // Assert — read back and verify line-by-line integrity.
            ms.Position = 0;
            using StreamReader reader = new(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            string content = reader.ReadToEnd();
            string[] lines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            // 1. All writes accounted for — no dropped or extra lines.
            Assert.AreEqual(
                expected: totalWrites,
                actual: lines.Length,
                message: $"Expected {totalWrites} intact lines but got {lines.Length}.");

            // 2. Every line matches the exact pattern (no torn writes).
            //    A torn write would produce a malformed line that doesn't fit
            //    the "thread-XX-write-YYYY" template.
            string[] malformed = lines
                .Where(l => !System.Text.RegularExpressions.Regex.IsMatch(l, @"^thread-\d{2}-write-\d{4}$"))
                .ToArray();
            Assert.AreEqual(
                expected: 0,
                actual: malformed.Length,
                message: $"Found {malformed.Length} torn/interleaved lines. First: '{(malformed.Length > 0 ? malformed[0] : string.Empty)}'.");

            // 3. The set of emitted lines exactly matches the set produced by threads.
            HashSet<string> actualSet = new(lines);
            HashSet<string> expectedSet = new(expected);
            Assert.IsTrue(
                actualSet.SetEquals(expectedSet),
                "Set of emitted lines does not match set produced by threads.");
        }

        /// <summary>
        /// A late <see cref="McpStdoutWriter.Dispose"/> racing with concurrent
        /// writes must be safe: writes that win the lock first complete; writes
        /// that arrive after Dispose silently no-op. No exception, no crash.
        /// </summary>
        [TestMethod]
        public void Dispose_DuringConcurrentWrites_DoesNotThrow()
        {
            // Arrange
            using MemoryStream ms = new();
            StreamWriter inner = new(
                ms,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: -1,
                leaveOpen: true)
            {
                AutoFlush = true
            };
            McpStdoutWriter writer = new(inner);

            // Act — kick off a producer in the background and dispose mid-flight.
            Task producer = Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    writer.WriteLine($"line-{i:D4}");
                }
            });

            // Small delay to let some writes happen, then dispose.
            Thread.Sleep(5);
            writer.Dispose();

            // Wait for the producer to finish — must not throw.
            producer.Wait(TimeSpan.FromSeconds(5));

            // Assert
            Assert.IsTrue(producer.IsCompletedSuccessfully,
                $"Producer task did not complete successfully. Status: {producer.Status}, Exception: {producer.Exception?.Message}");
        }

        /// <summary>
        /// The default constructor must NOT open the real stdout stream.
        /// This is critical: DI registers the writer eagerly during host build,
        /// and we must not interfere with stdout until the first actual write.
        /// (Verified indirectly: constructing then disposing must not throw,
        /// even when stdout is in an unusual state during test execution.)
        /// </summary>
        [TestMethod]
        public void Constructor_DoesNotOpenStdout()
        {
            // Act — default ctor must complete without touching stdout.
            McpStdoutWriter writer = new();

            // Dispose must also be safe when no write ever occurred (lazy init
            // means _writer is still null inside Dispose).
            writer.Dispose();

            // Assert — no exception is the success criterion.
        }
    }
}
