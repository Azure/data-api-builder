// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Resolvers
{
    [TestClass]
    public class ArrayPoolWriterTests
    {
        [TestMethod]
        public void Constructor_ShouldInitializeProperly()
        {
            // Arrange & Act
            using ArrayPoolWriter writer = new();

            // Assert
            Assert.AreEqual(0, writer.GetWrittenSpan().Length);
        }

        [TestMethod]
        public void GetWrittenMemory_ShouldReturnReadOnlyMemory()
        {
            // Arrange
            using ArrayPoolWriter writer = new();

            // Act
            ReadOnlyMemory<byte> memory = writer.GetWrittenMemory();

            // Assert
            Assert.AreEqual(0, memory.Length);
        }

        [TestMethod]
        public void GetWrittenSpan_ShouldReturnReadOnlySpan()
        {
            // Arrange
            using ArrayPoolWriter writer = new();

            // Act
            ReadOnlySpan<byte> span = writer.GetWrittenSpan();

            // Assert
            Assert.AreEqual(0, span.Length);
        }

        [TestMethod]
        public void Advance_ShouldAdvanceCorrectly()
        {
            // Arrange
            using ArrayPoolWriter writer = new();
            writer.GetSpan(10);

            // Act
            writer.Advance(5);

            // Assert
            Assert.AreEqual(5, writer.GetWrittenSpan().Length);
        }

        [TestMethod]
        public void GetMemory_ShouldReturnMemoryWithCorrectSizeHint()
        {
            // Arrange
            using ArrayPoolWriter writer = new();

            // Act
            Memory<byte> memory = writer.GetMemory(10);

            // Assert
            Assert.IsGreaterThanOrEqualTo(10, memory.Length);
        }

        [TestMethod]
        public void GetSpan_ShouldReturnSpanWithCorrectSizeHint()
        {
            // Arrange
            using ArrayPoolWriter writer = new();

            // Act
            Span<byte> span = writer.GetSpan(10);

            // Assert
            Assert.IsGreaterThanOrEqualTo(10, span.Length);
        }

        [TestMethod]
        public void Dispose_ShouldDisposeCorrectly()
        {
            // Arrange
            ArrayPoolWriter writer = new();

            // Act
            writer.Dispose();

            // Assert
            Assert.Throws<ObjectDisposedException>(() => writer.GetMemory());
            Assert.Throws<ObjectDisposedException>(() => writer.GetSpan());
            Assert.Throws<ObjectDisposedException>(() => writer.Advance(0));
        }

        [TestMethod]
        public void Advance_ShouldThrowWhenDisposed()
        {
            // Arrange
            ArrayPoolWriter writer = new();
            writer.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => writer.Advance(0));
        }

        [TestMethod]
        public void Advance_ShouldThrowWhenNegativeCount()
        {
            // Arrange
            using ArrayPoolWriter writer = new();

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.Advance(-1));
        }

        [TestMethod]
        public void Advance_ShouldThrowWhenCountGreaterThanCapacity()
        {
            // Arrange
            using ArrayPoolWriter writer = new();

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(
                () => writer.Advance(1024));
        }

        [TestMethod]
        public void GetMemory_ShouldThrowWhenDisposed()
        {
            // Arrange
            ArrayPoolWriter writer = new();
            writer.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => writer.GetMemory());
        }

        [TestMethod]
        public void GetMemory_ShouldThrowWhenNegativeSizeHint()
        {
            // Arrange
            using ArrayPoolWriter writer = new();

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.GetMemory(-1));
        }

        [TestMethod]
        public void GetSpan_ShouldThrowWhenDisposed()
        {
            // Arrange
            ArrayPoolWriter writer = new();
            writer.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => writer.GetSpan());
        }

        [TestMethod]
        public void GetSpan_ShouldThrowWhenNegativeSizeHint()
        {
            // Arrange
            using ArrayPoolWriter writer = new();

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.GetSpan(-1));
        }

        [TestMethod]
        public void WriteBytesToSpan_ShouldWriteCorrectly()
        {
            // Arrange
            using ArrayPoolWriter writer = new();
            byte[] testData = { 1, 2, 3, 4 };

            // Act
            Span<byte> span = writer.GetSpan(4);
            testData.CopyTo(span);
            writer.Advance(4);

            // Assert
            Assert.AreEqual(4, writer.GetWrittenSpan().Length);
            ReadOnlySpan<byte> writtenSpan = writer.GetWrittenSpan();
            Assert.IsTrue(testData.SequenceEqual(writtenSpan.ToArray()));
        }

        [TestMethod]
        public void WriteBytesToMemory_ShouldWriteCorrectly()
        {
            // Arrange
            using ArrayPoolWriter writer = new();
            byte[] testData = { 1, 2, 3, 4 };

            // Act
            Memory<byte> memory = writer.GetMemory(4);
            testData.CopyTo(memory);
            writer.Advance(4);

            // Assert
            Assert.AreEqual(4, writer.GetWrittenSpan().Length);
            ReadOnlyMemory<byte> writtenMemory = writer.GetWrittenMemory();
            Assert.IsTrue(testData.SequenceEqual(writtenMemory.ToArray()));
        }

        [TestMethod]
        public void WriteBytesExceedingInitialBufferSize_ShouldExpandAndWriteCorrectly()
        {
            // Arrange
            using ArrayPoolWriter writer = new();
            byte[] testData = new byte[1024];

            for (int i = 0; i < testData.Length; i++)
            {
                testData[i] = (byte)(i % 256);
            }

            // Act
            for (int i = 0; i < testData.Length; i += 128)
            {
                Span<byte> span = writer.GetSpan(128);
                testData.AsSpan(i, 128).CopyTo(span);
                writer.Advance(128);
            }

            // Assert
            Assert.AreEqual(1024, writer.GetWrittenSpan().Length);
            ReadOnlySpan<byte> writtenSpan = writer.GetWrittenSpan();
            Assert.IsTrue(testData.SequenceEqual(writtenSpan.ToArray()));
        }
    }
}
